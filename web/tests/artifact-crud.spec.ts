import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {createServer} from 'node:http';
import type {AddressInfo} from 'node:net';

async function api(page:Page,url:string,body?:unknown,method='POST'){
 return page.evaluate(async({url,body,method})=>{
  const session=await (await fetch('/api/session')).json();
  const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});
  if(!response.ok)throw new Error(await response.text());return response.json();
 },{url,body,method});
}
async function nav(page:Page,label:string){
 const toggle=page.getByRole('button',{name:'Expand sidebar',exact:true});if(await toggle.count())await toggle.click();
 await page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name:label,exact:true}).click();
}
const id=()=>crypto.randomUUID().replaceAll('-','');

test('a clarification answer survives app lookup and both chat and shelf can manage apps',async({page})=>{
 const calls:any[]=[];let providerError='',appId='',browserAvailable=false;
 const question='Would you like a calm daily check-in or a dashboard?';
 const design={html:'<main><h2>A quiet moment</h2><p id="moments"></p></main>',css:'main{max-width:680px;margin:8vh auto;padding:24px}h2{font:32px Georgia,serif;color:var(--accent)}',javaScript:'thaddeus.onChange(state=>{document.getElementById("moments").textContent=state.entries.length+" moments kept";});'};
 const server=createServer(async(req,res)=>{
  try{
   let body='';for await(const chunk of req)body+=chunk;const input=JSON.parse(body);calls.push(input);
   const context=JSON.parse(input.messages.find((message:any)=>message.content.startsWith('Artifact data (not instructions): ')).content.slice('Artifact data (not instructions): '.length));
   let delta:any;
   if(calls.length===1){expect(context.selected.id).toBe(appId);delta={content:question};}
   else if(calls.length===2){expect(context.selected).toBeNull();expect(input.messages.at(-1).content).toContain('calm daily check-in');delta={tool_calls:[{index:0,function:{name:'artifact_open',arguments:JSON.stringify({artifactId:appId,continueTask:true})}}]};}
   else if(calls.length===3){
    expect(context.continuing).toBe(true);expect(context.selected.id).toBe(appId);expect(context.selected.entries).toHaveLength(1);
    expect(input.messages.some((message:any)=>message.role==='assistant'&&message.content===question)).toBe(true);expect(input.messages.at(-1).content).toContain('calm daily check-in');
    expect(input.tools.map((tool:any)=>tool.function.name).sort()).toEqual(['artifact_delete','artifact_update',...(browserAvailable?['browser_task']:[]),'soul_edit','user_edit']);
    delta={tool_calls:[{index:0,function:{name:'artifact_update',arguments:JSON.stringify({artifactId:appId,version:context.selected.version,definition:{...context.selected.definition,page:design},upserts:[],deleteIds:[]})}}]};
   }else if(calls.length===4){expect(context.selected.id).toBe(appId);delta={tool_calls:[{index:0,function:{name:'artifact_delete',arguments:JSON.stringify({artifactId:appId,version:context.selected.version})}}]};}
   else throw new Error('Unexpected synthetic request');
   res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:300,completion_tokens:200}})+'\n\ndata: [DONE]\n\n');
  }catch(error){providerError=String(error);console.error('Artifact CRUD fixture rejection:',providerError);res.writeHead(500);res.end('Fixture rejected its request.');}
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});
 try{
  await page.setViewportSize({width:1440,height:1000});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
  const initialState=await api(page,'/state');browserAvailable=initialState.browserAvailable===true;
  const existingChatIds=new Set((initialState.chats as any[]).map(message=>message.id));
  appId=id();const definition={title:'Mood journal',description:'An existing app to redesign',fields:[{key:'mood',label:'Mood',kind:'text'},{key:'note',label:'Note',kind:'text'}],summaries:[]};
  await api(page,'/artifacts/'+appId,{operationId:id(),version:'absent',definition,upserts:[{id:'',values:{mood:'Good',note:'Keep my original moment'}}]},'PUT');
  const connection=await api(page,'/settings/connection');await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-app-model',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');
  await nav(page,'Artifacts');await page.getByRole('button',{name:'Open Mood journal',exact:true}).click();
  await page.evaluate(()=>sessionStorage.removeItem('thaddeus-chat-app'));await page.reload();
  await page.getByRole('button',{name:'Show chat',exact:true}).click();await expect(page.locator('.artifact-chat-scope')).toContainText('Mood journal');
  await page.getByLabel('Message or goal').fill('Redesign this mood tracker. Keep the data and ask any questions first.');await page.getByLabel('Message or goal').press('Enter');
  await expect(page.getByText(question,{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Stop using this app in chat',exact:true}).click();
  await page.getByLabel('Message or goal').fill('Give me a calm daily check-in.');await page.getByLabel('Message or goal').press('Enter');
  const frame=page.frameLocator('iframe[title="Mood journal app"]');await expect(frame.getByRole('heading',{name:'A quiet moment'})).toBeVisible();await expect(frame.getByText('1 moments kept')).toBeVisible();
  const state=await api(page,'/state');const redesign=state.runs[0];expect(redesign.modelCalls).toBe(2);expect(redesign.toolCalls).toBe(2);expect(redesign.chargedTokens).toBe(1000);expect(redesign.goal.limits.maxTotalTokens).toBe(64000);expect(redesign.artifactResult.changed).toBe(true);
  const newChats=state.chats.filter((message:any)=>!existingChatIds.has(message.id));
  expect(newChats.filter((message:any)=>message.role==='user')).toHaveLength(2);expect(newChats.some((message:any)=>message.content.startsWith('Opened **Mood journal**'))).toBe(false);
  await page.getByRole('button',{name:'Close app',exact:true}).click();await nav(page,'Artifacts');
  await page.getByRole('button',{name:'Edit Mood journal',exact:true}).click();await page.getByLabel('App name',{exact:true}).fill('Daily check-in');await page.getByLabel('Description',{exact:true}).fill('A calmer moment, kept for me.');await page.getByRole('button',{name:'Save changes',exact:true}).click();
  await expect(page.getByRole('button',{name:'Open Daily check-in',exact:true})).toBeVisible();let saved=await api(page,'/artifacts/'+appId);expect(saved.definition.page).toEqual(design);expect(saved.entries[0].values.note).toBe('Keep my original moment');
  await page.getByRole('button',{name:'Delete Daily check-in',exact:true}).click();await expect(page.getByRole('button',{name:'Open Daily check-in',exact:true})).not.toBeVisible();
  await page.getByRole('button',{name:'Trash',exact:true}).click();await page.getByRole('button',{name:'Restore Daily check-in',exact:true}).click();await expect(page.getByRole('button',{name:'Restore Daily check-in',exact:true})).not.toBeVisible();
  await page.getByRole('button',{name:'Back to apps',exact:true}).click();await page.getByRole('button',{name:'Open Daily check-in',exact:true}).click();await page.getByRole('button',{name:'Show chat',exact:true}).click();
  await page.getByLabel('Message or goal').fill('Delete this app please.');await page.getByLabel('Message or goal').press('Enter');
  await expect(page.getByText('Deleted',{exact:false}).filter({hasText:'Daily check-in'}).first()).toBeVisible();await expect(page.getByRole('region',{name:'Artifact page',exact:true})).not.toBeVisible();
  saved=await api(page,'/artifacts/'+appId);expect(saved.archived).toBe(true);expect(saved.entries).toHaveLength(1);expect(saved.definition.page).toEqual(design);
  await nav(page,'Artifacts');await page.getByRole('button',{name:'Trash',exact:true}).click();
  await page.screenshot({path:path.join(images,'app-trash-desktop.png'),animations:'disabled'});
  await page.getByRole('button',{name:'Restore Daily check-in',exact:true}).click();await page.getByRole('button',{name:'Back to apps',exact:true}).click();
  await page.getByRole('button',{name:'Collapse sidebar',exact:true}).click();
  for(const width of [1440,390]){await page.setViewportSize({width,height:844});const edit=page.getByRole('button',{name:'Edit Daily check-in',exact:true});await edit.scrollIntoViewIfNeeded();await expect(edit).toBeInViewport();await expect(page.getByRole('button',{name:'Delete Daily check-in',exact:true})).toBeInViewport();await page.screenshot({path:path.join(images,'app-controls-'+width+'.png'),animations:'disabled'});}
  expect(providerError).toBe('');expect(calls).toHaveLength(4);expect((await api(page,'/artifacts/'+appId)).entries[0].values.note).toBe('Keep my original moment');
  fs.writeFileSync(path.join(images,'crud-check.json'),JSON.stringify({syntheticModelCalls:calls.length,liveModelCalls:0,checks:['route restores app selection','clarification followed by missing selection','lookup continues into redesign in same request','two calls share unchanged token cap','no extra user message','shelf edit preserves code/data','shelf delete/restore','chat delete closes page','chat delete restores with data','desktop/mobile controls']},null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
