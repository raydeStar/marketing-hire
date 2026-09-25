import {expect,test,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,view='today'){
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/?view=${view}#launch=${(await issued.json()).ticket}`);
}

for(const width of [1280,390])test(`owner reaches device pairing from Settings at ${width}px`,async({page,request,baseURL})=>{
  await page.setViewportSize({width,height:800});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!);
  try{
    const mainViews=page.getByRole('navigation',{name:'Main views'});
    await expect(mainViews.getByRole('button')).toHaveText([/^Today/,/^Chat/,/^Inbox/,/^Campaigns/,/^Assets/,/^Tasks/,/^Wiki/,/^Team/,/^History/]);
    // On a phone the rail is off-canvas until the menu opens.
    if(width<768)await page.getByRole('button',{name:'Open menu'}).click();
    const rail=page.getByRole('complementary',{name:'Main navigation'});
    await rail.getByRole('button',{name:'Settings',exact:true}).click();
    const settings=page.getByRole('main',{name:'Settings'});
    await expect(settings.getByRole('heading',{name:'Team access'})).toBeVisible();
    // Pairing needs the host's trusted HTTPS address; a local-only fixture host has none.
    const phoneOrigin=await page.evaluate(async()=>(await (await fetch('/api/state')).json()).phoneOrigin as string|null);
    const pairing=settings.getByRole('button',{name:'Create one-time pairing code'});
    if(phoneOrigin)await expect(pairing).toBeEnabled();
    else{await expect(pairing).toBeDisabled();await expect(settings).toContainText('A trusted HTTPS address is needed before a teammate can pair.');}
    await expect(settings).toContainText('Pairing gives a browser an identity.');
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
  }finally{
    await page.evaluate(async()=>{
      const session=await fetch('/api/session').then(response=>response.json());
      await fetch('/api/devices/'+session.id+'/revoke',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:'{}'});
    }).catch(()=>{});
  }
});

test('private HTTPS entry offers a pairing code instead of the owner key',async({page})=>{
  test.skip(!process.env.MARKETING_PRIVATE_ORIGIN,'Set MARKETING_PRIVATE_ORIGIN to a trusted private HTTPS route.');
  await page.goto(process.env.MARKETING_PRIVATE_ORIGIN!);
  await expect(page.getByRole('heading',{name:'Join this workspace'})).toBeVisible();
  await expect(page.getByRole('button',{name:'Request pairing'})).toBeVisible();
  await expect(page.getByText('Host access key',{exact:true})).toHaveCount(0);
  await expect(page.getByRole('button',{name:'Use the host access key instead'})).toHaveCount(0);
});
