import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('settings sections preserve drafts, support keyboard and narrow screens, and make no changes during navigation',async({page})=>{
  const directory=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(directory,{recursive:true});
  await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  const before=await page.evaluate(async()=>(await fetch('/api/export')).json());
  const mutations:string[]=[];
  page.on('request',request=>{if(new URL(request.url()).pathname.startsWith('/api/')&&!['GET','HEAD'].includes(request.method()))mutations.push(request.url());});
  await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();
  await page.getByRole('button',{name:'Settings',exact:true}).click();
  await page.getByRole('button',{name:'Collapse sidebar',exact:true}).click();
  const navigation=page.getByRole('navigation',{name:'Settings sections'});
  const connection=page.getByRole('region',{name:'Model connection',exact:true});
  await connection.getByLabel('Provider',{exact:true}).selectOption('compatible');
  await connection.getByLabel('Exact model ID',{exact:true}).fill('unsaved-cobalt-model');
  const services=page.getByRole('region',{name:'Connected services',exact:true});
  await expect(services.getByText(/Windows Credential Manager|macOS Keychain|Linux Secret Service/).first()).toBeVisible();
  await expect(services.getByLabel('Connection list',{exact:true})).toBeVisible();
  await expect(services.getByLabel('Google OAuth client ID',{exact:true})).toHaveCount(0);
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    for(const [name,panel] of [['Connections','Connection settings'],['Soul','Soul settings'],['User','User settings'],['Research worker','Research worker settings'],['Permissions & devices','Permissions and devices settings'],['Storage & backups','Storage and backup settings']]){
      const button=navigation.getByRole('button',{name,exact:true});await button.focus();await page.keyboard.press('Enter');
      await expect(button).toBeFocused();await expect(button).toHaveAttribute('aria-current','page');
      await expect(page.getByRole('region',{name:panel,exact:true})).toBeVisible();
      await expect(page.locator('.settings-panel:visible')).toHaveCount(1);
      await expect(page.getByRole('button',{name:/token usage$/})).toBeVisible();
      if(name==='Soul'){
        await expect(page.getByLabel('SOUL.md',{exact:true})).toHaveValue(/Sir Thaddeus/);
        await expect(page.getByText(/Personality never grants tools or permissions/)).toBeVisible();
      }
      if(name==='User'){
        await expect(page.getByLabel('USER.md',{exact:true})).toHaveValue(/Nothing saved yet/);
        await expect(page.getByText(/will not silently infer sensitive traits or save secrets/)).toBeVisible();
      }
      expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
      if(name==='Permissions & devices'){
        await expect(page.getByRole('button',{name:/Revoke owner session/})).toHaveCount(0);
        await page.getByText(/Manage owner browser sessions \(/).click();
        await expect(page.getByRole('button',{name:/Revoke owner session/}).first()).toBeVisible();
        await page.getByText(/Manage owner browser sessions \(/).click();
      }
      await page.evaluate(()=>window.scrollTo(0,0));
      await page.screenshot({path:path.join(directory,`settings-${name.split(' ')[0].toLowerCase()}-${width}.png`),fullPage:true});
    }
    await navigation.getByRole('button',{name:'Connections',exact:true}).click();
    await expect(connection.getByLabel('Exact model ID',{exact:true})).toHaveValue('unsaved-cobalt-model');
    await expect(services.getByLabel('Connection list',{exact:true})).toBeVisible();
  }
  await services.getByRole('button',{name:/Connect Google with Thaddeus/}).click();
  const secureSetup=page.getByRole('region',{name:'Secure connection setup',exact:true});
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  await expect(secureSetup).toBeVisible();
  await expect(secureSetup.getByText('Credentials stay on this computer, outside our chat.',{exact:true})).toBeVisible();
  await secureSetup.getByText('App setup · one time',{exact:true}).click();
  await expect(secureSetup.getByLabel('Import Google setup file',{exact:true})).toHaveAttribute('type','file');
  await secureSetup.getByRole('button',{name:'Close connection setup',exact:true}).click();
  await page.setViewportSize({width:1440,height:1000});
  await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();
  await page.getByRole('button',{name:'Settings',exact:true}).click();
  await page.getByRole('button',{name:'Collapse sidebar',exact:true}).click();
  await navigation.getByRole('button',{name:'Permissions & devices',exact:true}).click();
  await page.evaluate(()=>window.dispatchEvent(new Event('offline')));
  await expect(page.getByLabel('Knowledge writes',{exact:true})).toBeDisabled();
  expect(mutations).toEqual([]);
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
});
