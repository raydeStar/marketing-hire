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
  const google=page.getByRole('region',{name:'Google Workspace',exact:true});
  await expect(google.getByText(/Windows Credential Manager|macOS Keychain|Linux Secret Service/).first()).toBeVisible();
  await google.getByLabel('Google OAuth client ID',{exact:true}).fill('fictional-browser-client');
  await google.getByLabel('Google OAuth client secret',{exact:true}).fill('fictional-browser-secret');
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    for(const [name,panel] of [['Connections','Connection settings'],['Research worker','Research worker settings'],['Permissions & devices','Permissions and devices settings'],['Storage & backups','Storage and backup settings']]){
      const button=navigation.getByRole('button',{name,exact:true});await button.focus();await page.keyboard.press('Enter');
      await expect(button).toBeFocused();await expect(button).toHaveAttribute('aria-current','page');
      await expect(page.getByRole('region',{name:panel,exact:true})).toBeVisible();
      await expect(page.locator('.settings-panel:visible')).toHaveCount(1);
      await expect(page.getByRole('button',{name:/token usage$/})).toBeVisible();
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
    await expect(google.getByLabel('Google OAuth client ID',{exact:true})).toHaveValue('fictional-browser-client');
    await expect(google.getByLabel('Google OAuth client secret',{exact:true})).toHaveValue('fictional-browser-secret');
  }
  await navigation.getByRole('button',{name:'Permissions & devices',exact:true}).click();
  await page.evaluate(()=>window.dispatchEvent(new Event('offline')));
  await expect(page.getByLabel('Knowledge writes',{exact:true})).toBeDisabled();
  expect(mutations).toEqual([]);
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
});
