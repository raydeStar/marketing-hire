import {test,expect,type BrowserContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('two isolated browser principals share one versioned campaign without private access or inference',async({browser,baseURL})=>{
  const origin=baseURL!;
  const dataRoot=process.env.THADDEUS_TEST_DATA;
  if(!dataRoot)throw new Error('THADDEUS_TEST_DATA must point to a disposable fixture host.');
  const key=fs.readFileSync(path.join(dataRoot,'host-key.txt'),'utf8').trim();
  const owner=await browser.newContext({baseURL:origin,viewport:{width:1440,height:900}});
  const colleague=await browser.newContext({baseURL:origin,viewport:{width:1280,height:800}});
  await owner.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  const post=async(context:BrowserContext,route:string,body:unknown,csrf:string)=>context.request.post(origin+route,{
    headers:{Origin:origin,'X-CSRF':csrf},data:body
  });
  try{
    const ownerLogin=await owner.request.post(origin+'/api/auth/login',{headers:{Origin:origin},data:{key}});
    expect(ownerLogin.status()).toBe(200);
    const ownerSession=await ownerLogin.json() as {id:string;csrf:string;owner:boolean};
    const colleagueLogin=await colleague.request.post(origin+'/api/auth/login',{headers:{Origin:origin},data:{key}});
    expect(colleagueLogin.status()).toBe(200);
    const fixtureSession=await post(colleague,'/api/marketing/fixture/collaborator-session',{},
      (await colleagueLogin.json() as {csrf:string}).csrf);
    expect(fixtureSession.status()).toBe(200);
    const colleagueSession=await fixtureSession.json() as {id:string;csrf:string;owner:boolean};
    expect(colleagueSession.owner).toBe(false);
    expect(colleagueSession.id).not.toBe(ownerSession.id);
    expect((await (await colleague.request.get(origin+'/api/session')).json()).owner).toBe(false);

    const seed=await post(owner,'/api/marketing/runway/fixture/seed',{requestId:crypto.randomUUID()},ownerSession.csrf);
    expect(seed.status()).toBe(200);
    const seeded=await seed.json();
    const id=seeded.project.id as string;
    const source=seeded.artifacts[0],asset=seeded.artifacts[1];
    const saved=await post(owner,`/api/marketing/runway/${id}/campaign-brief`,{
      requestId:crypto.randomUUID(),projectVersion:seeded.project.version,version:0,
      sourceArtifactId:source.id,sourceArtifactDigest:source.digest,
      brief:{audience:'Founders',problem:'Marketing time',hypothesis:'A bounded draft may clarify the offer',
        priority_rationale:'Synthetic founder notes for this fixture',proposition:'Configurable marketing employee',
        desired_behavior:'Ask for a demo',channel:'Owner reviewed draft',primary_metric:'Qualified replies',
        metric_definition:'Count relevant replies',guardrail:'No outcome guarantee',
        review_timing:'At owner review; no calendar date set',non_goals:'No publication'},
      experiment:{intervention:'One fixture draft',target_population:'Founders',observation_window:'Seven days',
        metric_source:'Fixture observation',decision_rule:'minimum_sample',minimum_sample:2}
    },ownerSession.csrf);
    expect(saved.status()).toBe(200);
    const savedSnapshot=await saved.json();
    const selected=savedSnapshot.campaign.asset_artifact_id as string;
    expect(selected).toBe(asset.id);
    expect((await colleague.request.get(origin+`/api/marketing/campaigns/${id}/review`)).status()).toBe(403);
    for(const route of ['/api/marketing/history','/api/marketing/runways/'+id,'/api/events','/api/runs/test'])
      expect((await colleague.request.get(origin+route)).status(),route).toBe(403);
    const privateState=await (await colleague.request.get(origin+'/api/state')).json();
    expect(privateState.chats).toEqual([]);
    expect(privateState.runs).toEqual([]);
    expect((await post(colleague,`/api/marketing/runway/${id}/input`,{content:'forged'},colleagueSession.csrf)).status()).toBe(403);
    expect((await post(colleague,`/api/marketing/campaigns/${id}/access`,{
      deviceId:colleagueSession.id,action:'grant'},colleagueSession.csrf)).status()).toBe(403);

    const granted=await post(owner,`/api/marketing/campaigns/${id}/access`,{
      deviceId:colleagueSession.id,action:'grant'},ownerSession.csrf);
    expect(granted.status()).toBe(200);
    const ownerReview=await (await owner.request.get(origin+`/api/marketing/campaigns/${id}/review`)).json();
    const colleagueReview=await (await colleague.request.get(origin+`/api/marketing/campaigns/${id}/review`)).json();
    expect(colleagueReview.artifact.id).toBe(ownerReview.artifact.id);
    expect(colleagueReview.artifact.digest).toBe(ownerReview.artifact.digest);
    expect(colleagueReview.project.goal).toBe(ownerReview.project.goal);
    expect(colleagueReview.project.version).toBe(savedSnapshot.project.version);
    expect(colleagueReview.native.boundDevice).toBe(true);
    expect(colleagueReview.native.gatewayProfileObserved).toBe(false);
    expect(colleagueReview.native.sessionConnected).toBe(false);
    expect((await post(owner,`/api/marketing/campaigns/${id}/native/start`,{},ownerSession.csrf)).status()).toBe(409);
    expect((await post(colleague,`/api/marketing/campaigns/${id}/native/start`,{},colleagueSession.csrf)).status()).toBe(403);
    expect((await colleague.request.get(origin+`/api/marketing/campaigns/${crypto.randomUUID().replaceAll('-','')}/review`)).status()).toBe(403);
    expect((await post(colleague,`/api/marketing/runway/${id}/shared/suggestions`,{
      requestId:crypto.randomUUID(),version:savedSnapshot.project.version,content:'No native inference.'
    },colleagueSession.csrf)).status()).toBe(409);

    const ownerPage=await owner.newPage();
    const colleaguePage=await colleague.newPage();
    await ownerPage.goto('/?view=campaigns');
    await colleaguePage.goto('/');
    // A collaborator only has the shared campaigns view.
    await expect(colleaguePage.getByRole('navigation',{name:'Main views'}).getByRole('button')).toHaveText(['Shared campaigns']);
    await expect(ownerPage.getByRole('region',{name:'Campaign review workspace'})).toContainText('SIMULATED campaign');
    const shared=colleaguePage.getByRole('region',{name:'Shared campaign review'});
    await expect(shared).toContainText('SIMULATED campaign');
    await expect(shared.getByRole('heading',{name:'Draft post angles'})).toBeVisible();
    await colleaguePage.screenshot({path:'../artifacts/enterprise-review/collaborator-1280-fixture.png'});
    expect(await colleaguePage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await colleaguePage.setViewportSize({width:1440,height:900});
    expect(await colleaguePage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await colleaguePage.screenshot({path:'../artifacts/enterprise-review/collaborator-1440-fixture.png'});
    await colleaguePage.setViewportSize({width:1280,height:800});

    const requestId=crypto.randomUUID();
    const message='Make the first founder hook specific to the saved source.';
    const requestBody={requestId,kind:'revision_request',artifactId:asset.id,
      artifactDigest:asset.digest,projectVersion:savedSnapshot.project.version,content:message,
      actorId:'owner-forged',actorName:'Owner forged',activate:true};
    const requested=await post(colleague,`/api/marketing/campaigns/${id}/inputs`,requestBody,colleagueSession.csrf);
    expect(requested.status()).toBe(200);
    expect((await requested.json()).status).toBe('awaiting_owner_authorization');
    const duplicate=await post(colleague,`/api/marketing/campaigns/${id}/inputs`,requestBody,colleagueSession.csrf);
    expect(duplicate.status()).toBe(200);
    const updatedOwner=await (await owner.request.get(origin+`/api/marketing/campaigns/${id}/review`)).json();
    const updatedColleague=await (await colleague.request.get(origin+`/api/marketing/campaigns/${id}/review`)).json();
    expect(updatedOwner.discussion).toHaveLength(1);
    expect(updatedColleague.discussion).toEqual(updatedOwner.discussion);
    expect(updatedOwner.discussion[0].actorId).toBe(colleagueSession.id);
    expect(updatedOwner.discussion[0].actorName).toBe('Fixture collaborator');
    expect(updatedOwner.discussion[0].inputId).toBeTruthy();
    expect(updatedOwner.discussion[0].status).toBe('awaiting_owner_authorization');
    expect((await post(colleague,`/api/marketing/campaigns/${id}/requests/${requestId}/authorize`,{
      projectVersion:updatedOwner.project.version},colleagueSession.csrf)).status()).toBe(403);
    const stale=await post(colleague,`/api/marketing/campaigns/${id}/inputs`,{
      ...requestBody,requestId:crypto.randomUUID(),content:'This stale version must not save.'},colleagueSession.csrf);
    expect(stale.status()).toBe(409);
    const authorized=await post(owner,`/api/marketing/campaigns/${id}/requests/${requestId}/authorize`,{
      projectVersion:updatedOwner.project.version},ownerSession.csrf);
    expect(authorized.status()).toBe(200);
    expect((await authorized.json()).status).toBe('authorized_execution_unavailable');
    const authorizedReview=await (await colleague.request.get(origin+`/api/marketing/campaigns/${id}/review`)).json();
    expect(authorizedReview.discussion[0].status).toBe('authorized_execution_unavailable');
    expect(authorizedReview.execution.liveEnabled).toBe(false);
    const ledger=await (await owner.request.get(origin+`/api/marketing/runways/${id}`)).json();
    expect(ledger.project.token_used).toBe(0);
    expect(ledger.model_requests).toHaveLength(0);
    expect(ledger.inputs.some((item:{id:string})=>item.id===updatedOwner.discussion[0].inputId)).toBe(true);

    await expect(shared).toContainText(message,{timeout:15000});
    await ownerPage.getByRole('region',{name:'Campaign review workspace'}).getByRole('button',{name:'Activity & sharing'}).click();
    await expect(ownerPage.getByRole('region',{name:'Shared discussion'})).toContainText(message,{timeout:15000});
    await expect(shared).toContainText('Authorized; execution unavailable',{timeout:15000});
    await shared.getByRole('textbox',{name:/Comment for draft/}).fill('The owner may compare this hook with the previous draft.');
    await shared.getByRole('button',{name:'Save comment'}).click();
    await expect(shared).toContainText('The owner may compare this hook with the previous draft.',{timeout:15000});
    await expect(ownerPage.getByRole('region',{name:'Shared discussion'})).toContainText(
      'The owner may compare this hook with the previous draft.',{timeout:15000});
    await ownerPage.getByRole('region',{name:'Shared discussion'}).getByRole('heading').scrollIntoViewIfNeeded();
    await ownerPage.screenshot({path:'../artifacts/enterprise-review/owner-waiting-fixture.png'});
    const linkedRoute=`/api/marketing/fixture/campaigns/${id}/requests/${requestId}/linked-revision`;
    const [linkedResponse,competingResponse]=await Promise.all([
      post(owner,linkedRoute,{},ownerSession.csrf),post(owner,linkedRoute,{},ownerSession.csrf)
    ]);
    expect(linkedResponse.status(),await linkedResponse.text()).toBe(200);
    expect(competingResponse.status(),await competingResponse.text()).toBe(200);
    const linked=await linkedResponse.json();
    expect((await competingResponse.json()).project.id).toBe(linked.project.id);
    expect(linked.project.source_runway_id).toBe(id);
    expect(linked.project.source_artifact_id).toBe(asset.id);
    expect(linked.inputs.some((item:{source_input_id:string})=>
      item.source_input_id===updatedOwner.discussion[0].inputId)).toBe(true);
    expect(linked.executions).toHaveLength(1);
    expect(linked.model_requests).toHaveLength(0);
    expect(linked.project.token_used).toBe(0);
    const repeatedLinked=await post(owner,linkedRoute,{},ownerSession.csrf);
    expect(repeatedLinked.status(),await repeatedLinked.text()).toBe(200);
    expect((await repeatedLinked.json()).artifacts).toHaveLength(1);
    const revised=linked.artifacts[0];
    const approvedRevision=await post(owner,`/api/marketing/runway/${linked.project.id}/review`,{
      requestId:crypto.randomUUID(),artifactId:revised.id,digest:revised.digest,
      decision:'approved',version:linked.project.version
    },ownerSession.csrf);
    expect(approvedRevision.status(),await approvedRevision.text()).toBe(200);
    const approved=await approvedRevision.json();
    const sourceBeforeAdopt=await (await owner.request.get(origin+`/api/marketing/runways/${id}`)).json();
    const adoption=await post(owner,`/api/marketing/runway/${id}/campaign-adopt-revision`,{
      requestId:crypto.randomUUID(),projectVersion:sourceBeforeAdopt.project.version,
      version:sourceBeforeAdopt.campaign.version,revisionRunwayId:linked.project.id,
      revisionProjectVersion:approved.project.version,revisionArtifactId:revised.id,
      revisionArtifactDigest:revised.digest,revisionReviewId:approved.reviews.at(-1).id
    },ownerSession.csrf);
    expect(adoption.status(),await adoption.text()).toBe(200);
    const adopted=await adoption.json();
    expect(adopted.campaign.asset_artifact_id).toBe(revised.id);
    expect(adopted.campaign_actions.at(-1).owner_verified).toBe(true);
    const sharedAfterAdopt=await colleague.request.get(origin+`/api/marketing/campaigns/${id}/review`);
    expect(sharedAfterAdopt.status(),await sharedAfterAdopt.text()).toBe(200);
    const adoptedShared=await sharedAfterAdopt.json();
    expect(adoptedShared.artifact.id).toBe(revised.id);
    expect(adoptedShared.execution.requestChangesAvailable).toBe(false);
    expect((await post(colleague,`/api/marketing/campaigns/${id}/inputs`,{
      requestId:crypto.randomUUID(),kind:'revision_request',artifactId:revised.id,
      artifactDigest:revised.digest,projectVersion:adoptedShared.project.version,
      content:'A second linked worker revision must stay unavailable.'
    },colleagueSession.csrf)).status()).toBe(409);
    await ownerPage.reload();
    await ownerPage.getByRole('complementary',{name:'Campaign list'}).getByRole('button',{
      name:/SIMULATED campaign: test an internal founder message/
    }).first().click();
    const desk=ownerPage.getByRole('region',{name:'Campaign review workspace'});
    await expect(desk.getByRole('heading',{name:'Revised post angles'})).toBeVisible();
    await desk.getByRole('button',{name:'Compare with predecessor'}).click();
    await expect(desk.getByRole('heading',{name:'Predecessor'})).toBeVisible();
    await desk.getByRole('heading',{name:'Predecessor'}).scrollIntoViewIfNeeded();
    await ownerPage.screenshot({path:'../artifacts/enterprise-review/owner-comparison-fixture.png'});
    await expect(shared.getByRole('heading',{name:'Revised post angles'})).toBeVisible({timeout:15000});
    await expect(shared.getByRole('radio',{name:'Request a change'})).toBeDisabled();
    await shared.getByRole('heading',{name:'Revised post angles'}).scrollIntoViewIfNeeded();
    await colleaguePage.screenshot({path:'../artifacts/enterprise-review/collaborator-revision-fixture.png'});
    const revoked=await post(owner,`/api/marketing/campaigns/${id}/access`,{
      deviceId:colleagueSession.id,action:'revoke'},ownerSession.csrf);
    expect(revoked.status()).toBe(200);
    expect((await colleague.request.get(origin+`/api/marketing/campaigns/${id}/review`)).status()).toBe(403);
    await colleaguePage.reload();
    await expect(shared).not.toContainText(message,{timeout:15000});
    expect((await post(owner,`/api/devices/${colleagueSession.id}/revoke`,{},ownerSession.csrf)).status()).toBe(200);
    expect((await colleague.request.get(origin+'/api/session')).status()).toBe(401);
  }finally{await colleague.close();await owner.close();}
});
