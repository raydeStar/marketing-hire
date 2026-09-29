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

// The employee's records are stubbed; everything the owner types and clicks goes through the real UI.
test('chat keeps a failed message, retries it once under the same request, and says plainly when a reply is missing, pending or paused',async({page,context,request,baseURL})=>{
  test.setTimeout(90000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const now=Math.floor(Date.now()/1000);
  let messages:any[]=[],requests:any[]=[],blocked:string|null=null,sessionKey='';
  let failNext=true;const sent:any[]=[],tasks:any[]=[],saved:any[]=[];
  await page.route('**/api/marketing/state',async route=>{
    const json=await (await route.fetch()).json();sessionKey=json.employee.sessionKey;
    return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:blocked,runway:null,drafts:[],messages,requests}});
  });
  await page.route('**/api/marketing/chat',async route=>{
    const body=route.request().postDataJSON();sent.push(body);
    if(failNext){failNext=false;return route.fulfill({status:502,json:{error:'The fictional runtime did not answer.'}});}
    messages=[...messages,{id:body.requestId+':user',sessionKey,role:'user',content:body.content,createdAt:now},
      {id:body.requestId+':assistant',sessionKey,role:'assistant',content:'Here is a short plan for the week.\n\n1. Interview two customers.\n2. Draft one post.',createdAt:now+1}];
    requests=[...requests,{requestId:body.requestId,sessionKey,status:'succeeded'}];
    return route.fulfill({json:{ok:true}});
  });
  await page.route('**/api/marketing/tasks',async route=>{if(route.request().method()!=='POST')return route.continue();tasks.push(route.request().postDataJSON());return route.fulfill({json:{id:'t-1'}});});
  await page.route('**/api/company-wiki',async route=>{if(route.request().method()!=='PUT')return route.continue();saved.push(route.request().postDataJSON());return route.fulfill({json:{id:'w-1',version:1}});});
  // No status cards from other records: this check is about the conversation itself.
  await page.route('**/api/weekly',route=>route.request().method()==='GET'?route.fulfill({json:{settings:{enabled:false,timeZone:'UTC',planDay:1,planTime:'08:00',updateDay:5,updateTime:'16:00',emailDraft:false},latest:[]}}):route.continue());
  await page.route('**/api/shifts',route=>route.request().method()==='GET'?route.fulfill({json:{runtime:'scripted',live:false,stages:[],current:null,recent:[]}}):route.continue());

  await launch(page,request,baseURL!,'pane=chat');
  const chat=page.getByRole('region',{name:/Conversation with/});
  const composer=chat.getByLabel('Message to marketing employee');
  const sendButton=chat.getByRole('button',{name:'Send',exact:true});
  await expect(composer).toBeEnabled();

  // Shift+Enter is a new line; nothing is sent until Enter.
  await composer.fill('Plan our week');await composer.press('Shift+Enter');await composer.pressSequentially('around the launch');
  await expect(composer).toHaveValue('Plan our week\naround the launch');
  expect(sent).toHaveLength(0);

  // A failed send keeps the words in the box and offers a retry that cannot send twice.
  await composer.press('Enter');
  const failure=chat.getByRole('alert').filter({hasText:/didn’t answer/});
  await expect(failure).toBeVisible();
  await expect(failure).toContainText('Your message is still in the box.');
  await expect(composer).toHaveValue('Plan our week\naround the launch');
  await failure.getByText('Details',{exact:true}).click();
  await expect(failure).toContainText('The fictional runtime did not answer.');
  await failure.getByRole('button',{name:'Try again',exact:true}).click();
  await expect(chat.locator('article.fe-msg.assistant')).toContainText('Here is a short plan for the week.');
  expect(sent).toHaveLength(2);
  expect(sent[1].requestId).toBe(sent[0].requestId);
  expect(sent[1].content).toBe('Plan our week\naround the launch');
  await expect(chat.locator('article.fe-msg.user')).toHaveCount(1);
  await expect(composer).toHaveValue('');
  await expect(failure).toHaveCount(0);

  // A reply becomes lasting work in one click: copied, kept in the Library, or made a task.
  const reply=chat.locator('article.fe-msg.assistant').last();
  await context.grantPermissions(['clipboard-read','clipboard-write']);
  await reply.getByRole('button',{name:'Copy'}).click();
  await expect(reply.getByRole('button',{name:'Copied'})).toBeVisible();
  expect(await page.evaluate(()=>navigator.clipboard.readText())).toContain('Interview two customers.');
  await reply.getByRole('button',{name:'Save to Library'}).click();
  await expect(reply.getByRole('button',{name:'Saved to the Library'})).toBeVisible();
  expect(saved[0]).toMatchObject({title:'Here is a short plan for the week',kind:'fact',status:'draft'});
  await reply.getByRole('button',{name:'Make a task'}).click();
  await expect(reply.getByRole('button',{name:'Task created'})).toBeVisible();
  expect(tasks[0]).toMatchObject({title:'Here is a short plan for the week',status:'ready'});

  // No reply came back: say so plainly, keep the box open for something new, and resend the original in one click
  // as a fresh request (the old one's outcome is already recorded, so reusing it would only replay that).
  messages=[...messages,{id:'lost:user',sessionKey,role:'user',content:'Did the brief reach you?',createdAt:now+10}];
  requests=[...requests,{requestId:'lost',sessionKey,status:'unknown',error:'The operation was canceled.'}];
  await page.reload();
  const unconfirmed=chat.getByRole('status').filter({hasText:'No reply came back to your last message'});
  await expect(unconfirmed).toBeVisible();
  await expect(unconfirmed).toContainText('took too long to answer.');
  await expect(chat.locator('article.fe-msg.user').last()).toContainText('No reply');
  await composer.fill('Something new');
  await expect(sendButton).toBeEnabled();
  await composer.fill('');
  await unconfirmed.getByRole('button',{name:'Send it again',exact:true}).click();
  await expect(unconfirmed).toHaveCount(0);
  expect(sent).toHaveLength(3);
  expect(sent[2].content).toBe('Did the brief reach you?');
  expect(sent[2].requestId).not.toBe('lost');
  await expect(composer).toHaveValue('');

  // Still answering: the owner can write, but not send a second message over the first.
  requests=[...requests,{requestId:'slow',sessionKey,status:'pending'}];
  await page.reload();
  await expect(chat.getByRole('status').filter({hasText:'is still writing a reply'})).toBeVisible();
  await expect(chat.getByText('is writing…')).toBeVisible();
  await composer.fill('One more thing');
  await expect(sendButton).toBeDisabled();
  await composer.press('Enter');
  expect(sent).toHaveLength(3);
  await composer.fill('');

  // Paused: the reason is shown and the next message can be written now, sent later.
  requests=requests.filter(item=>item.requestId!=='slow');blocked='A fictional maintenance window is running.';
  await page.reload();
  const paused=chat.getByRole('status').filter({hasText:'is busy for a moment'});
  await expect(paused).toContainText('A fictional maintenance window is running.');
  await composer.fill('Next: the launch post');
  await expect(sendButton).toBeDisabled();
  await composer.press('Enter');
  expect(sent).toHaveLength(3);

  // The phone layout keeps the composer on screen and the page from scrolling sideways.
  await page.setViewportSize({width:390,height:844});
  await expect(composer).toBeInViewport();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

