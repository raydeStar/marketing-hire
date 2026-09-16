import {test,expect,type Page} from '@playwright/test';
import {createServer,type ServerResponse} from 'node:http';
import {createServer as createPortReservation,type AddressInfo} from 'node:net';
import {spawn,type ChildProcess} from 'node:child_process';
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
 response.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:240,completion_tokens:90}})+'\n\ndata: [DONE]\n\n');
}

function delegationFacts(input:any){
 const content=input.messages.find((message:any)=>typeof message.content==='string'&&message.content.includes('Frozen request timestamp:'))?.content||'';
 const requested=content.match(/Frozen request timestamp: ([^\n]+)/)?.[1];
 const timeZone=content.match(/Exact local timezone: ([^\n]+)/)?.[1];
 const jobsText=content.split('Current delegated jobs (host data): ')[1]?.split('\nNo additional reminder proposal')[0];
 if(!requested||!timeZone)throw new Error('Synthetic provider did not receive frozen delegation facts.');
 return{requested:new Date(requested),timeZone,jobs:jobsText?JSON.parse(jobsText):[]};
}

function tool(input:any,predicate:(name:string)=>boolean){
 const match=input.tools.find((item:any)=>predicate(item.function.name));
 if(!match)throw new Error('Expected delegation tool was not advertised.');
 return match.function;
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

async function reservePort(){
 const reservation=createPortReservation();await new Promise<void>(resolve=>reservation.listen(0,'127.0.0.1',resolve));
 const port=(reservation.address() as AddressInfo).port;await new Promise<void>((resolve,reject)=>reservation.close(error=>error?reject(error):resolve()));return port;
}

async function stop(child:ChildProcess){
 if(child.exitCode!==null)return;child.kill('SIGTERM');
 await Promise.race([new Promise<void>(resolve=>child.once('exit',()=>resolve())),new Promise<void>((_,reject)=>setTimeout(()=>reject(new Error('MCP fixture did not stop.')),10000))]);
}

test('packaged chat manages email and recurring brief through an MCP connector',async({page})=>{
 test.setTimeout(120000);
 const mcpPort=await reservePort();
 const fixture=path.resolve('../tools/Thaddeus.McpFixture/bin/Debug/net10.0/Thaddeus.McpFixture.dll');
 expect(fs.existsSync(fixture),'Build Thaddeus.McpFixture before this packaged check.').toBe(true);
 const mcp=spawn('dotnet',[fixture,'--urls',`http://127.0.0.1:${mcpPort}`],{windowsHide:true,stdio:'ignore'});
 const calls:any[]=[];let providerError='';
 const model=createServer(async(request,response)=>{
  try{
   expect(request.url).toBe('/v1/chat/completions');let raw='';for await(const chunk of request)raw+=chunk;
   const input=JSON.parse(raw);calls.push(input);const call=calls.length;const facts=delegationFacts(input);
   if(call===1||call===2){
    const email=tool(input,name=>name.startsWith('delegation_email_'));
    answer(response,{tool_calls:[{index:0,function:{name:email.name,arguments:JSON.stringify({dueUtc:new Date(facts.requested.getTime()+2*3_600_000).toISOString(),timeZone:facts.timeZone,arguments:{to:'owner-test@example.invalid',subject:'Running late',body:'I am running late.'}})}}]});
   }else if(call===3)answer(response,{content:'The exact email is scheduled for two hours from now. It has not been sent yet.'});
   else if(call===4){
    const job=facts.jobs.find((item:any)=>item.kind==='email');tool(input,name=>name==='delegation_edit_email');
    answer(response,{tool_calls:[{index:0,function:{name:'delegation_edit_email',arguments:JSON.stringify({jobId:job.id,version:job.version,recipient:'owner-test@example.invalid',subject:'Running late',body:'I am running about fifteen minutes late.'})}}]});
   }else if(call===5||call===6){
    const brief=tool(input,name=>name.startsWith('delegation_brief_'));
    answer(response,{tool_calls:[{index:0,function:{name:brief.name,arguments:JSON.stringify({localTime:'08:00',timeZone:facts.timeZone,destination:'owner:in-app',emailSelectionRule:'At most 10 unread or important messages since the prior brief.',emailArguments:{limit:10,since:'{{sinceUtc}}'},calendarArguments:{timeMin:'{{startUtc}}',timeMax:'{{endUtc}}'}})}}]});
   }else if(call===7)answer(response,{content:'The weekday brief is scheduled for 8:00 AM. Source items remain unchanged.'});
   else if(call===8||call===9){
    const job=facts.jobs.find((item:any)=>item.kind==='brief');const name=call===8?'delegation_pause_brief':'delegation_resume_brief';tool(input,value=>value===name);
    answer(response,{tool_calls:[{index:0,function:{name,arguments:JSON.stringify({jobId:job.id,version:job.version})}}]});
   }else if(call===10){
    const job=facts.jobs.find((item:any)=>item.kind==='brief');tool(input,name=>name==='delegation_edit_brief');
    answer(response,{tool_calls:[{index:0,function:{name:'delegation_edit_brief',arguments:JSON.stringify({jobId:job.id,version:job.version,localTime:'09:00',timeZone:facts.timeZone,emailSelectionRule:'At most 5 unread or important messages since the prior brief.',emailArguments:{limit:5,since:'{{sinceUtc}}'},calendarArguments:{timeMin:'{{startUtc}}',timeMax:'{{endUtc}}'}})}}]});
   }else throw new Error('Unexpected synthetic model request '+call+'.');
  }catch(error){providerError=String(error);response.writeHead(500);response.end('Synthetic provider rejected its request.');}
 });
 await new Promise<void>(resolve=>model.listen(0,'127.0.0.1',resolve));
 try{
  for(let attempt=0;attempt<100;attempt++){
   if(mcp.exitCode!==null)throw new Error('MCP fixture exited before readiness.');
   try{if((await fetch(`http://127.0.0.1:${mcpPort}/health`)).ok)break;}catch{}
   if(attempt===99)throw new Error('MCP fixture did not become ready.');await new Promise(resolve=>setTimeout(resolve,100));
  }
  await page.setViewportSize({width:1440,height:1000});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
  const connection=await api(page,'/settings/connection');
  await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-delegation-model',reasoning:'high',endpoint:`http://127.0.0.1:${(model.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');
  const before=await api(page,'/settings/mcp');
  const configured=await api(page,'/settings/mcp',{version:before.version,name:'Fictional owner services',endpoint:`http://127.0.0.1:${mcpPort}/mcp`,storage:'none'},'PUT');
  expect(configured.connectors).toHaveLength(1);expect(configured.connectors[0].tools).toHaveLength(3);
  expect(configured.connectors[0].tools.filter((item:any)=>item.effect==='read external data')).toHaveLength(2);

  const conversation=page.getByRole('region',{name:'Conversation',exact:true});
  await send(page,"In two hours, email my boss and tell them I'm running late.");
  await expect(conversation.getByText(/What exact email address should receive this/)).toBeVisible();
  expect((await api(page,'/state')).delegations).toHaveLength(0);

  await send(page,'Send it to owner-test@example.invalid in two hours.');
  await page.getByRole('button',{name:/1 approval/}).click();let dialog=page.getByRole('dialog');
  await expect(dialog.getByRole('heading',{name:'Scheduled email review'})).toBeVisible();await expect(dialog).toContainText('Fictional owner services');
  await expect(dialog).toContainText('owner-test@example.invalid');await expect(dialog).toContainText('I am running late.');
  await dialog.getByRole('button',{name:'Schedule this email',exact:true}).click();
  await expect(conversation.getByText(/The exact email is scheduled for two hours from now/)).toBeVisible();await dialog.getByRole('button',{name:'Close dialog'}).click();
  let state=await api(page,'/state');let email=state.delegations.find((item:any)=>item.kind==='email');const originalEmailVersion=email.version;
  expect(email.state).toBe('scheduled');expect(email.action.payload.body).toBe('I am running late.');

  await send(page,'Change the scheduled email to say I am running about fifteen minutes late.');
  await page.getByRole('button',{name:/1 approval/}).click();dialog=page.getByRole('dialog');
  await expect(dialog.getByRole('heading',{name:'Edit scheduled email'})).toBeVisible();await expect(dialog).toContainText('I am running about fifteen minutes late.');
  await dialog.getByRole('button',{name:'Replace exact email',exact:true}).click();await dialog.getByRole('button',{name:'Close dialog'}).click();
  state=await api(page,'/state');email=state.delegations.find((item:any)=>item.kind==='email');expect(email.version).toBeGreaterThan(originalEmailVersion);expect(email.action.payload.body).toBe('I am running about fifteen minutes late.');

  await send(page,'Every weekday morning, give me my calendar and important email.');
  await expect(conversation.getByText(/What local time should the weekday brief arrive/)).toBeVisible();
  expect((await api(page,'/state')).delegations.filter((item:any)=>item.kind==='brief')).toHaveLength(0);

  await send(page,'At 8 AM.');
  await page.getByRole('button',{name:/1 approval/}).click();dialog=page.getByRole('dialog');
  await expect(dialog.getByRole('heading',{name:'Recurring brief review'})).toBeVisible();await expect(dialog).toContainText('08:00');
  await expect(dialog).toContainText('At most 10 unread or important messages');
  await dialog.getByRole('button',{name:'Schedule this brief',exact:true}).click();
  await expect(conversation.getByText(/The weekday brief is scheduled for 8:00 AM/)).toBeVisible();await dialog.getByRole('button',{name:'Close dialog'}).click();
  state=await api(page,'/state');let brief=state.delegations.find((item:any)=>item.kind==='brief');expect(brief.state).toBe('scheduled');expect(brief.schedule.localTime).toBe('08:00');

  await send(page,'Pause my weekday brief.');await approve(page,'Pause this brief');
  state=await api(page,'/state');brief=state.delegations.find((item:any)=>item.kind==='brief');expect(brief.state).toBe('paused');
  await send(page,'Resume my weekday brief.');await approve(page,'Resume this brief');
  state=await api(page,'/state');brief=state.delegations.find((item:any)=>item.kind==='brief');expect(brief.state).toBe('scheduled');
  const preEditVersion=brief.version;
  await send(page,'Move my weekday brief to 9 AM and limit it to five important messages.');await approve(page,'Replace brief scope');
  state=await api(page,'/state');brief=state.delegations.find((item:any)=>item.kind==='brief');expect(brief.version).toBeGreaterThan(preEditVersion);expect(brief.schedule.localTime).toBe('09:00');
  expect(brief.action.payload.emailSelectionRule).toContain('At most 5');expect(brief.action.payload.email.arguments.limit).toBe(5);

  const health=await(await fetch(`http://127.0.0.1:${mcpPort}/health`)).json();expect(health.externalCallsAttempted).toBe(0);
  expect(calls).toHaveLength(10);expect(providerError).toBe('');
  const screenshots=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(screenshots,{recursive:true});
  fs.writeFileSync(path.join(screenshots,'delegation-connected-chat-check.json'),JSON.stringify({passed:true,protocol:'MCP Streamable HTTP via official .NET SDK',connectorTools:3,syntheticModelCalls:calls.length,liveModelCalls:0,externalToolCalls:health.externalCallsAttempted,checks:['relationship recipient clarification','exact email review','scheduled email replacement','missing brief time clarification','bounded read-only brief review','pause recurring brief','resume recurring brief','edit recurring brief scope']},null,2));
 }finally{
  model.closeAllConnections();await new Promise<void>(resolve=>model.close(()=>resolve()));await stop(mcp);
 }
});
