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

// The employee's records are stubbed; everything the owner clicks goes through the real UI.
test('chat tells the owner what happened and answers with one click: approve, schedule, open anywhere',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const now=Math.floor(Date.now()/1000);
  const seven=new Date();seven.setDate(seven.getDate()+1);seven.setHours(7,0,0,0);
  const drafts:any[]=[
    {id:40,channel:'LinkedIn',destination:'https://www.linkedin.com/feed/',content:'We built an employee that asks before acting. https://example.com/?utm_source=linkedin&utm_campaign=a',rationale:'r',rules_url:'UNVERIFIED',status:'pending',revision:1,digest:'a'.repeat(64),created:now-600},
    {id:41,channel:'LinkedIn',destination:'https://www.linkedin.com/feed/',content:'Shifts, not prompts. https://example.com/?utm_source=linkedin&utm_campaign=b',rationale:'r',rules_url:'UNVERIFIED',status:'approved',revision:1,digest:'b'.repeat(64),created:now-7200,decided_at:now-3600}];
  const reply='I can put the approved post out tomorrow morning.\n\n```action\n{"type":"schedule","draftId":41,"at":"'+seven.toISOString()+'"}\n```\n```action\n{"type":"open","target":"section:calendar","label":"Open the content calendar"}\n```';
  const decisions:any[]=[];let published:any=null;
  await page.route('**/api/marketing/state',async route=>{
    const json=await (await route.fetch()).json();
    const sessionKey=json.employee.sessionKey;
    return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,drafts,requests:[],
      messages:[{id:'m-user',sessionKey,role:'user',content:'Can we get the shifts post out tomorrow at 7?',createdAt:now-120},{id:'m-reply',sessionKey,role:'assistant',content:reply,createdAt:now-60}]}});
  });
  await page.route('**/api/marketing/drafts/40/decision',async route=>{decisions.push(route.request().postDataJSON());drafts[0]={...drafts[0],status:'approved',decided_at:now};return route.fulfill({json:drafts[0]});});
  await page.route('**/api/publishing',route=>route.fulfill({json:{redirectUri:'http://127.0.0.1:5190/api/publishing/oauth/callback',kinds:[{kind:'linkedin',name:'LinkedIn',channels:['linkedin'],limit:3000}],
    connections:[{id:'li-1',kind:'linkedin',status:'ready',account:'Mark Hall',address:null,createdAt:new Date().toISOString(),expiresAt:null,saveAsDraft:false}],publications:published?[published]:[]}}));
  await page.route('**/api/publishing/drafts/41',async route=>{const body=route.request().postDataJSON();published={id:'p-1',draftId:41,connectionId:'li-1',kind:'linkedin',status:'scheduled',scheduledFor:body.at,publishedAt:null,url:null,error:null,body};return route.fulfill({json:published});});
  const ended=new Date(Date.now()-900000).toISOString();
  await page.route('**/api/shifts',route=>route.request().method()==='GET'?route.fulfill({json:{runtime:'scripted',live:false,stages:['sense','prioritize','create','align','launch','measure','decide','institutionalize'],current:null,
    recent:[{id:'s-1',status:'completed',hours:1,cycleMinutes:60,turnBudget:8,turnsUsed:8,tokensUsed:0,runtime:'scripted',startedBy:'Owner',startedAt:ended,endsAt:ended,nextCycleAt:null,endedAt:ended,stopReason:'Done',cycles:[],reportWikiId:'rep1',handled:[],created:['wiki:a A','draft:40 LinkedIn draft #40'],decisions:['draft:40 LinkedIn draft #40']}]}}):route.continue());

  await launch(page,request,baseURL!,'pane=chat');
  const chat=page.getByRole('region',{name:/Conversation with/});
  // Action blocks never show as raw text; the reply's buttons say exactly what they do.
  await expect(chat).toContainText('I can put the approved post out tomorrow morning.');
  await expect(chat).not.toContainText('"type":"schedule"');
  // The shift report arrives as an update with a way in.
  const report=chat.getByRole('article',{name:/My shift is done: 2 pieces of work, 1 waiting on you/});
  await expect(report).toBeVisible();
  // The pending draft: approve it right here; the next update offers to put it out.
  const review=chat.getByRole('article',{name:/I drafted a LinkedIn post for you to review/});
  await review.getByRole('button',{name:'Approve'}).click();
  await expect.poll(()=>decisions.length).toBe(1);
  expect(decisions[0]).toMatchObject({decision:'approved',revision:1,digest:'a'.repeat(64)});
  // The reply's schedule card: confirmed, then sent as the exact instant of 7:00 local time.
  const card=chat.locator('.fe-action-card').filter({hasText:'Schedule LinkedIn draft #41'});
  await expect(card).toContainText('Posts to Mark Hall');
  page.once('dialog',dialog=>{expect(dialog.message()).toContain('as Mark Hall');void dialog.accept();});
  await card.getByRole('button',{name:'Schedule'}).click();
  await expect(card).toContainText('Scheduled for');
  expect(new Date(published.body.at).getTime()).toBe(seven.getTime());
  expect(published.body).toMatchObject({connectionId:'li-1',digest:'b'.repeat(64)});
  // Navigation: the calendar in Work, and the report beside the chat.
  await chat.locator('.fe-action-card').filter({hasText:'Open the content calendar'}).getByRole('button',{name:'Open'}).click();
  await expect(page.getByRole('region',{name:'Content calendar'})).toBeInViewport();
  await page.goto('/?pane=chat');
  await page.getByRole('article',{name:/My shift is done/}).getByRole('button',{name:'Open the report'}).click();
  await expect(page).toHaveURL(/open=wiki%3Arep1|open=wiki:rep1/);
  // Dismissed updates stay dismissed.
  await page.getByRole('article',{name:/My shift is done/}).getByRole('button',{name:'Dismiss this update'}).click();
  await expect(page.getByRole('article',{name:/My shift is done/})).toHaveCount(0);
});
