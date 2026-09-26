import {expect,test,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?"?"+query:""}#launch=${(await issued.json()).ticket}`);
}

for(const width of [1280,390])test(`owner reaches device pairing from Settings at ${width}px`,async({page,request,baseURL})=>{
  await page.setViewportSize({width,height:800});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!);
  try{
    const rail=page.getByRole('complementary',{name:'Main navigation'});
    await expect(rail.getByRole('navigation',{name:'Main views'}).getByRole('button')).toHaveText([/Chat/,'Search','Library','Team']);   // a waiting count may precede the label
    // Settings and account sit at the foot of the rail (the bottom bar on a phone).
    await rail.getByRole('button',{name:'Settings and account'}).click();
    const menu=page.getByRole('menu',{name:'Settings and account'});
    await expect(menu).toContainText('Owner ·');
    await expect(menu.getByRole('menuitem',{name:'Settings'})).toBeInViewport();
    await menu.getByRole('menuitem',{name:'Settings'}).click();
    const settings=page.getByRole('main',{name:'Settings'});
    await settings.getByRole('region',{name:'Workspace'}).getByRole('button',{name:/People and roles/}).click();
    // Pairing lives in Team → People → Invite.
    const team=page.getByRole('main',{name:'Team'});
    await expect(team.getByRole('navigation',{name:'Team sections'}).getByRole('button',{name:'People'})).toHaveAttribute('aria-pressed','true');
    await team.getByRole('button',{name:'Invite'}).click();
    await expect(team.getByRole('heading',{name:'Pair a browser'})).toBeVisible();
    // Pairing needs the host's trusted HTTPS address; a local-only fixture host has none.
    const phoneOrigin=await page.evaluate(async()=>(await (await fetch('/api/state')).json()).phoneOrigin as string|null);
    const pairing=team.getByRole('button',{name:'Create one-time code'});
    if(phoneOrigin)await expect(pairing).toBeEnabled();
    else{await expect(pairing).toHaveCount(0);await expect(team).toContainText('A trusted HTTPS address is needed before a browser can pair.');}
    await expect(team.getByRole('heading',{name:'Invite a reviewer by email'})).toBeVisible();
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
