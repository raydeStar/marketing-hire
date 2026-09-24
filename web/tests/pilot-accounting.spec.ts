import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('fixture pilot requires accounting acceptance and a separate first-request usage checkpoint',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  const profile={id:'marketing',display_name:'Marketing employee',product_summary:'Configurable marketing agents',audience:'Founders',goals:'Learn from a draft',voice:'Plain',guardrails:'Internal drafts only',channels:'',version:1,updated_at:1780000000};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Customer growth'}],agents:[{id:'marketing-main',name:'Marketing employee',role:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  let runway:any=null,created=0,released=0;
  const deadline=Date.now()/1000+900;
  const tokenEvent=(id:string,daysAgo:number,totalTokens:number|null)=>({id,kind:'autonomous',source:'provider_receipt',createdAt:new Date(new Date().getFullYear(),new Date().getMonth(),new Date().getDate()-daysAgo,0,1).getTime()/1000,totalTokens,inputTokens:null,outputTokens:null,status:totalTokens===null?'unknown':'reported'});
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/meetings',route=>route.fulfill({json:[]}));
  await page.route('**/api/marketing/**',async route=>{
    const req=route.request(),url=new URL(req.url());
    if(url.pathname==='/api/marketing/sources/search')return route.fulfill({json:{candidates:[111,222,333].map(id=>({url:`https://news.ycombinator.com/item?id=${id}`,title:`Discussion ${id}`,publishedAt:Date.now()/1000,comments:5}))}});
    if(url.pathname==='/api/marketing/usage')return route.fulfill({json:{chat:[],autonomous:{events:[tokenEvent('today',0,100),tokenEvent('week',3,200),tokenEvent('month',15,300),tokenEvent('unknown',0,null)],reservedTokens:25000},autonomousAvailable:true,fixture:false,updatedAt:new Date().toISOString()}});
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:profile.display_name,model:'fixture',sessionKey:'agent:main:fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,runwayLiveEnabled:true,profile,drafts:[],evidence:[],ownerDecisions:[],tasks:[],activity:[],messages:[],requests:[],runway}});
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    if(url.pathname==='/api/marketing/runway'&&req.method()==='POST'){
      const body=req.postDataJSON();expect(body.acceptPostResponseAccounting).toBe(true);expect(body.sourceUrls).toHaveLength(2);created++;
      runway={project:{id:'f'.repeat(32),goal:body.goal,status:'needs_review',version:2,run_count:1,max_runs:3,max_model_requests:3,accounting_mode:'post_response',request_allowance:1,token_limit:75000,token_used:800,token_reserved:0,deadline_at:deadline,wait_reason:'Review observed usage'},
        steps:[{id:'a'.repeat(32),kind:'audience_note',status:'done',attempts:1}],artifacts:[],reviews:[],inputs:[],executions:[],
        model_requests:[{request_id:'b'.repeat(32),execution_id:'b'.repeat(32),status:'reported',reserved_tokens:25000,reported_tokens:800,created_at:Date.now()/1000}]};
      return route.fulfill({json:runway});
    }
    if(url.pathname==='/api/marketing/runway/continue-pilot'){
      const body=req.postDataJSON();expect(body.usageReviewed).toBe(true);expect(body.version).toBe(2);expect(body.id).toBe(runway.project.id);released++;
      runway.project.request_allowance=3;runway.project.status='ready';runway.project.version++;
      expect(runway.project.deadline_at).toBe(deadline);
      return route.fulfill({json:runway});
    }
    return route.fulfill({status:404,json:{error:'Fixture route unavailable'}});
  });
  try{
    await page.goto('/#launch='+ticket);
    const tracker=page.getByRole('region',{name:'Employee token usage'});
    await expect(tracker).toContainText('Last 30 days incomplete · 1 entry');
    await expect(tracker).toContainText('25,000 reserved');
    await tracker.getByText('Day / week / month history',{exact:true}).click();
    await expect(tracker).toContainText('Last 7 days · 300 reported');
    await tracker.getByRole('button',{name:'Day',exact:true}).click();
    await expect(tracker).toContainText('Today · 100 reported');
    await tracker.getByRole('button',{name:'Month',exact:true}).click();
    await expect(tracker).toContainText('Last 30 days · 600 reported');
    await page.getByRole('navigation',{name:'Main views'}).getByRole('button',{name:'Work',exact:true}).click();
    const panel=page.getByRole('region',{name:'Standing marketing assignment'});
    await panel.getByText('Find current discussions',{exact:true}).click();
    await panel.getByRole('button',{name:'Search discussions',exact:true}).click();
    await panel.locator('li').filter({has:page.getByRole('link',{name:'Discussion 111',exact:true})}).getByRole('button',{name:'Use source'}).click();
    await panel.locator('li').filter({has:page.getByRole('link',{name:'Discussion 222',exact:true})}).getByRole('button',{name:'Use source'}).click();
    await expect(panel.getByLabel('Source 1',{exact:true})).toHaveValue('https://news.ycombinator.com/item?id=111');
    await expect(panel.getByLabel('Source 2',{exact:true})).toHaveValue('https://news.ycombinator.com/item?id=222');
    await expect(panel.locator('li').filter({has:page.getByRole('link',{name:'Discussion 333',exact:true})}).getByRole('button',{name:'Use source'})).toBeDisabled();
    const start=panel.getByRole('button',{name:'Start bounded work'});
    await expect(start).toBeDisabled();
    await panel.getByRole('checkbox',{name:/I understand usage is measured/}).check();
    await start.click();expect(created).toBe(1);
    const checkpoint=panel.getByRole('region',{name:'First request usage checkpoint'});
    await expect(checkpoint).toContainText('800 tokens reported');
    const release=checkpoint.getByRole('button',{name:'Release remaining pilot requests'});
    await expect(release).toBeDisabled();
    await checkpoint.getByRole('checkbox',{name:/I reviewed this usage/}).check();
    await release.click();
    await expect(checkpoint).toHaveCount(0);expect(released).toBe(1);
    runway.project.request_allowance=1;runway.project.status='needs_review';runway.project.deadline_at=Date.now()/1000-1;runway.project.version++;
    await page.reload();
    await page.getByRole('navigation',{name:'Main views'}).getByRole('button',{name:'Work',exact:true}).click();
    await expect(checkpoint).toBeVisible();
    await checkpoint.getByRole('checkbox',{name:/I reviewed this usage/}).check();
    await expect(release).toBeDisabled();expect(released).toBe(1);
  }finally{
    const sessionResponse=await page.request.get(origin+'/api/session');
    if(sessionResponse.ok()){
      const session=await sessionResponse.json();
      await page.request.post(origin+'/api/devices/'+session.id+'/revoke',{headers:{Origin:origin,'X-CSRF':session.csrf},data:{}});
    }
  }
});
