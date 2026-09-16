import {test,expect,type Page} from '@playwright/test';
import {createServer} from 'node:http';
import type {AddressInfo} from 'node:net';
import fs from 'node:fs';
import path from 'node:path';

const id=()=>crypto.randomUUID().replaceAll('-','');
async function api(page:Page,url:string,body?:unknown,method='PUT'){
 return page.evaluate(async({url,body,method})=>{const session=await(await fetch('/api/session')).json();const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});if(!response.ok)throw new Error(await response.text());return response.json();},{url,body,method});
}
test('unfinished drafts recover with their context and never dispatch on reload',async({page,context})=>{
 test.setTimeout(90000);let calls=0;
 const server=createServer(async(req,res)=>{for await(const _ of req){}calls++;res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta:{content:'The fictional draft arrived once.'}}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:20,completion_tokens:10}})+'\n\ndata: [DONE]\n\n');});
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 const shots=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(shots,{recursive:true});
 try{
  await page.setViewportSize({width:1440,height:960});await page.goto('/');
  const key=fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim();
  await page.getByLabel('Host access key',{exact:true}).fill(key);await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  const composer=page.getByLabel('Message or goal');await expect(composer).toBeVisible();
  const connection=await api(page,'/settings/connection');await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-draft-recovery',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'});
  const appId=id();await api(page,'/artifacts/'+appId,{operationId:id(),version:'absent',definition:{title:'Fictional draft notebook',description:'Only test data.',fields:[{key:'note',label:'Note',kind:'text'}],summaries:[]},upserts:[]});
  await page.goto('/apps/'+appId);await page.getByRole('button',{name:'Show chat',exact:true}).click();await page.getByRole('button',{name:'Close app',exact:true}).click();
  await composer.fill('Keep this unfinished thought\nand its second line.');
  await page.locator('input[type=file]').setInputFiles({name:'draft-context.txt',mimeType:'text/plain',buffer:Buffer.from('A fictional draft context.')});
  await expect(page.getByRole('button',{name:'Remove attachment draft-context.txt'})).toBeVisible();
  await page.reload();await expect(composer).toHaveValue('Keep this unfinished thought\nand its second line.');await expect(page.getByRole('button',{name:'Remove attachment draft-context.txt'})).toBeVisible();await expect(page.locator('.artifact-chat-scope')).toContainText('Fictional draft notebook');await expect(page.getByText('Draft restored in this tab.',{exact:true})).toBeVisible();expect(calls).toBe(0);
  const other=await context.newPage();await other.goto('/');await expect(other.getByLabel('Message or goal')).toHaveValue('');await expect(other.getByRole('button',{name:'Remove attachment draft-context.txt'})).toHaveCount(0);await other.close();
  await page.setViewportSize({width:390,height:844});await expect(composer).toBeInViewport();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:path.join(shots,'draft-recovery-mobile.png'),animations:'disabled'});
  await page.getByRole('button',{name:'Remove attachment draft-context.txt'}).click();await page.getByRole('button',{name:'Stop using this app in chat'}).click();
  await page.getByRole('button',{name:'Message options',exact:true}).click();await page.getByRole('button',{name:'Research with selected sources',exact:true}).click();
  await page.getByLabel('Public source websites (optional)',{exact:true}).fill('example.org');
  const research=page.getByRole('region',{name:'Research scope'});await research.getByText('Resource limits & provider guarantees',{exact:true}).click();await research.getByLabel('Total token allowance',{exact:true}).fill('12345');await research.getByLabel('Require certified input/output token bounds before any model call').check();
  await page.reload();await expect(composer).toHaveValue('Keep this unfinished thought\nand its second line.');await expect(page.getByLabel('Public source websites (optional)',{exact:true})).toHaveValue('example.org');await research.getByText('Resource limits & provider guarantees',{exact:true}).click();await expect(research.getByLabel('Total token allowance',{exact:true})).toHaveValue('12345');await expect(research.getByLabel('Require certified input/output token bounds before any model call')).toBeChecked();expect(calls).toBe(0);
  await page.getByRole('button',{name:'Message options',exact:true}).click();await page.getByRole('group',{name:'Message options',exact:true}).getByRole('button',{name:'Chat',exact:true}).click();await composer.press('Enter');await expect(page.locator('.chat.assistant')).toContainText('The fictional draft arrived once.');await page.reload();await expect(composer).toHaveValue('');expect(calls).toBe(1);
  await page.setViewportSize({width:1440,height:960});await page.goto('/apps/'+appId);await page.getByRole('button',{name:'Show chat',exact:true}).click();await page.getByRole('button',{name:'Close app',exact:true}).click();await composer.fill('A draft with a missing attachment');await page.locator('input[type=file]').setInputFiles({name:'unavailable.txt',mimeType:'text/plain',buffer:Buffer.from('Disposable fiction.')});await expect(page.getByRole('button',{name:'Remove attachment unavailable.txt'})).toBeVisible();
  const state=await api(page,'/state'),file=state.uploads.find((file:any)=>file.name==='unavailable.txt'),app=state.artifacts.find((app:any)=>app.id===appId);
  // Hide the page while context disappears, as if another tab moved it to Trash.
  await page.goto('about:blank');await (async()=>{const admin=await context.newPage();await admin.goto('/');await expect(admin.getByLabel('Message or goal')).toBeVisible();await api(admin,'/uploads/'+file.id,{version:file.version,archived:true});await api(admin,'/artifacts/'+appId,{operationId:id(),version:app.version,archived:true});await admin.close();})();
  await page.goto('/');await expect(composer).toHaveValue('A draft with a missing attachment');await expect(page.getByText('Draft restored. Some original files or the selected app are unavailable; review the remaining context before sending.',{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Remove attachment unavailable.txt'})).toHaveCount(0);await expect(page.getByRole('button',{name:'Stop using this app in chat'})).toHaveCount(0);
  const session=await api(page,'/session');await page.evaluate(sessionId=>sessionStorage.setItem('thaddeus-composer-v1:'+sessionId,'{broken'),session.id);await page.reload();await expect(composer).toHaveValue('');await expect(page.getByText('Draft recovery is unavailable in this browser. Keep a copy of unfinished text before reloading.',{exact:true})).toBeVisible();await composer.fill('Keep drafts out of the next sign-in');
  await context.clearCookies();await page.reload();await page.getByLabel('Host access key',{exact:true}).fill(key);await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(composer).toHaveValue('');expect((await api(page,'/session')).id).not.toBe(session.id);expect(calls).toBe(1);expect((await api(page,'/state')).runs).toHaveLength(1);
  await page.setViewportSize({width:1440,height:960});await composer.fill('A thought kept safely in this tab.');await page.reload();if(await page.getByRole('button',{name:'Close activity log',exact:true}).isVisible())await page.getByRole('button',{name:'Close activity log',exact:true}).click();await page.screenshot({path:path.join(shots,'draft-recovery-desktop.png'),animations:'disabled'});
  fs.writeFileSync(path.join(shots,'draft-recovery-check.json'),JSON.stringify({passed:true,syntheticCalls:calls,liveModelCalls:0,braveCalls:0,checks:['reload text and line breaks','attachment and selected app recovery','independent tab','mobile fit','research selection and limits','no automatic send','acknowledged send clears draft','unavailable context disclosure','invalid storage recovery','separate sign-in']},null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
