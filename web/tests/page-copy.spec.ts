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
const quiet=(page:Page)=>page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');localStorage.setItem('fe-getting-started-dismissed:*','yes');}catch{}});

test('proposed page copy is read beside the live page, approved, then applied by the owner',async({page,request,baseURL})=>{
  await page.setViewportSize({width:1440,height:900});await quiet(page);
  await page.context().grantPermissions(['clipboard-read','clipboard-write'],{origin:baseURL!});
  const proposal={id:'p1',url:'https://acme.test/pricing',title:'Pricing page',before:'Pricing. Plans for teams. Contact sales.',after:'# Plans that ask first\n\nStarter is for founders doing their own marketing.',
    rationale:'Leads with approval.',status:'pending',createdAt:new Date().toISOString(),by:'Marketing employee (shift)',decidedAt:null,decidedBy:null,note:null,appliedUrl:null,appliedAt:null};
  let decided:any=null,applied:any=null;
  await page.route('**/api/page-proposals',route=>route.fulfill({json:{ownSite:'acme.test',proposals:[proposal]}}));
  await page.route('**/api/page-proposals/p1/decision',route=>{decided=route.request().postDataJSON();proposal.status=decided.decision;return route.fulfill({json:proposal});});
  await page.route('**/api/page-proposals/p1/applied',route=>{applied=route.request().postDataJSON();proposal.status='applied';proposal.appliedAt=new Date().toISOString();proposal.appliedUrl=applied.url;return route.fulfill({json:proposal});});
  await page.route('**/api/publishing',route=>route.fulfill({json:{redirectUri:'x',kinds:[],connections:[],publications:[]}}));
  await launch(page,request,baseURL!,'pane=work');
  await page.getByRole('tab',{name:'Listening',exact:true}).click();
  const section=page.locator('section[aria-label="Page changes"]');
  await section.scrollIntoViewIfNeeded();
  await section.getByRole('button',{name:/Pricing page/}).click();
  const view=page.getByRole('article',{name:'New copy for acme.test/pricing'});
  await expect(view.getByRole('region',{name:'Now'})).toContainText('Contact sales.');
  await expect(view.getByRole('region',{name:'Proposed'}).getByRole('heading',{name:'Plans that ask first'})).toBeVisible();
  await view.getByLabel(/Your reason/).fill('Good headline.');
  await view.getByRole('button',{name:'Approve'}).click();
  await expect.poll(()=>decided).toEqual({decision:'approved',note:'Good headline.'});
  await expect(view).toContainText('Nothing is changed on the live page from here.');
  await view.getByRole('button',{name:'Copy the new text'}).click();
  // The system clipboard may hand line breaks back as CRLF.
  expect((await page.evaluate(()=>navigator.clipboard.readText())).replace(/\r\n/g,'\n')).toBe(proposal.after);
  await view.getByLabel('Link to the updated page').fill('https://acme.test/pricing');
  await view.getByRole('button',{name:'It’s applied'}).click();
  await expect(view.getByRole('status')).toContainText('Applied');
  expect(applied).toEqual({url:'https://acme.test/pricing'});
});

test('a page on your own site with findings can be handed to the next shift for new copy',async({page,request,baseURL})=>{
  await page.setViewportSize({width:1440,height:900});await quiet(page);
  const result={site:'acme.test',at:new Date().toISOString(),pages:4,robots:true,sitemap:true,reportWikiId:null,issues:[
    {severity:'warning',check:'Description',url:'https://acme.test/pricing',detail:'There is no meta description.'},
    {severity:'error',check:'Broken link',url:'https://acme.test/',detail:'Links to https://acme.test/old, which answers 404.'}]};
  await page.route('**/api/site-audit',route=>route.fulfill({json:{sites:['acme.test','rival.test'],ownSite:'acme.test',latest:[result]}}));
  let task:any=null;
  await page.route('**/api/marketing/tasks',route=>{task=route.request().postDataJSON();return route.fulfill({json:{id:'t1'}});});
  await launch(page,request,baseURL!,'pane=work');
  await page.getByRole('tab',{name:'Listening',exact:true}).click();
  const section=page.locator('section[aria-label="Site check"]');
  await section.scrollIntoViewIfNeeded();
  // Only fixable page findings offer it; a broken link is fixed where the link is.
  await expect(section.getByRole('button',{name:/for me$/})).toHaveCount(1);
  await section.getByRole('button',{name:'Fix https://acme.test/pricing for me'}).click();
  await expect(section.getByRole('status')).toContainText('Chip starts on the fix right away');
  expect(task.title).toBe('New copy for acme.test/pricing');
  expect(task.next_action).toContain('as a page deliverable (page: https://acme.test/pricing)');
  expect(task.next_action).toContain('Description: There is no meta description.');
  expect(task.next_action.length).toBeLessThan(1000);
});
