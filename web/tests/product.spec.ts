import {chooseMessageMode,closeSidebarOverlay,navigateStudy,openLog,openSettings,resizeLog} from './navigation';
import {test,expect,type Page,type BrowserContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
const screenshots=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(screenshots,{recursive:true});
const hostKey=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
let ownerCookies:Awaited<ReturnType<BrowserContext['cookies']>>|undefined;
async function unlock(page:Page,{freshSession=false}={}){
  // Reuse the owner's session for UI checks; the doorman still counts every login.
  if(!freshSession&&ownerCookies) await page.context().addCookies(ownerCookies);
  await page.goto('/');
  if(freshSession||!ownerCookies){
    await page.getByLabel('Host access key',{exact:true}).fill(hostKey());
    const response=page.waitForResponse(response=>response.url().endsWith('/api/auth/login')&&response.request().method()==='POST');
    await page.getByRole('button',{name:'Open workspace'}).click();
    expect((await response).status(),'Host-key login response').toBe(200);
    if(!freshSession) ownerCookies=await page.context().cookies();
  }
  await expect(page.getByRole('heading',{name:'Conversation'})).toBeVisible();
}
async function mutation(page:any,url:string,body:any,method='POST'){return page.evaluate(async({url,body,method}:any)=>{const s=await(await fetch('/api/session')).json();const r=await fetch('/api'+url,{method,headers:{'Content-Type':'application/json','X-CSRF':s.csrf},body:JSON.stringify(body)});return {status:r.status,body:await r.json().catch(()=>null)};},{url,body,method});}
test('responsive real workflow, exact approval, editable result and activity receipts',async({page})=>{
  await page.setViewportSize({width:1440,height:1000});await unlock(page);
  await page.screenshot({path:path.join(screenshots,'home-1440.png'),fullPage:true});
  await page.getByLabel('Message or goal').fill('hello');await page.getByRole('button',{name:'Send message',exact:true}).click();
  await expect(page.getByText('At your service. A little order, with the mystery left intact.',{exact:false}).first()).toBeVisible();
  await page.reload();await expect(page.getByText('At your service. A little order, with the mystery left intact.',{exact:false}).first()).toBeVisible();
  await mutation(page,'/demo/seed',{});
  const created=await mutation(page,'/runs',{objective:'Turn my scattered notes into a useful weekly plan.',readScope:['notes/deadlines.md','notes/constraints.md','notes/conflict.md'],demoFailure:true});
  expect(created.status).toBe(200);
  await expect.poll(async()=>await page.evaluate(async id=>(await(await fetch('/api/runs/'+id)).json()).state,created.body.id)).toBe('awaitingApproval');
  await openLog(page);await page.locator(`[data-run-id="${created.body.id}"]`).click();
  await expect(page.getByRole('heading',{name:'Your permission, precisely.'})).toBeVisible();
  await page.screenshot({path:path.join(screenshots,'approval-1440.png'),fullPage:true});
  for(const width of [390,768]){await page.setViewportSize({width,height:900});await page.screenshot({path:path.join(screenshots,`approval-${width}.png`),fullPage:true});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();}
  await page.getByRole('button',{name:'Approve exact write'}).click();
  await expect.poll(async()=>await page.evaluate(async id=>(await(await fetch('/api/runs/'+id)).json()).state,created.body.id)).toBe('succeeded');
  const finished=await page.evaluate(async id=>await(await fetch('/api/runs/'+id)).json(),created.body.id);expect(finished.outputPath).toBe('plans/weekly-plan.md');
  await page.reload();await openLog(page);const finishedRow=page.locator(`[data-run-id="${created.body.id}"]`);await finishedRow.click();await expect(finishedRow).toHaveAttribute('aria-current','true');await expect(page.getByRole('heading',{name:'Turn my scattered notes into a useful weekly plan.'})).toBeVisible();await page.getByText('Checks, sources & full receipts',{exact:true}).click();await expect(page.getByRole('button',{name:'Open editable plan'})).toBeVisible();
  await page.getByRole('button',{name:'Open editable plan'}).click();await expect(page.getByRole('complementary',{name:'Activity log'})).toHaveCount(0);await expect(page.getByLabel('Markdown editor')).toBeVisible();await expect(page.getByLabel('Markdown editor')).toContainText('Unresolved');
  await page.getByLabel('Markdown editor').fill((await page.getByLabel('Markdown editor').inputValue())+'\n\nMy review: decision still pending.');
  await page.getByRole('button',{name:'Save my edits'}).click();
  await page.screenshot({path:path.join(screenshots,'plan-768.png'),fullPage:true});
  for(const width of [390,1440]){await page.setViewportSize({width,height:1000});await page.screenshot({path:path.join(screenshots,`plan-${width}.png`),fullPage:true});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();}
  await openLog(page);await expect(page.getByRole('heading',{name:'Activity log'})).toBeVisible();await expect(page.getByRole('button').filter({hasText:'Edit plans/weekly-plan.md'}).first()).toBeVisible();
  for(const width of [1440,768,390]){await resizeLog(page,width,900);await page.screenshot({path:path.join(screenshots,`activity-${width}.png`),fullPage:true});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();}
  await page.reload();await expect(page.getByRole('heading',{name:'Conversation'})).toBeVisible();
  for(const width of [390,768]){await page.setViewportSize({width,height:900});await page.screenshot({path:path.join(screenshots,`home-${width}.png`),fullPage:true});}
});
test('unauthenticated, CSRF, origin, hostile Markdown and denial boundaries',async({page,request})=>{
  expect((await request.get('/api/state')).status()).toBe(401);await unlock(page,{freshSession:true});
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
  expect(p.status).toBe(200);await navigateStudy(page,'Artifacts');await page.reload();await navigateStudy(page,'Artifacts');await page.getByRole('button',{name:'Notes & memory',exact:true}).click();await page.getByRole('button',{name:'notes/hostile.md',exact:true}).click();await page.getByText('Reading view',{exact:true}).click();expect(await page.evaluate(()=>(window as any).pwned)).toBeUndefined();expect(await page.locator('a[href^="javascript:"]').count()).toBe(0);
});
test('second browser decision updates first browser via durable event stream',async({page,browser})=>{
  await unlock(page);await mutation(page,'/demo/seed',{});const objective='Two browsers, one approval '+Date.now();const {body:r}=await mutation(page,'/runs',{objective,readScope:['notes/constraints.md']});
  await expect.poll(async()=>await page.evaluate(async(id:string)=>(await(await fetch('/api/runs/'+id)).json()).state,r.id)).toBe('awaitingApproval');
  await openLog(page);await page.locator(`[data-run-id="${r.id}"]`).click();
  const other=await browser.newPage();await unlock(other,{freshSession:true});const run=await other.evaluate(async(id:string)=>await(await fetch('/api/runs/'+id)).json(),r.id);
  await mutation(other,'/runs/'+r.id+'/approve',{approvalId:run.approval.id,digest:run.approval.digest,allow:false});await expect(page.getByRole('dialog',{name:objective}).locator('.activity-summary')).toHaveText('Write denied · nothing saved');await other.close();
  await page.context().setOffline(true);await expect(page.getByText('Connection lost.',{exact:false})).toBeVisible({timeout:20000});await page.screenshot({path:path.join(screenshots,'disconnected.png'),fullPage:true});await page.context().setOffline(false);
});
test('revocation blocks the next API call and replay is read-only',async({page})=>{
  await unlock(page,{freshSession:true});await mutation(page,'/demo/seed',{});
  await page.getByLabel('Message or goal').fill('hello');await page.getByRole('button',{name:'Send message',exact:true}).click();await expect(page.getByText('At your service. A little order, with the mystery left intact.',{exact:false})).toBeVisible();
  const runId=await page.evaluate(async()=>(await(await fetch('/api/state')).json()).runs.find((run:any)=>run.goal.kind==='conversation').id);
  const result=await page.evaluate(async(runId:string)=>{
    const before=await(await fetch('/api/knowledge?path=notes/constraints.md')).text();
    const events=await(await fetch('/api/runs/'+runId+'/replay')).json();
    const tail=await(await fetch('/api/runs/'+runId+'/replay?after='+events[0].cursor)).json();
    const after=await(await fetch('/api/knowledge?path=notes/constraints.md')).text();
    const session=await(await fetch('/api/session')).json();
    await fetch('/api/devices/'+session.id+'/revoke',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:'{}'});
    return {unchanged:before===after,tail:tail.length,events:events.length,status:(await fetch('/api/state')).status,localKeys:Object.keys(localStorage),cookies:document.cookie};
  },runId);
  expect(result.unchanged).toBeTruthy();expect(result.tail).toBe(result.events-1);expect(result.status).toBe(401);expect(result.localKeys).toEqual([]);expect(result.cookies).not.toContain('thaddeus-session');
});


