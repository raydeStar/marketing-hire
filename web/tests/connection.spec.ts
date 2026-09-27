import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {createServer} from 'node:http';
import type {AddressInfo} from 'node:net';

test('owner connects a session key, checks discovery and removes it without model dispatch',async({page})=>{
 const key='fictional-browser-credential-not-a-provider-key';let requests=0;
 const server=createServer((request,response)=>{requests++;expect(request.method).toBe('GET');expect(request.url).toBe('/v1/models');expect(request.headers.authorization).toBe('Bearer '+key);expect(request.headers.cookie).toBeUndefined();response.writeHead(200,{'Content-Type':'application/json'});response.end(JSON.stringify({data:[{id:'fixture-model'}]}));});
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Open workspace',exact:true}).click();await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  const before=await page.evaluate(async()=>(await fetch('/api/state')).json());
  await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();
  await page.getByRole('button',{name:'Settings',exact:true}).click();const panel=page.getByRole('region',{name:'Model connection',exact:true});
  await panel.getByLabel('Provider',{exact:true}).selectOption('compatible');
  const endpoint=`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`;
  await panel.getByLabel('Provider URL',{exact:true}).fill(endpoint);await panel.getByLabel('Exact model ID',{exact:true}).fill('fixture-model');
  await panel.getByLabel('API key storage',{exact:true}).selectOption('session');await panel.getByLabel('API key',{exact:true}).fill(key);
  await panel.getByRole('button',{name:'Save connection',exact:true}).click();await expect(panel.getByText('Connection saved. No model call was made.',{exact:true})).toBeVisible();
  await expect(panel.getByLabel('API key',{exact:true})).toHaveCount(0);expect(await page.locator('body').innerText()).not.toContain(key);
  const storage=await page.evaluate(()=>JSON.stringify({local:{...localStorage},session:{...sessionStorage}}));expect(storage).not.toContain(key);
  await panel.getByRole('button',{name:'Check saved connection',exact:true}).click();await expect(panel.getByRole('status')).toContainText('Discovery succeeded');expect(requests).toBe(1);
  expect(await panel.locator('datalist option').getAttribute('value')).toBe('fixture-model');
  const exported=await page.evaluate(async()=>(await fetch('/api/export')).text());expect(exported).not.toContain(key);
  const after=await page.evaluate(async()=>(await fetch('/api/state')).json());expect(after.runs).toEqual(before.runs);expect(after.chats).toEqual(before.chats);
  await panel.getByLabel('Provider URL',{exact:true}).fill(endpoint+'/changed');await expect(panel.getByRole('button',{name:'Save connection',exact:true})).toBeDisabled();
  await panel.getByRole('button',{name:'Reload saved settings',exact:true}).click();await expect(panel.getByLabel('Provider URL',{exact:true})).toHaveValue(endpoint);
  const images=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(images,{recursive:true});
  for(const width of [1440,390]){await page.setViewportSize({width,height:1000});await expect(panel.getByRole('heading',{name:'Connect a model'})).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:path.join(images,`model-connection-${width}.png`),fullPage:true});}
  const closeSidebar=page.getByRole('button',{name:'Close sidebar',exact:true});if(await closeSidebar.isVisible())await closeSidebar.click();
  await panel.getByText('Manage saved credentials (1)',{exact:true}).click();await panel.getByRole('button',{name:'Remove key',exact:true}).click();
  await expect(panel.getByText('The selected key was removed.',{exact:false})).toBeVisible();await panel.getByRole('button',{name:'Check saved connection',exact:true}).click();await expect(panel.getByRole('alert')).toContainText('credential is missing');expect(requests).toBe(1);
  await panel.getByLabel('Provider',{exact:true}).selectOption('scripted');await panel.getByRole('button',{name:'Save connection',exact:true}).click();await expect(panel.getByText('Connection saved. No model call was made.',{exact:true})).toBeVisible();
 }finally{await new Promise<void>((resolve,reject)=>server.close(error=>error?reject(error):resolve()));}
});
