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

test('the site check runs on a listed site and leads with what to fix',async({page,request,baseURL})=>{
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const result={site:'acme.test',at:new Date().toISOString(),pages:12,robots:true,sitemap:false,reportWikiId:'w-seo',issues:[
    {severity:'notice',check:'Thin content',url:'https://acme.test/about',detail:'About 80 words; pages with little text rarely rank.'},
    {severity:'error',check:'Broken link',url:'https://acme.test/',detail:'Links to https://acme.test/old, which answers 404.'},
    {severity:'warning',check:'Description',url:'https://acme.test/pricing',detail:'There is no meta description.'}]};
  let ran:any=null,done=false;
  await page.route('**/api/site-audit',route=>{if(route.request().method()==='POST'){ran=route.request().postDataJSON();done=true;return route.fulfill({json:result});}
    return route.fulfill({json:{sites:['acme.test'],latest:done?[result]:[]}});});
  await launch(page,request,baseURL!,'pane=work');
  const section=page.locator('section[aria-label="Site check"]');
  await section.scrollIntoViewIfNeeded();
  await section.getByRole('button',{name:'Check the site'}).click();
  await expect.poll(()=>ran).toEqual({site:'acme.test'});
  await expect(section.getByLabel('Site check of acme.test')).toContainText('To fix1');
  // Errors first: the broken link leads the table.
  await expect(section.locator('tbody tr').first()).toContainText('Broken link');
  await expect(section).toContainText('sitemap missing');
  await expect(section.getByRole('button',{name:'Full report'})).toBeVisible();
});
