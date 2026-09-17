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
  await expect(composer).toBeFocused();
  await expect(page.getByRole('complementary',{name:'Activity log',exact:true})).toHaveCount(0);
  await expect(page.getByText(/opened a secure Google connection card below/)).toBeVisible();
  await expect(secureSetup.getByText('Credentials stay on this computer, outside our chat.',{exact:true})).toBeVisible();
  await expect(secureSetup.getByLabel('Google Workspace permission',{exact:true})).toHaveValue('calendar');
  await expect(secureSetup.getByLabel('Google OAuth client ID',{exact:true})).toHaveCount(0);
  await expect(secureSetup.getByLabel('Google OAuth client secret',{exact:true})).toHaveCount(0);
  await expect(secureSetup.getByRole('button',{name:'Continue with Google',exact:true})).toBeDisabled();
  await expect(secureSetup.getByText(/one-time app setup before anyone can sign in/)).toBeVisible();
  const state=await page.evaluate(async()=>(await fetch('/api/state')).json());
  const run=state.runs.find((item:{connectionSetup?:string})=>item.connectionSetup==='google');
  expect(run).toMatchObject({state:'succeeded',connectionSetup:'google',modelCalls:0,toolCalls:0});
  await expect(page.locator('#chat-'+run.id+'-assistant').getByRole('region',{name:'Secure connection setup',exact:true})).toBeVisible();
  await expect(page.locator('.conversation-compose').getByRole('region',{name:'Secure connection setup',exact:true})).toHaveCount(0);
  // Setup belongs to this reply, even when the conversation continues below it.
  await composer.fill('Hello, just checking that I can keep chatting.');await composer.press('Enter');
  await expect(page.getByText('Hello, just checking that I can keep chatting.',{exact:true})).toBeVisible();
  await expect(page.locator('#chat-'+run.id+'-assistant').getByRole('region',{name:'Secure connection setup',exact:true})).toBeVisible();
  await expect(page.locator('#chat-'+run.id+'-assistant').getByRole('button',{name:'Try again',exact:true})).toHaveCount(0);
  const continueSetup=page.getByRole('button',{name:'Continue connection setup',exact:true});
  await secureSetup.getByRole('button',{name:'Close connection setup',exact:true}).click();
  await expect(secureSetup).toHaveCount(0);
  await expect(continueSetup).toBeVisible();
  await continueSetup.click();
  await expect(secureSetup).toBeVisible();
  await expect(secureSetup).toBeFocused();
  const cardBox=await secureSetup.boundingBox();
  expect(cardBox!.width).toBeLessThanOrEqual(560);
  expect(await secureSetup.evaluate(element=>getComputedStyle(element).borderRadius)).toBe('16px');
  const exported=JSON.stringify(await page.evaluate(async()=>(await fetch('/api/export')).json()));
  expect(exported).not.toContain('fictional-browser-secret');
  await page.screenshot({path:path.join(directory,'connection-setup-chat.png'),fullPage:true});
});

