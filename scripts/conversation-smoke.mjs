import {chromium} from '../web/node_modules/playwright-core/index.mjs';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const browser=await chromium.launch();
const page=await browser.newPage({viewport:{width:390,height:900}});
try {
  await page.goto('http://localhost:5179');
  await page.getByLabel('Host access key',{exact:true}).fill((await readFile('.data/host-key.txt','utf8')).trim());
  await page.getByRole('button',{name:'Unlock study'}).click();
  await page.getByRole('heading',{name:'Make room for what matters.'}).waitFor();
  const before=await page.evaluate(async()=>await(await fetch('/api/state')).json());
  if(before.provider.model!=='gpt-5.6-luna'||before.provider.reasoning!=='high')throw Error('Expected saved Luna High profile.');
  const replies=[];
  for(const content of ['For this fictional conversation, my paper raven is named Juniper. Acknowledge in one short sentence.', 'What is my paper raven named? Reply with its name only.']) {
    await page.getByLabel('Message or goal').fill(content);
    await page.getByRole('button',{name:'Send message'}).click();
    await page.waitForFunction(n=>document.querySelectorAll('.chat').length>n,before.chats.length,{timeout:10000}).catch(()=>{});
    const run=await page.evaluate(async()=>{const s=await(await fetch('/api/state')).json();return s.runs.find(r=>r.goal.kind==='conversation');});
    if(!run)throw Error('Conversation was not admitted.');
    await mkdir('artifacts/screenshots',{recursive:true});
    await page.screenshot({path:'artifacts/screenshots/conversation-luna-active-390.png',fullPage:true});
    let result; const deadline=Date.now()+200000;
    do { result=await page.evaluate(async id=>await(await fetch('/api/runs/'+id)).json(),run.id); if(!['queued','running'].includes(result.state))break; await new Promise(resolve=>setTimeout(resolve,1000)); } while(Date.now()<deadline);
    replies.push(result);
    if(result.state!=='succeeded'||result.toolCalls!==0||result.goal.provider.model!=='gpt-5.6-luna')throw Error('Conversation failed its runtime gate: '+result.summary);
  }
  if(!/^Juniper[.!]?$/i.test(replies[1].draftText.trim()))throw Error('Context recall failed.');
  const after=await page.evaluate(async()=>await(await fetch('/api/state')).json());
  if(JSON.stringify(before.pages)!==JSON.stringify(after.pages))throw Error('Conversation changed knowledge.');
  await page.getByText('Juniper',{exact:true}).waitFor();
  await page.getByRole('button',{name:'Cancel reply',exact:true}).waitFor({state:'hidden'});
  await page.screenshot({path:'artifacts/screenshots/conversation-luna-complete-390.png',fullPage:true});
  await writeFile('artifacts/conversation-luna.json',JSON.stringify({kind:'two-turn integration smoke, not an efficacy benchmark',replies,knowledgeUnchanged:true},null,2));
  console.log(JSON.stringify({passed:true,model:after.provider,turns:replies.map(r=>({id:r.id,input:r.inputTokens,output:r.outputTokens,text:r.draftText}))}));
} finally {await browser.close();}
