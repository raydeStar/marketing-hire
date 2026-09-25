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

// A connected LinkedIn channel and an approved LinkedIn draft, stubbed: the owner schedules it for 7:00 AM and sees it on the calendar.
test('an approved draft is scheduled for a morning slot in the owner’s time zone and shows on the content calendar',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const draft={id:41,channel:'LinkedIn',destination:'https://www.linkedin.com/feed/',content:'Founders keep telling us follow-through is the hard part. https://example.com/?utm_source=linkedin&utm_campaign=launch',rationale:'Leads with the problem.',rules_url:'UNVERIFIED',status:'approved',revision:1,digest:'d'.repeat(64),decided_by:'Owner'};
  const connection={id:'li-1',kind:'linkedin',status:'ready',account:'Mark Hall',address:null,createdAt:new Date().toISOString(),expiresAt:new Date(Date.now()+50*86400000).toISOString(),saveAsDraft:false};
  const publications:any[]=[];
  let sent:any=null;
  const kinds=[{kind:'linkedin',name:'LinkedIn',channels:['linkedin'],limit:3000},{kind:'email',name:'Email (Gmail drafts)',channels:['email'],limit:null}];
  await page.route('**/api/publishing',route=>route.fulfill({json:{redirectUri:'http://127.0.0.1:5190/api/publishing/oauth/callback',kinds,connections:[connection],publications}}));
  await page.route('**/api/publishing/drafts/41',async route=>{
    sent=route.request().postDataJSON();
    const made={id:'p-1',draftId:41,connectionId:'li-1',kind:'linkedin',status:'scheduled',scheduledFor:sent.at,publishedAt:null,url:null,error:null};
    publications.push(made);return route.fulfill({json:made});
  });
  await page.route('**/api/marketing/state',async route=>{
    const response=await route.fetch();const json=await response.json();
    return route.fulfill({json:{...json,drafts:[draft]}});
  });
  await launch(page,request,baseURL!,'pane=work&open=draft:41');
  const bar=page.getByRole('region',{name:'Publishing'}).or(page.locator('[aria-label="Publishing"]')).first();
  await expect(page.locator('.fe-window')).toContainText('Approved · ready to post');
  await bar.getByLabel('Schedule').check();
  await bar.getByRole('button',{name:'Tomorrow 7:00 AM'}).click();
  const expected=new Date();expected.setDate(expected.getDate()+1);expected.setHours(7,0,0,0);
  await expect(bar.getByLabel('When')).toHaveValue(`${expected.getFullYear()}-${String(expected.getMonth()+1).padStart(2,'0')}-${String(expected.getDate()).padStart(2,'0')}T07:00`);
  await expect(bar).toContainText('Times are in');
  page.once('dialog',dialog=>{expect(dialog.message()).toContain('as Mark Hall');void dialog.accept();});
  await bar.getByRole('button',{name:'Schedule',exact:true}).click();
  await expect(bar).toContainText('Scheduled for');
  // The host receives the exact instant (UTC) of 7:00 local time, with the reviewed digest.
  expect(new Date(sent.at).getTime()).toBe(expected.getTime());
  expect(sent).toMatchObject({connectionId:'li-1',digest:'d'.repeat(64)});
  await page.getByRole('button',{name:'Close'}).click();
  const calendar=page.getByRole('region',{name:'Content calendar'});
  await expect(calendar).toContainText('LinkedIn · Mark Hall');
  await expect(calendar).toContainText('Scheduled');
  await expect(calendar).toContainText('7:00');
});
