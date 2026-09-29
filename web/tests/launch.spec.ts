import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('launcher link unlocks once, removes its fragment and keeps the session and work across reload',async({page,request,browser,baseURL})=>{
 const origin=baseURL!;
 const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
 const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
 expect(issued.status()).toBe(200);
 const {ticket}=await issued.json();
 expect(ticket).not.toBe(key);
 const requests:string[]=[];
 page.on('request',request=>requests.push(request.url()));
 await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');}catch{}});
 await page.goto('/#launch='+ticket);
 const views=page.getByRole('navigation',{name:'Main views'});
 await expect(views).toBeVisible();
 expect(new URL(page.url()).hash).toBe('');
 const work=async()=>page.evaluate(async()=>{const state=await(await fetch('/api/marketing/state')).json();return {messages:state.messages,tasks:state.tasks,drafts:state.drafts};});
 const before=await work();
 await page.reload();
 await expect(views).toBeVisible();
 await expect(page.getByLabel('Host access key',{exact:true})).toHaveCount(0);
 expect(await work()).toEqual(before);
 expect(requests.every(url=>!url.includes(ticket)&&!url.includes(key))).toBeTruthy();
 const fresh=await browser.newContext();
 try{
  const replay=await fresh.newPage();await replay.goto(origin+'/#launch='+ticket);
  await expect(replay.getByRole('button',{name:'Open workspace'})).toBeVisible();
  await expect(replay.getByRole('alert')).toContainText('expired or was already used');
  expect(new URL(replay.url()).hash).toBe('');
  const invalid=replay.waitForResponse(response=>response.url().endsWith('/api/auth/claim-launch')&&response.request().method()==='POST');
  await replay.goto(origin+'/#launch=');
  expect((await invalid).status()).toBe(401);
  await expect(replay.getByRole('alert')).toContainText('expired or was already used');
  await expect(replay).toHaveURL(origin+'/');
  await expect(replay.getByRole('navigation',{name:'Main views'})).toHaveCount(0);
 }finally{await fresh.close();}
});
