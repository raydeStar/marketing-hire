import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {openLog,resizeLog} from './navigation';

async function unlock(page:Page){
 await page.goto('/');
 await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
 await page.getByRole('button',{name:'Unlock study',exact:true}).click();
 await expect(page.getByLabel('Message or goal')).toBeVisible();
}
const images=()=>{const dir=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(dir,{recursive:true});return dir;};

test('chat heading scrolls with history and latest messages stays reachable from the margins',async({page})=>{
 await page.setViewportSize({width:1440,height:900});await unlock(page);
 await page.route('**/api/state',async route=>{
  const state=await(await route.fetch()).json();
  const chats=Array.from({length:16},(_,i)=>({id:'presentation-'+i,role:i%2?'assistant':'user',content:i===15?'The reminder is scheduled: “Confirm the final Thaddeus notification. This is fictional QA.”':('Fictional reminder conversation '+i+'. ').repeat(12)}));
  await route.fulfill({json:{...state,runs:[],chats}});
 });
 await page.reload();
 const close=page.getByRole('button',{name:'Close activity log',exact:true});if(await close.isVisible())await close.click();
 const scroller=page.locator('.conversation-scroll'),heading=page.getByRole('heading',{name:'Conversation',exact:true});
 await expect(page.locator('#chat-presentation-15')).toBeInViewport();
 await expect(heading).not.toBeInViewport();
 // Exercise a real wheel over the empty margin, where the owner's cursor usually rests.
 const bounds=await scroller.boundingBox();expect(bounds).not.toBeNull();
 await page.mouse.move(bounds!.x+8,bounds!.y+80);await page.mouse.wheel(0,-100000);
 await expect(heading).toBeInViewport();
 const latest=page.getByRole('button',{name:'Latest messages',exact:true});await expect(latest).toBeInViewport();
 await latest.click();await expect(page.locator('#chat-presentation-15')).toBeInViewport();await expect(heading).not.toBeInViewport();
 await page.screenshot({path:path.join(images(),'chat-desktop.png')});
 await page.setViewportSize({width:390,height:844});
 await expect(page.locator('#chat-presentation-15')).toBeInViewport();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.screenshot({path:path.join(images(),'chat-mobile.png')});
});

test('saved reminder leads with exact message, keeps receipt tucked away and marks read without re-dispatch',async({page})=>{
 const due='2026-09-17T20:42:00Z',jobId='a'.repeat(32),id='b'.repeat(64);let readAt:string|null=null,reads=0;
 const message='Confirm the final Thaddeus notification.\nThis is fictional QA.';
 await page.setViewportSize({width:1440,height:900});await unlock(page);
 await page.route('**/api/state',async route=>{
  const state=await(await route.fetch()).json();
  await route.fulfill({json:{...state,delegations:[{id:jobId,version:2,kind:'reminder',title:'Confirm final Thaddeus notification',schedule:{kind:'once',atUtc:due,timeZone:'America/Denver'},action:{kind:'reminder',target:'owner:windows',payload:{message},requiresModel:false},state:'succeeded',requestedAt:due,created:due,updated:due,nextRunUtc:null,cancellationRequested:false}],delegationOccurrences:[{id,version:readAt?3:2,jobId,sequence:1,dueUtc:due,state:'succeeded',dispatchState:'accepted',completedAt:due,summary:'Windows accepted the notification and the reminder remains saved in Thaddeus.',providerId:'fixture-only:123',providerEvidence:{accepted:true},actionSucceeded:true,notificationStatus:'accepted',readAt}]}});
 });
 await page.route('**/api/delegation-occurrences/*/read',async route=>{expect(route.request().postDataJSON()).toEqual({version:2});reads++;readAt=due;await route.fulfill({json:{}});});
 await page.reload();await openLog(page);await page.getByRole('button',{name:'Upcoming',exact:true}).click();
 const card=page.locator('.delegation-card').filter({hasText:'Confirm final Thaddeus notification'});
 await expect(card).not.toContainText('No future run');
 await card.getByRole('button',{name:'Inspect latest result for Confirm final Thaddeus notification'}).click();
 const dialog=page.getByRole('dialog',{name:'Reminder',exact:true});
 await expect(dialog.getByRole('heading',{name:'Confirm final Thaddeus notification'})).toBeInViewport();
 await expect(dialog.locator('.delegation-reminder-message')).toHaveText(message);
 await expect(dialog.locator('pre')).not.toBeVisible();
 await expect(dialog).not.toContainText('Delivered');
 await page.screenshot({path:path.join(images(),'reminder-desktop.png')});
 await dialog.getByRole('button',{name:'Close dialog'}).click();
 await resizeLog(page,390,844);
 await page.getByRole('button',{name:'Upcoming',exact:true}).click();
 await card.getByRole('button',{name:'Inspect latest result for Confirm final Thaddeus notification'}).click();
 await expect(dialog.getByRole('button',{name:'Close dialog'})).toBeInViewport();
 expect(await dialog.evaluate(el=>el.scrollWidth<=el.clientWidth)).toBe(true);
 await page.screenshot({path:path.join(images(),'reminder-mobile.png')});
 await dialog.getByRole('button',{name:'Mark result read',exact:true}).click();
 await expect(dialog.getByRole('button',{name:'Mark result read',exact:true})).toHaveCount(0);expect(reads).toBe(1);
 await dialog.getByText('Technical receipt',{exact:true}).click();await expect(dialog.locator('pre')).toContainText('fixture-only:123');
 await dialog.getByRole('button',{name:'Close dialog'}).click();await expect(card).not.toContainText('Unread');
});
