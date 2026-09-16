import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {navigateStudy,openLog} from './navigation';
const images=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(images,{recursive:true});
async function unlock(page:Page){
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study'}).click();await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
}
async function state(page:Page){return page.evaluate(async()=>(await fetch('/api/state')).json());}
test('owner collections persist, synchronize and reject stale edits without model dispatch',async({page})=>{
  await unlock(page);const before=await state(page);
  await navigateStudy(page,'To-do');await page.getByRole('button',{name:'Add a to-do',exact:true}).click();
  await page.getByLabel('Title',{exact:true}).fill('Review the cobalt workshop');await page.getByLabel('Notes',{exact:true}).fill('Choose the final handout.');await page.getByLabel('Due date',{exact:true}).fill('2026-09-20');
  await page.getByRole('button',{name:'Save item',exact:true}).click();
  const item=page.locator('article.todo-row').filter({hasText:'Review the cobalt workshop'});await expect(item).toBeVisible();
  const id=(await state(page)).library.find((entry:any)=>entry.title==='Review the cobalt workshop').id;
  const other=await page.context().newPage();await other.goto('/');await navigateStudy(other,'To-do');
  await other.locator('article.todo-row').filter({hasText:'Review the cobalt workshop'}).getByRole('button',{name:'Edit Review the cobalt workshop',exact:true}).click();
  const editor=other.getByRole('form',{name:'Edit to-do',exact:true});await expect(editor).toBeVisible();const notes=editor.locator('textarea');await expect(notes).toHaveCount(1);await notes.fill('Draft in the second window.');
  await item.getByRole('button',{name:'Mark complete: Review the cobalt workshop',exact:true}).click();
  await expect(other.getByText('This item changed in another window.',{exact:false})).toBeVisible();await expect(notes).toHaveValue('Draft in the second window.');
  await expect(other.getByRole('button',{name:'Save item',exact:true})).toBeDisabled();await other.close();
  await page.reload();await navigateStudy(page,'To-do');await page.locator('.collection-filters').getByRole('button',{name:'Completed',exact:false}).click();
  await expect(item).toBeVisible();await item.getByRole('button',{name:'Mark unfinished:',exact:false}).click();
  await page.locator('.collection-filters').getByRole('button',{name:'Active',exact:false}).click();await expect(item).toBeVisible();
  for(const width of [1440,390]){await page.setViewportSize({width,height:1000});await expect(page.getByRole('main',{name:'Workspace'})).toBeVisible();await page.screenshot({path:path.join(images,`todo-${width}.png`),fullPage:true});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);}
  const after=await state(page);expect(after.runs).toEqual(before.runs);expect(after.chats).toEqual(before.chats);
  const forbidden=await page.evaluate(async id=>(await fetch('/api/library/'+id,{method:'PUT',headers:{'Content-Type':'application/json'},body:'{}'})).status,id);expect(forbidden).toBe(403);
  const exported=await page.evaluate(async()=>(await fetch('/api/export')).json());expect(exported.schemaVersion).toBe(10);expect(exported.library.find((entry:any)=>entry.id===id).status).toBe('open');expect(exported.libraryChanges.filter((entry:any)=>entry.itemId===id).map((entry:any)=>entry.kind)).toEqual(['created','done','open']);
});
test('ideas, saved reading and search work on mobile without sending a message',async({page})=>{
  await page.setViewportSize({width:390,height:900});await unlock(page);const before=await state(page);
  for(const [tab,add,title,notes] of [['Ideas','Save an idea','A cobalt field notebook','A thought worth keeping.'],['Feed','Save something to read','Cobalt reading note','Read over tea.']]){
    await navigateStudy(page,tab as 'Ideas'|'Feed');if(tab==='Feed')await page.getByRole('navigation',{name:'Feed sections'}).getByRole('button',{name:/Saved links/}).click();else await page.getByRole('navigation',{name:'Idea sections'}).getByRole('button',{name:/Saved ideas/}).click();await page.getByRole('button',{name:add,exact:true}).click();await page.getByLabel('Title',{exact:true}).fill(title);await page.getByLabel('Notes',{exact:true}).fill(notes);if(tab==='Feed')await page.getByLabel('Link (optional)').fill('https://example.org/reading');await page.getByRole('button',{name:'Save item',exact:true}).click();await expect(page.getByRole('heading',{name:title,exact:true})).toBeVisible();
  }
  await navigateStudy(page,'Search');await page.getByLabel('Search your study').fill('cobalt');await expect(page.getByRole('status')).toContainText('result');await expect(page.getByRole('button').filter({hasText:'A cobalt field notebook'})).toBeVisible();
  await page.screenshot({path:path.join(images,'search-390.png'),fullPage:true});
  await page.getByRole('button').filter({hasText:'A cobalt field notebook'}).click();const idea=page.locator('[data-item-id]').filter({hasText:'A cobalt field notebook'});await idea.getByRole('button',{name:'Discuss',exact:false}).click();await expect(page.getByLabel('Message or goal')).toContainText('A cobalt field notebook');
  expect((await state(page)).runs).toEqual(before.runs);await page.screenshot({path:path.join(images,'conversation-390.png'),fullPage:true});
  await openLog(page);await expect(page.getByRole('main',{name:'Workspace'})).not.toBeVisible();await expect(page.locator('.header-companion .raven')).toBeVisible();await page.screenshot({path:path.join(images,'log-390.png'),fullPage:true});
  await page.keyboard.press('Escape');await expect(page.getByRole('button',{name:'Thaddeus: open activity log',exact:true})).toBeFocused();await expect(page.getByRole('main',{name:'Workspace'})).toBeVisible();
  await page.setViewportSize({width:1440,height:1000});await openLog(page);await page.screenshot({path:path.join(images,'workspace-1440.png'),fullPage:true});
  await page.emulateMedia({reducedMotion:'reduce'});expect(await page.locator('.companion .raven-body').evaluate(el=>getComputedStyle(el).animationName)).toBe('none');
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});
