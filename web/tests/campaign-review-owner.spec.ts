import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const projectId=process.env.MARKETING_PERSISTENT_PROJECT_ID;
test.skip(!projectId,'Set MARKETING_PERSISTENT_PROJECT_ID for the read-only saved-pilot presentation check.');

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?"?"+query:""}#launch=${(await issued.json()).ticket}`);
}

for(const width of [1440,1280]){
  test(`saved owner campaign review at ${width}px`,async({page,request,baseURL})=>{
    await page.setViewportSize({width,height:width===1440?900:800});
    await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
    await launch(page,request,baseURL!,'pane=work&open=campaign:current');
    const desk=page.getByRole('region',{name:'Campaign review workspace'});
    await expect(desk).toBeVisible();
    // The held current assignment hands the desk to the saved campaign automatically.
    await expect(desk.locator('.campaign-desk-header .eyebrow')).toHaveText('Saved campaign');
    await expect(desk.getByRole('heading',{level:2})).toBeVisible();
    await expect(desk).toContainText('Three draft post angles');
    await expect(desk).toContainText('A newer assignment is on hold while its last step is checked');
    // Older review packets have no saved self-review; none is inferred.
    await expect(desk.getByRole('heading',{name:'Marketing’s self-review'})).toHaveCount(0);
    await desk.scrollIntoViewIfNeeded();
    const output=path.resolve(`../artifacts/enterprise-review/owner-${width}.png`);
    fs.mkdirSync(path.dirname(output),{recursive:true});
    await page.screenshot({path:output});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
    await desk.getByRole('button',{name:'Brief',exact:true}).click();
    await expect(desk.getByRole('heading',{name:'Campaign brief',exact:true})).toBeVisible();
    await desk.getByRole('button',{name:'Activity & sharing'}).click();
    await expect(desk.getByRole('button',{name:'Connect native conversation'})).toBeEnabled();
  });
}