test('conversation keeps planning choices in message options without per-message goal clutter',async({page})=>{
  await unlock(page);await mutation(page,'/demo/seed',{});
  const message='Prepare my fictional week '+Date.now();
  await page.getByLabel('Message or goal').fill(message);await page.getByRole('button',{name:'Send message',exact:true}).click();
  const bubble=page.locator('article.chat.user').filter({hasText:message});
  await expect(bubble).toBeVisible();await expect(bubble.getByRole('button',{name:'Create a goal from this message'})).toHaveCount(0);
  await page.getByLabel('Message or goal').fill('Plan from the selected notes without losing this draft.');await chooseMessageMode(page,'research');
  await expect(page.getByLabel('Message or goal')).toHaveValue('Plan from the selected notes without losing this draft.');
  const scope=page.getByRole('region',{name:'Research scope'});await scope.getByText('Resource limits & provider guarantees',{exact:true}).click();
  await expect(scope.getByLabel('Total token allowance')).toBeVisible();await expect(page.getByRole('button',{name:'Start research',exact:true})).toBeDisabled();
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
 await openLog(page);await page.locator(`[data-run-id="${run.id}"]`).click();
 await expect(page.getByText('Replay recorded events · 2005 receipts · no re-execution',{exact:true})).toBeVisible();
 expect(cursors).toContain(2000);
});