// What the owner sends shows in the thread at once, with the employee writing beneath it, not when the reply arrives.
test('a sent message shows at once, before the reply',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const now=Math.floor(Date.now()/1000);
  let messages:any[]=[],requests:any[]=[],sessionKey='',release=()=>{};
  const held=new Promise<void>(resolve=>{release=resolve;});
  await page.route('**/api/marketing/state',async route=>{
    const json=await (await route.fetch()).json();sessionKey=json.employee.sessionKey;
    return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,runway:null,drafts:[],messages,requests}});
  });
  await page.route('**/api/marketing/chat',async route=>{
    const body=route.request().postDataJSON();
    await held;
    messages=[{id:body.requestId+':user',sessionKey,role:'user',content:body.content,createdAt:now},{id:body.requestId+':assistant',sessionKey,role:'assistant',content:'Done: weekdays, 9 to 5.',createdAt:now+1}];
    requests=[{requestId:body.requestId,sessionKey,status:'succeeded'}];
    return route.fulfill({json:{ok:true}});
  });
  await page.route('**/api/weekly',route=>route.request().method()==='GET'?route.fulfill({json:{settings:{enabled:false,timeZone:'UTC',planDay:1,planTime:'08:00',updateDay:5,updateTime:'16:00',emailDraft:false},latest:[]}}):route.continue());
  await page.route('**/api/shifts',route=>route.request().method()==='GET'?route.fulfill({json:{runtime:'scripted',live:false,stages:[],current:null,recent:[]}}):route.continue());

  await launch(page,request,baseURL!,'pane=chat');
  const chat=page.getByRole('region',{name:/Conversation with/});
  const composer=chat.getByLabel('Message to marketing employee');
  await expect(composer).toBeEnabled();
  await composer.fill('Working hours should be 9 to 5');await composer.press('Enter');
  // The reply is still being written: the message is already in the thread, once, with the employee writing beneath it.
  await expect(chat.locator('article.fe-msg.user')).toContainText('Working hours should be 9 to 5');
  await expect(chat.getByLabel(/is writing/)).toBeVisible();
  release();
  await expect(chat.locator('article.fe-msg.assistant')).toContainText('Done: weekdays, 9 to 5.');
  await expect(chat.locator('article.fe-msg.user')).toHaveCount(1);
});

