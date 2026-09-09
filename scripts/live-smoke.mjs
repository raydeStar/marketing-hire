import {chromium} from '../web/node_modules/playwright-core/index.mjs';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const browser=await chromium.launch();const page=await browser.newPage({viewport:{width:1440,height:1000}});
const key=(await readFile('.data/host-key.txt','utf8')).trim();await page.goto('http://localhost:5179');await page.getByLabel('Host access key',{exact:true}).fill(key);await page.getByRole('button',{name:'Unlock study'}).click();await page.getByRole('heading',{name:'Make room for what matters.'}).waitFor();
async function call(url,body,method='POST'){return page.evaluate(async({url,body,method})=>{const s=await(await fetch('/api/session')).json();const r=await fetch('/api'+url,{method,headers:{'Content-Type':'application/json','X-CSRF':s.csrf},body:JSON.stringify(body)});if(!r.ok)throw new Error('HTTP '+r.status);return r.json().catch(()=>null);},{url,body,method});}
await call('/demo/seed',{});
await call('/settings/provider',{kind:'compatible',model:'gpt-5.6-luna',reasoning:'high',endpoint:'http://127.0.0.1:5181/v1'},'PUT');
const run=process.argv.includes('--resume')?JSON.parse(await readFile('artifacts/luna-live-run.json','utf8')):await call('/runs',{objective:'Make a useful weekly plan from these fictional notes. Preserve the unresolved conflict; cite every source.',readScope:['notes/deadlines.md','notes/constraints.md','notes/conflict.md'],budget:{modelCalls:3,toolCalls:8,maxOutputTokens:4096,seconds:180,repairs:1}});
await page.getByRole('button',{name:'Tasks',exact:true}).click();await page.locator(`[data-run-id="${run.id}"]`).click();
await mkdir('artifacts/screenshots',{recursive:true});
if(!process.argv.includes('--resume'))await page.screenshot({path:'artifacts/screenshots/active-luna-1440.png',fullPage:true});
await page.setViewportSize({width:390,height:900});if(!process.argv.includes('--resume'))await page.screenshot({path:'artifacts/screenshots/active-luna-390.png',fullPage:true});
let result;const until=Date.now()+200000;
do{result=await page.evaluate(async id=>await(await fetch('/api/runs/'+id)).json(),run.id);if(!['queued','running'].includes(result.state))break;await new Promise(resolve=>setTimeout(resolve,1000));}while(Date.now()<until);
if(result.state==='awaitingApproval'){
  await page.screenshot({path:'artifacts/screenshots/approval-luna-390.png',fullPage:true});
  result=await call('/runs/'+run.id+'/approve',{approvalId:result.approval.id,digest:result.approval.digest,allow:true});
}
await writeFile('artifacts/luna-live-run.json',JSON.stringify(result,null,2));
await call('/settings/provider',{kind:'scripted',model:'fictional-weekly-v1',reasoning:'high'},'PUT');
console.log(JSON.stringify({state:result.state,summary:result.summary,model:result.goal.provider,modelCalls:result.modelCalls,inputTokens:result.inputTokens,outputTokens:result.outputTokens}));
await browser.close();if(result.state!=='succeeded')process.exitCode=1;
