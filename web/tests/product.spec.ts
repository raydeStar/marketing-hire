import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
const screenshots=path.resolve('../artifacts/screenshots');fs.mkdirSync(screenshots,{recursive:true});
const hostKey=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function unlock(page:any){await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(hostKey());await page.getByRole('button',{name:'Unlock study'}).click();await expect(page.getByRole('heading',{name:'Make room for what matters.'})).toBeVisible();}
async function mutation(page:any,url:string,body:any,method='POST'){return page.evaluate(async({url,body,method}:any)=>{const s=await(await fetch('/api/session')).json();const r=await fetch('/api'+url,{method,headers:{'Content-Type':'application/json','X-CSRF':s.csrf},body:JSON.stringify(body)});return {status:r.status,body:await r.json().catch(()=>null)};},{url,body,method});}
test('responsive real workflow, exact approval, editable result and activity receipts',async({page})=>{
  await page.setViewportSize({width:1440,height:1000});await unlock(page);
  await page.screenshot({path:path.join(screenshots,'home-1440.png'),fullPage:true});
  await page.getByLabel('Message or goal').fill('hello');await page.getByRole('button',{name:'Send message'}).click();
  await expect(page.getByText('At your service. A little order, with the mystery left intact.',{exact:false}).first()).toBeVisible();
  await page.reload();await expect(page.getByText('At your service. A little order, with the mystery left intact.',{exact:false}).first()).toBeVisible();
  await page.getByRole('button',{name:'Try the fictional weekly plan'}).click();
  await page.getByRole('checkbox',{name:'Demo only: exercise one bounded draft repair'}).check();
  await page.getByRole('button',{name:'Read selected notes & create a plan'}).click();
  await expect(page.getByRole('heading',{name:'Your permission, precisely.'})).toBeVisible();
  await page.screenshot({path:path.join(screenshots,'approval-1440.png'),fullPage:true});
  for(const width of [390,768]){await page.setViewportSize({width,height:900});await page.screenshot({path:path.join(screenshots,`approval-${width}.png`),fullPage:true});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();}
  await page.getByRole('button',{name:'Approve exact write'}).click();await expect(page.getByRole('button',{name:'Open editable plan'})).toBeVisible();
  await page.getByRole('button',{name:'Open editable plan'}).click();await expect(page.getByLabel('Markdown editor')).toContainText('Unresolved');
  await page.getByLabel('Markdown editor').fill((await page.getByLabel('Markdown editor').inputValue())+'\n\nMy review: decision still pending.');
  await page.getByRole('button',{name:'Save my edits'}).click();
  await page.screenshot({path:path.join(screenshots,'plan-768.png'),fullPage:true});
  for(const width of [390,1440]){await page.setViewportSize({width,height:1000});await page.screenshot({path:path.join(screenshots,`plan-${width}.png`),fullPage:true});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();}
  await page.getByRole('button',{name:'Activity',exact:true}).click();await expect(page.getByRole('heading',{name:'A record worth keeping.'})).toBeVisible();await expect(page.getByRole('button').filter({hasText:'Edit plans/weekly-plan.md'}).first()).toBeVisible();
  for(const width of [1440,768,390]){await page.setViewportSize({width,height:900});await page.screenshot({path:path.join(screenshots,`activity-${width}.png`),fullPage:true});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();}
  await page.reload();await expect(page.getByRole('heading',{name:'Make room for what matters.'})).toBeVisible();
  for(const width of [390,768]){await page.setViewportSize({width,height:900});await page.screenshot({path:path.join(screenshots,`home-${width}.png`),fullPage:true});}
});
test('unauthenticated, CSRF, origin, hostile Markdown and denial boundaries',async({page,request})=>{
  expect((await request.get('/api/state')).status()).toBe(401);await unlock(page);
  expect(await page.evaluate(async()=> (await fetch('/api/demo/seed',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'})).status)).toBe(403);
  expect((await request.post('/api/auth/login',{headers:{Origin:'https://hostile.example'},data:{key:'bad'}})).status()).toBe(403);
  await mutation(page,'/demo/seed',{});
  const result=await mutation(page,'/runs',{objective:'Denied test plan',readScope:['notes/conflict.md']});
  const id=result.body.id;await expect.poll(async()=>await page.evaluate(async(id:string)=>(await(await fetch('/api/runs/'+id)).json()).state,id)).toBe('awaitingApproval');
  const run=await page.evaluate(async(id:string)=>await(await fetch('/api/runs/'+id)).json(),id);
  const before=await page.evaluate(async()=>await(await fetch('/api/knowledge?path=plans/weekly-plan.md')).text());
  expect((await mutation(page,'/runs/'+id+'/approve',{approvalId:run.approval.id,digest:'tampered',allow:true})).status).toBe(409);
  expect((await mutation(page,'/runs/'+id+'/approve',{approvalId:run.approval.id,digest:run.approval.digest,allow:false})).status).toBe(200);
  expect(await page.evaluate(async()=>await(await fetch('/api/knowledge?path=plans/weekly-plan.md')).text())).toBe(before);
  expect((await mutation(page,'/knowledge',{path:'../escape.md',content:'bad',version:'absent'},'PUT')).status).toBe(400);
  const currentVersion=await page.evaluate(async()=>{const r=await fetch('/api/knowledge?path=notes/hostile.md');return r.ok?(await r.json()).version:'absent';});
  const p=await mutation(page,'/knowledge',{path:'notes/hostile.md',content:'# Untrusted\n<script>window.pwned=true</script>\n[bad](javascript:alert(1))',version:currentVersion},'PUT');
  expect(p.status).toBe(200);await page.getByRole('button',{name:'Knowledge',exact:true}).click();await page.reload();await page.getByRole('button',{name:'Knowledge',exact:true}).click();await page.getByRole('button',{name:'notes/hostile.md',exact:true}).click();await page.getByText('Reading view',{exact:true}).click();expect(await page.evaluate(()=>(window as any).pwned)).toBeUndefined();expect(await page.locator('a[href^="javascript:"]').count()).toBe(0);
});
test('second browser decision updates first browser via durable event stream',async({page,browser})=>{
  await unlock(page);await mutation(page,'/demo/seed',{});const objective='Two browsers, one approval '+Date.now();const {body:r}=await mutation(page,'/runs',{objective,readScope:['notes/constraints.md']});
  await expect.poll(async()=>await page.evaluate(async(id:string)=>(await(await fetch('/api/runs/'+id)).json()).state,r.id)).toBe('awaitingApproval');
  await page.getByRole('button',{name:'Tasks',exact:true}).click();await page.getByRole('button').filter({hasText:objective}).click();
  const other=await browser.newPage();await unlock(other);const run=await other.evaluate(async(id:string)=>await(await fetch('/api/runs/'+id)).json(),r.id);
  await mutation(other,'/runs/'+r.id+'/approve',{approvalId:run.approval.id,digest:run.approval.digest,allow:false});await expect(page.getByText('Write denied · nothing saved',{exact:true})).toBeVisible();await other.close();
  await page.context().setOffline(true);await expect(page.getByText('Connection lost.',{exact:false})).toBeVisible({timeout:20000});await page.screenshot({path:path.join(screenshots,'disconnected.png'),fullPage:true});await page.context().setOffline(false);
});
test('raven uses real running state and reduced motion',async({page})=>{
  await unlock(page);await page.emulateMedia({reducedMotion:'reduce'});
  const animation=await page.locator('.raven svg').first().evaluate((el:any)=>getComputedStyle(el).animationName);expect(animation).toBe('none');
});
test('revocation blocks the next API call and replay is read-only',async({page})=>{
  await unlock(page);
  const result=await page.evaluate(async()=>{
    const state=await(await fetch('/api/state')).json();const r=state.runs[0];
    const before=await(await fetch('/api/knowledge?path=plans/weekly-plan.md')).text();
    const events=await(await fetch('/api/runs/'+r.id+'/replay')).json();
    const tail=await(await fetch('/api/runs/'+r.id+'/replay?after='+events[0].cursor)).json();
    const after=await(await fetch('/api/knowledge?path=plans/weekly-plan.md')).text();
    const session=await(await fetch('/api/session')).json();
    await fetch('/api/devices/'+session.id+'/revoke',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:'{}'});
    return {unchanged:before===after,tail:tail.length,events:events.length,status:(await fetch('/api/state')).status,localKeys:Object.keys(localStorage),cookies:document.cookie};
  });
  expect(result.unchanged).toBeTruthy();expect(result.tail).toBe(result.events-1);expect(result.status).toBe(401);expect(result.localKeys).toEqual([]);expect(result.cookies).not.toContain('thaddeus-session');
});


test('conversation becomes an explicit scoped goal with budget controls',async({page})=>{
  await unlock(page);await mutation(page,'/demo/seed',{});
  const message='Prepare my fictional week '+Date.now();
  await page.getByLabel('Message or goal').fill(message);await page.getByRole('button',{name:'Send message'}).click();
  const bubble=page.locator('article.chat.user').filter({hasText:message});
  await expect(bubble).toBeVisible();await bubble.getByRole('button',{name:'Create a goal from this message'}).click();
  await expect(page.getByLabel('Message or goal')).toHaveValue(message);
  await page.getByRole('region',{name:'Plan scope'}).getByText('Resource limits & provider guarantees',{exact:true}).click();
  await expect(page.getByRole('region',{name:'Plan scope'}).getByLabel('Total token allowance')).toBeVisible();
  await expect(page.getByRole('button',{name:'Read selected notes & create a plan'})).toBeVisible();
});


test('long replay follows cursor pages to the final receipt',async({page})=>{
 await unlock(page);await mutation(page,'/demo/seed',{});
 const {body:run}=await mutation(page,'/runs',{objective:'Long replay pagination fixture',readScope:['notes/conflict.md']});
 await expect.poll(async()=>await page.evaluate(async id=>(await(await fetch('/api/runs/'+id)).json()).state,run.id)).toBe('awaitingApproval');
 const cursors:number[]=[];
 await page.route('**/api/runs/'+run.id+'/replay?*',async route=>{
  const after=Number(new URL(route.request().url()).searchParams.get('after'));cursors.push(after);
  const records=Array.from({length:Math.min(2000,2005-after)},(_,i)=>({eventId:'fixture-'+(after+i+1),cursor:after+i+1,sequence:after+i+1,type:'fixture.receipt',timestamp:new Date(0).toISOString()}));
  await route.fulfill({json:records});
 });
 await page.getByRole('button',{name:'Tasks',exact:true}).click();await page.locator(`[data-run-id="${run.id}"]`).click();
 await expect(page.getByText('Replay recorded events · 2005 receipts · no re-execution',{exact:true})).toBeVisible();
 expect(cursors).toContain(2000);
});

test('worker setup reports observed readiness without enabling unqualified execution',async({page})=>{
 await unlock(page);await page.getByRole('button',{name:'Settings',exact:true}).click();
 const setup=page.getByRole('region',{name:'Isolated worker setup'});
 await expect(setup.getByRole('heading',{name:'Thaddeus’s computer'})).toBeVisible();
 await setup.getByRole('button',{name:'Check worker setup'}).click();
 await expect(setup.getByText('Agent execution remains unavailable until worker isolation is verified.',{exact:false})).toBeVisible();
 await expect(setup.getByRole('button',{name:'Check worker setup'})).toBeEnabled({timeout:40000});
 const actual=await page.evaluate(async()=>(await fetch('/api/settings/sandbox')).json());
 expect(actual.executionEnabled).toBe(false);expect(actual.lastInspection.backend).toBe('docker-sandboxes');
 expect(actual.lastInspection.observedAt).toBeTruthy();
 for(const width of [1440,390]){
   await page.setViewportSize({width,height:1000});
   await setup.screenshot({path:path.join(screenshots,`worker-setup-${width}.png`)});
   expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
 }
 const exported=await page.evaluate(async()=>(await fetch('/api/export')).json());
 expect(exported.schemaVersion).toBe(3);expect(exported.databaseSchemaVersion).toBe(2);
 expect(exported.events.length).toBeGreaterThan(0);
});

test('research composer displays its scope and cannot start an unqualified worker',async({page})=>{
 await unlock(page);await mutation(page,'/demo/seed',{});
 await page.reload();await page.getByLabel('Message mode').selectOption('research');
 const scope=page.getByRole('region',{name:'Research scope'});
 await expect(scope).toBeVisible();await expect(scope.getByText('Isolated research is not ready on this host.',{exact:false})).toBeVisible();
 await page.getByLabel('Message or goal').fill('Investigate these notes');
 await page.getByLabel('Public source websites (optional)').fill('docs.docker.com');
 await expect(page.getByRole('button',{name:'Start research'})).toBeDisabled();
 const before=await page.evaluate(async()=>(await(await fetch('/api/state')).json()).runs.length);
 expect((await mutation(page,'/chat',{content:'Investigate these notes',mode:'research',readScope:['notes/conflict.md']})).status).toBe(409);
 expect(await page.evaluate(async()=>(await(await fetch('/api/state')).json()).runs.length)).toBe(before);
 for(const width of [1440,390]){
   await page.setViewportSize({width,height:1000});await page.screenshot({path:path.join(screenshots,`research-scope-${width}.png`),fullPage:true});
   expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
 }
 await page.getByLabel('Message mode').selectOption('chat');await expect(page.getByRole('button',{name:'Send message'})).toBeEnabled();
});


test('token usage exposes incomplete accounting and the next reply allowance',async({page})=>{
 await unlock(page);
 await expect(page.getByRole('group',{name:'Token usage'})).toBeVisible();
 await expect(page.getByText('Next reply: 64,000 token allowance',{exact:false})).toBeVisible();
 await page.getByText('Resource limits & provider guarantees',{exact:true}).click();
 await page.getByLabel('Total token allowance').fill('2000');
 await expect(page.getByText('Next reply: 2,000 token allowance',{exact:false})).toBeVisible();
 await page.route('**/api/state',async route=>{
   const response=await route.fetch();const state=await response.json();
   state.runs=[{id:'usage-fixture',goal:{objective:'Unknown usage fixture',kind:'conversation',provider:{kind:'compatible',model:'fixture',reasoning:'high'},limits:{maxTotalTokens:2000},criteria:[]},state:'needsAttention',summary:'Unreported usage',created:new Date().toISOString(),updated:new Date().toISOString(),modelCalls:1,toolCalls:0,repairs:0,evidence:[],inputTokens:null,outputTokens:null,chargedTokens:2000,reservedTokens:0}];
   await route.fulfill({json:state});
 });
 await page.reload();
 const usage=page.getByRole('group',{name:'Token usage'});
 await expect(usage.locator('summary')).toContainText('1 task with unreported usage');
 await usage.locator('summary').click();
 await expect(usage.getByText('Input unreported',{exact:false})).toBeVisible();
 await expect(usage.getByText('remaining allowance 0',{exact:false})).toBeVisible();
 await expect(usage.getByText('The reported total is incomplete',{exact:false})).toBeVisible();
});
