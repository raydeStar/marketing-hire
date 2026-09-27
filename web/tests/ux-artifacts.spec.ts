import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const id=()=>crypto.randomUUID().replaceAll('-','');
async function api(page:Page,url:string,body?:unknown){
 return page.evaluate(async({url,body})=>{
  const session=await (await fetch('/api/session')).json();
  const response=await fetch('/api'+url,body?{method:'PUT',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)}:{});
  if(!response.ok)throw new Error(await response.text());return response.json();
 },{url,body});
}
async function nav(page:Page,name:string){
 const toggle=page.getByRole('button',{name:'Expand sidebar',exact:true});if(await toggle.count())await toggle.click();
 await page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name,exact:true}).click();
}

test('native app forms save locally, navigation preserves chat, and shelf changes are reversible',async({page})=>{
 await page.setViewportSize({width:1440,height:1000});await page.goto('/');
 await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
 await page.getByRole('button',{name:'Open workspace',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
 const appId=id(),leaks:string[]=[];
 const definition={title:'UX field notes',description:'A fictional form to exercise the browser boundary.',fields:[{key:'note',label:'Note',kind:'text'}],summaries:[],page:{
  html:'<form id="local"><label>Note<input id="note" required></label><button>Save note</button></form><p id="count"></p><p id="theme"></p><p id="status" role="status"></p><form action="https://artifact-form-leak.invalid/submit" method="post"><input name="note" value="fictional"><button>Try external submit</button></form><form action="/api/artifacts" method="post"><input name="note" value="fictional"><button>Try host submit</button></form><p id="blocked"></p>',
  css:'input{background:var(--surface);color:var(--text)}button{background:var(--accent);color:var(--on-accent);padding:12px;margin:10px}',
  javaScript:`let current;thaddeus.onChange(state=>{current=state;document.getElementById('count').textContent=state.entries.length+' notes';document.getElementById('theme').textContent=state.theme;});document.getElementById('local').addEventListener('submit',async event=>{event.preventDefault();try{await thaddeus.save({upserts:[{id:'',values:{note:document.getElementById('note').value}}],deleteIds:[]});document.getElementById('status').textContent='Saved';}catch(error){document.getElementById('status').textContent=error.message;}});document.addEventListener('securitypolicyviolation',event=>{if(event.violatedDirective==='form-action')document.getElementById('blocked').textContent='Blocked '+event.blockedURI;});`
 }};
 await api(page,'/artifacts/'+appId,{operationId:id(),version:'absent',definition,upserts:[]});
 await page.route('https://artifact-form-leak.invalid/**',route=>{leaks.push(route.request().url());return route.abort();});
 page.on('request',request=>{if(request.method()==='POST'&&request.url().endsWith('/api/artifacts'))leaks.push(request.url());});
 await nav(page,'Artifacts');await page.getByRole('button',{name:'Open UX field notes',exact:true}).click();
 const frame=page.frameLocator('iframe[title="UX field notes app"]');
 await frame.getByRole('button',{name:'Save note',exact:true}).click();await expect(frame.getByText('0 notes',{exact:true})).toBeVisible();
 await frame.getByLabel('Note',{exact:true}).fill('A native form works');await frame.getByLabel('Note',{exact:true}).press('Enter');
 await expect(frame.getByText('1 notes',{exact:true})).toBeVisible();await expect(frame.getByRole('status')).toHaveText('Saved');
 await frame.getByRole('button',{name:'Try external submit',exact:true}).click();await expect(page.getByRole('alert')).toContainText('The page left its app document');expect(leaks).toEqual([]);await page.getByRole('button',{name:'Retry page',exact:true}).click();await expect(frame.getByText('1 notes',{exact:true})).toBeVisible();
 await frame.getByRole('button',{name:'Try host submit',exact:true}).click();await expect(frame.locator('#blocked')).toContainText('/api/artifacts');expect(leaks).toEqual([]);
 await page.getByRole('button',{name:'Show chat',exact:true}).click();
 const draft=page.getByLabel('Message or goal');await draft.fill('Keep this thought');await draft.press('Shift+Enter');await draft.press('a');await expect(draft).toHaveValue('Keep this thought\na');
 await page.getByRole('button',{name:'Full screen',exact:true}).click();await page.getByRole('button',{name:'Show chat',exact:true}).click();await expect(draft).toHaveValue('Keep this thought\na');
 await page.getByRole('button',{name:'Close app',exact:true}).click();await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();await expect(draft).toBeFocused();await expect(draft).toHaveValue('Keep this thought\na');
 await draft.fill('');await nav(page,'Artifacts');await page.getByRole('button',{name:'Open UX field notes',exact:true}).click();await page.getByRole('button',{name:'Close app',exact:true}).click();await expect(page.getByRole('heading',{name:'Artifacts',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Open UX field notes',exact:true}).click();await page.getByRole('button',{name:'Show chat',exact:true}).click();await page.reload();await page.getByRole('button',{name:'Close app',exact:true}).click();await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
 await nav(page,'Artifacts');await page.getByRole('button',{name:'Edit UX field notes',exact:true}).click();await page.getByLabel('App name',{exact:true}).fill('UX notes renamed');await page.getByRole('button',{name:'Save changes',exact:true}).click();await expect(page.getByRole('status')).toContainText('Changes to UX notes renamed saved.');
 await page.getByRole('button',{name:'Delete UX notes renamed',exact:true}).click();await expect(page.getByRole('status')).toContainText('moved to Trash');await page.getByRole('button',{name:'Undo delete',exact:true}).click();await expect(page.getByRole('button',{name:'Open UX notes renamed',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Delete UX notes renamed',exact:true}).click();await page.getByRole('button',{name:'Trash',exact:true}).click();await page.getByRole('button',{name:'Restore UX notes renamed',exact:true}).click();await expect(page.getByRole('status')).toContainText('restored');await page.getByRole('button',{name:'Back to apps',exact:true}).click();
 await page.getByRole('button',{name:'Open UX notes renamed',exact:true}).click();await page.goBack();await expect(page.getByRole('heading',{name:'Artifacts',exact:true})).toBeVisible();await page.goForward();await expect(page.getByRole('heading',{name:'UX notes renamed',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Show chat',exact:true}).click();if(await page.getByRole('button',{name:'Expand sidebar',exact:true}).count())await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();await page.getByRole('button',{name:'Switch to the paper study',exact:true}).click();await page.keyboard.press('Escape');
 const renamedFrame=page.frameLocator('iframe[title="UX notes renamed app"]');await expect(renamedFrame.locator('#theme')).toHaveText('light');
 await page.setViewportSize({width:390,height:844});await expect(draft).toBeVisible();await page.locator('.artifact-chat-scope').getByRole('button',{name:'UX notes renamed',exact:true}).click();await expect(renamedFrame.getByLabel('Note',{exact:true})).toBeVisible();await expect(draft).not.toBeVisible();
 await expect(page.getByRole('button',{name:'Close app',exact:true})).toBeInViewport();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});await page.screenshot({path:path.join(images,'native-form-mobile.png')});
 await page.getByRole('button',{name:'Show chat',exact:true}).click();if(await page.getByRole('button',{name:'Expand sidebar',exact:true}).count())await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();await page.getByRole('button',{name:'Switch to the dark study',exact:true}).click();await page.keyboard.press('Escape');await page.locator('.artifact-chat-scope').getByRole('button',{name:'UX notes renamed',exact:true}).click();await expect(renamedFrame.locator('#theme')).toHaveText('dark');
 const saved=await api(page,'/artifacts/'+appId);expect(saved.entries).toHaveLength(1);expect(saved.entries[0].values.note).toBe('A native form works');expect((await api(page,'/state')).runs).toHaveLength(0);
 fs.writeFileSync(path.join(images,'ux-contract.json'),JSON.stringify({passed:true,nativeFormSaved:true,externalAndHostSubmissionsBlocked:true,closeContextAndDraftPreserved:true,shelfUndoAndRestore:true,mobileAndThemes:true,liveModelCalls:0},null,2));
});