test('one secure import enables later connections without copying keys',async({page})=>{
  const directory=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(directory,{recursive:true});
  await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  const composer=page.getByLabel('Message or goal',{exact:true});
  await composer.fill('Connect my Gmail for read-only email access');await composer.press('Enter');
  const setup=page.getByRole('region',{name:'Secure connection setup',exact:true});
  await expect(setup).toBeVisible();
  await setup.getByText('App setup · one time',{exact:true}).click();
  const input=setup.getByLabel('Import Google setup file',{exact:true});
  await input.setInputFiles({name:'wrong-client.json',mimeType:'application/json',buffer:Buffer.from('{"web":{}}')});
  await expect(setup.getByRole('alert')).toContainText('Desktop app credentials JSON');
  await expect(setup.getByRole('button',{name:'Continue with Google',exact:true})).toBeDisabled();
  const credentials={installed:{client_id:'123-fixture.apps.googleusercontent.com',client_secret:'fictional-browser-secret',auth_uri:'https://accounts.google.com/o/oauth2/auth',token_uri:'https://oauth2.googleapis.com/token'}};
  try{
    await input.setInputFiles({name:'desktop-client.json',mimeType:'application/json',buffer:Buffer.from(JSON.stringify(credentials))});
    await expect(setup.getByText('Google app setup saved. You can now continue with Google.',{exact:true})).toBeVisible();
    await expect(setup.getByRole('button',{name:'Continue with Google',exact:true})).toBeEnabled();
    await composer.fill('Connect my Google Calendar');await composer.press('Enter');
    await expect(setup.getByLabel('Google Workspace permission',{exact:true})).toHaveValue('calendar');
    await page.reload();
    await composer.fill('Connect my Google Calendar');await composer.press('Enter');
    await expect(setup.getByLabel('Google Workspace permission',{exact:true})).toHaveValue('calendar');
    await expect(setup.getByRole('button',{name:'Continue with Google',exact:true})).toBeEnabled();
    await expect(setup.getByLabel('Import Google setup file',{exact:true})).toHaveCount(0);
    await expect(setup.getByLabel('Google OAuth client secret',{exact:true})).toHaveCount(0);
    expect((await setup.boundingBox())!.height).toBeLessThan(400);
    await page.screenshot({path:path.join(directory,'google-connect-ready.png'),fullPage:true});
    // Consent is a fixture here: never open Google or dispatch a model in this routine UX check.
    let sent:Record<string,unknown>|undefined;
    await page.route('**/api/settings/mcp/google/start',async route=>{
      sent=route.request().postDataJSON();
      await route.fulfill({json:{attemptId:'consent-fixture',authorizationUrl:'https://accounts.google.com/o/oauth2/auth?fixture=true',browserOpened:true}});
    });
    await page.route('**/api/settings/mcp/google/status/consent-fixture',route=>route.fulfill({json:{phase:'failed',error:'Fixture: Google consent was denied.'}}));
    await setup.getByRole('button',{name:'Continue with Google',exact:true}).click();
    await expect(setup.getByText(/Google sign-in opened in your default browser/)).toBeVisible();
    expect(Object.keys(sent!).sort()).toEqual(['product','version']);expect(sent!.product).toBe('calendar');
    await expect(setup.getByRole('alert')).toHaveText('Fixture: Google consent was denied.');
    await expect(setup.getByRole('button',{name:'Continue with Google',exact:true})).toBeEnabled();
    const exported=JSON.stringify(await page.evaluate(async()=>(await fetch('/api/export')).json()));
    expect(exported).not.toContain('fictional-browser-secret');expect(exported).not.toContain('123-fixture.apps.googleusercontent.com');
    const view=JSON.stringify(await page.evaluate(async()=>(await fetch('/api/settings/mcp')).json()));
    expect(view).not.toContain('fictional-browser-secret');
    await page.setViewportSize({width:390,height:844});
    await setup.scrollIntoViewIfNeeded();
    await expect(setup.getByRole('button',{name:'Continue with Google',exact:true})).toBeVisible();
    expect(await setup.evaluate(element=>element.scrollWidth<=element.clientWidth+2)).toBe(true);
    await expect(composer).toBeInViewport();
    await page.screenshot({path:path.join(directory,'google-connect-mobile.png'),fullPage:true});
  }finally{
    // Remove only this disposable study's app-registration credential, even on a failed assertion.
    const removed=await page.evaluate(async()=>{
      const session=await (await fetch('/api/session')).json();
      const view=await (await fetch('/api/settings/mcp')).json();
      if(!view.google.clientSetup.canRemove)return true;
      return (await fetch('/api/settings/mcp/google/client/remove',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify({version:view.version})})).ok;
    });
    expect(removed,'Fixture app registration removed from the system vault').toBe(true);
  }
});
