import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('launcher link unlocks once, removes its fragment and preserves work across reload',async({page,request,browser,baseURL})=>{
 const origin=baseURL!;
 const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
 const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
 expect(issued.status()).toBe(200);
 const {ticket}=await issued.json();
 expect(ticket).not.toBe(key);
 const requests:string[]=[];
 page.on('request',request=>requests.push(request.url()));
 await page.goto('/#launch='+ticket);
 await expect(page.getByRole('heading',{name:'Make room for what matters.'})).toBeVisible();
 expect(new URL(page.url()).hash).toBe('');
 const before=await page.evaluate(async()=>(await fetch('/api/state')).json());
 await page.reload();
 await expect(page.getByRole('heading',{name:'Make room for what matters.'})).toBeVisible();
 const after=await page.evaluate(async()=>(await fetch('/api/state')).json());
 expect(after.runs).toEqual(before.runs);expect(after.chats).toEqual(before.chats);
 expect(requests.every(url=>!url.includes(ticket)&&!url.includes(key))).toBeTruthy();
 const fresh=await browser.newContext();
 try{
  const replay=await fresh.newPage();await replay.goto(origin+'/#launch='+ticket);
  await expect(replay.getByRole('button',{name:'Unlock study'})).toBeVisible();
  await expect(replay.getByRole('alert')).toContainText('expired or was already used');
  expect(new URL(replay.url()).hash).toBe('');
  await replay.goto(origin+'/#launch=');
  await expect(replay.getByRole('alert')).toContainText('expired or was already used');
  expect(new URL(replay.url()).hash).toBe('');
 }finally{await fresh.close();}
});
