import {test,expect,type Page,type APIRequestContext,type Locator} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,view='today'){
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/?view=${view}#launch=${(await issued.json()).ticket}`);
}
// Assignment management is a collapsed <details> once a project exists.
async function openManagement(panel:Locator){
  const details=panel.locator('details.runway-management');
  await expect(details).toBeVisible();
  if(await details.getAttribute('open')===null)await details.locator(':scope > summary').click();
  await expect(details).toHaveAttribute('open','');
}

test.afterEach(async({page,baseURL})=>{
  const res=await page.request.get(baseURL!+'/api/session');
  if(res.ok()){
    const session=await res.json();
    await page.request.post(baseURL!+'/api/devices/'+session.id+'/revoke',{
      headers:{Origin:baseURL!,'X-CSRF':session.csrf},data:{}});
  }
});

test('current and archived response records remain plain text without draft approval controls',async({page,request,baseURL})=>{
  const id='a'.repeat(32),old='d'.repeat(32),execution='b'.repeat(32),step='c'.repeat(32);
  const makeRun=(projectId:string,content:string)=>({project:{id:projectId,goal:'Audit the stopped worker response',status:'needs_review',version:3,run_count:1,max_runs:3,max_model_requests:3,request_allowance:1,accounting_mode:'post_response',token_limit:75000,token_used:0,token_reserved:25000,created_at:1780000000,active_execution:null},
    steps:[],artifacts:[],reviews:[],inputs:[],executions:[{id:execution,status:'failed',reserved_tokens:25000,reported_tokens:null,started_at:1780000000}],terminal_receipts:[{execution_id:execution}],model_requests:[{execution_id:execution,request_id:execution,status:'unknown',reserved_tokens:25000,reported_tokens:null,created_at:1780000000}],
    worker_responses:[{execution_id:execution,step_id:step,kind:'audience_note',configured_model:'fixture',content,digest:'f'.repeat(64),original_characters:13000,truncated:true,received_at:1780000000}]});
  const runway=makeRun(id,'<img src=x onerror="alert(1)"> Unverified current reply');
  const archived=makeRun(old,'Archived response text');
  await page.route('**/api/organization',route=>route.fulfill({json:{directory:{version:1,departments:[{id:'marketing',name:'Marketing'}],agents:[{id:'marketing-main',name:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]},canConfigure:true}}));
  await page.route('**/api/marketing/**',route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:'Marketing',model:'fixture'},connection:{status:'connected'},canConfigure:true,taskStoreAvailable:true,runwayArchiveEnabled:true,runwayLiveEnabled:false,profile:{id:'marketing',display_name:'Marketing',product_summary:'Fixture offer',audience:'Founders',goals:'Learn',voice:'Plain',guardrails:'Internal only',channels:'',version:1,updated_at:1780000000},runway,tasks:[],drafts:[],messages:[],requests:[],activity:[]}});
    if(url.pathname==='/api/marketing/runways')return route.fulfill({json:{projects:[{...archived.project,artifact_count:0}]}});
    if(url.pathname==='/api/marketing/runways/'+old)return route.fulfill({json:archived});
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    return route.fulfill({status:404,json:{error:'Fixture route unavailable'}});
  });
    await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
    await launch(page,request,baseURL!,'campaigns');
    await openManagement(page.getByRole('region',{name:'Standing marketing assignment'}));
    const records=page.getByRole('region',{name:'Employee response records'});
    await records.getByText('Employee response records · 1',{exact:true}).click();
    await records.getByText(/audience note · received/).click();
    await expect(records.locator('pre')).toHaveText(runway.worker_responses[0].content);
    await expect(records.locator('img')).toHaveCount(0);
    await expect(records).toContainText('do not establish token usage or approve a draft');
    await expect(records).toContainText('Preview shortened to 12,000 characters');
    for(const name of ['Approve','Approve exact draft'])await expect(page.getByRole('button',{name,exact:true})).toHaveCount(0);
    await expect(page.getByRole('region',{name:'First request usage checkpoint'})).toHaveCount(0);
    await page.getByRole('button',{name:/0 saved results/}).click();
    const past=page.locator('#previous-marketing-assignments');
    const oldRecords=past.getByRole('region',{name:'Employee response records'});
    await oldRecords.getByText('Employee response records · 1',{exact:true}).click();
    await oldRecords.getByText(/audience note · received/).click();
    await expect(oldRecords.locator('pre')).toHaveText('Archived response text');
});
