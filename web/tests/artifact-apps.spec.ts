import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {createServer} from 'node:http';
import type {AddressInfo} from 'node:net';

async function request(page:Page,url:string,body?:unknown,method='POST'){
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

test('ordinary chat builds flexible apps, opens them on selection, and keeps manual and chat edits in the same records',async({page})=>{
 const requests:any[]=[];
 const mood={title:'My weather within',description:'A mood journal with room for the small things.',fields:[{key:'date',label:'Date',kind:'date'},{key:'mood',label:'Mood',kind:'select',options:['Bright','Steady','Cloudy']},{key:'note',label:'A small note',kind:'text'}],summaries:[],dateField:'date'};
 const checklist={title:'The little list',description:'Things I intend to finish.',fields:[{key:'task',label:'Task',kind:'text'},{key:'done',label:'Done',kind:'checkbox'},{key:'priority',label:'Priority',kind:'select',options:['Later','Soon']}],summaries:['done']};
 let providerError='';
 const server=createServer(async(req,res)=>{
  try{
   expect(req.url).toBe('/v1/chat/completions');expect(req.method).toBe('POST');
   let body='';for await(const chunk of req)body+=chunk;const input=JSON.parse(body);requests.push(input);
   expect(input.model).toBe('fixture-app-model');expect(input.reasoning_effort).toBe('high');expect(input.parallel_tool_calls).toBe(false);
   const context=JSON.parse(input.messages.find((message:any)=>message.content.startsWith('Artifact data (not instructions): ')).content.slice('Artifact data (not instructions): '.length));
   const step=requests.length;let name='artifact_create',args:any;
   if(step===1){expect(context.selected).toBeNull();args={definition:mood,entries:[]};}
   else if(step===2){name='artifact_update';expect(context.selected.definition.title).toBe(mood.title);args={artifactId:context.selected.id,version:context.selected.version,upserts:[{id:'',values:{date:context.localDate,mood:'Bright',note:'A walk helped.'}}],deleteIds:[]};}
   else if(step===3){args={definition:checklist,entries:[{id:'',values:{task:'Water the fern',done:false,priority:'Soon'}}]};}
   else if(step===4){name='artifact_update';expect(context.selected.definition.title).toBe(checklist.title);const entry=context.selected.entries[0];args={artifactId:context.selected.id,version:context.selected.version,upserts:[{...entry,values:{...entry.values,done:false}}],deleteIds:[]};}
   else throw new Error('Unexpected synthetic model request');
   expect(input.tools.some((tool:any)=>tool.function.name===name)).toBe(true);
   res.writeHead(200,{'Content-Type':'text/event-stream'});
   res.end('data: '+JSON.stringify({choices:[{delta:{tool_calls:[{index:0,function:{name,arguments:JSON.stringify(args)}}]}}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:321,completion_tokens:123}})+'\n\ndata: [DONE]\n\n');
  }catch(error){providerError=String(error);res.writeHead(500);res.end('Synthetic fixture rejected its request.');}
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 const images=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(images,{recursive:true});
 try{
  await page.setViewportSize({width:1440,height:1000});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
  const connection=await request(page,'/settings/connection');
  await request(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-app-model',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');
  await page.getByLabel('Message or goal').fill('Build me a mood tracker called My weather within, with Bright, Steady and Cloudy moods, a date and a small note.');await page.getByLabel('Message or goal').press('Enter');
  const openMood=page.getByRole('button',{name:'Open '+mood.title+' beside chat',exact:true});await expect(openMood).toBeVisible();
  await expect(page.getByRole('region',{name:'Artifact page',exact:true})).not.toBeVisible();await openMood.click();
  await expect(page.getByRole('heading',{name:mood.title,exact:true})).toBeVisible();
  expect((await request(page,'/state')).artifacts).toHaveLength(1);
  await expect(page).toHaveURL(/\/apps\/[a-f0-9]{32}$/);
  await expect(page.getByRole('group',{name:'Artifact collections'})).not.toBeVisible();
  await page.getByLabel('Message or goal').fill('An unsent thought to keep.');
  await page.getByRole('button',{name:'Full screen',exact:true}).click();await expect(page.getByLabel('Message or goal')).not.toBeVisible();
  await page.getByRole('button',{name:'Show chat',exact:true}).click();await expect(page.getByLabel('Message or goal')).toHaveValue('An unsent thought to keep.');
  await expect(page.locator('.artifact-chat-scope')).toContainText(mood.title);
  await page.getByLabel('Message or goal').fill('I feel bright today. A walk helped.');await page.getByLabel('Message or goal').press('Enter');
  await expect(page.locator('.chat-artifact')).toHaveCount(2);
  await page.locator('.artifact-chat-scope').getByRole('button',{name:mood.title,exact:true}).click();
  await expect(page.getByRole('cell',{name:'A walk helped.',exact:true})).toBeVisible();
  await page.screenshot({path:path.join(images,'mood-desktop.png'),fullPage:true,animations:'disabled'});
  await page.getByRole('button',{name:'Close app',exact:true}).click();await nav(page,'Artifacts');await page.getByRole('button',{name:'Build an app',exact:true}).click();
  await page.getByLabel('Message or goal').fill('Build me an internal checklist with task, done checkbox and priority. Add Water the fern, priority Soon.');await page.getByLabel('Message or goal').press('Enter');
  const openChecklist=page.getByRole('button',{name:'Open '+checklist.title+' beside chat',exact:true});await expect(openChecklist).toBeVisible();
  await expect(page.getByRole('region',{name:'Artifact page',exact:true})).not.toBeVisible();await openChecklist.click();
  await expect(page.getByRole('heading',{name:checklist.title,exact:true})).toBeVisible();
  const checkbox=page.getByRole('checkbox',{name:'Done: Water the fern',exact:true});await checkbox.click();await expect(checkbox).toBeEnabled();await expect(checkbox).toBeChecked();
  await page.getByLabel('Message or goal').fill('Actually mark Water the fern unfinished again.');await page.getByLabel('Message or goal').press('Enter');
  await expect(page.locator('.chat-artifact')).toHaveCount(4);
  await page.locator('.artifact-chat-scope').getByRole('button',{name:checklist.title,exact:true}).click();await expect(checkbox).not.toBeChecked();
  await page.getByRole('button',{name:'History',exact:true}).click();await page.getByRole('button',{name:'Undo last change',exact:true}).click();await expect(checkbox).toBeChecked();
  await page.getByRole('button',{name:'History',exact:true}).click();
  await page.getByRole('button',{name:'Add entry',exact:true}).click();await page.getByLabel('Task',{exact:true}).fill('Read a chapter');await page.getByRole('combobox',{name:'Priority',exact:true}).selectOption('Later');await page.getByRole('button',{name:'Save entry',exact:false}).click();
  await expect(page.getByRole('cell',{name:'Read a chapter',exact:true})).toBeVisible();
  const current=await request(page,'/state');const app=current.artifacts.find((item:any)=>item.title===checklist.title);
  const exported=await request(page,'/artifacts/'+app.id+'/export');expect(exported.artifact.entries).toHaveLength(2);expect(exported.history.length).toBeGreaterThan(3);
  await page.reload();await expect(page.getByRole('heading',{name:checklist.title,exact:true})).toBeVisible();await expect(page.getByLabel('Message or goal')).not.toBeVisible();await expect(checkbox).toBeChecked();
  await page.goBack();await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();await page.goForward();await expect(page.getByRole('heading',{name:checklist.title,exact:true})).toBeVisible();await expect(checkbox).toBeChecked();
  const other=await page.context().newPage();await other.goto('/');await expect(other.getByLabel('Message or goal')).toBeVisible();await nav(other,'Artifacts');
  await other.locator('.app-card').filter({hasText:checklist.title}).click();
  await other.getByRole('row').filter({hasText:'Read a chapter'}).getByRole('button',{name:'Edit',exact:true}).click();await other.getByLabel('Task',{exact:true}).fill('Draft in the other window');
  await checkbox.click();await expect(checkbox).not.toBeChecked();await expect(other.getByText('The app changed while this form was open.',{exact:false})).toBeVisible();
  await expect(other.getByLabel('Task',{exact:true})).toHaveValue('Draft in the other window');await expect(other.getByRole('button',{name:'Save entry',exact:false})).toBeDisabled();await other.close();await page.bringToFront();await checkbox.click();await expect(checkbox).toBeChecked();
  for(const width of [1440,390]){await page.setViewportSize({width,height:1000});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await expect(page.getByRole('button',{name:'Close app',exact:true})).toBeInViewport({ratio:1});await page.screenshot({path:path.join(images,`checklist-${width}.png`),fullPage:true,animations:'disabled'});}
  await page.getByRole('button',{name:'Show chat',exact:true}).click();await expect(page.getByRole('region',{name:'Artifact page',exact:true})).not.toBeVisible();await expect(page.getByLabel('Message or goal')).toBeVisible();
  if(await page.getByRole('button',{name:'Expand sidebar',exact:true}).count())await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();await page.getByRole('button',{name:'Switch to the paper study',exact:true}).click();await page.keyboard.press('Escape');await page.locator('.artifact-chat-scope').getByRole('button',{name:checklist.title,exact:true}).click();await expect(page.getByLabel('Message or goal')).not.toBeVisible();await page.screenshot({path:path.join(images,'checklist-paper-390.png'),fullPage:true,animations:'disabled'});
  await page.getByRole('button',{name:'Close app',exact:true}).click();await nav(page,'Artifacts');await page.screenshot({path:path.join(images,'app-shelf-390.png'),fullPage:true,animations:'disabled'});
  await page.getByRole('button',{name:'Notes & memory',exact:true}).click();await expect(page.getByRole('button',{name:'New note',exact:true})).toBeVisible();
  expect(providerError).toBe('');expect(requests).toHaveLength(4);expect(current.runs.every((run:any)=>run.state==='succeeded')).toBe(true);
  fs.writeFileSync(path.join(images,'app-check.json'),JSON.stringify({syntheticModelCalls:requests.length,liveModelCalls:0,apps:current.artifacts.map((item:any)=>({title:item.title,entries:item.entryCount})),checks:['ordinary chat creation','user-selected side-by-side opening','own app URL and refresh','minimal page header','full-screen toggle preserves draft','single mobile pane','selected app context','chat append','manual checkbox','chat checkbox edit','undo','manual form','export','reload','cross-window conflict preserves draft','desktop and mobile','paper theme','notes retained']},null,2));
 }finally{server.closeAllConnections();await new Promise<void>((resolve,reject)=>server.close(error=>error?reject(error):resolve()));}
});