test('worker setup reports observed readiness without enabling unqualified execution',async({page})=>{
 await unlock(page);const eventCountBefore=await page.evaluate(async()=>(await(await fetch('/api/export')).json()).events.length);await openSettings(page);
 await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
 await expect(page.getByRole('region',{name:'Stored research workspaces'}).getByText('No private research workspaces are retained.',{exact:true})).toBeVisible();
 await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Research worker',exact:true}).click();
 const host=page.getByRole('region',{name:'Host research setup'});
 await expect(host.getByRole('heading',{name:'Set up this host'})).toBeVisible();
 await expect(host.getByRole('button',{name:'Check installed worker'})).toBeDisabled();
 expect((await mutation(page,'/settings/worker',{installationDigest:'untrusted-browser-choice',enabled:true})).status).toBe(409);
 await page.getByText('Docker diagnostics',{exact:true}).click();
 const setup=page.getByRole('region',{name:'Isolated worker setup'});
 await expect(setup.getByRole('heading',{name:'Docker worker diagnostics'})).toBeVisible();
 await setup.getByRole('button',{name:'Check worker setup'}).click();
 await expect(setup.getByText('They do not change the selected worker or research setup above.',{exact:false})).toBeVisible();
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
 expect(exported.schemaVersion).toBe(12);expect(exported.databaseSchemaVersion).toBe(12);
 expect(exported.events).toHaveLength(eventCountBefore);
});

test('research composer displays its scope and cannot start an unqualified worker',async({page})=>{
 await unlock(page);await mutation(page,'/demo/seed',{});
 await page.reload();await chooseMessageMode(page,'research');
 const scope=page.getByRole('region',{name:'Research scope'});
 await expect(scope).toBeVisible();await expect(scope.getByText('No worker package is configured on this host.',{exact:false})).toBeVisible();
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
 await chooseMessageMode(page,'chat');await expect(page.getByRole('button',{name:'Send message',exact:true})).toBeEnabled();
});


