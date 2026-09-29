import {expect,test,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  if(process.env.THADDEUS_TEST_PLOW==='1'){
    // The disposable Plow entrance supplies its fictional owner; no host key leaves the container.
    await page.goto('/'+(query?'?'+query:''));return;
  }
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?"?"+query:""}#launch=${(await issued.json()).ticket}`);
}

for(const width of [1280,390])test(`owner reaches supported invitations from Settings at ${width}px`,async({page,request,baseURL})=>{
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
    // Team only offers invitation methods supported by this installation.
    const team=page.getByRole('main',{name:'Team'});
    await expect(team.getByRole('navigation',{name:'Team sections'}).getByRole('button',{name:'People'})).toHaveAttribute('aria-pressed','true');
    await team.getByRole('button',{name:'Invite'}).click();
    const access=await page.evaluate(async()=>({
      session:await (await fetch('/api/session')).json(),login:await (await fetch('/api/auth/customer')).json()
    }));
    if(access.login.companion?.people){
      await expect(team.getByRole('link',{name:'Invite or manage teammates'})).toHaveAttribute('href',access.login.companion.people);
      await expect(team.getByRole('heading',{name:'Pair a browser'})).toHaveCount(0);
      await expect(team.getByRole('heading',{name:'Invite a reviewer by email'})).toHaveCount(0);
    }else{
      await expect(team.getByRole('heading',{name:'Pair a browser'})).toHaveCount(access.session.canPair?1:0);
      await expect(team.getByRole('button',{name:'Create one-time code'})).toHaveCount(access.session.canPair?1:0);
      if(access.session.canPair)await expect(team.getByRole('button',{name:'Create one-time code'})).toBeEnabled();
      await expect(team.getByRole('heading',{name:'Invite a reviewer by email'})).toHaveCount(access.login.enabled?1:0);
      if(!access.session.canPair&&!access.login.enabled){
        await expect(team).toContainText('This installation has no teammate invitation service connected.');
        await expect(team.getByRole('link',{name:'Choose a shared workspace'})).toHaveAttribute('href','https://hirezero.app/account/?choose=1');
      }
    }
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
    if(process.env.THADDEUS_SCREENSHOTS){
      fs.mkdirSync(process.env.THADDEUS_SCREENSHOTS,{recursive:true});
      await page.screenshot({path:path.join(process.env.THADDEUS_SCREENSHOTS,`team-invites-${width}.png`)});
    }
  }finally{
    await page.evaluate(async()=>{
      const session=await fetch('/api/session').then(response=>response.json());
      await fetch('/api/devices/'+session.id+'/revoke',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:'{}'});
    }).catch(()=>{});
  }
});

for(const width of [1280,390])test(`shared workspace invitation takes precedence over device and email methods at ${width}px`,async({page,request,baseURL})=>{
  await page.setViewportSize({width,height:800});
  await page.addInitScript(()=>{localStorage.setItem('fe-onboarding-dismissed','yes');});
  const people='https://hirezero.app/account/?workspace='+'a'.repeat(32)+'&people=1';
  // Only invitation availability is fictional. Sign-in and the surrounding Team view use the real host.
  await page.route('**/api/auth/customer',route=>route.fulfill({json:{enabled:true,companion:{people}}}));
  await launch(page,request,baseURL!,'view=team');
  try{
    const team=page.getByRole('main',{name:'Team'});
    await team.getByRole('button',{name:'Invite',exact:true}).click();
    await expect(team.getByRole('link',{name:'Invite or manage teammates'})).toHaveAttribute('href',people);
    await expect(team.getByRole('heading',{name:'Pair a browser'})).toHaveCount(0);
    await expect(team.getByRole('heading',{name:'Invite a reviewer by email'})).toHaveCount(0);
    await expect(team.getByRole('link',{name:'Choose a shared workspace'})).toHaveCount(0);
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
