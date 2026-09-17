import {test,expect,type Page} from '@playwright/test';
import {createServer} from 'node:http';
import type {AddressInfo} from 'node:net';
import fs from 'node:fs';
import path from 'node:path';

async function api(page:Page,url:string,body?:unknown,method='PUT'){
 return page.evaluate(async({url,body,method})=>{const session=await(await fetch('/api/session')).json();const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});if(!response.ok)throw new Error(await response.text());return response.json();},{url,body,method});
}
async function nav(page:Page,name:string){
 const item=page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name,exact:true});
 if(!await item.isVisible()){
  const toggle=page.getByRole('button',{name:'Expand sidebar',exact:true});await expect(toggle).toBeVisible();await toggle.click();await expect(item).toBeVisible();
 }
 await item.click();
}
test('task changes undo precisely and idea failures remain actionable',async({page})=>{
 test.setTimeout(60000);page.setDefaultTimeout(10000);let calls=0,hold=false;
 const server=createServer(async(req,res)=>{for await(const _ of req){}calls++;if(hold)return;if(calls===1){res.writeHead(503);res.end('Fictional outage');return;}
  const delta={tool_calls:[{index:0,function:{name:'ideas_save',arguments:JSON.stringify({ideas:[{title:'A fictional reading nook',description:'Arrange books you want to read.',category:'Reading',prompt:'Help me plan a reading nook.'}]})}}]};res.writeHead(200,{'Content-Type':'text/event-stream'});res.end('data: '+JSON.stringify({choices:[{delta}]})+'\n\ndata: '+JSON.stringify({choices:[],usage:{prompt_tokens:20,completion_tokens:10}})+'\n\ndata: [DONE]\n\n');
 });await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 const shots=process.env.THADDEUS_SCREENSHOTS!;fs.mkdirSync(shots,{recursive:true});
 try{
  await page.setViewportSize({width:1440,height:1000});await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
  await nav(page,'To-do');await page.getByRole('button',{name:'Add to Tracked',exact:true}).click();const editor=page.getByRole('form',{name:'Edit to-do'});await editor.getByLabel('Title',{exact:true}).fill('Fictional weekly walk');await editor.getByLabel('Next check-in',{exact:true}).fill('2026-09-20');await editor.getByLabel('Next step',{exact:true}).fill('Pick a route');await editor.getByRole('button',{name:'Save item',exact:true}).click();
  const before=(await api(page,'/state')).library[0];const check=page.getByRole('button',{name:'Check in: Fictional weekly walk',exact:true});await check.click();await expect(check).toBeDisabled();await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Collapse sidebar',exact:true}).click();await page.setViewportSize({width:390,height:844});await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeInViewport();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:path.join(shots,'todo-undo-mobile.png'),animations:'disabled'});await page.getByRole('button',{name:'Undo',exact:true}).click();await expect(check).toBeEnabled();expect((await api(page,'/state')).library[0].tracking).toEqual(before.tracking);
  await check.click();const checked=(await api(page,'/state')).library[0];await api(page,'/library/'+checked.id,{...checked,content:'Newer edit from another window'});await expect(page.getByText('This item changed again. Undo is unavailable so the newer changes stay intact.',{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Undo',exact:true})).toHaveCount(0);expect((await api(page,'/state')).library[0].content).toBe('Newer edit from another window');
  await page.getByRole('button',{name:'Archive',exact:true}).click();await expect(check).toHaveCount(0);await page.getByRole('button',{name:'Undo',exact:true}).click();await expect(check).toBeVisible();expect((await api(page,'/state')).library[0].status).toBe('open');
  await page.getByRole('button',{name:'Finish tracking',exact:true}).click();await page.getByRole('button',{name:'Undo',exact:true}).click();await expect(check).toBeVisible();expect((await api(page,'/state')).library[0].tracking).toEqual(checked.tracking);
  const connection=await api(page,'/settings/connection');await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'fixture-task-recovery',reasoning:'high',endpoint:`http://127.0.0.1:${(server.address() as AddressInfo).port}/v1`},credentialMode:'none'});
  await nav(page,'Ideas');await page.getByRole('button',{name:'Fresh ideas',exact:true}).click();await expect(page.getByRole('alert')).toContainText('Could not refresh ideas.');await expect(page.getByRole('alert')).toContainText('Your saved ideas are unchanged.');await expect(page.getByRole('button',{name:'Fresh ideas',exact:true})).toBeEnabled();expect(calls).toBe(1);
  await page.getByRole('button',{name:'View details',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();await expect(page.getByRole('dialog')).toContainText('Provider request failed');await page.getByRole('button',{name:'Close dialog',exact:true}).click();await page.reload();await nav(page,'Ideas');await expect(page.getByRole('alert')).toContainText('Could not refresh ideas.');expect(calls).toBe(1);
  await page.screenshot({path:path.join(shots,'ideas-failure-mobile.png'),animations:'disabled'});
  hold=true;await page.getByRole('button',{name:'Fresh ideas',exact:true}).click();await expect.poll(()=>calls).toBe(2);await expect(page.getByRole('status').filter({hasText:'Finding fresh ideas.'})).toBeVisible();await page.getByRole('button',{name:'Cancel',exact:true}).click();await expect(page.getByRole('alert')).toContainText('Finding ideas was cancelled.');hold=false;
  await page.getByRole('button',{name:'Fresh ideas',exact:true}).click();await expect(page.getByRole('heading',{name:'A fictional reading nook',exact:true})).toBeVisible();await expect(page.getByText('Fresh ideas are ready.',{exact:true})).toBeVisible();await expect(page.getByRole('alert')).toHaveCount(0);
  await page.setViewportSize({width:1440,height:1000});await page.screenshot({path:path.join(shots,'ideas-recovered-desktop.png'),animations:'disabled'});const state=await api(page,'/state');expect(calls).toBe(3);expect(state.runs).toHaveLength(3);expect(state.library.filter((item:any)=>item.kind==='idea')).toHaveLength(1);expect(state.search.budget.used).toBe(0);
  fs.writeFileSync(path.join(shots,'task-recovery-check.json'),JSON.stringify({passed:true,syntheticCalls:calls,liveModelCalls:0,braveCalls:0,checks:['undo exact recurring schedule','newer changes prevent undo','archive undo','finish tracking undo','failure is visible','details opens log','failure survives reload','cancel idea generation','explicit fresh attempt','successful recovery','mobile layout']},null,2));
 }finally{server.closeAllConnections();await new Promise<void>(resolve=>server.close(()=>resolve()));}
});
