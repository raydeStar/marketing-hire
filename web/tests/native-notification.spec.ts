import {test,expect} from '@playwright/test';
import {createServer} from 'node:http';
import type {AddressInfo} from 'node:net';
import fs from 'node:fs';
import path from 'node:path';

// Explicit opt-in: ordinary browser checks must never disturb the owner's desktop.
test('reviewed reminder dispatches once with the browser closed',async({page,browser})=>{
 test.skip(process.platform!=='win32'||process.env.THADDEUS_NATIVE_NOTIFICATION!=='1','Requires an explicitly requested Windows notification acceptance run.');
 test.setTimeout(90000);
 const title='Thaddeus scheduled notification check';
 const message='Astra acceptance C1: the browser is closed; your reminder still arrived.';
 let calls=0,providerError='',dueUtc='',timeZone='';
 const server=createServer(async(request,response)=>{
  try{
   expect(request.url).toBe('/v1/chat/completions');let raw='';for await(const chunk of request)raw+=chunk;
   const input=JSON.parse(raw);calls++;
   let delta:unknown;
   if(calls===1){
    const facts=input.messages.find((item:any)=>typeof item.content==='string'&&item.content.includes('Frozen request timestamp:'))?.content||'';
    const frozen=facts.match(/Frozen request timestamp: ([^\n]+)/)?.[1];
    timeZone=facts.match(/Exact local timezone: ([^\n]+)/)?.[1];
    if(!frozen||!timeZone)throw new Error('The host must supply an exact timestamp and timezone.');
    dueUtc=new Date(new Date(frozen).getTime()+30000).toISOString();
    delta={tool_calls:[{index:0,function:{name:'delegation_schedule_reminder',arguments:JSON.stringify({title,message,dueUtc,timeZone})}}]};
   }else if(calls===2)delta={content:'The reviewed reminder is scheduled. The host must remain running and awake.'};
   else throw new Error('Unexpected model call at dispatch.');
   response.writeHead(200,{'Content-Type':'text/event-stream'});
   response.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:220,completion_tokens:80}})+'\n\ndata: [DONE]\n\n');
  }catch(error){providerError=String(error);response.writeHead(500);response.end('Synthetic provider failure.');}
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Open workspace',exact:true}).click();
  await expect(page.getByLabel('Message or goal')).toBeVisible();
  const cookies=await page.context().cookies();
  const cookie=cookies.map(item=>item.name+'='+item.value).join('; ');
  const origin=process.env.THADDEUS_TEST_ORIGIN!;
  const api=async(url:string,body?:unknown,method='GET')=>{
   const session=await(await fetch(origin+'/api/session',{headers:{Cookie:cookie}})).json();
   const response=await fetch(origin+'/api'+url,{method,headers:{Cookie:cookie,Origin:origin,'Content-Type':'application/json','X-CSRF':session.csrf},...(body===undefined?{}:{body:JSON.stringify(body)})});
   if(!response.ok)throw new Error('Fixture API failed: '+response.status);return response.json();
  };
  const connection=await api('/settings/connection');
  await api('/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-notification',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');
  await page.getByLabel('Message or goal').fill(`In thirty seconds, remind me: ${message}`);
  await page.getByLabel('Message or goal').press('Enter');
  await page.getByRole('button',{name:/1 approval/}).click();
  const dialog=page.getByRole('dialog');
  await expect(dialog.getByRole('heading',{name:'Reminder review'})).toBeVisible();
  await expect(dialog).toContainText(title);await expect(dialog).toContainText(message);
  const evidence=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(evidence,{recursive:true});
  await page.screenshot({path:path.join(evidence,'native-notification-review.png')});
  await dialog.getByRole('button',{name:'Schedule reminder',exact:true}).click();
  await expect.poll(async()=> (await api('/state')).delegations.length).toBe(1);
  await expect.poll(()=>calls).toBe(2);
  const approved=await api('/state');
  expect(approved.delegations[0].schedule.kind).toBe('once');
  expect(approved.delegationOccurrences).toHaveLength(0);
  const browserClosedAt=new Date().toISOString();
  await browser.close();
  expect(browser.isConnected()).toBe(false);
  await expect.poll(async()=> (await api('/state')).delegationOccurrences.find((item:any)=>item.completedAt)?.state,{timeout:60000,intervals:[1000]}).toBe('succeeded');
  const completed=await api('/state'),occurrence=completed.delegationOccurrences[0];
  expect(completed.delegationOccurrences).toHaveLength(1);
  expect(completed.delegations[0].nextRunUtc).toBeNull();
  expect(occurrence.readAt).toBeNull();expect(occurrence.notificationStatus).toBe('accepted');
  expect(occurrence.providerEvidence.mechanism).toBe('AppNotificationManager');
  expect(occurrence.providerEvidence.retainedInNotificationCenter).toBe(true);
  expect(calls).toBe(2);expect(providerError).toBe('');
  fs.writeFileSync(path.join(evidence,'native-notification-receipt.json'),JSON.stringify({
   passed:true,scope:'Real scheduled Windows dispatch; synthetic planning provider; human visual acceptance is separate.',
   title,message,dueUtc,timeZone,browserClosedAt,hostRanAfterBrowserClosed:true,
   job:completed.delegations[0],occurrence,syntheticModelCalls:calls,liveModelCalls:0,humanObserved:null
  },null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
