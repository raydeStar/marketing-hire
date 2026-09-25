import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string){
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/?pane=work#launch=${(await issued.json()).ticket}`);
}

test('the rail expands to labels and the cockpit can be dragged wider; both are remembered',async({page,request,baseURL})=>{
  await page.setViewportSize({width:1600,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!);
  const rail=page.getByRole('complementary',{name:'Main navigation'});
  await expect(rail.getByRole('button',{name:'Library'})).toBeVisible();
  const narrow=(await rail.boundingBox())!.width;
  await rail.getByRole('button',{name:'Expand sidebar'}).click();
  await expect(rail.getByRole('button',{name:'Collapse sidebar'})).toBeVisible();
  expect((await rail.boundingBox())!.width).toBeGreaterThan(narrow+100);
  await expect(rail.getByText('Library',{exact:true})).toBeVisible();

  const cockpit=page.getByRole('complementary',{name:'Cockpit'});
  const before=(await cockpit.boundingBox())!.width;
  const handle=page.getByRole('separator',{name:'Resize cockpit'});
  const box=(await handle.boundingBox())!;
  await page.mouse.move(box.x+box.width/2,box.y+300);
  await page.mouse.down();
  await page.mouse.move(box.x-180,box.y+300,{steps:6});
  await page.mouse.up();
  const wider=(await cockpit.boundingBox())!.width;
  expect(wider).toBeGreaterThan(before+150);

  await page.reload();
  await expect(page.getByRole('complementary',{name:'Main navigation'}).getByRole('button',{name:'Collapse sidebar'})).toBeVisible();
  expect(Math.abs((await page.getByRole('complementary',{name:'Cockpit'}).boundingBox())!.width-wider)).toBeLessThan(4);
  await page.getByRole('separator',{name:'Resize cockpit'}).dblclick();
  await expect.poll(async()=>Math.round((await page.getByRole('complementary',{name:'Cockpit'}).boundingBox())!.width)).toBe(320);
  await page.getByRole('separator',{name:'Resize cockpit'}).focus();
  await page.keyboard.press('ArrowLeft');
  await expect.poll(async()=>Math.round((await page.getByRole('complementary',{name:'Cockpit'}).boundingBox())!.width)).toBe(344);
});
