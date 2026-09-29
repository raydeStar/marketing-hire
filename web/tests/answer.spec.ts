import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?'?'+query:''}#launch=${(await issued.json()).ticket}`);
}

// Chip asked something it needs to go on: the question sits in the cockpit with a box to answer it; Enter sends the task back with the answer.
test('a question Chip asks is answered right in the cockpit, and the task goes back to it',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');localStorage.setItem('fe-getting-started-dismissed:*','yes');localStorage.setItem('fe-cockpit-open','yes');}catch{}});
  const task={id:'t-ask',title:'Homepage headline options',status:'needs_you',priority:'high',next_action:'Write three headline options.',action_state:'user_waiting',
    blocker:'Which audience should the headline speak to first: solo founders or small agencies?',conversation_key:'task:t-ask',version:3,updated_at:Math.floor(Date.now()/1000)};
  let saved:any=null;
  await page.route('**/api/marketing/state',async route=>{const json=await (await route.fetch()).json();return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,tasks:saved?[{...task,...saved,version:4}]:[task]}});});
  await page.route('**/api/marketing/tasks/t-ask',route=>{saved=route.request().postDataJSON();return route.fulfill({json:{...task,...saved,version:4}});});
  await launch(page,request,baseURL!,'pane=chat');
  const asks=page.getByRole('complementary',{name:'Cockpit'}).getByLabel('Needs your answer');
  await expect(asks).toContainText('solo founders or small agencies');
  const box=asks.getByLabel('Answer Chip');
  await box.fill('Solo founders first.');await box.press('Enter');
  await expect.poll(()=>saved).not.toBeNull();
  expect(saved).toMatchObject({version:3,status:'ready',action_state:'agent_ready',blocker:''});
  expect(saved.next_action).toContain('Write three headline options.');
  expect(saved.next_action).toContain('Solo founders first.');
  await expect(page.getByRole('complementary',{name:'Cockpit'}).getByLabel('Needs your answer')).toHaveCount(0);   // answered: it's back in Chip's hands
});
