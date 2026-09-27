import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {openLog} from './navigation';

test('website receipts, failures, log details and feed reading drafts are visible without model calls',async({page})=>{
 const shots=process.env.THADDEUS_SCREENSHOTS!;fs.mkdirSync(shots,{recursive:true});
 const now=new Date().toISOString(),id='a'.repeat(32),url='https://example.org/article';
 const receipt={operationId:'page-1',name:'thaddeus_fetch_public_page',authority:'broker-observed',recorded:now,isError:false,result:{source:{url,title:'Fictional raven article',retrieved:now,truncated:true}}};
 const run:any={id,version:1,state:'succeeded',summary:'Replied · website reading recorded in the log',created:now,updated:now,draftText:'The fictional article describes seventeen ravens.',goal:{kind:'conversation',objective:'Read this article',readScope:[],provider:{kind:'compatible',model:'fixture-web'},limits:{modelCalls:2,toolCalls:2,maxOutputTokens:4096},criteria:[]},capabilities:[receipt],evidence:[],attempts:[],modelCalls:2,toolCalls:1,repairs:0,inputTokens:123,outputTokens:45};
 await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Open workspace',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
 const before=await page.evaluate(async()=>(await fetch('/api/export')).json());
 await page.route('**/api/state',async route=>{const actual=await(await route.fetch()).json();await route.fulfill({json:{...actual,runs:[run],chats:[{id:id+'-user',role:'user',content:'Read this article'},{id:id+'-assistant',role:'assistant',content:run.draftText}],feeds:{revision:'1',subscriptions:[{id:'source',url:'https://example.org/feed',title:'Fictional source',paused:false,version:'v1',created:now,nextRefresh:now,lastChecked:now,failures:0}],entries:[{id:'entry',key:'entry',subscriptionId:'source',title:'Fictional raven article',summary:'',url,published:now,received:now,read:false,version:'v1'}]}}});});
 await page.route('**/api/replay**',async route=>route.fulfill({json:{events:[],nextCursor:null}}));
 await page.reload();await page.getByText('Read 1 source',{exact:true}).click();await expect(page.locator('.website-readings a')).toHaveAttribute('href',url);await expect(page.locator('.website-readings')).toContainText('excerpt only');
 await openLog(page);await page.locator('.ledger-row').click();await expect(page.getByRole('dialog')).toContainText('website reading recorded');await page.getByRole('dialog').getByText('Read 1 source',{exact:true}).click();await expect(page.getByRole('dialog').locator('.website-readings a')).toHaveAttribute('href',url);
 await page.screenshot({path:path.join(shots,'website-log-desktop.png'),animations:'disabled'});await page.getByRole('button',{name:'Close dialog',exact:true}).click();
 await page.setViewportSize({width:390,height:844});await expect(page.getByText('Read 1 source',{exact:true})).toBeInViewport();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:path.join(shots,'website-source-mobile.png'),animations:'disabled'});
 run.capabilities=[{...receipt,isError:true,result:{error:'The website refused this read (HTTP 403).'}}];await page.reload();await page.getByText('Website could not be read',{exact:true}).click();await expect(page.locator('.website-readings')).toContainText('HTTP 403');expect(await page.locator('.website-readings a').count()).toBe(0);
 await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();await page.getByRole('button',{name:'Feed',exact:true}).click();
 await page.route('**/api/feeds/entries/entry/feedback',async route=>route.fulfill({json:{}}));await page.getByRole('button',{name:'Discuss',exact:false}).click();
 await expect(page.getByLabel('Message or goal')).toHaveValue(/Read this article from its URL/);await expect(page.getByLabel('Message or goal')).toHaveValue(new RegExp(url.replaceAll('.','\\.')));
 expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
});
