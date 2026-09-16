import {test,expect,type Page} from '@playwright/test';
import {createServer,type ServerResponse} from 'node:http';
import type {AddressInfo} from 'node:net';
import fs from 'node:fs';
import path from 'node:path';

async function api(page:Page,url:string,body?:unknown,method='POST'){
 return page.evaluate(async({url,body,method})=>{
  const session=await(await fetch('/api/session')).json();
  const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});
  if(!response.ok)throw new Error(await response.text());return response.json();
 },{url,body,method});
}

function answer(response:ServerResponse,delta:unknown){
 response.writeHead(200,{'Content-Type':'text/event-stream'});
 response.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:220,completion_tokens:80}})+'\n\ndata: [DONE]\n\n');
}

function delegationFacts(input:any){
 const content=input.messages.find((message:any)=>typeof message.content==='string'&&message.content.includes('Frozen request timestamp:'))?.content||'';
 const requested=content.match(/Frozen request timestamp: ([^\n]+)/)?.[1];
 const timeZone=content.match(/Exact local timezone: ([^\n]+)/)?.[1];
 const jobsText=content.split('Current delegated jobs (host data): ')[1]?.split('\nNo additional reminder proposal')[0];
 if(!requested||!timeZone)throw new Error('Synthetic provider did not receive frozen delegation facts.');
 return{requested:new Date(requested),timeZone,jobs:jobsText?JSON.parse(jobsText):[]};
}

async function send(page:Page,message:string){
 const composer=page.getByLabel('Message or goal');await composer.fill(message);await composer.press('Enter');
}

async function approve(page:Page,button:string){
 await page.getByRole('button',{name:/1 approval/}).click();
 const dialog=page.getByRole('dialog');await expect(dialog.getByRole('button',{name:button,exact:true})).toBeVisible();
 await dialog.getByRole('button',{name:button,exact:true}).click();
 await expect.poll(async()=>((await api(page,'/state')).runs as any[]).every(run=>run.state!=='running'&&run.state!=='paused'),{timeout:15000}).toBe(true);
 await dialog.getByRole('button',{name:'Close dialog'}).click();
}

