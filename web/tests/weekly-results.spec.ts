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

// Stubbed records; the clicks go through the real UI.
test('results show on the calendar, the weekly update is one click, and a draft becomes versions for other channels',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const now=Math.floor(Date.now()/1000);
  const draft={id:52,channel:'LinkedIn',destination:'https://www.linkedin.com/feed/',content:'Shifts, not prompts. https://example.com/?utm_source=linkedin&utm_campaign=b',rationale:'r',rules_url:'UNVERIFIED',status:'posted',revision:1,digest:'c'.repeat(64),created:now-90000,decided_at:now-86000};
  const published={id:'p-9',draftId:52,connectionId:'bs-1',kind:'bluesky',status:'published',scheduledFor:null,publishedAt:new Date(Date.now()-86000000).toISOString(),url:'https://bsky.app/profile/a/post/1',error:null,channel:'Bluesky',excerpt:'Shifts, not prompts.',
    results:{likes:12,reposts:3,replies:2,quotes:0,impressions:null,visits:17,checkedAt:new Date().toISOString(),note:null}};
  const docs:any[]=[];let chat:any=null;
  await page.route('**/api/marketing/state',async route=>{const json=await (await route.fetch()).json();return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,drafts:[draft],requests:[]}});});
  await page.route('**/api/publishing',route=>route.fulfill({json:{redirectUri:'x',kinds:[{kind:'bluesky',name:'Bluesky',channels:['bluesky'],limit:300}],connections:[{id:'bs-1',kind:'bluesky',status:'ready',account:'@mark.bsky.social',address:'https://bsky.social',createdAt:new Date().toISOString(),expiresAt:null,saveAsDraft:false}],publications:[published]}}));
  await page.route('**/api/weekly',route=>route.request().method()==='GET'?route.fulfill({json:{settings:{enabled:false,timeZone:'UTC',planDay:1,planTime:'08:00',updateDay:5,updateTime:'16:00',emailDraft:false},latest:docs}}):route.continue());
  await page.route('**/api/weekly/update',async route=>{const doc={kind:'update',week:'2026-W39',wikiId:'wk1',title:'Weekly update: week of Sep 21',at:new Date().toISOString(),emailUrl:null};docs.push(doc);return route.fulfill({json:doc});});
  await page.route('**/api/marketing/chat',async route=>{chat=route.request().postDataJSON();return route.fulfill({json:{requestId:chat.requestId,status:'succeeded',reply:'On it.'}});});

  await launch(page,request,baseURL!,'pane=work');
  // Results: the channel's counts and visits from the tracking link.
  const calendar=page.getByRole('region',{name:'Content calendar'});
  await expect(calendar.getByLabel('12 likes, 3 reposts, 2 replies, 17 visits')).toBeVisible();
  // This week: write the update now; it opens.
  const week=page.getByRole('region',{name:'This week'});
  await expect(week).toContainText('No weekly update yet');
  await week.getByRole('button',{name:'Write now'}).nth(1).click();
  await expect(page).toHaveURL(/open=wiki%3Awk1|open=wiki:wk1/);
  await page.getByRole('button',{name:'Close',exact:true}).click();
  await expect(week).toContainText('Weekly update: week of Sep 21');
  // Versions: from the draft, pick channels; the request goes to the employee in chat.
  await page.goto('/?pane=work&open=draft:52');
  await page.getByRole('button',{name:'Versions for other channels…'}).click();
  const dialog=page.getByRole('dialog',{name:'Versions for other channels'});
  await dialog.getByText('Mastodon',{exact:true}).click(); // X and Bluesky are preselected
  await dialog.getByLabel(/Anything to change/).fill('shorter for X');
  await dialog.getByRole('button',{name:/Ask for \d versions?/}).click();
  await expect.poll(()=>chat?.content||'').toContain('Please adapt draft #52 (LinkedIn) into new drafts for X, Bluesky, Mastodon');
  expect(chat.content).toContain('Adapted from draft #52');expect(chat.content).toContain('Also: shorter for X');
  expect(chat.timeZone).toBeTruthy();
});
