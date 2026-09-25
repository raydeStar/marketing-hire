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

// An image for a post, drawn in the browser from the draft's own words and saved to the Library.
test('a draft becomes a sized, on-brand image saved to the Library',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const draft={id:71,channel:'LinkedIn',destination:'https://www.linkedin.com/feed/',content:'Founders lose 6 hours a week to marketing busywork. First Employee drafts it and asks before anything goes out. https://example.com #marketing',
    rationale:'r',rules_url:'UNVERIFIED',status:'approved',revision:1,digest:'f'.repeat(64),created:1,decided_at:1};
  let upload='';
  await page.route('**/api/marketing/state',async route=>{const json=await (await route.fetch()).json();return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,drafts:[draft],requests:[]}});});
  await page.route('**/api/publishing',route=>route.fulfill({json:{redirectUri:'x',kinds:[],connections:[],publications:[]}}));
  await page.route('**/api/uploads',async route=>{upload=route.request().postDataBuffer()?.toString('latin1')||'';
    return route.fulfill({json:{id:'u1',name:'linkedin-number-1200x627.png',mediaType:'image/png',bytes:upload.length,sha256:'0'.repeat(64),created:new Date().toISOString(),version:1,archived:false}});});
  await launch(page,request,baseURL!,'pane=work&open=draft:71');
  await page.getByRole('button',{name:'Make an image…'}).click();
  const dialog=page.getByRole('dialog',{name:'Image for this post'});
  // A number in the draft leads; the words are the draft's first sentence, without the link or hashtag.
  await expect(dialog.getByRole('button',{name:'Key number'})).toHaveAttribute('aria-pressed','true');
  await expect(dialog.getByLabel('Key number')).toHaveValue('6 hours');
  await expect(dialog.getByLabel('Words')).toHaveValue('Founders lose 6 hours a week to marketing busywork.');
  const size=async()=>dialog.locator('canvas').evaluate((canvas:HTMLCanvasElement)=>[canvas.width,canvas.height]);
  expect(await size()).toEqual([1200,627]);
  await dialog.getByLabel('Size').selectOption('square');
  expect(await size()).toEqual([1080,1080]);
  await dialog.getByLabel('Size').selectOption('landscape');
  await dialog.getByLabel('Brand name').fill('First Employee');
  await dialog.getByRole('button',{name:'Save to Library'}).click();
  await expect(dialog.getByRole('status')).toContainText('Saved to Library → Media');
  expect(upload).toContain('filename="linkedin-number-1200x627.png"');
  expect(upload).toContain('Content-Type: image/png');
  expect(upload).toContain('\x89PNG');
  await expect(dialog.getByRole('link',{name:'Download'})).toHaveAttribute('download','linkedin-number-1200x627.png');
});

test('the SEC contact for research data is saved from Settings',async({page,request,baseURL})=>{
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  let saved:any=null;
  await page.route('**/api/settings/research-data',route=>{if(route.request().method()==='PUT'){saved=route.request().postDataJSON();return route.fulfill({json:{contact:saved.contact}});}return route.fulfill({json:{contact:''}});});
  await launch(page,request,baseURL!,'view=settings');
  const section=page.locator('section[aria-label="Research data"]');
  await expect(section).toContainText('Bureau of Labor Statistics');
  await section.getByLabel('Contact for SEC requests').fill('Acme Research ops@acme.test');
  await section.getByRole('button',{name:'Save'}).click();
  await expect(section).toContainText('Saved.');
  expect(saved).toEqual({contact:'Acme Research ops@acme.test'});
});
