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
const quiet=(page:Page)=>page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});

test('a reply goes out from the post itself: X’s reply box, or the post with the reply copied',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});await quiet(page);
  await page.context().grantPermissions(['clipboard-read','clipboard-write'],{origin:baseURL!});
  const drafts=[{id:81,channel:'X',destination:'https://x.com/pat/status/1234567',content:'Happy to share how we handle approvals: one click per post.',rationale:'r',rules_url:'UNVERIFIED',status:'approved',revision:1,digest:'a'.repeat(64),created:1,decided_at:1},
    {id:82,channel:'Bluesky',destination:'https://bsky.app/profile/pat.bsky.social/post/3kxyz',content:'We keep a receipt of every post it drafts.',rationale:'r',rules_url:'UNVERIFIED',status:'approved',revision:1,digest:'b'.repeat(64),created:1,decided_at:1}];
  await page.route('**/api/marketing/state',async route=>{const json=await (await route.fetch()).json();return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,drafts,requests:[]}});});
  // A Bluesky connection exists, but a reply never goes through it.
  await page.route('**/api/publishing',route=>route.fulfill({json:{redirectUri:'x',kinds:[{kind:'bluesky',name:'Bluesky',channels:['bluesky'],limit:300},{kind:'x',name:'X',channels:['x'],limit:280}],
    connections:[{id:'c1',kind:'bluesky',status:'ready',account:'@me.bsky.social',address:'https://bsky.social',createdAt:new Date().toISOString(),expiresAt:null,saveAsDraft:false}],publications:[]}}));
  await page.route('**/api/publishing/drafts/*/assist',route=>route.fulfill({json:{id:'as',draftId:81,connectionId:'',kind:'x',status:'awaiting_link',scheduledFor:null,publishedAt:null,url:null,error:null,createdAt:new Date().toISOString()}}));
  await page.context().route('https://x.com/**',route=>route.fulfill({body:'<title>X</title>',contentType:'text/html'}));
  await page.context().route('https://bsky.app/**',route=>route.fulfill({body:'<title>Bluesky</title>',contentType:'text/html'}));
  await launch(page,request,baseURL!,'pane=work&open=draft:81');
  let bar=page.locator('[aria-label="Publishing"]');
  await expect(bar).toContainText('reply box on that post');
  const [composer]=await Promise.all([page.waitForEvent('popup'),bar.getByRole('button',{name:'Reply yourself on X'}).click()]);
  expect(composer.url()).toBe('https://x.com/intent/post?in_reply_to=1234567&text='+encodeURIComponent(drafts[0].content));
  await composer.close();

  await page.evaluate(()=>{history.pushState(history.state,'','/?pane=work&open=draft:82');dispatchEvent(new PopStateEvent('popstate'));});
  bar=page.locator('[aria-label="Publishing"]');
  await expect(bar).toContainText('the reply is copied to your clipboard');
  await expect(bar.getByRole('button',{name:'Publish from here instead'})).toHaveCount(0);
  const [post]=await Promise.all([page.waitForEvent('popup'),bar.getByRole('button',{name:'Reply yourself on Bluesky'}).click()]);
  expect(post.url()).toBe('https://bsky.app/profile/pat.bsky.social/post/3kxyz');
  expect(await page.evaluate(()=>navigator.clipboard.readText())).toBe(drafts[1].content);
});

test('a mention worth answering becomes a reply task for the next shift',async({page,request,baseURL})=>{
  await page.setViewportSize({width:1440,height:900});await quiet(page);
  const mention={id:'m1',topic:'ai marketing',source:'Bluesky',title:'Anyone tried an AI marketing employee that asks before posting?',snippet:'Anyone tried an AI marketing employee that asks before posting?',
    url:'https://bsky.app/profile/pat.bsky.social/post/3kxyz',publishedAt:new Date().toISOString(),sentiment:'neutral'};
  const news={...mention,id:'m2',source:'Google News',title:'AI agents in marketing',url:'https://news.google.com/rss/articles/abc'};
  await page.route('**/api/listening',route=>route.fulfill({json:{topics:['ai marketing'],feeds:[],lastScanAt:new Date().toISOString(),errors:[],stats:[],mentions:[mention,news]}}));
  let task:any=null;
  await page.route('**/api/marketing/tasks',route=>{task=route.request().postDataJSON();return route.fulfill({json:{id:'t1'}});});
  await launch(page,request,baseURL!,'pane=work');
  const section=page.locator('section[aria-label="Listening"]');
  await section.scrollIntoViewIfNeeded();
  await expect(section.getByRole('button',{name:/Ask for a reply/})).toHaveCount(1);
  await section.getByRole('button',{name:`Ask for a reply to ${mention.title}`}).click();
  await expect(section).toContainText('drafts the reply on its next shift');
  expect(task).toMatchObject({status:'ready',action_state:'agent_ready',title:'Reply on Bluesky: '+mention.title});
  expect(task.next_action).toContain('destination is exactly https://bsky.app/profile/pat.bsky.social/post/3kxyz');
});
