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

test('watched competitor pages show their prices and flag a recent price change',async({page,request,baseURL})=>{
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');localStorage.setItem('fe-getting-started-dismissed:*','yes');}catch{}});
  const now=new Date().toISOString();
  await page.route('**/api/listening',route=>route.fulfill({json:{topics:[],feeds:[],lastScanAt:now,errors:[],stats:[],mentions:[],watch:[
    {url:'https://www.jasper.ai/pricing',title:'Plans & Pricing | Jasper',checkedAt:now,prices:['$69/month'],error:null,lastChange:{at:now,kind:'prices',summary:'Prices changed on jasper.ai/pricing: No longer shown: $59/month; now shown: $69/month.'}},
    {url:'https://www.copy.ai/prices',title:'Plans & Pricing | Copy.ai',checkedAt:now,prices:['$29','$1,000'],error:null,lastChange:null}]}}));
  await launch(page,request,baseURL!,'pane=work');
  await page.getByRole('tab',{name:'Listening',exact:true}).click();
  const table=page.getByRole('table',{name:'Watched pages'});
  await table.scrollIntoViewIfNeeded();
  const jasper=table.locator('tbody tr').first();
  await expect(jasper).toHaveClass(/bad/);
  await expect(jasper).toContainText('Price change');
  await expect(jasper).toContainText('now shown: $69/month');
  await expect(table.locator('tbody tr').nth(1)).toContainText('No change since the first read');
  await expect(table.locator('tbody tr').nth(1)).toContainText('$29, $1,000');
});
