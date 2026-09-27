import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {openLog,resizeLog} from './navigation';

async function unlock(page:Page){
 await page.goto('/');
 await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
 await page.getByRole('button',{name:'Open workspace',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Conversation'})).toBeVisible();
}

test('delegation controls, recovery guidance and source-linked receipts remain usable on desktop and mobile',async({page})=>{
 const now=new Date(),due=new Date(now.getTime()-60_000).toISOString(),next=new Date(now.getTime()+86_400_000).toISOString();
  const briefId='a'.repeat(32),briefOccurrenceId='b'.repeat(64),reminderId='c'.repeat(32),reminderOccurrenceId='d'.repeat(64),unknownId='e'.repeat(32),unknownOccurrenceId='f'.repeat(64),watchId='1'.repeat(32),watchOccurrenceId='2'.repeat(64);
  const notificationError='Windows refused the app notification. Open Windows Settings → System → Notifications, allow notifications for Thaddeus, and check Do not disturb. Use Review in Chat to schedule a new reminder only if it is still useful; this occurrence will not fire again automatically.';
 let state='scheduled',version=3,nextRun:string|null=next,readAt:string|null=null,watchInstruction='Surface direct requests and meaningful deadlines.';
 const requests:string[]=[];
 await unlock(page);
 await page.route('**/api/state',async route=>{
  const actual=await(await route.fetch()).json();
  await route.fulfill({json:{...actual,hostMustRemainAwake:true,delegations:[
   {id:briefId,version,kind:'brief',title:'Weekday morning brief',schedule:{kind:'weekdays',timeZone:'America/Denver',localTime:'08:00'},action:{kind:'brief',target:'owner:in-app',payload:{},requiresModel:true},state,requestedAt:now.toISOString(),created:now.toISOString(),updated:now.toISOString(),nextRunUtc:nextRun,lastSummary:state==='paused'?'Paused before the next occurrence.':'Morning brief saved as an unread in-app result.',cancellationRequested:false},
   {id:reminderId,version:2,kind:'reminder',title:'Call dentist',schedule:{kind:'once',timeZone:'America/Denver',atUtc:due},action:{kind:'reminder',target:'owner:windows',payload:{},requiresModel:false},state:'succeeded',requestedAt:now.toISOString(),created:now.toISOString(),updated:now.toISOString(),nextRunUtc:null,lastSummary:'The reminder was saved as an unread result in Thaddeus, but its Windows notification was not displayed.',cancellationRequested:false},
   {id:unknownId,version:4,kind:'email',title:'Follow-up email',schedule:{kind:'once',timeZone:'America/Denver',atUtc:due},action:{kind:'email',target:'owner-test@example.invalid',payload:{},requiresModel:false},state:'unknown',requestedAt:now.toISOString(),created:now.toISOString(),updated:now.toISOString(),nextRunUtc:null,lastSummary:'The provider outcome is unknown. No automatic retry was started.',cancellationRequested:false},
   {id:watchId,version:1,kind:'inbox-watch',title:'Important inbox watch',schedule:{kind:'interval',timeZone:'America/Denver',intervalMinutes:5},action:{kind:'inbox-watch',target:'owner:in-app',payload:{importanceInstruction:watchInstruction},requiresModel:true},state:'scheduled',requestedAt:now.toISOString(),created:now.toISOString(),updated:now.toISOString(),nextRunUtc:next,lastSummary:'Inbox watch surfaced 1 message needing attention.',cancellationRequested:false}
  ],delegationOccurrences:[
   {id:briefOccurrenceId,version:2,jobId:briefId,sequence:1,dueUtc:due,state:'succeeded',dispatchState:'accepted',completedAt:now.toISOString(),summary:'Morning brief saved as an unread in-app result. Source data was not modified.',providerId:'brief:fixture-operation',providerEvidence:{brief:'# Morning brief\n\n- [Dentist](https://calendar.example.test/event-1)\n- Review the contract email.',sourceMutation:false,sources:[{kind:'calendar',connector:'Owner calendar',tool:'list_events',status:'available',hash:'c'.repeat(64),characters:120,truncated:false},{kind:'email',connector:'Owner mail',tool:'search_email',status:'available',hash:'d'.repeat(64),characters:200,truncated:false}]},actionSucceeded:true,notificationStatus:'in-app-result',readAt},
   {id:reminderOccurrenceId,version:2,jobId:reminderId,sequence:1,dueUtc:due,state:'succeeded',dispatchState:'accepted',completedAt:now.toISOString(),summary:'The reminder was saved as an unread result in Thaddeus, but its Windows notification was not displayed.',providerEvidence:{mechanism:'Shell_NotifyIcon',accepted:false,error:5},actionSucceeded:true,notificationStatus:'failed',notificationError,readAt:null},
   {id:unknownOccurrenceId,version:2,jobId:unknownId,sequence:1,dueUtc:due,state:'unknown',dispatchState:'unknown',completedAt:now.toISOString(),summary:'The provider outcome is unknown. No automatic retry was started.',providerEvidence:{classification:'DelegationOutcomeUnknownException'},actionSucceeded:false,notificationStatus:'not-attempted',readAt:null},
   {id:watchOccurrenceId,version:1,jobId:watchId,sequence:1,dueUtc:due,state:'succeeded',dispatchState:'accepted',completedAt:now.toISOString(),summary:'Inbox watch surfaced 1 message needing attention.',providerId:'inbox-watch:fixture',providerEvidence:{attention:'**Contract response due today** — Avery\n\nA direct response is due today.\n\n[Open original email](https://mail.google.com/mail/u/0/#inbox/important-1)',sourceMutation:false,readOnly:true},actionSucceeded:true,notificationStatus:'in-app-result',readAt:null}
  ]}});
 });
 await page.route('**/api/delegations/*/pause',async route=>{requests.push('pause');state='paused';version++;nextRun=null;await route.fulfill({json:{}});});
 await page.route('**/api/delegations/*/resume',async route=>{requests.push('resume');state='scheduled';version++;nextRun=next;await route.fulfill({json:{}});});
 await page.route('**/api/delegations/*/inbox-instruction',async route=>{requests.push('edit-watch');watchInstruction=(await route.request().postDataJSON()).instruction;await route.fulfill({json:{}});});
 await page.route('**/api/delegation-occurrences/*/read',async route=>{requests.push('read');readAt=new Date().toISOString();await route.fulfill({json:{}});});
 await page.reload();await openLog(page);await page.getByRole('button',{name:'Upcoming',exact:true}).click();
 const briefCard=page.locator('article.delegation-card').filter({hasText:'Weekday morning brief'});
 await expect(briefCard.getByText('Weekday morning brief',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Inspect latest result for Weekday morning brief'}).click();
 const receipt=page.getByRole('dialog',{name:'Saved result',exact:true});
 await expect(receipt.getByRole('heading',{name:'Morning brief',exact:true})).toBeVisible();
 await expect(receipt.getByRole('link',{name:'Dentist'})).toHaveAttribute('href','https://calendar.example.test/event-1');
 await expect(receipt.locator('p').filter({hasText:'Owner calendar'})).toBeVisible();
 await receipt.getByText('Technical receipt',{exact:true}).click();
 await expect(receipt).toContainText('sourceMutation');
 await receipt.getByRole('button',{name:'Close dialog'}).click();

 await briefCard.getByRole('button',{name:'Pause',exact:true}).click();
 await expect(briefCard.getByRole('button',{name:'Resume',exact:true})).toBeVisible();
 await briefCard.getByRole('button',{name:'Resume',exact:true}).click();
 await expect(briefCard.getByRole('button',{name:'Pause',exact:true})).toBeVisible();
  await briefCard.getByRole('button',{name:'Mark result read',exact:true}).click();
  expect(requests).toEqual(['pause','resume','read']);

  const reminderCard=page.locator('article.delegation-card').filter({hasText:'Call dentist'});
  await expect(reminderCard).toContainText('Saved · Notification failed');
  await expect(reminderCard).toContainText('Open Windows Settings');
  await reminderCard.getByRole('button',{name:'Inspect latest result for Call dentist'}).click();
  const reminderReceipt=page.getByRole('dialog',{name:'Reminder',exact:true});
  await expect(reminderReceipt).toContainText('Notification issue');
  await expect(reminderReceipt).toContainText('will not fire again automatically');
  await reminderReceipt.getByRole('button',{name:'Close dialog'}).click();
  await reminderCard.getByRole('button',{name:'Review in Chat',exact:true}).click();
  await expect(page.getByLabel('Message or goal')).toContainText('notification failed');
  await page.getByLabel('Message or goal').fill('');

  await openLog(page);await page.getByRole('button',{name:'Upcoming',exact:true}).click();
  const unknownCard=page.locator('article.delegation-card').filter({hasText:'Follow-up email'});
 await expect(unknownCard.getByRole('button',{name:'Cancel',exact:true})).toHaveCount(0);
 await unknownCard.getByRole('button',{name:'Review in Chat',exact:true}).click();
 await expect(page.getByLabel('Message or goal')).toContainText('without retrying it automatically');

 await openLog(page);await page.getByRole('button',{name:'Upcoming',exact:true}).click();
 const watchCard=page.locator('article.delegation-card').filter({hasText:'Important inbox watch'});
 await expect(watchCard).toContainText('Every 5 minutes');
 await expect(watchCard).toContainText('Last successful check');
 await watchCard.getByRole('button',{name:'Edit importance',exact:true}).click();
 await watchCard.getByLabel('What deserves attention').fill('Only direct requests that need my reply.');
 await watchCard.getByRole('button',{name:'Save instruction',exact:true}).click();
 await expect.poll(()=>watchInstruction).toBe('Only direct requests that need my reply.');
 await watchCard.getByRole('button',{name:'Inspect latest result for Important inbox watch'}).click();
 const watchReceipt=page.getByRole('dialog',{name:'Saved result',exact:true});
 await expect(watchReceipt.getByRole('link',{name:'Open original email'})).toHaveAttribute('href','https://mail.google.com/mail/u/0/#inbox/important-1');
 await watchReceipt.getByRole('button',{name:'Close dialog'}).click();

 await resizeLog(page,390,844);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await page.getByRole('button',{name:'Upcoming',exact:true}).click();
 await page.getByRole('button',{name:'Inspect latest result for Weekday morning brief'}).click();
 await expect(page.getByRole('dialog').getByRole('button',{name:'Close dialog'})).toBeInViewport();
});
