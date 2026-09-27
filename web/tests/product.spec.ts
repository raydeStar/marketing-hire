import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?'?'+query:''}#launch=${(await issued.json()).ticket}`);
  await expect(page.getByRole('navigation',{name:'Main views'})).toBeVisible();
}
async function call(page:Page,url:string,{body,method='POST',csrf=true}:{body?:unknown;method?:string;csrf?:boolean}={}){
  return page.evaluate(async({url,body,method,csrf})=>{
    const session=await(await fetch('/api/session')).json();
    const response=await fetch('/api'+url,{method,headers:{'Content-Type':'application/json',...(csrf?{'X-CSRF':session.csrf}:{})},body:JSON.stringify(body??{})});
    return {status:response.status,body:await response.json().catch(()=>null)};
  },{url,body,method,csrf});
}
const hostile='# Untrusted note\n\nOrdinary words stay readable.\n\n<script>window.pwned=true</script>\n<img src="x" onerror="window.pwned=true">\n\n[bad link](javascript:window.pwned=true)';
test.beforeEach(async({page})=>{await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});});

test('signed-out, cross-site, CSRF-less and path-escaping requests are refused',async({page,request,baseURL})=>{
  expect((await request.get('/api/state')).status()).toBe(401);
  expect((await request.get('/api/marketing/state')).status()).toBe(401);
  expect((await request.post('/api/auth/login',{headers:{Origin:'https://hostile.example'},data:{key:'bad'}})).status()).toBe(403);
  expect((await request.post('/api/auth/launch',{headers:{Origin:'https://hostile.example'},data:{key:key()}})).status()).toBe(403);
  await launch(page,request,baseURL!);
  const doc={requestId:crypto.randomUUID().replaceAll('-',''),id:null,version:0,scope:'company',scopeId:'company',title:'CSRF probe',body:'Should not be saved.',kind:'fact',status:'draft'};
  expect((await call(page,'/company-wiki',{body:doc,method:'PUT',csrf:false})).status).toBe(403);
  expect((await call(page,'/knowledge',{body:{path:'../escape.md',content:'bad',version:'absent'},method:'PUT'})).status).toBe(400);
  const titles=await page.evaluate(async()=>JSON.stringify(await(await fetch('/api/company-wiki')).json()));
  expect(titles).not.toContain('CSRF probe');
});

test('hostile Markdown in a Library document and in a chat reply is shown as text and never runs',async({page,request,baseURL})=>{
  await page.setViewportSize({width:1440,height:900});
  await launch(page,request,baseURL!);
  const saved=await call(page,'/company-wiki',{method:'PUT',body:{requestId:crypto.randomUUID().replaceAll('-',''),id:null,version:0,scope:'company',scopeId:'company',title:'Untrusted note',body:hostile,kind:'fact',status:'draft'}});
  expect(saved.status).toBe(200);
  await page.goto('/?view=library&open=wiki%3A'+encodeURIComponent(saved.body.id));
  const reader=page.getByRole('main',{name:'Library'});
  await expect(reader.getByText('Ordinary words stay readable.')).toBeVisible();
  await reader.getByText('bad link').click().catch(()=>{});
  expect(await page.evaluate(()=>(window as any).pwned)).toBeUndefined();
  expect(await page.locator('main a[href^="javascript:"]').count()).toBe(0);
  expect(await page.locator('main script, main img[onerror]').count()).toBe(0);

  // The same words as the employee's reply.
  await page.route('**/api/marketing/state',async route=>{
    const json=await (await route.fetch()).json();
    return route.fulfill({json:{...json,connection:{status:'connected'},chatBlockedReason:null,requests:[],messages:[{id:'hostile-reply',sessionKey:json.employee.sessionKey,role:'assistant',content:hostile,createdAt:Math.floor(Date.now()/1000)}]}});
  });
  await page.goto('/?pane=chat');
  const chat=page.getByRole('region',{name:/Conversation with/});
  await expect(chat.getByText('Ordinary words stay readable.')).toBeVisible();
  await chat.getByText('bad link').click().catch(()=>{});
  expect(await page.evaluate(()=>(window as any).pwned)).toBeUndefined();
  expect(await chat.locator('a[href^="javascript:"], script, img[onerror]').count()).toBe(0);
});

test('revoking this browser blocks its very next call and returns it to the sign-in screen',async({page,request,baseURL})=>{
  await launch(page,request,baseURL!);
  const result=await page.evaluate(async()=>{
    const session=await(await fetch('/api/session')).json();
    const before=(await fetch('/api/marketing/state')).status;
    const revoked=(await fetch('/api/devices/'+session.id+'/revoke',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:'{}'})).status;
    return {before,revoked,after:(await fetch('/api/marketing/state')).status,session:(await fetch('/api/session')).status};
  });
  expect(result).toEqual({before:200,revoked:200,after:401,session:401});
  await page.reload();
  await expect(page.getByLabel('Host access key',{exact:true})).toBeVisible();
  await expect(page.getByRole('navigation',{name:'Main views'})).toHaveCount(0);
});

test('losing the host pauses changes with a banner, and reconnecting clears it',async({page,context,request,baseURL})=>{
  await launch(page,request,baseURL!);
  const banner=page.getByRole('alert').filter({hasText:'The host is offline. Changes are paused until it reconnects.'});
  await expect(banner).toHaveCount(0);
  await context.setOffline(true);
  await expect(banner).toBeVisible({timeout:20000});
  await context.setOffline(false);
  await expect(banner).toHaveCount(0,{timeout:20000});
});
