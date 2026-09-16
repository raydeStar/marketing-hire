import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {createServer} from 'node:http';
import type {AddressInfo} from 'node:net';

async function api(page:Page,url:string,body?:unknown,method='POST'){
 return page.evaluate(async({url,body,method})=>{const session=await(await fetch('/api/session')).json();const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});if(!response.ok)throw new Error(await response.text());return response.json();},{url,body,method});
}
async function nav(page:Page,name:string){const toggle=page.getByRole('button',{name:'Expand sidebar',exact:true});if(await toggle.count())await toggle.click();await (name==='Settings'?page.getByRole('button',{name,exact:true}):page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name,exact:true})).click();}
async function unlock(page:Page){await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();}

test('search settings keep unsaved limits, save both fields, accept Enter, and preserve a failed edit',async({page})=>{
 await unlock(page);await nav(page,'Settings');const search=page.getByRole('region',{name:'Public search connection'}),limit=search.getByLabel('Monthly search limit',{exact:true});
 await limit.fill('999');await search.getByLabel('Search key storage').selectOption('session');await search.getByLabel('Brave Search API key',{exact:true}).fill('fictional-browser-search-key');
 await search.getByRole('button',{name:'Save search settings',exact:true}).click();await expect(search.getByText('Search settings saved. No provider request was made.')).toBeVisible();
 expect((await api(page,'/state')).search.budget.monthlyLimit).toBe(999);expect((await api(page,'/state')).search.configured).toBe(false);
 await limit.fill('750');await search.getByRole('button',{name:'Check saved search key',exact:true}).click();await expect(limit).toHaveValue('750');await expect(limit).toBeEnabled();await limit.press('Enter');await expect(search.getByText('Saved: 750 searches per month. No search was sent.')).toBeVisible();
 const current=await api(page,'/settings/search');await api(page,'/settings/search/budget',{version:current.summary.budget.version,monthlyLimit:888},'PUT');
 await limit.fill('777');await limit.press('Enter');await expect(search.getByRole('alert')).toContainText('Search limit changed');await expect(limit).toHaveValue('777');
 await limit.fill('999');await limit.press('Enter');await expect(search.getByText('Saved: 999 searches per month. No search was sent.')).toBeVisible();
 await page.reload();await nav(page,'Settings');await expect(page.getByLabel('Monthly search limit',{exact:true})).toHaveValue('999');expect((await api(page,'/state')).search.budget.used).toBe(0);
});

