import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?"?"+query:""}#launch=${(await issued.json()).ticket}`);
}

test('saved feedback requires explicit revision allowance and retries the same held grant',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const profile={id:'marketing',display_name:'Marketing employee',product_summary:'Configurable marketing agents',audience:'Founders',goals:'Learn from a draft',voice:'Plain',guardrails:'Internal drafts only',channels:'',version:1,updated_at:1780000000};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Customer growth'}],agents:[{id:'marketing-main',name:'Marketing employee',role:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  const source='a'.repeat(32),draft='b'.repeat(32),review='c'.repeat(32),grant='d'.repeat(32);
  const artifact={id:draft,kind:'post_angles',content:JSON.stringify({angles:[{title:'Original angle',hook:'An initial draft',why:'Fixture evidence only',sourceUrl:'https://news.ycombinator.com/item?id=111',claimLimit:'Provisional'}]}),digest:'b'.repeat(64),source_urls:'[]',created_at:Date.now()/1000,step_id:'e'.repeat(32)};
  let runway:any={project:{id:source,goal:'Improve a saved marketing draft',status:'needs_review',version:4,run_count:3,max_runs:3,max_model_requests:3,accounting_mode:'post_response',request_allowance:3,token_limit:75000,token_used:1000,token_reserved:0,max_active_seconds:900},steps:[],artifacts:[artifact],
    reviews:[{id:review,artifact_id:draft,artifact_digest:artifact.digest,decision:'revision_requested',instruction:'Remove the unsupported claim.',actor_name:'Fixture owner',owner_verified:true,created_at:Date.now()/1000}],inputs:[],executions:[],model_requests:[],revision_grants:[]};
  let prepared=0,released=0;
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/marketing/**',async route=>{
    const req=route.request(),url=new URL(req.url());
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:profile.display_name,model:'fixture',sessionKey:'agent:main:fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,runwayLiveEnabled:true,profile,drafts:[],evidence:[],ownerDecisions:[],tasks:[],activity:[],messages:[],requests:[],runway}});
    if(url.pathname==='/api/marketing/usage')return route.fulfill({json:{chat:[],autonomous:{events:[],reservedTokens:0},autonomousAvailable:true,fixture:true,updatedAt:new Date().toISOString()}});
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    if(url.pathname===`/api/marketing/runway/${source}/revision-grants`){
      const body=req.postDataJSON();expect(body.acceptPostResponseAccounting).toBe(true);expect(body.reviewId).toBe(review);expect(body.digest).toBe(artifact.digest);
      expect(body.maxModelRequests).toBe(1);expect(body.maxRuns).toBe(1);expect(body.budgetMode).toBe('fresh_pilot');expect(body.tokenLimit).toBe(25000);prepared++;
      runway.revision_grants=[{id:grant,source_review_id:review,source_artifact_id:draft,source_artifact_digest:artifact.digest,scope:'internal_revision_draft',status:'held_for_metering',accounting_mode:'post_response',budget_mode:'fresh_pilot',max_runs:1,max_model_requests:1,token_limit:25000,max_active_seconds:300,deadline_at:body.deadlineAt,created_at:Date.now()/1000}];
      return route.fulfill({json:runway});
    }
    if(url.pathname===`/api/marketing/revision-grants/${grant}/release`){
      released++;
      if(released===1)return route.fulfill({status:503,json:{error:'Fixture transport unavailable'}});
      runway={...runway,project:{...runway.project,id:grant,scope:'internal_revision_draft',version:2,run_count:1,max_runs:1,max_model_requests:1,request_allowance:1,token_used:500,source_runway_id:source,source_review_id:review},artifacts:[{...artifact,id:'f'.repeat(32),kind:'revision_angles',content:JSON.stringify({angles:[{title:'Revised angle',hook:'The unsupported claim is removed.',why:'Follows saved owner feedback',sourceUrl:'https://news.ycombinator.com/item?id=111',claimLimit:'Provisional'}]})}],reviews:[],revision_grants:[],model_requests:[{request_id:grant,execution_id:grant,status:'reported',reported_tokens:500,reserved_tokens:25000,created_at:Date.now()/1000}]};
      return route.fulfill({json:runway});
    }
    return route.fulfill({status:404,json:{error:'Fixture route unavailable'}});
  });
  try{
    await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
    await launch(page,request,origin);
    // Campaigns are listed under Work; the row opens the review desk.
    await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Work'}).click();
    await page.getByRole('region',{name:'Campaigns'}).getByRole('button',{name:/Improve a saved marketing draft/}).click();
    const panel=page.getByRole('region',{name:'Run saved revision'});
    await expect(panel).toContainText('Remove the unsupported claim.');
    const start=panel.getByRole('button',{name:'Authorize and run one revision'});
    await expect(start).toBeDisabled();
    await panel.getByRole('checkbox',{name:/I authorize one new revision/}).check();
    await start.click();await expect(panel).toContainText('Fixture transport unavailable');
    expect(prepared).toBe(1);expect(released).toBe(1);
    await panel.getByRole('button',{name:'Release the saved revision grant'}).click();
    await expect(panel).toHaveCount(0);expect(prepared).toBe(1);expect(released).toBe(2);
    await expect(page.getByRole('region',{name:'First request usage checkpoint'})).toHaveCount(0);
    await expect(page.getByRole('region',{name:'Campaign review workspace'})).toContainText('The unsupported claim is removed.');
  }finally{
    const response=await page.request.get(origin+'/api/session');
    if(response.ok()){const session=await response.json();await page.request.post(origin+'/api/devices/'+session.id+'/revoke',{headers:{Origin:origin,'X-CSRF':session.csrf},data:{}});}
  }
});
