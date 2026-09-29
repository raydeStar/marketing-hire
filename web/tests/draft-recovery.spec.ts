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

// The employee's records are stubbed; the drafts live in this browser and are never sent without Enter or Send.
test('unfinished chat and task drafts survive reload, rail navigation and a disconnected employee, and are never sent on their own',async({page,request,baseURL})=>{
  test.setTimeout(90000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const now=Math.floor(Date.now()/1000);
  let messages:any[]=[],requests:any[]=[],connected=true,sessionKey='';const sent:any[]=[];
  const task={id:'t-7',title:'Plan the spring newsletter',status:'ready',priority:'normal',next_action:'Outline three sections.',action_state:'agent_ready',blocker:null,conversation_key:'task-t-7',version:1,updated_at:now};
  await page.route('**/api/marketing/state',async route=>{
    const json=await (await route.fetch()).json();sessionKey=json.employee.sessionKey;
    return route.fulfill({json:{...json,connection:{status:connected?'connected':'disconnected'},chatBlockedReason:null,runway:null,drafts:[],tasks:[task],taskStoreAvailable:true,messages,requests}});
  });
  await page.route('**/api/marketing/chat',async route=>{
    const body=route.request().postDataJSON();sent.push(body);
    messages=[...messages,{id:body.requestId+':user',sessionKey,role:'user',content:body.content,createdAt:now},{id:body.requestId+':assistant',sessionKey,role:'assistant',content:'Noted.',createdAt:now+1}];
    requests=[...requests,{requestId:body.requestId,sessionKey,status:'succeeded'}];
    return route.fulfill({json:{ok:true}});
  });
  await page.route('**/api/weekly',route=>route.request().method()==='GET'?route.fulfill({json:{settings:{enabled:false,timeZone:'UTC',planDay:1,planTime:'08:00',updateDay:5,updateTime:'16:00',emailDraft:false},latest:[]}}):route.continue());
  await page.route('**/api/shifts',route=>route.request().method()==='GET'?route.fulfill({json:{runtime:'scripted',live:false,stages:[],current:null,recent:[]}}):route.continue());

  await launch(page,request,baseURL!,'pane=chat');
  const chat=page.getByRole('region',{name:/Conversation with/});
  const composer=chat.getByLabel('Message to marketing employee');
  const draft='Keep this unfinished thought\nand its second line.';
  await composer.fill(draft);

  // A reload brings the draft back, line breaks included, and sends nothing.
  await page.reload();
  await expect(composer).toHaveValue(draft);

  // So does leaving for the Library and coming back through the rail.
  const rail=page.getByRole('navigation',{name:'Main views'});
  await rail.getByRole('button',{name:'Library',exact:true}).click();
  await expect(page.getByRole('main',{name:'Library'})).toBeVisible();
  await rail.getByRole('button',{name:/^Chat/}).click();
  await expect(composer).toHaveValue(draft);

  // A task's own conversation (its tab; Discuss with Zero now tags the task in the main chat) keeps its own draft, apart from the main chat's.
  await page.goto('/?pane=chat&open=task%3At-7');
  const window=page.locator('.fe-split-pane');
  await window.getByRole('navigation',{name:'Task detail views'}).getByRole('button',{name:/Conversation/}).click();
  const discussion=page.getByRole('region',{name:'Discussion for Plan the spring newsletter'});
  const taskComposer=discussion.getByLabel('Message to marketing employee');
  await expect(taskComposer).toHaveValue('');
  await taskComposer.fill('Ask about the newsletter length');
  await expect(composer).toHaveValue(draft);
  await page.reload();
  await window.getByRole('navigation',{name:'Task detail views'}).getByRole('button',{name:/Conversation/}).click();
  await expect(taskComposer).toHaveValue('Ask about the newsletter length');
  await expect(composer).toHaveValue(draft);

  // While the employee is disconnected the box is read-only, and the draft is still there.
  connected=false;
  await page.goto('/?pane=chat');
  await expect(composer).toBeDisabled();
  await expect(composer).toHaveValue(draft);
  await expect(chat).toContainText('Chat is unavailable until the employee reconnects. Your draft is saved.');
  expect(sent).toHaveLength(0);

  // Sent once, on Enter, and the saved draft goes with it.
  connected=true;
  await page.reload();
  await composer.press('Enter');
  await expect(chat.locator('article.fe-msg.assistant')).toContainText('Noted.');
  expect(sent).toHaveLength(1);
  expect(sent[0].content).toBe(draft);
  expect(sent[0].taskId).toBeUndefined();
  await page.reload();
  await expect(composer).toHaveValue('');
  expect(sent).toHaveLength(1);

  await page.setViewportSize({width:390,height:844});
  await composer.fill('A thought kept on a phone');
  await page.reload();
  await expect(composer).toHaveValue('A thought kept on a phone');
  await expect(composer).toBeInViewport();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  expect(sent).toHaveLength(1);
});