test('model badge opens token accounting and editable reply allowances',async({page})=>{
 await unlock(page);
 const badge=page.getByRole('button',{name:/token usage$/});await badge.hover();await expect(page.getByRole('tooltip')).toContainText('0 reported tokens');await badge.click();
 const info=page.getByRole('region',{name:'Model and token information'});const usage=info.getByRole('group',{name:'Token usage'});await expect(usage).toBeVisible();
 const history=info.getByRole('region',{name:'Token usage history'});await expect(history).toBeVisible();await expect(history.getByRole('img')).toHaveAttribute('aria-label',/Last 7 days token usage/);
 await history.getByRole('button',{name:'Day'}).click();await expect(history.getByRole('img')).toHaveAttribute('aria-label',/Today token usage/);
 await history.getByRole('button',{name:'Month'}).click();await expect(history.getByRole('img')).toHaveAttribute('aria-label',/Last 30 days token usage/);await page.screenshot({path:path.join(screenshots,'token-usage-history.png'),animations:'disabled'});
 await expect(info.getByText('64,000 token allowance',{exact:false})).toBeVisible();await info.getByText('Resource limits & provider guarantees',{exact:true}).click();
 await info.getByLabel('Total token allowance').fill('2000');await expect(info.getByText('2,000 token allowance',{exact:false})).toBeVisible();
 await page.route('**/api/state',async route=>{
   const response=await route.fetch();const state=await response.json();
   const now=new Date();const yesterday=new Date(now);yesterday.setDate(yesterday.getDate()-1);
   const run=(id:string,objective:string,updated:string,inputTokens:number|null,outputTokens:number|null)=>({id,goal:{objective,kind:'conversation',provider:{kind:'compatible',model:'fixture',reasoning:'high'},limits:{maxTotalTokens:2000},criteria:[]},state:'succeeded',summary:'Usage fixture',created:updated,updated,modelCalls:1,toolCalls:0,repairs:0,evidence:[],inputTokens,outputTokens,chargedTokens:(inputTokens??0)+(outputTokens??0),reservedTokens:0});
   const unknown=run('usage-fixture','Unknown usage fixture',now.toISOString(),null,null);unknown.chargedTokens=2000;
   state.runs=[run('today-usage','Today usage fixture',now.toISOString(),200,100),run('yesterday-usage','Yesterday usage fixture',yesterday.toISOString(),500,200),unknown];
   await route.fulfill({json:state});
 });
 await page.reload();
 const dailyBadge=page.getByRole('button',{name:/token usage$/});await dailyBadge.hover();const tooltip=page.getByRole('tooltip');await expect(tooltip).toContainText('300 reported tokens today');await expect(tooltip).toContainText('retained total 1,000');await expect(tooltip).toContainText('Today is incomplete');
 await dailyBadge.click();
 const refreshed=page.getByRole('region',{name:'Model and token information'}).getByRole('group',{name:'Token usage'});
 await expect(refreshed.locator('summary')).toContainText('Today · 300 reported');await expect(refreshed.locator('summary')).toContainText('retained total 1,000');
 await expect(refreshed.locator('summary')).toContainText('1 incomplete');
 await page.screenshot({path:path.join(screenshots,'token-usage-daily-fixture.png'),animations:'disabled'});
 await expect(refreshed.getByText('Input unreported',{exact:false})).toBeVisible();
 await expect(refreshed.getByText('remaining allowance 0',{exact:false})).toBeVisible();
 await expect(refreshed.getByText('The reported total is incomplete',{exact:false})).toBeVisible();
});