// Tagging (@): a picker as you type, a chip on the message, and the item goes with it so the answer is about exactly that.
test('typing @ tags a document, and the tag goes with the message',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const now=Math.floor(Date.now()/1000);
  let messages:any[]=[],requests:any[]=[],sessionKey='';const sent:any[]=[];
  await page.route('**/api/marketing/state',async route=>{
    const json=await (await route.fetch()).json();sessionKey=json.employee.sessionKey;
    return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,runway:null,drafts:[],messages,requests}});
  });
  await page.route('**/api/company-wiki',route=>route.request().method()==='GET'?route.fulfill({json:[{id:'pos1',title:'Positioning one-pager',status:'draft',body:'',kind:'fact',version:1,scope:'company',scopeId:'company',author:'Marketing employee (shift)',updatedAt:new Date().toISOString()}]}):route.continue());
  await page.route('**/api/marketing/chat',async route=>{
    const body=route.request().postDataJSON();sent.push(body);
    messages=[{id:body.requestId+':user',sessionKey,role:'user',content:body.content,createdAt:now},{id:body.requestId+':assistant',sessionKey,role:'assistant',content:'Here is what I would change in it.',createdAt:now+1}];
    requests=[{requestId:body.requestId,sessionKey,status:'succeeded'}];
    return route.fulfill({json:{ok:true}});
  });
  await page.route('**/api/weekly',route=>route.request().method()==='GET'?route.fulfill({json:{settings:{enabled:false,timeZone:'UTC',planDay:1,planTime:'08:00',updateDay:5,updateTime:'16:00',emailDraft:false},latest:[]}}):route.continue());
  await page.route('**/api/shifts',route=>route.request().method()==='GET'?route.fulfill({json:{runtime:'scripted',live:false,stages:[],current:null,recent:[]}}):route.continue());

  await launch(page,request,baseURL!,'pane=chat');
  const chat=page.getByRole('region',{name:/Conversation with/});
  const composer=chat.getByLabel('Message to marketing employee');
  await expect(composer).toBeEnabled();
  await composer.pressSequentially('What would you change in @Posit');
  const picker=page.getByRole('listbox',{name:'Tag something'});
  await expect(picker.getByRole('option')).toContainText(['Positioning one-pager']);
  await composer.press('Enter');
  await expect(page.getByLabel('Tagged')).toContainText('@Positioning one-pager');
  await expect(composer).toHaveValue('What would you change in @Positioning one-pager ');
  await composer.pressSequentially('before I approve it?');await composer.press('Enter');
  await expect.poll(()=>sent.length).toBe(1);
  expect(sent[0].refs).toEqual(['wiki:pos1']);
  expect(sent[0].content).toBe('What would you change in @Positioning one-pager before I approve it?');
  await expect(page.getByLabel('Tagged')).toHaveCount(0);
});
