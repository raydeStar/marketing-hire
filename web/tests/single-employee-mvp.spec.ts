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

test('the shell is a single-employee workspace with no meeting controls or records',async({page,request,baseURL})=>{
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Build the brand'}],agents:[{id:'marketing-main',name:'Marketing agent',role:'Research and drafts',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  const profile={id:'marketing',display_name:'Marketing agent',product_summary:'Personal brand selling marketing agents',audience:'',voice:'',goals:'Learn which message to test',guardrails:'Evidence before claims',channels:'',version:6,updated_at:1780000000};
  const meetingCalls:string[]=[];
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  // Meetings are paused server-side; the shell must not read or offer them.
  // Past meeting records may be read (to keep their tasks out of today's work); nothing may start or change one.
  await page.route(/\/api\/meetings(?:\/.*)?$/,route=>{if(route.request().method()==='GET')return route.fulfill({json:[]});meetingCalls.push(route.request().method()+' '+route.request().url());return route.fulfill({status:409,json:{error:'Meetings paused'}});});
  await page.route('**/api/marketing/**',route=>{
    const pathname=new URL(route.request().url()).pathname;
    if(pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:'Marketing agent',model:'fixture',sessionKey:'agent:main:marketing-business-main'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,profile,tasks:[],messages:[],requests:[],evidence:[],drafts:[],ownerDecisions:[],activity:[],runway:null}});
    if(pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    return route.fulfill({status:404,json:{error:'Unexpected marketing request '+pathname}});
  });
  await page.setViewportSize({width:1440,height:950});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!);

  const views=page.getByRole('navigation',{name:'Main views'});
  await expect(views.getByRole('button')).toHaveText(['Chat','Search','Library','Team']);
  await expect(page.getByRole('heading',{name:'What should Marketing agent work on?'})).toBeVisible();
  // The cockpit's morning meeting is a chat prompt, not the paused meeting workflow.
  await expect(page.getByRole('complementary',{name:'Cockpit'}).getByRole('button',{name:'Start',exact:true})).toBeVisible();
  for(const name of ['Start meeting','Develop plan & review','Approve scope & assign work','Add CEO agent'])
    await expect(page.getByRole('button',{name,exact:true})).toHaveCount(0);

  await views.getByRole('button',{name:'Team',exact:true}).click();
  await page.getByRole('navigation',{name:'Team sections'}).getByRole('button',{name:'AI employees'}).click();
  const employees=page.getByRole('region',{name:'AI employees'}).getByRole('button',{name:/Online|Setup needed/});
  await expect(employees).toHaveCount(1);
  await expect(employees).toContainText('Marketing agent');
  await expect(page.getByRole('main',{name:'Team'})).not.toContainText('CEO');

  await page.goto('/?pane=work');
  await expect(page.getByRole('region',{name:'Team tasks'})).toBeVisible();
  await expect(page.getByText('Past meeting work')).toHaveCount(0);
  await expect(page.getByRole('region',{name:'Meeting history'})).toHaveCount(0);
  await page.screenshot({path:'../artifacts/single-employee-mvp-desktop.png',fullPage:true});
  expect(meetingCalls).toEqual([]);

  await page.setViewportSize({width:390,height:844});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
});
