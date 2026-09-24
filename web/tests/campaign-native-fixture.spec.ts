import {test,expect,type BrowserContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('two device sessions produce distinct native Gateway receipts without model work',async({browser,baseURL})=>{
  if(process.env.THADDEUS_NATIVE_FIXTURE!=='1')test.skip(true,'Requires the disposable native proof Gateway and HTTPS fixture.');
  const local=baseURL!,remote=process.env.THADDEUS_TEST_PHONE_ORIGIN!;
  const key=fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim();
  const owner=await browser.newContext({baseURL:local});
  const colleague=await browser.newContext({baseURL:remote,ignoreHTTPSErrors:true});
  const post=(context:BrowserContext,origin:string,route:string,body:unknown,csrf?:string)=>
    context.request.post(origin+route,{headers:{Origin:origin,...(csrf?{'X-CSRF':csrf}:{})},data:body});
  try{
    const signedIn=await owner.request.post(local+'/api/auth/login',{headers:{Origin:local},data:{key}});
    expect(signedIn.status()).toBe(200);
    const ownerSession=await signedIn.json() as {csrf:string};
    const seed=await post(owner,local,'/api/marketing/runway/fixture/seed',
      {requestId:crypto.randomUUID()},ownerSession.csrf);
    expect(seed.status(),await seed.text()).toBe(200);
    const seeded=await seed.json();
    const id=seeded.project.id as string,source=seeded.artifacts[0],asset=seeded.artifacts[1];
    const brief=await post(owner,local,`/api/marketing/runway/${id}/campaign-brief`,{
      requestId:crypto.randomUUID(),projectVersion:seeded.project.version,version:0,
      sourceArtifactId:source.id,sourceArtifactDigest:source.digest,
      brief:{audience:'Founders',problem:'Marketing time',hypothesis:'A draft may clarify the offer',
        priority_rationale:'Synthetic source for native fixture',proposition:'Configurable marketing employee',
        desired_behavior:'Ask for a demo',channel:'Internal review',primary_metric:'Qualified replies',
        metric_definition:'Count relevant replies',guardrail:'No outcome guarantee',
        review_timing:'At owner review; no date set',non_goals:'No publication'},
      experiment:{intervention:'One fixture draft',target_population:'Founders',observation_window:'Seven days',
        metric_source:'Fixture observation',decision_rule:'minimum_sample',minimum_sample:2}
    },ownerSession.csrf);
    expect(brief.status(),await brief.text()).toBe(200);
    const saved=await brief.json();

    const connected=await post(owner,local,`/api/marketing/campaigns/${id}/native/start`,{},ownerSession.csrf);
    expect(connected.status(),await connected.text()).toBe(200);
    const ownerReview=await (await owner.request.get(local+`/api/marketing/campaigns/${id}/review`)).json();
    expect(ownerReview.native.sessionConnected).toBe(true);
    expect(ownerReview.native.gatewayProfileObserved).toBe(true);
    expect(ownerReview.native.gatewayProfileId).toMatch(/^[a-f0-9-]{36}$/);

    const started=await post(owner,local,'/api/pair/start',{},ownerSession.csrf);
    expect(started.status()).toBe(200);
    const pair=await started.json() as {id:string;code:string};
    const claimed=await post(colleague,remote,'/api/pair/claim',{code:pair.code,name:'Native fixture colleague'});
    expect(claimed.status(),await claimed.text()).toBe(200);
    expect((await post(owner,local,`/api/pair/${pair.id}/confirm`,{},ownerSession.csrf)).status()).toBe(200);
    const exchanged=await post(colleague,remote,'/api/pair/exchange',{});
    expect(exchanged.status(),await exchanged.text()).toBe(200);
    const colleagueSession=await exchanged.json() as {id:string;csrf:string;owner:boolean};
    expect(colleagueSession.owner).toBe(false);
    const grant=await post(owner,local,`/api/marketing/campaigns/${id}/access`,
      {deviceId:colleagueSession.id,action:'grant'},ownerSession.csrf);
    expect(grant.status(),await grant.text()).toBe(200);

    const requestId=crypto.randomUUID();
    const body={requestId,kind:'comment',artifactId:asset.id,artifactDigest:asset.digest,
      projectVersion:saved.project.version,content:'Verify the first founder claim against the saved source.',
      identity:'owner@cockpit.local',actorName:'Forged owner'};
    const sent=await post(colleague,remote,`/api/marketing/campaigns/${id}/inputs`,body,colleagueSession.csrf);
    expect(sent.status(),await sent.text()).toBe(200);
    expect((await sent.json()).status).toBe('recorded');
    const duplicate=await post(colleague,remote,`/api/marketing/campaigns/${id}/inputs`,body,colleagueSession.csrf);
    expect(duplicate.status()).toBe(200);
    const reviewed=await (await owner.request.get(local+`/api/marketing/campaigns/${id}/review`)).json();
    expect(reviewed.discussion).toHaveLength(1);
    expect(reviewed.discussion[0].actorId).toBe(colleagueSession.id);
    expect(reviewed.discussion[0].actorName).toBe('Native fixture colleague');
    expect(reviewed.discussion[0].nativeRecorded).toBe(true);
    expect(reviewed.discussion[0].nativeProfileId).toMatch(/^[a-f0-9-]{36}$/);
    expect(reviewed.discussion[0].nativeProfileId).not.toBe(ownerReview.native.gatewayProfileId);
    const colleagueReview=await (await colleague.request.get(remote+`/api/marketing/campaigns/${id}/review`)).json();
    expect(colleagueReview.native.boundDevice).toBe(true);
    expect(colleagueReview.native.gatewayProfileId).toBe(reviewed.discussion[0].nativeProfileId);
    expect((await post(colleague,remote,`/api/marketing/campaigns/${id}/native/start`,{},colleagueSession.csrf)).status()).toBe(403);
    expect((await colleague.request.get(remote+'/api/marketing/history')).status()).toBe(403);
    const ledger=await (await owner.request.get(local+`/api/marketing/runways/${id}`)).json();
    expect(ledger.model_requests).toHaveLength(0);
    expect(ledger.project.token_used).toBe(0);

    const revoke=await post(owner,local,`/api/marketing/campaigns/${id}/access`,
      {deviceId:colleagueSession.id,action:'revoke'},ownerSession.csrf);
    expect(revoke.status()).toBe(200);
    expect((await colleague.request.get(remote+`/api/marketing/campaigns/${id}/review`)).status()).toBe(403);
  }finally{await colleague.close();await owner.close();}
});