test('cancelled artifact mismatch retains its receipt without offering an import',async({page})=>{
 await unlock(page);
 await page.route('**/api/state',async route=>{
  const response=await route.fetch(); const state=await response.json(); const now=new Date().toISOString();
  state.runs=[{id:'artifact-check-fixture',goal:{objective:'Artifact mismatch fixture',kind:'research',provider:{kind:'scripted',model:'fixture',reasoning:'high'},limits:{modelCalls:6,toolCalls:16,maxTotalTokens:96000},criteria:[]},
   state:'cancelled',summary:'Cancelled; artifact mismatch retained',created:now,updated:now,modelCalls:0,toolCalls:0,repairs:0,evidence:[],
   research:{phase:'finished',message:'Worker retired; workspace retained',workerRetained:true},
   approval:{id:'rejected-approval',digest:'fixture-digest',decision:'pending',expires:now,action:{name:'knowledge.write',path:'plans/report.md',content:'# Proposed report'}},
   artifactChecks:[{approvalId:'rejected-approval',artifact:'report.md',expectedSha256:'a'.repeat(64),observedSha256:'b'.repeat(64),status:'content-mismatch',checkedAt:now}]}];
  await route.fulfill({json:state});
 });
 await page.route('**/api/runs/artifact-check-fixture/replay?*',route=>route.fulfill({json:[]}));
 await page.reload(); await openLog(page); await page.locator('[data-run-id="artifact-check-fixture"]').click();await page.getByText('Checks, sources & full receipts',{exact:true}).click();
 const receipt=page.getByRole('region',{name:'Artifact verification'});
 await expect(receipt).toBeVisible();
 await expect(receipt.getByText('Written artifact differs from the proposal',{exact:false})).toBeVisible();
 await receipt.getByText('Compared artifact hashes',{exact:true}).click();
 await expect(receipt.getByText('a'.repeat(64),{exact:true})).toBeVisible();
 await expect(receipt.getByText('b'.repeat(64),{exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Approve exact write'})).toHaveCount(0);
});

test('explicit remembered context can be corrected from its source and forgotten across tabs',async({page})=>{
 await unlock(page);
 const sourcePath='notes/memory-browser-'+Date.now()+'.md';
 const source=await mutation(page,'/knowledge',{path:sourcePath,content:'Morning workshops last 45 minutes.\nAfternoon workshops last 90 minutes.',version:'absent'},'PUT');expect(source.status).toBe(200);
 await page.reload();await navigateStudy(page,'Artifacts');await page.getByRole('button',{name:'Notes & memory',exact:true}).click();
 const memory=page.getByRole('group',{name:'Remembered context'});await memory.locator('summary').first().click();
 await memory.getByLabel('Source note',{exact:true}).selectOption(sourcePath);
 await memory.getByLabel('Remembered statement').fill('Morning workshops last 45 minutes.');
 await memory.getByLabel('Exact source quotation').fill('Morning workshops last 45 minutes.');
 await memory.getByRole('button',{name:'Remember this statement'}).click();
 const entry=memory.locator('[data-memory-id]');await expect(entry).toHaveCount(1);
 const changed=await mutation(page,'/knowledge',{path:sourcePath,content:'Morning workshops last 60 minutes.\nAfternoon workshops last 90 minutes.',version:source.body.version},'PUT');expect(changed.status).toBe(200);
 await expect(entry.getByText('Source changed or missing · review before reuse',{exact:true})).toBeVisible();
 await entry.getByRole('button',{name:'Correct or review entry'}).click();
 await memory.getByLabel('Remembered statement').fill('Morning workshops last 60 minutes.');
 await memory.getByLabel('Exact source quotation').fill('Morning workshops last 60 minutes.');
 await memory.getByRole('button',{name:'Save reviewed correction'}).click();
 await expect(entry.getByText('Source version unchanged',{exact:true})).toBeVisible();
 await expect(entry.locator('blockquote')).toHaveText('Morning workshops last 60 minutes.');
 for(const width of [1440,390]){
   await page.setViewportSize({width,height:1000});await memory.screenshot({path:path.join(screenshots,`remembered-context-${width}.png`)});
   expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
 }
 await closeSidebarOverlay(page);
 const other=await page.context().newPage();await other.goto('/');await navigateStudy(other,'Artifacts');await other.getByRole('button',{name:'Notes & memory',exact:true}).click();
 const otherMemory=other.getByRole('group',{name:'Remembered context'});await otherMemory.locator('summary').first().click();
 await expect(otherMemory.locator('[data-memory-id]')).toHaveCount(1);
 await entry.getByRole('button',{name:'Forget entry'}).click();
 await expect(memory.getByText('No remembered entries yet.',{exact:true})).toBeVisible();
 await expect(otherMemory.getByText('No remembered entries yet.',{exact:true})).toBeVisible();await other.close();
 const exported=await page.evaluate(async()=>(await fetch('/api/export')).json());
 expect(exported.memories).toHaveLength(1);expect(exported.memories[0].forgotten).toBe(true);expect(exported.memories[0].statement).toBe('');expect(exported.memories[0].source).toBeNull();
 expect(exported.memoryChanges.map((change:any)=>change.kind)).toEqual(['remembered','corrected','forgotten']);
 expect(exported.pages.find((entry:any)=>entry.path===sourcePath).content).toContain('60 minutes');
 await page.reload();await navigateStudy(page,'Artifacts');await page.getByRole('button',{name:'Notes & memory',exact:true}).click();await memory.locator('summary').first().click();
 await expect(memory.getByText('No remembered entries yet.',{exact:true})).toBeVisible();
});
