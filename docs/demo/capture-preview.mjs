import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn, execFileSync} from 'node:child_process';
import {createServer} from 'node:net';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import ts from '../../web/node_modules/typescript/lib/typescript.js';
import {chromium} from '../../web/node_modules/playwright/index.mjs';
import {layoutFaults} from '../../web/tools/layout-faults.mjs';
import {cleanArtifactPaths, requireArtifactSpace} from '../../scripts/artifact-storage.mjs';

const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const evidence=path.join(repo,'artifacts/marketing-demo-20260927');
await fs.mkdir(evidence,{recursive:true});
const scratch=path.join(evidence,'scratch'),origin='http://localhost:5185';
const images=path.join(evidence,'captures');
const receipt={capturedAt:new Date().toISOString(),sourceHead:execFileSync('git',['rev-parse','HEAD'],{cwd:repo,encoding:'utf8'}).trim(),fictionalData:true,liveModelCalls:false,audioGenerated:false,sourceDirty:execFileSync('git',['status','--porcelain'],{cwd:repo,encoding:'utf8'}).trim().split('\n').filter(Boolean),shots:[],commands:[]};
await requireArtifactSpace(repo,128*1024**2,'Marketing video capture');
const guard=createServer();await new Promise((resolve,reject)=>{guard.once('error',reject);guard.listen(5185,'127.0.0.1',resolve);});await new Promise(resolve=>guard.close(resolve));
await fs.mkdir(scratch);await fs.mkdir(images,{recursive:true});
let host,browser;
async function command(file,args,cwd=repo){
  const child=spawn(file,args,{cwd,windowsHide:true,stdio:['ignore','pipe','pipe']});let log='';
  child.stdout.on('data',x=>log+=x);child.stderr.on('data',x=>log+=x);
  const code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',resolve);});
  await fs.writeFile(path.join(evidence,'build.log'),log);receipt.commands.push({file,args,code});assert.equal(code,0,log.slice(-1500));
}
try{
  await command(process.execPath,[path.join(repo,'web/node_modules/vite/bin/vite.js'),'build','--outDir',path.join(scratch,'wwwroot')],path.join(repo,'web'));
  const env=Object.fromEntries(Object.entries(process.env).filter(([key])=>! /^(Thaddeus|Marketing|ASPNETCORE|DOTNET_)/i.test(key)));
  Object.assign(env,{Thaddeus__Data:path.join(scratch,'study'),Thaddeus__LocalOrigin:origin,Thaddeus__ApiRequestsPerMinute:'3000',Thaddeus__AuthRequestsPerMinute:'120',Marketing__Container:'nonexistent-marketing-video-fixture',Marketing__SharedContainer:'nonexistent-marketing-video-fixture',Marketing__ShiftPump:'off'});
  const dll=path.join(repo,'src/Thaddeus.Host/bin/Release/net10.0/Thaddeus.Host.dll');
  receipt.hostSha256=createHash('sha256').update(await fs.readFile(dll)).digest('hex');
  host=spawn('dotnet',[dll,'--contentRoot',scratch,'--webroot',path.join(scratch,'wwwroot')],{cwd:scratch,env,windowsHide:true,stdio:['ignore','pipe','pipe']});
  let hostLog='';host.stdout.on('data',x=>hostLog+=x);host.stderr.on('data',x=>hostLog+=x);
  host.finished=new Promise((resolve,reject)=>{host.once('error',reject);host.once('exit',resolve);});
  let ready=false;for(let i=0;i<80;i++){assert.equal(host.exitCode,null,hostLog);try{if((await fetch(origin,{signal:AbortSignal.timeout(500)})).ok){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,250));}
  assert.ok(ready,hostLog);
  const source=await fs.readFile(path.join(repo,'web/tests/magical-cockpit.spec.ts'),'utf8');
  const helpers=source.slice(source.indexOf("const taskId="),source.indexOf('async function launch('));
  const {fiction,mock}=new Function(ts.transpileModule(helpers,{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText+';return {fiction,mock};')();
  const data=fiction();data.today.opportunity.why='The introduction, launch post and website headline are ready to review together.';
  data.today.opportunity.headline='Introduce HireZero through the work';
  data.today.opportunity.evidence=[{title:'Launch positioning and owner-approved claims',key:'wiki:plan'}];
  data.today.today=[{id:'quote',kind:'task',title:'Confirm the customer quote',detail:'Permission is still needed before this can be used.',target:'task:'+'a'.repeat(32)}];
  data.today.later=[{id:'followup',kind:'document',title:'Plan the follow-up email',detail:'After the launch package is reviewed.',target:'wiki:email'}];
  data.state.profile.product_summary='A marketing employee that prepares campaign work for a founder to review.';
  data.state.profile.audience='Solo founders and small teams who need a practical marketing partner.';
  data.state.profile.goals='Prepare one clear launch introduction and start qualified conversations.';
  data.state.profile.voice='Plain-spoken, specific and warm. Show useful work before making a big claim.';
  data.state.profile.guardrails='Keep customer quotes on hold until permission is confirmed. No unsupported performance claims.';
  data.state.businessBriefEvidenceEnabled=true;
  data.state.employee.model='Illustrative workspace';data.state.drafts[0].status='rejected';
  data.state.messages=[{id:'demo-owner',sessionKey:data.state.employee.sessionKey,role:'user',content:'Prepare our introduction for solo founders. Lead with the work they can review, and keep unsupported claims out.',createdAt:1790500800},{id:'demo-claw',sessionKey:data.state.employee.sessionKey,role:'assistant',content:'The introduction is ready for your review.\n\nI grouped the LinkedIn opening, website headline and objection answer into one campaign. Each piece keeps its claim and review notes alongside it.\n\nMy recommendation: show one useful example first. The customer quote stays on hold until permission is confirmed.',createdAt:1790500820}];
  browser=await chromium.launch();
  const context=await browser.newContext({viewport:{width:1440,height:960},colorScheme:'light',deviceScaleFactor:2});
  await context.route('**/*',route=>new URL(route.request().url()).origin===origin?route.continue():route.abort());
  await context.addInitScript(()=>{localStorage.setItem('thaddeus-theme','light');localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');localStorage.setItem('fe-cockpit-open','yes');});
  const page=await context.newPage();const errors=[];page.on('pageerror',error=>errors.push(error.message));
  await mock(page,data);
  await page.route('**/api/campaigns/*/pieces',route=>route.fulfill({json:{campaignId:'launch-week',angle:'Show the work before explaining the technology.',pieces:[
    {key:'draft:8',week:'2026-09-28',channel:'LinkedIn',grade:'A',claims:[{text:'Owner reviews each draft.',url:'https://example.org/evidence'}],blockers:[]},
    {key:'pagecopy:'+'b'.repeat(16),week:'2026-09-28',channel:'Website',grade:'A',claims:[{text:'Owner reviews each draft.'}],blockers:[]},
    {key:'wiki:email',week:'2026-10-05',channel:'Email',grade:'B',claims:[{text:'Owner reviews each draft.'}],blockers:[]},
    {key:'task:'+'a'.repeat(32),week:'2026-10-05',channel:'Email',grade:null,claims:[],blockers:['Customer has not approved the quote.']}
  ]}}));
  await page.route('**/api/drafts/media',route=>route.fulfill({json:{}}));
  const key=(await fs.readFile(path.join(scratch,'study/host-key.txt'),'utf8')).trim();
  const issued=await (await fetch(origin+'/api/auth/launch',{method:'POST',headers:{Origin:origin,'Content-Type':'application/json'},body:JSON.stringify({key})})).json();
  await page.goto(origin+'/?pane=chat#launch='+issued.ticket);await page.locator('.fe-app').waitFor();
  async function shot(name){
    await page.waitForTimeout(900);await page.mouse.move(2,2);
    const layout=await page.evaluate(layoutFaults);const file=path.join(images,name+'.png');
    await page.screenshot({path:file,animations:'disabled'});
    receipt.shots.push({name,viewport:page.viewportSize(),layout,errors:[...errors],sha256:createHash('sha256').update(await fs.readFile(file)).digest('hex')});
    assert.deepEqual(layout,[],name+' layout faults');assert.deepEqual(errors,[],name+' JS errors');
  }
  await page.getByRole('region',{name:'Prepared opportunity'}).waitFor();await shot('cockpit-desktop');
  await page.getByRole('region',{name:'Prepared opportunity'}).screenshot({path:path.join(images,'opportunity-card.png'),animations:'disabled'});
  await page.goto(origin+'/?pane=work&open=employee:marketing-main');
  await page.getByRole('button',{name:'Business brief',exact:true}).click();
  await page.getByRole('region',{name:'Business brief',exact:true}).waitFor();await shot('business-brief');
  await page.getByRole('region',{name:'Business brief',exact:true}).screenshot({path:path.join(images,'brief-card.png'),animations:'disabled'});
  await page.goto(origin+'/?pane=chat&open=campaign:launch-week');await page.getByRole('region',{name:'Campaign package'}).waitFor();await shot('campaign-desktop');
  await page.getByRole('button',{name:'Expand to full width'}).click();
  await page.getByRole('region',{name:'Campaign package'}).evaluate(el=>el.scrollIntoView({block:'start'}));await shot('campaign-pieces-desktop');
  const pack=page.getByRole('region',{name:'Campaign package'});
  await pack.getByRole('article',{name:'LinkedIn draft #8'}).getByRole('button',{name:'Review piece'}).click();
  const desk=page.getByRole('region',{name:'Piece review'});await desk.getByText('Compare the polished draft with #7').click();
  await desk.locator('.fe-artifact-compare').scrollIntoViewIfNeeded();await shot('revision-desktop');
  await desk.locator('.fe-artifact-compare').screenshot({path:path.join(images,'revision-card.png'),animations:'disabled'});
  await desk.getByRole('button',{name:'Close review'}).click();
  await page.setViewportSize({width:390,height:844});await page.locator('.fe-window-body').evaluate(el=>{el.scrollTop=0;});await shot('campaign-phone');
  await page.getByRole('article',{name:'LinkedIn draft #8'}).scrollIntoViewIfNeeded();await shot('campaign-pieces-phone');
  await page.goto(origin+'/?pane=chat');await page.getByRole('button',{name:/Show cockpit/}).click();await page.getByRole('region',{name:'Prepared opportunity'}).waitFor();await shot('cockpit-phone');
  await context.close();await fs.writeFile(path.join(evidence,'host.log'),hostLog);
  receipt.passed=true;
}catch(error){receipt.error=error.stack;throw error;}
finally{
  if(browser)await browser.close();
  if(host&&host.exitCode===null){host.kill();await host.finished;}
  receipt.removed=await cleanArtifactPaths(evidence,['scratch']);
  await fs.writeFile(path.join(evidence,'receipt.json'),JSON.stringify(receipt,null,2)+'\n');
}
console.log('Marketing UI captures checked. The fictional household has vacated port 5185.');
