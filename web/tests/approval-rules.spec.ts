import {test,expect,type Page} from '@playwright/test';
import {createServer,type ServerResponse} from 'node:http';
import type {AddressInfo} from 'node:net';
import fs from 'node:fs';
import path from 'node:path';

function answer(response:ServerResponse,delta:unknown){
 response.writeHead(200,{'Content-Type':'text/event-stream'});
 response.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:120,completion_tokens:40}})+'\n\ndata: [DONE]\n\n');
}

async function api(page:Page,url:string,body?:unknown,method='POST'){
 return page.evaluate(async({url,body,method})=>{
  const session=await(await fetch('/api/session')).json();
  const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});
  if(!response.ok)throw new Error(await response.text());return response.json();
 },{url,body,method});
}

async function send(page:Page,message:string){const composer=page.getByLabel('Message or goal');await composer.fill(message);await composer.press('Enter');}

test('chat reviews exact actions and keeps remembered choices in a removable list',async({page})=>{
 test.setTimeout(60000);
 let calls=0;
 const server=createServer(async(request,response)=>{
  let raw='';for await(const chunk of request)raw+=chunk;const input=JSON.parse(raw);calls++;
  if(calls%2===1){
   const context=input.messages.find((message:any)=>typeof message.content==='string'&&message.content.includes('Frozen request timestamp:'))?.content||'';
   const requested=context.match(/Frozen request timestamp: ([^\n]+)/)?.[1];
   const timeZone=context.match(/Exact local timezone: ([^\n]+)/)?.[1];
   const number=(calls+1)/2;
   answer(response,{tool_calls:[{index:0,function:{name:'delegation_schedule_reminder',arguments:JSON.stringify({title:`Review fixture ${number}`,message:`Fixture ${number}.`,dueUtc:new Date(new Date(requested).getTime()+number*3_600_000).toISOString(),timeZone})}}]});
  }else answer(response,{content:'The reviewed reminder is scheduled.'});
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  await page.setViewportSize({width:1280,height:900});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
  const connection=await api(page,'/settings/connection');
  await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-approval-model',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');

  await send(page,'In one hour, remind me about the first fixture.');
  let review=page.getByRole('region',{name:'Reminder review'});await expect(review).toBeVisible();
  await expect(review).toContainText('Review fixture 1');await expect(review).toContainText('A remembered choice applies only to');
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  await review.getByRole('button',{name:'Always allow this type',exact:true}).click();
  await expect(page.getByText('The reviewed reminder is scheduled.',{exact:true})).toBeVisible();

  await page.setViewportSize({width:1280,height:900});
  await send(page,'In two hours, remind me about the second fixture.');
  await expect(page.getByText('The reviewed reminder is scheduled.',{exact:true})).toHaveCount(2);
  expect(calls).toBe(4);

  await send(page,'Show my approval settings');
  await expect(page.getByRole('heading',{name:'Remembered approval choices'})).toBeVisible();
  const rules=page.getByLabel('Remembered approval choices');
  await expect(rules).toContainText('Always allow');await expect(rules).toContainText('Schedule reminders and notifications');
  await rules.getByRole('button',{name:'Remove',exact:true}).click();await expect(rules).toContainText('No remembered choices');

  await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();
  await page.getByRole('button',{name:'Chat',exact:true}).click();
  await send(page,'In three hours, remind me about the third fixture.');
  review=page.getByRole('region',{name:'Reminder review'});await expect(review).toContainText('Review fixture 3');
  await review.getByRole('button',{name:'Always deny this type',exact:true}).click();
  await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('button',{name:'Permissions & devices',exact:true}).click();
  await expect(page.getByLabel('Remembered approval choices')).toContainText('Always deny');
  expect(calls).toBe(5);

  const screenshots=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(screenshots,{recursive:true});
  fs.writeFileSync(path.join(screenshots,'approval-rules-check.json'),JSON.stringify({passed:true,syntheticModelCalls:calls,liveModelCalls:0,checks:['inline exact review','mobile no overflow','remembered allow','automatic matching action','chat-opened settings','rule removal restores review','remembered deny']},null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
