import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('chat opens a host-only connection card without a model call',async({page})=>{
  const directory=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(directory,{recursive:true});
  await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  const composer=page.getByLabel('Message or goal',{exact:true});
  await composer.fill('Connect my Google Calendar');
  await composer.press('Enter');
  const secureSetup=page.getByRole('region',{name:'Secure connection setup',exact:true});
  await expect(secureSetup).toBeVisible();
  await expect(page.getByText(/opened a secure Google connection card below/)).toBeVisible();
  await expect(secureSetup.getByText(/never become chat messages or model context/)).toBeVisible();
  await expect(secureSetup.getByLabel('Google Workspace permission',{exact:true})).toHaveValue('calendar');
  await expect(secureSetup.getByLabel('Google OAuth client ID',{exact:true})).toBeVisible();
  await expect(secureSetup.getByLabel('Google OAuth client secret',{exact:true})).toHaveAttribute('type','password');
  const state=await page.evaluate(async()=>(await fetch('/api/state')).json());
  const run=state.runs.find((item:{connectionSetup?:string})=>item.connectionSetup==='google');
  expect(run).toMatchObject({state:'succeeded',connectionSetup:'google',modelCalls:0,toolCalls:0});
  const exported=JSON.stringify(await page.evaluate(async()=>(await fetch('/api/export')).json()));
  expect(exported).not.toContain('fictional-browser-secret');
  await page.screenshot({path:path.join(directory,'connection-setup-chat.png'),fullPage:true});
});
