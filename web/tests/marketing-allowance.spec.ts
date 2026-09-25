import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test.afterEach(async({page,baseURL})=>{
  const res=await page.request.get(baseURL!+'/api/session');
  if(res.ok()){
    const session=await res.json();
    await page.request.post(baseURL!+'/api/devices/'+session.id+'/revoke',{
      headers:{Origin:baseURL!,'X-CSRF':session.csrf},data:{}});
  }
});

test('allowance history distinguishes renewal, unknown employee use, and stale observations',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const launched=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(launched.ok()).toBe(true);const {ticket}=await launched.json();
  const now=Date.now()/1000;
  const makeSample=(age:number,used:number,reset:number)=>({accountKey:'fixture',accountLabel:'ow•••@example.test',plan:'pro',observedAt:now-age,resetCredits:0,windows:[{key:'codex:primary',label:'Codex · Weekly',usedPercent:used,windowMinutes:10080,resetsAt:reset}]});
  const history={configured:true,latest:makeSample(0,0,now+604800),samples:[makeSample(2400,94,now+600),makeSample(1200,97,now+600),makeSample(0,0,now+604800)],lastAttemptAt:now,error:null as string|null,stale:false,pollSeconds:300};
  let dispatched=0;
  await page.route('**/api/organization',route=>route.fulfill({json:{directory:{version:1,departments:[{id:'marketing',name:'Marketing'}],agents:[{id:'marketing-main',name:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]},canConfigure:true}}));
  await page.route('**/api/meetings',route=>route.fulfill({json:[]}));
  await page.route('**/api/marketing/**',route=>{
    if(route.request().method()!=='GET')dispatched++;
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/allowance')return route.fulfill({json:history});
    if(url.pathname==='/api/marketing/usage')return route.fulfill({json:{chat:[],autonomous:{events:[{id:'unknown',kind:'autonomous',createdAt:now,totalTokens:null,inputTokens:null,outputTokens:null,source:'provider_receipt',status:'unknown'}],reservedTokens:25000},autonomousAvailable:true,fixture:true,updatedAt:new Date().toISOString()}});
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:'Marketing',model:'fixture'},connection:{status:'connected'},canConfigure:true,taskStoreAvailable:true,runwayArchiveEnabled:true,runwayLiveEnabled:false,profile:{id:'marketing',display_name:'Marketing',product_summary:'Fixture',audience:'Founders',goals:'Learn',voice:'Plain',guardrails:'Internal only',channels:'',version:1,updated_at:now},runway:null,tasks:[],drafts:[],messages:[],requests:[],activity:[]}});
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    return route.fulfill({status:404,json:{error:'Fixture route unavailable'}});
  });
  await page.goto('/?view=settings#launch='+ticket);
  const allowance=page.getByRole('region',{name:'Shared account allowance'});
  await expect(allowance.getByRole('meter')).toHaveAttribute('value','100');
  await expect(allowance).toContainText('Includes other Codex work using this account');
  await allowance.getByText('Allowance history',{exact:true}).click();
  await expect(allowance).toContainText('3 percentage points used since previous check');
  await expect(allowance).toContainText('Allowance window changed');
  await expect(allowance).not.toContainText('-97 percentage points used');
  for(const label of ['Day','Week','Month']){
    const button=allowance.getByRole('button',{name:label,exact:true});await button.click();await expect(button).toHaveAttribute('aria-pressed','true');
  }
  const employee=page.getByRole('region',{name:'Employee token usage'});
  await expect(employee).toContainText('1 entry with unreported usage');
  await expect(employee).toContainText('25,000 reserved');
  const download=page.waitForEvent('download');await allowance.getByRole('button',{name:'Download 30-day history'}).click();
  expect((await download).suggestedFilename()).toBe('shared-allowance-history.json');
  history.stale=true;history.error='Allowance check failed. Last saved observations remain available; no new usage is assumed.';
  await page.reload();
  await expect(allowance).toContainText('100% remaining · saved');
  await expect(allowance).toContainText('not a current balance');
  await expect(allowance.getByRole('button',{name:/redeem|reset/i})).toHaveCount(0);
  expect(dispatched).toBe(0);
});
