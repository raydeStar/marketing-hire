import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {openLog} from './navigation';

// Explicit acceptance only. Routine checks never borrow a model, a GPU, or search credits.
const endpoint=process.env.THADDEUS_WEB_ACCEPTANCE_PROVIDER;
test.skip(!endpoint,'Requires an explicitly authorized live provider endpoint.');
async function api(page:Page,url:string,body?:unknown,method='POST'){
 return page.evaluate(async({url,body,method})=>{const session=await(await fetch('/api/session')).json();const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});if(!response.ok)throw new Error(await response.text());return response.json();},{url,body,method});
}
test('Luna reads the reported article through Chat and exposes its source and usage',async({page})=>{
 test.setTimeout(240000);
 const provider=new URL(endpoint!);expect(['localhost','127.0.0.1']).toContain(provider.hostname);
 const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});
 await page.setViewportSize({width:1400,height:950});await page.goto('/');
 await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Unlock study',exact:true}).click();
 await expect(page.getByLabel('Message or goal')).toBeVisible();
 const connection=await api(page,'/settings/connection');
 await api(page,'/settings/connection',{version:connection.version,provider:{kind:'compatible',model:'gpt-5.6-luna',reasoning:'high',endpoint:endpoint!},credentialMode:'none'},'PUT');
 const url='https://huggingface.co/blog/ibm-research/altk-evolve-consistency';
 const composer=page.getByLabel('Message or goal');
 await composer.fill('Read this article from its URL. In at most two sentences, explain one specific consistency problem it discusses and link to the source: '+url);await composer.press('Enter');
 await expect.poll(async()=>{const state=await api(page,'/state');return state.runs[0]?.state;},{timeout:210000,intervals:[1000,2000]}).toMatch(/succeeded|failed|needsAttention|cancelled/);
 const state=await api(page,'/state'),run=state.runs[0],read=run.capabilities?.find((c:any)=>c.name==='thaddeus_fetch_public_page');
 fs.writeFileSync(path.join(images,'live-acceptance.json'),JSON.stringify({state:run.state,summary:run.summary,provider:run.goal.provider,modelCalls:run.modelCalls,toolCalls:run.toolCalls,inputTokens:run.inputTokens,outputTokens:run.outputTokens,chargedTokens:run.chargedTokens,answer:run.draftText,source:read?.result.source?{...read.result.source,text:undefined}:null,error:read?.result.error,mainStudyTouched:false,braveSearchCalls:0},null,2));
 expect(run.state).toBe('succeeded');expect(run.modelCalls).toBe(2);expect(run.toolCalls).toBe(1);expect(run.chargedTokens).toBeGreaterThan(0);
 expect(read?.isError).toBe(false);expect(read?.result.source.url).toBe(url);expect(read?.result.source.text.length).toBeGreaterThan(1000);expect(run.draftText).toContain('huggingface.co');
 await page.getByText('Read 1 source',{exact:true}).click();await expect(page.locator('.website-readings a')).toHaveAttribute('href',url);
 await page.screenshot({path:path.join(images,'website-chat-desktop.png'),animations:'disabled'});
 await openLog(page);await page.locator('.ledger-row').filter({hasText:'Read this article from its URL.'}).click();
 await expect(page.getByRole('dialog')).toContainText('website reading recorded');
 await expect(page.getByRole('dialog').getByText('Read 1 source',{exact:true})).toBeVisible();
 await page.setViewportSize({width:390,height:844});await page.screenshot({path:path.join(images,'website-chat-mobile.png'),animations:'disabled'});
});