test('packaged chat clarifies ambiguous delegated work and creates approved source-linked To-dos',async({page})=>{
 test.setTimeout(90000);
 const calls:any[]=[];let providerError='';
 const server=createServer(async(request,response)=>{
  try{
   expect(request.url).toBe('/v1/chat/completions');let raw='';for await(const chunk of request)raw+=chunk;
   const input=JSON.parse(raw);calls.push(input);const call=calls.length;
   if(call===1||call===3){
    const facts=delegationFacts(input),hours=call===1?2:3,title=call===1?'Call dentist':'Pick up prescription';
    answer(response,{tool_calls:[{index:0,function:{name:'delegation_schedule_reminder',arguments:JSON.stringify({title,message:title+'.',dueUtc:new Date(facts.requested.getTime()+hours*3_600_000).toISOString(),timeZone:facts.timeZone})}}]});
   }else if(call===2)answer(response,{content:'The dentist reminder is scheduled. The host must remain awake.'});
   else if(call===4)answer(response,{content:'The prescription reminder is scheduled. The host must remain awake.'});
   else if(call===5||call===6){
    const facts=delegationFacts(input);expect(facts.jobs).toHaveLength(2);const selected=facts.jobs[call===5?0:1];
    answer(response,{tool_calls:[{index:0,function:{name:'delegation_cancel',arguments:JSON.stringify({jobId:selected.id,version:selected.version})}}]});
   }else if(call===7){
    const facts=delegationFacts(input),job=facts.jobs.find((item:any)=>item.title==='Call dentist');
    answer(response,{tool_calls:[{index:0,function:{name:'delegation_reschedule_reminder',arguments:JSON.stringify({jobId:job.id,version:job.version,dueUtc:new Date(facts.requested.getTime()+4*3_600_000).toISOString(),timeZone:facts.timeZone})}}]});
   }else if(call===8){
    const tool=input.tools.find((item:any)=>item.function.name==='todo_batch_create');
    const reference=tool.function.parameters.properties.sourceReference.enum[0],version=tool.function.parameters.properties.sourceVersion.enum[0];
    answer(response,{tool_calls:[{index:0,function:{name:'todo_batch_create',arguments:JSON.stringify({sourceReference:reference,sourceVersion:version,items:[
     {title:'Submit the fictional application',notes:'Use the portal named in the supplied checklist.',due:'2026-09-18',ambiguity:null},
     {title:'Follow up about the fictional interview',notes:'Follow up after submitting the application.',due:null,ambiguity:'The source says next week but gives no exact date.'}
    ]})}}]});
   }else throw new Error('Unexpected synthetic model request '+call+'.');
  }catch(error){providerError=String(error);response.writeHead(500);response.end('Synthetic provider rejected its request.');}
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  await page.setViewportSize({width:1440,height:1000});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
  const conversation=page.getByRole('region',{name:'Conversation',exact:true});
  const connection=await api(page,'/settings/connection');
  await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-delegation-model',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');

  await send(page,'In two hours, remind me to call the dentist.');
  await page.getByRole('button',{name:/1 approval/}).click();let dialog=page.getByRole('dialog');
  await expect(dialog.getByRole('heading',{name:'Reminder review'})).toBeVisible();await expect(dialog).toContainText('Call dentist');
  await dialog.getByRole('button',{name:'Schedule reminder',exact:true}).click();
  await expect(conversation.getByText('The dentist reminder is scheduled. The host must remain awake.',{exact:true})).toBeVisible();
  await dialog.getByRole('button',{name:'Close dialog'}).click();

  await send(page,'In three hours, remind me to pick up the prescription.');
  await approve(page,'Schedule reminder');
  await expect(conversation.getByText('The prescription reminder is scheduled. The host must remain awake.',{exact:true})).toBeVisible();

  await send(page,'Cancel one of my reminders.');
  await expect(conversation.getByText('Which one should I cancel?',{exact:false})).toBeVisible();
  await expect(conversation.getByRole('listitem').filter({hasText:'Call dentist'})).toBeVisible();await expect(conversation.getByRole('listitem').filter({hasText:'Pick up prescription'})).toBeVisible();
  await expect(conversation.getByText('Reply with the number or title.',{exact:false})).toBeVisible();await expect(conversation.getByText('Nothing has changed yet',{exact:false})).toBeVisible();await expect(page.getByRole('button',{name:/approval/})).toHaveCount(0);

  await send(page,'Cancel the second one.');
  await page.getByRole('button',{name:/1 approval/}).click();dialog=page.getByRole('dialog');
  await expect(dialog.getByRole('heading',{name:'Cancel delegated work'})).toBeVisible();await expect(dialog).toContainText('Pick up prescription');
  await dialog.getByRole('button',{name:'Cancel this job',exact:true}).click();
  await expect(conversation.getByText('Cancelled Pick up prescription.',{exact:true})).toBeVisible();
  await dialog.getByRole('button',{name:'Close dialog'}).click();
  let state=await api(page,'/state');const originalDentistDue=state.delegations.find((job:any)=>job.title==='Call dentist').nextRunUtc;expect(state.delegations.find((job:any)=>job.title==='Call dentist').state).toBe('scheduled');expect(state.delegations.find((job:any)=>job.title==='Pick up prescription').state).toBe('cancelled');

  await send(page,'Move the dentist reminder to four hours from now.');
  await page.getByRole('button',{name:/1 approval/}).click();dialog=page.getByRole('dialog');
  await expect(dialog.getByRole('heading',{name:'Reschedule reminder'})).toBeVisible();await expect(dialog).toContainText('Call dentist');await expect(dialog).toContainText('New time');
  await dialog.getByRole('button',{name:'Use this new time',exact:true}).click();
  await expect(conversation.getByText(/Rescheduled Call dentist for/)).toBeVisible();await dialog.getByRole('button',{name:'Close dialog'}).click();
  state=await api(page,'/state');expect(state.delegations.find((job:any)=>job.title==='Call dentist').nextRunUtc).not.toBe(originalDentistDue);expect(state.delegations.find((job:any)=>job.title==='Pick up prescription').state).toBe('cancelled');

  await page.locator('.conversation-compose input[type=file]').setInputFiles({name:'fictional-checklist.txt',mimeType:'text/plain',buffer:Buffer.from('Submit the fictional application by September 18. Follow up about the fictional interview next week.')});
  await send(page,'Turn this into To-dos.');
  await page.getByRole('button',{name:/1 approval/}).click();dialog=page.getByRole('dialog');
  await expect(dialog.getByRole('heading',{name:'To-do batch review'})).toBeVisible();await expect(dialog).toContainText('Submit the fictional application');await expect(dialog).toContainText('gives no exact date');
  await dialog.getByRole('button',{name:'Create these To-dos',exact:true}).click();
  await expect(conversation.getByText(/Created 2 editable To-dos from upload:/)).toBeVisible();await expect(conversation.getByText(/1 item keeps an unresolved detail/)).toBeVisible();
  await dialog.getByRole('button',{name:'Close dialog'}).click();
  state=await api(page,'/state');const todos=state.library.filter((item:any)=>item.kind==='todo');expect(todos).toHaveLength(2);expect(todos.every((item:any)=>item.content.includes('Source: upload:'))).toBe(true);
  expect(calls).toHaveLength(8);expect(providerError).toBe('');
  const screenshots=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(screenshots,{recursive:true});
  fs.writeFileSync(path.join(screenshots,'delegation-chat-check.json'),JSON.stringify({passed:true,syntheticModelCalls:calls.length,liveModelCalls:0,checks:['two reviewed reminders','host-enforced ambiguous choice','ordinal follow-up','exact cancellation approval','reviewed reminder reschedule','superseded schedule invalidated','uploaded reading','one batch approval','two editable source-linked To-dos','unresolved date retained']},null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
