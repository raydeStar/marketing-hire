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

// No X connection: the owner posts through X's own composer with the approved text, then pastes the link back.
test('an approved X post goes out through X’s own composer and its link is recorded',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const text='Shifts, not prompts. https://example.com/?utm_source=x&utm_campaign=launch';
  const draft={id:61,channel:'X',destination:'https://x.com/home',content:text,rationale:'r',rules_url:'UNVERIFIED',status:'approved',revision:1,digest:'e'.repeat(64),created:1,decided_at:1};
  const publications:any[]=[];let assist:any=null,link:any=null;
  await page.route('**/api/marketing/state',async route=>{const json=await (await route.fetch()).json();return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,drafts:[draft],requests:[]}});});
  await page.route('**/api/publishing',route=>route.fulfill({json:{redirectUri:'x',kinds:[{kind:'x',name:'X',channels:['x','twitter'],limit:280}],connections:[],publications}}));
  await page.route('**/api/publishing/drafts/61/assist',async route=>{assist=route.request().postDataJSON();const made={id:'as-1',draftId:61,connectionId:'',kind:'x',status:'awaiting_link',scheduledFor:null,publishedAt:null,url:null,error:null,createdAt:new Date().toISOString(),channel:'X',excerpt:text};publications.push(made);return route.fulfill({json:made});});
  await page.route('**/api/publishing/publications/as-1/link',async route=>{link=route.request().postDataJSON();Object.assign(publications[0],{status:'published',url:link.url,publishedAt:new Date().toISOString()});draft.status='posted';return route.fulfill({json:publications[0]});});
  await page.context().route('https://x.com/**',route=>route.fulfill({body:'<title>X</title>',contentType:'text/html'}));
  await launch(page,request,baseURL!,'pane=work&open=draft:61');
  const bar=page.locator('[aria-label="Publishing"]');
  await expect(bar).toContainText('Opens X’s own composer');
  const [composer]=await Promise.all([page.waitForEvent('popup'),bar.getByRole('button',{name:'Post it yourself on X'}).click()]);
  expect(composer.url()).toBe('https://x.com/intent/post?text='+encodeURIComponent(text));
  await composer.close();
  await expect.poll(()=>assist).toMatchObject({digest:'e'.repeat(64),at:null});
  await expect(bar).toContainText('then paste the link here');
  await bar.getByLabel('Link to the live post').fill('https://x.com/mark/status/1234567');
  await bar.getByRole('button',{name:'It’s posted'}).click();
  await expect.poll(()=>link?.url).toBe('https://x.com/mark/status/1234567');
  await expect(bar).toContainText('Published to X');
});

test('settings shows what is ready for live work and where to set up the Google app',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'view=settings');
  const checklist=page.getByRole('region',{name:'Go-live checklist'});
  await expect(checklist).toContainText('Objectives and positioning');
  await expect(checklist).toContainText('The scripted stand-in is running');
  await expect(checklist).toContainText('approved posts can always be posted through each network’s own composer');
  // One click: working hours, limits and the weekly rhythm, and the checklist marks them done.
  await checklist.getByRole('button',{name:'Put it to work'}).click();
  await expect(checklist.getByLabel('Put it to work')).toContainText('At work weekdays, 09:00–17:00');
  await expect(checklist).toContainText('The Monday plan and Friday update are automatic.');
  const google=page.getByRole('region',{name:'Google app'});
  await expect(google).toContainText('Google Analytics Admin API');
  await expect(google.getByLabel('Import Google credentials JSON')).toBeAttached();
  await checklist.getByRole('button',{name:'Set them'}).click();
  await expect(page).toHaveURL(/open=brief%3Aobjectives|open=brief:objectives/);
});
