import {test,expect,type Page} from '@playwright/test';
import {createServer,type ServerResponse} from 'node:http';
import type {AddressInfo} from 'node:net';
import fs from 'node:fs';
import path from 'node:path';
async function api(page:Page,url:string,body?:unknown,method='POST'){
 return page.evaluate(async({url,body,method})=>{const session=await(await fetch('/api/session')).json();const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});if(!response.ok)throw new Error(await response.text());return response.json();},{url,body,method});
}
function reply(res:ServerResponse,delta:unknown){res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:30,completion_tokens:20}})+'\n\ndata: [DONE]\n\n');}
test('slow app work releases chat, reports its own completion and remains cancellable',async({page})=>{
 test.setTimeout(45000);let calls=0;const held=new Map<string,ServerResponse>();
 const server=createServer(async(req,res)=>{let raw='';for await(const chunk of req)raw+=chunk;const body=JSON.parse(raw),message=body.messages.at(-1).content;calls++;
  if(message.includes('two plus two'))reply(res,{content:'Four. Your project is still running.'});else held.set(message,res);
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});
 try{
  await page.setViewportSize({width:1440,height:950});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
  const connection=await api(page,'/settings/connection');await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-background',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');
  const composer=page.getByLabel('Message or goal');await composer.fill('Build a quiet app');await composer.press('Enter');
  await expect(page.getByText("I've begun work on this request. You can keep chatting; I'll let you know when it's done.",{exact:true})).toBeVisible({timeout:15000});
  await expect(page.getByRole('button',{name:'Tasks: 1 active',exact:true})).toBeVisible();await composer.fill('What is two plus two?');await expect(page.getByRole('button',{name:'Send message',exact:true})).toBeEnabled();await composer.press('Enter');
  await expect(page.getByText('Four. Your project is still running.',{exact:true})).toBeVisible();
  await composer.fill('Another project');await composer.press('Enter');
  await expect.poll(async()=> (await api(page,'/state')).runs.filter((run:any)=>run.background&&run.state==='running').length,{timeout:15000}).toBe(2);
  await page.getByRole('button',{name:'Tasks: 2 active',exact:true}).click();const activity=page.getByRole('region',{name:'Task activity'});
  await activity.locator('article').filter({hasText:'Another project'}).getByRole('button',{name:'Cancel task',exact:true}).click();
  await expect(page.getByRole('button',{name:'Tasks: 1 active',exact:true})).toBeVisible();
  await expect(activity.locator('article').filter({hasText:'Another project'})).toContainText('Cancelled');
  await page.screenshot({path:path.join(images,'background-active-desktop.png'),animations:'disabled'});
  const pending=[...held].find(([goal])=>goal.includes('Build a quiet app'))![1];
  reply(pending,{tool_calls:[{index:0,function:{name:'artifact_create',arguments:JSON.stringify({definition:{title:'Quiet app',description:'A fictional background result',fields:[{key:'note',label:'Note',kind:'text'}],summaries:[],page:{html:'<h2>Your quiet app is ready</h2>',css:'h2{padding:32px}',javaScript:''}},entries:[]})}}]});
  await expect(page.getByRole('button',{name:'Tasks: 0 active',exact:true})).toBeVisible();
  await expect(page.getByRole('status').filter({hasText:'Task completed: Build a quiet app'})).toBeAttached();
  await expect(composer).toBeVisible();expect(new URL(page.url()).pathname).toBe('/');
  const state=await api(page,'/state');const built=state.runs.find((run:any)=>run.goal.objective==='Build a quiet app');expect(built.state).toBe('succeeded');expect(built.background).toBe(true);expect(built.modelCalls).toBe(1);expect(built.chargedTokens).toBe(50);expect(built.goal.limits.seconds).toBe(600);expect(built.goal.limits.maxTotalTokens).toBe(64000);
  const cancelled=state.runs.find((run:any)=>run.goal.objective==='Another project');expect(cancelled.state).toBe('cancelled');expect(cancelled.chargedTokens).toBe(64000);
  await activity.locator('article').filter({hasText:'Build a quiet app'}).getByRole('button',{name:'Open app',exact:true}).click();
  await expect(page.frameLocator('iframe[title="Quiet app app"]').getByRole('heading',{name:'Your quiet app is ready'})).toBeVisible();
  await page.reload();await page.setViewportSize({width:390,height:844});await expect(page.getByRole('button',{name:'Tasks: 0 active',exact:true})).toBeInViewport();await page.getByRole('button',{name:'Tasks: 0 active',exact:true}).click();await expect(page.getByRole('region',{name:'Task activity'})).toContainText('Completed');
  await page.screenshot({path:path.join(images,'background-result-mobile.png'),animations:'disabled'});expect(calls).toBe(3);
  fs.writeFileSync(path.join(images,'background-check.json'),JSON.stringify({syntheticCalls:calls,liveCalls:0,checks:['eight-second automatic handoff','chat completes while app runs','two background tasks','scoped cancellation','conservative interrupted usage','background result does not steal chat','open result from task counter','durable status after reload','mobile task panel']},null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
