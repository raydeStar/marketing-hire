import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('Feed starts with chosen sources and distinguishes waiting, failure, pause and read states',async({page})=>{
 test.setTimeout(60000);const shots=process.env.THADDEUS_SCREENSHOTS!;fs.mkdirSync(shots,{recursive:true});
 await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
 const before=await page.evaluate(async()=>(await fetch('/api/export')).json());
 let subscriptions:any[]=[],entries:any[]=[],revision=0;const followed:string[]=[];const now=new Date().toISOString();
 await page.route('**/api/state',async route=>{const actual=await(await route.fetch()).json();await route.fulfill({json:{...actual,feeds:{subscriptions,entries,revision:String(revision)}}});});
 await page.route(url=>url.pathname==='/api/feeds',async route=>{const {url}=route.request().postDataJSON();followed.push(url);subscriptions=[{id:'starter',url,title:'Fictional source',paused:false,version:'v1',created:now,nextRefresh:now,failures:0}];revision++;await route.fulfill({json:subscriptions[0]});});
 async function openFeed(){await page.getByRole('button',{name:'Expand sidebar',exact:true}).click();await page.getByRole('button',{name:'Feed',exact:true}).click();if(await page.getByRole('button',{name:'Collapse sidebar',exact:true}).count())await page.getByRole('button',{name:'Collapse sidebar',exact:true}).click();}
 await openFeed();await expect(page.getByRole('heading',{name:'Give your feed a starting point'})).toBeVisible();expect(followed).toHaveLength(0);
 await page.setViewportSize({width:390,height:900});await page.screenshot({animations:'disabled',path:path.join(shots,'feed-start-mobile.png'),fullPage:true});await page.getByRole('button',{name:'Follow Hugging Face',exact:true}).click();
 expect(followed).toEqual(['https://huggingface.co/blog/feed.xml']);await expect(page.getByRole('heading',{name:'Waiting for the first updates'})).toBeVisible();await expect(page.getByText('You’re caught up here.')).toHaveCount(0);
 subscriptions[0]={...subscriptions[0],lastChecked:now,error:'Fictional source unavailable',failures:1};revision++;await page.reload();await openFeed();await expect(page.getByRole('heading',{name:'A source needs attention'})).toBeVisible();
 subscriptions[0]={...subscriptions[0],error:null,paused:true};revision++;await page.reload();await openFeed();await expect(page.getByRole('heading',{name:'Your sources are paused'})).toBeVisible();
 subscriptions[0]={...subscriptions[0],paused:false};entries=Array.from({length:25},(_,i)=>({id:'entry-'+i,key:'key-'+i,subscriptionId:'starter',title:'Fictional article '+i,summary:'A bounded excerpt.',url:'https://example.org/'+i,published:now,received:now,read:false,version:'v1'}));revision++;await page.reload();await openFeed();
 await expect(page.locator('.feed-entry')).toHaveCount(20);await page.getByRole('button',{name:/Show more updates/}).click();await expect(page.locator('.feed-entry')).toHaveCount(25);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 entries=entries.map(entry=>({...entry,read:true}));revision++;await page.reload();await openFeed();await expect(page.getByRole('heading',{name:'You’re caught up here.'})).toBeVisible();await page.getByRole('button',{name:'Show read updates',exact:true}).click();await expect(page.locator('.feed-entry')).toHaveCount(20);
 await page.getByRole('button',{name:'Find sources',exact:true}).click();await expect(page.getByRole('button',{name:'Following Hugging Face',exact:true})).toBeDisabled();expect(followed).toHaveLength(1);
 expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
});
