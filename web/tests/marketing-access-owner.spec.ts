import {expect,test} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

for(const width of [1280,390])test(`owner reaches device pairing from the right company panel at ${width}px`,async({page,request,baseURL})=>{
  await page.setViewportSize({width,height:800});
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.ok()).toBeTruthy();
  const {ticket}=await issued.json();
  await page.goto('/#launch='+ticket);
  try{
    const mainViews=page.getByRole('navigation',{name:'Main views'});
    await expect(mainViews.getByRole('button')).toHaveCount(2);
    await mainViews.getByRole('button',{name:'Work'}).click();
    if(width<961)await page.getByRole('button',{name:'Show company panel'}).click();
    const panel=page.getByRole('complementary',{name:'Company sidebar'});
    await expect(panel).toBeVisible();
    await panel.getByRole('button',{name:'Settings'}).click();
    const settings=page.getByRole('main',{name:'Settings workspace'});
    await expect(settings.getByRole('heading',{name:'Team access'})).toBeVisible();
    await expect(settings.getByRole('button',{name:'Create one-time pairing code'})).toBeEnabled();
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
  await expect(page.getByRole('heading',{name:'Join this workspace. With an invitation.'})).toBeVisible();
  await expect(page.getByRole('button',{name:'Request pairing'})).toBeVisible();
  await expect(page.getByText('Host access key',{exact:true})).toHaveCount(0);
  await expect(page.getByRole('button',{name:'Use host access key'})).toHaveCount(0);
});
