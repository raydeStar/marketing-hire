import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

async function navigate(page:Page,name:string){
  const expand=page.getByRole('button',{name:'Expand sidebar',exact:true});if(await expand.isVisible())await expand.click();
  await page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name,exact:true}).click();
}

const file=(name:string)=>({name,mimeType:'text/plain',buffer:Buffer.from('A fictional packing note.')});
async function state(page:Page){return page.evaluate(async()=>await(await fetch('/api/state')).json());}

test('uploads show progress, keep partial successes and protect the unfinished chat',async({page,context})=>{
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  const composer=page.getByLabel('Message or goal');await expect(composer).toBeVisible();await composer.fill('Keep this draft while the files arrive');
  let release:()=>void=()=>{};const held=new Promise<void>(resolve=>{release=resolve;});let posts=0;
  await page.route('**/api/uploads',async route=>{if(route.request().method()==='POST'){posts++;if(posts===1)await held;}await route.continue();});
  const input=page.locator('.conversation-compose input[type=file]');
  try{
    await input.setInputFiles([file('first-note.txt'),file('unsupported.exe'),file('last-note.txt')]);
    await expect(page.getByRole('status').filter({hasText:'Uploading 1 of 3: first-note.txt'})).toBeVisible();
    await expect(input).toBeDisabled();await expect(page.getByRole('button',{name:'Send message',exact:true})).toBeDisabled();
    await composer.fill('I can keep writing during uploads');await composer.press('Enter');expect((await state(page)).runs).toHaveLength(0);
    await page.getByRole('button',{name:'Message options',exact:true}).click();await expect(page.getByRole('button',{name:'Attach files',exact:true})).toBeDisabled();await page.keyboard.press('Escape');
  }finally{release();}
  await expect(page.locator('.conversation-compose .upload-feedback')).toContainText('2 files uploaded.');
  await expect(page.locator('.conversation-compose .upload-feedback')).toContainText('unsupported.exe');
  await expect(page.getByRole('button',{name:'Remove attachment first-note.txt'})).toBeVisible();await expect(page.getByRole('button',{name:'Remove attachment last-note.txt'})).toBeVisible();await expect(composer).toHaveValue('I can keep writing during uploads');expect(posts).toBe(3);
  await input.setInputFiles([file('a.txt'),file('b.txt'),file('c.txt')]);await expect(page.locator('.upload-feedback')).toContainText('You have 2 spaces left.');expect(posts).toBe(3);
  await page.getByRole('button',{name:'Dismiss upload status'}).click();await expect(page.locator('.upload-feedback')).toHaveCount(0);
  await input.setInputFiles(file('third-note.txt'));await expect(page.getByRole('button',{name:'Remove attachment third-note.txt'})).toBeVisible();expect(posts).toBe(4);
  await context.setOffline(true);await expect(input).toBeDisabled();await context.setOffline(false);await expect(input).toBeEnabled();
  await navigate(page,'Artifacts');await page.getByRole('button',{name:'Documents',exact:true}).click();
  const shelf=page.getByRole('region',{name:'Uploaded files'});await shelf.locator('input[type=file]').setInputFiles([file('shelf-first.txt'),file('shelf-unsupported.exe'),file('shelf-last.txt')]);
  await expect(shelf.locator('.upload-feedback')).toContainText('2 files uploaded.');await expect(shelf.locator('.upload-feedback')).toContainText('shelf-unsupported.exe');
  await expect(shelf.getByRole('button',{name:'Attach shelf-first.txt to chat'})).toBeVisible();await expect(shelf.getByRole('button',{name:'Attach shelf-last.txt to chat'})).toBeVisible();
  const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});await page.screenshot({path:path.join(images,'uploads-desktop.png'),animations:'disabled'});
  await page.setViewportSize({width:390,height:844});const closeRail=page.getByRole('button',{name:'Close sidebar',exact:true});if(await closeRail.isVisible())await closeRail.click();await expect(shelf.locator('.upload-feedback')).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:path.join(images,'uploads-mobile.png'),animations:'disabled'});
  await navigate(page,'Chat');await expect(composer).toHaveValue('I can keep writing during uploads');await expect(page.getByRole('button',{name:'Remove attachment third-note.txt'})).toBeVisible();
  const final=await state(page);expect(final.uploads).toHaveLength(5);expect(final.runs).toHaveLength(0);expect(final.search.budget.used).toBe(0);
  fs.writeFileSync(path.join(images,'uploads-check.json'),JSON.stringify({passed:true,uploads:final.uploads.length,uploadRequests:posts,modelCalls:0,searchRequests:0,checks:['visible pending status','no overlapping chat upload or premature send','typing during upload','continue after rejected file','keep successful attachments','four attachment limit before dispatch','offline upload disabled','shelf partial success','mobile layout','navigation preserves draft']},null,2));
});
