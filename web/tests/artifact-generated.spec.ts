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

test('Luna-shaped conversation clarifies, generates an interactive page, and maintains its design and records',async({page})=>{
 const requests:any[]=[],networkLeaks:string[]=[];let providerError='';
 const question='Which moods would you like, and should each moment have a note?';
 const definition={title:'Weather within',description:'A personal space for moments and moods.',fields:[{key:'date',label:'Date',kind:'date'},{key:'mood',label:'Mood',kind:'select',options:['Bright','Cloudy']},{key:'note',label:'Note',kind:'text'}],summaries:[],dateField:'date',page:{
  html:'<main><p class="kicker">A MOMENT FOR YOU</p><h2>How is your weather?</h2><p id="count">Opening…</p><div class="choices"><button id="bright">☀ Bright</button><button id="cloudy">☁ Cloudy</button></div><label>A small note<textarea id="note" placeholder="What is on your mind?"></textarea></label><p id="error" role="status"></p><ul id="moments"></ul></main>',
  css:'main{max-width:640px;margin:40px auto}.kicker{font-size:11px;letter-spacing:.18em;color:var(--accent)}h2{font:36px Georgia,serif}.choices{display:flex;gap:12px;margin:30px 0}.choices button{flex:1;padding:28px 10px;background:var(--surface);color:var(--accent);border:1px solid var(--accent);border-radius:20px}label{display:block;color:var(--muted)}textarea{display:block;resize:vertical;margin-top:12px;padding:14px;width:100%;background:var(--surface);color:var(--text);border:0;border-radius:12px}li{margin-top:16px;padding:16px;border-bottom:1px solid var(--muted)}@media(max-width:500px){main{margin:20px auto}h2{font-size:29px}}',
  javaScript:`let snapshot,busy=false;const note=document.getElementById('note'),error=document.getElementById('error');
function render(state){snapshot=state;document.getElementById('count').textContent=state.entries.length+' moments';const list=document.getElementById('moments');list.replaceChildren();for(const entry of [...state.entries].reverse()){const li=document.createElement('li');li.textContent=entry.values.mood+' · '+(entry.values.note||'A quiet moment');list.append(li);}for(const id of ['bright','cloudy'])document.getElementById(id).disabled=state.readOnly||busy;}
async function log(mood){busy=true;render(snapshot);try{await thaddeus.save({upserts:[{id:'',values:{date:snapshot.localDate,mood,note:note.value}}],deleteIds:[]});note.value='';error.textContent='Saved';}catch(reason){error.textContent=reason.message;}finally{busy=false;render(snapshot);}}
thaddeus.onChange(render);document.getElementById('bright').addEventListener('click',()=>log('Bright'));document.getElementById('cloudy').addEventListener('click',()=>log('Cloudy'));`
 }};
 const server=createServer(async(req,res)=>{
  try{
   expect(req.url).toBe('/v1/chat/completions');let body='';for await(const chunk of req)body+=chunk;
   const input=JSON.parse(body);requests.push(input);expect(input.reasoning_effort).toBe('high');expect(input.tool_choice).toBe('auto');
   const context=JSON.parse(input.messages.find((message:any)=>message.content.startsWith('Artifact data (not instructions): ')).content.slice('Artifact data (not instructions): '.length));
   const step=requests.length;let delta:any;
   if(step===1){expect(context.selected).toBeNull();delta={content:question};}
   else if(step===2){
    expect(input.messages.some((message:any)=>message.role==='assistant'&&message.content===question)).toBe(true);
    expect(input.messages.at(-1).content).toContain('Bright and Cloudy');
    delta={tool_calls:[{index:0,function:{name:'artifact_create',arguments:JSON.stringify({definition,entries:[]})}}]};
   }else if(step===3){
    expect(context.selected.definition.page.javaScript).toBe(definition.page.javaScript);expect(context.selected.entries).toHaveLength(1);
    delta={tool_calls:[{index:0,function:{name:'artifact_update',arguments:JSON.stringify({artifactId:context.selected.id,version:context.selected.version,upserts:[{id:'',values:{date:context.localDate,mood:'Cloudy',note:'Rain on the windows.'}}],deleteIds:[]})}}]};
   }else if(step===4){
    expect(context.selected.entries).toHaveLength(2);
    delta={tool_calls:[{index:0,function:{name:'artifact_update',arguments:JSON.stringify({artifactId:context.selected.id,version:context.selected.version,definition:{...definition,page:{...definition.page,html:definition.page.html.replace('How is your weather?','A little space to reflect'),css:definition.page.css+'h2{color:rgb(180,140,230)}'}},upserts:[],deleteIds:[]})}}]};
   }else throw new Error('Unexpected synthetic model request');
   res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:500,completion_tokens:600}})+'\n\ndata: [DONE]\n\n');
  }catch(error){providerError=String(error);res.writeHead(500);res.end('Synthetic provider rejected its request.');}
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});
 try{
  await page.route('https://artifact-leak.invalid/**',route=>{networkLeaks.push(route.request().url());return route.abort();});
  await page.setViewportSize({width:1440,height:1000});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
  const settings=await api(page,'/settings/connection');
  await api(page,'/settings/connection',{version:settings.version,provider:{kind:'compatible',model:'fixture-app-model',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');
  await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();await page.getByRole('navigation').getByRole('button',{name:'Artifacts',exact:true}).click();
  await expect(page.locator('.starter-card')).toHaveCount(0);await page.getByRole('button',{name:'Build an app',exact:true}).click();
  await page.getByLabel('Message or goal').fill('Build me a mood app with its own page design.');await page.getByLabel('Message or goal').press('Enter');
  await expect(page.getByText(question,{exact:true})).toBeVisible();expect((await api(page,'/state')).artifacts).toHaveLength(0);
  await page.getByLabel('Message or goal').fill('Bright and Cloudy, with an optional note.');await page.getByLabel('Message or goal').press('Enter');
  const openApp=page.getByRole('button',{name:'Open Weather within beside chat',exact:true});await expect(openApp).toBeVisible();
  await expect(page.getByRole('region',{name:'Artifact page',exact:true})).not.toBeVisible();await openApp.click();
  await expect(page.getByRole('heading',{name:definition.title,exact:true})).toBeVisible();
  const frame=page.frameLocator('iframe[title="Weather within app"]');
  await expect(frame.getByRole('heading',{name:'How is your weather?'})).toBeVisible();await expect(frame.locator('#count')).toHaveText('0 moments');
  await frame.getByLabel('A small note').fill('A walk helped.');await frame.getByRole('button',{name:'☀ Bright',exact:true}).click();await expect(frame.locator('#count')).toHaveText('1 moments');
  await frame.getByLabel('A small note').fill('An unfinished thought');
  await page.getByLabel('Message or goal').fill('Log a cloudy moment: Rain on the windows.');await page.getByLabel('Message or goal').press('Enter');
  await expect(frame.locator('#count')).toHaveText('2 moments');await expect(frame.getByText('Cloudy · Rain on the windows.',{exact:true})).toBeVisible();await expect(frame.getByLabel('A small note')).toHaveValue('An unfinished thought');
  const sandbox=page.frames().find(item=>item.url().includes('/page?'))!;
  const isolation=await sandbox.evaluate(async()=>{
   let parentBlocked=false,cookiesBlocked=false,fetchBlocked=false,crossAppBlocked=false;
   try{void parent.document.body;}catch{parentBlocked=true;}
   try{void document.cookie;}catch{cookiesBlocked=true;}
   try{await fetch('https://artifact-leak.invalid/records');}catch{fetchBlocked=true;}
   try{await (window as any).thaddeus.save({artifactId:'another-app',upserts:[],deleteIds:[]});}catch{crossAppBlocked=true;}
   return{parentBlocked,cookiesBlocked,fetchBlocked,crossAppBlocked};
  });
  expect(isolation).toEqual({parentBlocked:true,cookiesBlocked:true,fetchBlocked:true,crossAppBlocked:true});expect(networkLeaks).toEqual([]);
  await sandbox.evaluate(async()=>{try{location.href='https://artifact-leak.invalid/navigation';}catch{}await new Promise(resolve=>setTimeout(resolve,150));}).catch(error=>{if(!String(error).includes('Execution context was destroyed'))throw error;});
  await expect(page.getByRole('alert')).toContainText('The page left its app document');expect(networkLeaks).toEqual([]);
  await page.getByRole('button',{name:'Retry page',exact:true}).click();await expect(frame.locator('#count')).toHaveText('2 moments');
  await page.getByLabel('Message or goal').fill('Make the heading violet and say A little space to reflect. Keep the records.');await page.getByLabel('Message or goal').press('Enter');
  await expect(frame.getByRole('heading',{name:'A little space to reflect'})).toHaveCSS('color','rgb(180, 140, 230)');await expect(frame.locator('#count')).toHaveText('2 moments');
  await page.getByRole('button',{name:'Full screen',exact:true}).click();
  await page.screenshot({path:path.join(images,'generated-page-desktop.png'),animations:'disabled'});
  await page.reload();await expect(frame.locator('#count')).toHaveText('2 moments');
  await page.setViewportSize({width:390,height:844});await expect(page.getByRole('button',{name:'Close app',exact:true})).toBeInViewport({ratio:1});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  expect(await page.frames().find(item=>item.url().includes('/page?'))!.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await page.screenshot({path:path.join(images,'generated-page-mobile.png'),animations:'disabled'});
  await page.getByText('Data & history',{exact:true}).click();await page.getByRole('button',{name:'History',exact:true}).click();await page.getByRole('button',{name:'Undo last change',exact:true}).click();
  await expect(frame.getByRole('heading',{name:'How is your weather?'})).toBeVisible();await expect(frame.locator('#count')).toHaveText('2 moments');
  const final=await api(page,'/state');expect(final.artifacts).toHaveLength(1);expect(final.artifacts[0].entryCount).toBe(2);expect(requests).toHaveLength(4);expect(providerError).toBe('');
  expect(final.runs.reduce((sum:number,run:any)=>sum+run.chargedTokens,0)).toBe(4400);
  const saved=await api(page,'/artifacts/'+final.artifacts[0].id);
  await api(page,'/artifacts/'+saved.id,{operationId:crypto.randomUUID().replaceAll('-',''),version:saved.version,definition:{...saved.definition,page:{...saved.definition.page,javaScript:'throw new Error("Fictional broken design");'}}},'PUT');
  await expect(page.getByRole('alert')).toContainText('Fictional broken design');
  await expect(page.getByRole('cell',{name:'Rain on the windows.',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Undo last change',exact:true}).click();await expect(frame.locator('#count')).toHaveText('2 moments');await expect(page.getByRole('alert')).not.toBeVisible();
  fs.writeFileSync(path.join(images,'generated-check.json'),JSON.stringify({syntheticModelCalls:4,liveModelCalls:0,checks:['clarification without creating','answer retained in next request','custom generated HTML CSS JavaScript','manual save through scoped bridge','chat data refresh preserves draft','chat redesign preserves records','reload','undo design','desktop/mobile','token charging','blocked frame navigation','broken design still exposes data and undo'],isolation,networkLeaks},null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