test('tracked follow-ups, uploads, generated ideas, app pages and readable activity work together',async({page})=>{
 test.setTimeout(60000);page.setDefaultTimeout(10000);const requests:any[]=[];let providerError='';
 const definition={title:'Little reading room',description:'A fictional custom app.',fields:[{key:'title',label:'Book',kind:'text'},{key:'done',label:'Read',kind:'checkbox'}],summaries:['done'],page:{html:'<main><h1>My reading room</h1><label>Book<input id="book"></label><button id="save">Add book</button><p id="count"></p></main>',css:'main{max-width:600px;margin:40px auto}input{color:var(--text);background:var(--surface)}',javaScript:"let current;thaddeus.onChange(state=>{current=state;document.getElementById('count').textContent=state.entries.length+' books';});document.getElementById('save').onclick=()=>thaddeus.save({upserts:[{id:'',values:{title:document.getElementById('book').value,done:false}}],deleteIds:[]});"}};
 const server=createServer(async(req,res)=>{try{let raw='';for await(const c of req)raw+=c;const body=JSON.parse(raw);requests.push(body);let delta:any;
  if(body.tools?.some((tool:any)=>tool.function.name==='ideas_save'))delta={tool_calls:[{index:0,function:{name:'ideas_save',arguments:JSON.stringify({ideas:[{title:'Keep a reading room',description:'A place for the books you want to read.',category:'Reading',prompt:'Build a little reading room app with a book title and read checkbox.'}]})}}]};
  else if(Array.isArray(body.messages.at(-1).content)){const content=body.messages.at(-1).content;expect(content.some((part:any)=>part.type==='image_url')).toBe(true);expect(content.some((part:any)=>part.text?.includes('fictional upload note'))).toBe(true);delta={content:'I can read the fictional attachments.'};}
  else delta={tool_calls:[{index:0,function:{name:'artifact_create',arguments:JSON.stringify({definition,entries:[]})}}]};
  res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:100,completion_tokens:50}})+'\n\ndata: [DONE]\n\n');
 }catch(error){providerError=String(error);res.writeHead(500);res.end('Fictional provider failed');}});await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});
 try{
  await page.setViewportSize({width:1440,height:1000});await unlock(page);
  const connection=await api(page,'/settings/connection');await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-mvp',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');
  await nav(page,'To-do');await page.getByRole('button',{name:'Add to Tracked',exact:true}).click();const editor=page.getByRole('form',{name:'Edit to-do'});
  await editor.getByLabel('Title',{exact:true}).fill('Fictional walks');await editor.getByLabel('Next step',{exact:true}).fill('Choose a trail');await editor.getByLabel('Next check-in',{exact:true}).fill('2026-09-20');await editor.getByRole('button',{name:'Save item',exact:true}).click();
  await page.getByRole('button',{name:'Check in: Fictional walks',exact:true}).click();await expect(page.getByText(/checked in today/)).toBeVisible();expect((await api(page,'/state')).library.find((i:any)=>i.title==='Fictional walks').status).toBe('open');
  for(const section of ['Daily to-do','Weekly to-do','Overall goals']){await page.getByRole('button',{name:'Add to '+section,exact:true}).click();await editor.getByLabel('Title',{exact:true}).fill('Fictional '+section);await editor.getByRole('button',{name:'Save item',exact:true}).click();await expect(page.getByRole('heading',{name:'Fictional '+section,exact:true})).toBeVisible();}
  await page.screenshot({animations:'disabled',path:path.join(images,'todo-desktop.png'),fullPage:true});
  await nav(page,'Artifacts');const shelf=page.getByRole('region',{name:'Uploaded files'});
  await shelf.locator('input[type=file]').setInputFiles([{name:'fictional.txt',mimeType:'text/plain',buffer:Buffer.from('A fictional upload note.')},{name:'fictional.png',mimeType:'image/png',buffer:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=','base64')}]);
  await expect(shelf.getByRole('button',{name:/fictional.txt/}).first()).toBeVisible();await page.getByRole('navigation',{name:'Artifact collections'}).getByRole('button',{name:'Images',exact:true}).click();await expect(shelf.locator('.file-card')).toHaveCount(1);
  await shelf.getByRole('button',{name:/fictional.png/}).first().click();await expect(page.getByRole('dialog',{name:'fictional.png'})).toBeVisible();await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('navigation',{name:'Artifact collections'}).getByRole('button',{name:'All artifacts',exact:true}).click();await shelf.getByRole('button',{name:'Delete fictional.txt',exact:true}).click();await page.getByRole('button',{name:'Trash',exact:true}).click();await shelf.getByRole('button',{name:'Restore',exact:true}).click();await page.getByRole('button',{name:'Trash',exact:true}).click();
  await shelf.getByRole('button',{name:'Attach fictional.txt to chat',exact:true}).click();await nav(page,'Artifacts');await page.getByRole('button',{name:'Attach fictional.png to chat',exact:true}).click();
  await page.getByLabel('Message or goal').fill('Read these fictional attachments');await page.getByLabel('Message or goal').press('Enter');await expect(page.getByText('I can read the fictional attachments.',{exact:true})).toBeVisible();
  await nav(page,'Ideas');await page.getByRole('button',{name:'Fresh ideas',exact:true}).click();await expect(page.getByRole('heading',{name:'Reading',exact:true})).toBeVisible();await page.screenshot({animations:'disabled',path:path.join(images,'ideas-desktop.png'),fullPage:true});
  await page.getByRole('button',{name:/^Keep a reading room/}).click();await expect(page.getByRole('heading',{name:'Little reading room',exact:true})).toBeVisible();
  const frame=page.frameLocator('iframe[title="Little reading room app"]');await frame.getByLabel('Book',{exact:true}).fill('A fictional book');await frame.getByRole('button',{name:'Add book',exact:true}).click();await expect(frame.getByText('1 books',{exact:true})).toBeVisible();
  if(await page.getByRole('button',{name:'Show chat',exact:true}).count())await page.getByRole('button',{name:'Show chat',exact:true}).click();await page.getByLabel('Message or goal').fill('An unfinished thought');await page.getByRole('button',{name:'Close app',exact:true}).click();await expect(page.getByLabel('Message or goal')).toHaveValue('An unfinished thought');
  await page.getByRole('button',{name:/Thaddeus.*activity log/i}).click();await page.locator('.log-entries button').first().click();await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('heading',{name:'Summary',exact:true})).toBeVisible();await page.getByRole('navigation',{name:'Activity steps'}).getByRole('button').nth(1).click();await expect(page.getByRole('dialog').getByText('Build a little reading room app with a book title and read checkbox.',{exact:true})).toHaveCount(2);await page.screenshot({animations:'disabled',path:path.join(images,'activity-desktop.png')});await page.setViewportSize({width:390,height:900});await expect(page.getByRole('dialog').getByRole('button',{name:'Close dialog'})).toBeInViewport();await page.screenshot({animations:'disabled',path:path.join(images,'activity-mobile.png')});await page.setViewportSize({width:1440,height:1000});
  await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByLabel('Message or goal')).toHaveValue('An unfinished thought');if(await page.getByRole('button',{name:'Close activity log',exact:true}).count())await page.getByRole('button',{name:'Close activity log',exact:true}).click();
  await nav(page,'Artifacts');for(const width of [1440,390]){await page.setViewportSize({width,height:900});if(width===390)await page.keyboard.press('Escape');expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({animations:'disabled',path:path.join(images,'artifacts-'+width+'.png'),fullPage:true});}
  expect(requests).toHaveLength(3);expect(providerError).toBe('');
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
