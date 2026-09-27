import {test,expect,type Page} from '@playwright/test';
import {createServer,type ServerResponse} from 'node:http';
import type {AddressInfo} from 'node:net';
import fs from 'node:fs';
import path from 'node:path';
async function api(page:Page,url:string,body?:unknown,method='POST'){
 return page.evaluate(async({url,body,method})=>{const session=await(await fetch('/api/session')).json();const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});if(!response.ok)throw new Error(await response.text());return response.json();},{url,body,method});
}
function answer(res:ServerResponse,delta:unknown){res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:30,completion_tokens:20}})+'\n\ndata: [DONE]\n\n');}
test('chat recovers failed replies, preserves drafts and offers deliberate new attempts',async({page,context})=>{
 test.setTimeout(90000);let calls=0,fail=true,hold=false;const observations:any[]=[];
 const server=createServer(async(req,res)=>{let raw='';for await(const chunk of req)raw+=chunk;const body=JSON.parse(raw);observations.push(body);calls++;
  if(hold)return;
  if(fail){fail=false;res.writeHead(503);res.end('Fictional provider unavailable');return;}
  if(JSON.stringify(body.messages.at(-1)).includes('Build fictional list'))answer(res,{tool_calls:[{index:0,function:{name:'artifact_create',arguments:JSON.stringify({definition:{title:'Fictional list',description:'Only fixture data',fields:[{key:'task',label:'Task',kind:'text'}],summaries:[],page:{html:'<h1>Fictional list</h1>',css:'h1{padding:20px}',javaScript:''}},entries:[]})}}]});
  else answer(res,{content:'Fixture reply '+calls+'. '+('A calm, useful answer with enough detail to exercise the transcript.\n\n').repeat(10)});
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});
 try{
  await page.setViewportSize({width:1440,height:960});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Open workspace',exact:true}).click();
  const composer=page.getByLabel('Message or goal');await expect(composer).toBeVisible();
  const connection=await api(page,'/settings/connection');await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-chat-qol',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'},'PUT');
  if(await page.getByRole('button',{name:'Close activity log',exact:true}).isVisible())await page.getByRole('button',{name:'Close activity log',exact:true}).click();
  await page.locator('input[type=file]').setInputFiles({name:'fictional-note.txt',mimeType:'text/plain',buffer:Buffer.from('The fictional raven is Juniper.')});
  await expect(page.getByRole('button',{name:'Remove attachment fictional-note.txt'})).toBeVisible();
  await composer.fill('Remember my fictional raven');await composer.press('Enter');
  const retry=page.getByRole('button',{name:'Retry',exact:true});await expect(retry).toBeVisible();
  const original=(await api(page,'/state')).runs[0];expect(original.state).toBe('failed');expect(calls).toBe(1);
  await composer.fill('Keep this unfinished draft');await retry.click();
  await expect(page.locator('.chat.assistant')).toContainText('Fixture reply 2.');
  await expect(composer).toHaveValue('Keep this unfinished draft');await expect(page.locator('.chat.user')).toHaveCount(1);
  let state=await api(page,'/state');const second=state.runs.find((r:any)=>r.conversationRetry);expect(second.conversationRetry.rootId).toBe(original.id);expect(second.chargedTokens).toBe(50);expect(state.runs.find((r:any)=>r.id===original.id).chargedTokens).toBe(original.chargedTokens);
  await context.grantPermissions(['clipboard-read','clipboard-write']);await page.locator('.chat.assistant').getByRole('button',{name:'Copy message',exact:true}).click();await expect(page.locator('.chat.assistant').getByRole('button',{name:'Copied message'})).toBeVisible();expect(await page.evaluate(()=>navigator.clipboard.readText())).toContain('Fixture reply 2.');
  await page.locator('.chat.user').getByRole('button',{name:'Edit and resend message'}).click();await expect(page.getByRole('dialog',{name:'Replace your draft?'})).toBeVisible();await page.getByRole('button',{name:'Keep current draft',exact:true}).click();await expect(composer).toHaveValue('Keep this unfinished draft');
  await page.locator('.chat.user').getByRole('button',{name:'Edit and resend message'}).click();await page.getByRole('button',{name:'Replace draft',exact:true}).click();await expect(composer).toHaveValue('Remember my fictional raven');await expect(page.getByRole('button',{name:'Remove attachment fictional-note.txt'})).toBeVisible();expect(calls).toBe(2);
  await composer.fill('Edited fictional raven question');await composer.press('Shift+Enter');await composer.press('x');await expect(composer).toHaveValue('Edited fictional raven question\nx');expect(calls).toBe(2);await composer.press('Enter');
  await expect(page.locator('.chat.assistant').last()).toContainText('Fixture reply 3.');await expect(page.locator('.chat.user')).toHaveCount(2);
  expect(JSON.stringify(observations[2].messages)).toContain('The fictional raven is Juniper.');
  await page.locator('.chat.assistant').first().getByRole('button',{name:'Try again',exact:true}).click();
  await expect(page.locator('.chat.assistant').first()).toContainText('Fixture reply 4.');await expect(page.locator('.chat.user')).toHaveCount(2);
  const attempts=page.getByRole('combobox',{name:'Reply attempt'});await attempts.selectOption(original.id);await expect(retry).toBeVisible();await attempts.selectOption(second.id);await expect(page.locator('.chat.assistant').first()).toContainText('Fixture reply 2.');
  await page.reload();await expect(page.locator('.chat.assistant').first()).toContainText('Fixture reply 4.');
  await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();await page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name:'Search',exact:true}).click();await page.getByLabel('Search your study').fill('Fixture reply 2.');await page.locator('.search-results button').filter({hasText:'Conversation'}).click();
  await expect(page.locator('#chat-'+second.id+'-assistant')).toBeInViewport();await expect(attempts).toHaveValue(second.id);
  const transcript=page.getByRole('region',{name:'Conversation',exact:true});await transcript.evaluate(el=>{el.scrollTop=0;});await expect(page.getByRole('button',{name:'Latest messages',exact:true})).toBeVisible();await page.getByRole('button',{name:'Latest messages',exact:true}).click();await expect(page.getByRole('button',{name:'Latest messages',exact:true})).toHaveCount(0);
  await page.screenshot({path:path.join(images,'chat-qol-desktop.png'),animations:'disabled'});
  hold=true;await composer.fill('A cancellable request');await composer.press('Enter');await expect(page.getByRole('button',{name:'Cancel task',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Try again',exact:true}).first()).toBeDisabled();await page.getByRole('button',{name:'Cancel task',exact:true}).click();await expect(retry).toBeVisible();hold=false;
  await retry.click();await expect(page.locator('.chat.assistant').last()).toContainText('Fixture reply 6.');
  await composer.fill('Build fictional list');await composer.press('Enter');const openApp=page.getByRole('button',{name:'Open Fictional list beside chat',exact:true});await expect(openApp).toBeVisible();await expect(page.getByRole('region',{name:'Artifact page',exact:true})).not.toBeVisible();await openApp.click();await expect(page.frameLocator('iframe[title="Fictional list app"]').getByRole('heading',{name:'Fictional list'})).toBeVisible();
  await page.getByRole('button',{name:'Close app',exact:true}).click();await expect(composer).toBeVisible();await expect(page.locator('.chat.assistant').last().getByRole('button',{name:'Try again',exact:true})).toHaveCount(0);
  await page.locator('.chat.user').last().getByRole('button',{name:'Edit and resend message'}).click();await expect(composer).toHaveValue('Build fictional list');await expect(page.getByRole('button',{name:'Stop using this app in chat'})).toHaveCount(0);await composer.fill('');
  await page.setViewportSize({width:390,height:844});await expect(composer).toBeInViewport();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:path.join(images,'chat-qol-mobile.png'),animations:'disabled'});
  await context.setOffline(true);await expect(page.getByText('Connection lost. Writes and approvals are disabled until the host reconnects.')).toBeVisible();await expect(page.getByRole('button',{name:'Try again',exact:true}).first()).toBeDisabled();await context.setOffline(false);
  state=await api(page,'/state');expect(calls).toBe(7);expect(state.artifacts).toHaveLength(1);expect(state.runs).toHaveLength(7);
  fs.writeFileSync(path.join(images,'chat-qol-check.json'),JSON.stringify({passed:true,syntheticCalls:calls,liveModelCalls:0,checks:['failed reply retry','separate usage','no duplicate user messages','copy','draft replacement confirmation','edit restores attachments','Shift+Enter newline','Enter send','explicit regeneration','previous attempts','reload persistence','scroll to latest','cancel and retry','completed app action cannot regenerate','app close returns to chat','mobile layout','offline controls']},null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
