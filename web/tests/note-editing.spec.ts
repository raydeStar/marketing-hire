import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

async function api(page:Page,url:string,body?:unknown){
  return page.evaluate(async({url,body})=>{
    const session=await(await fetch('/api/session')).json();
    const response=await fetch('/api'+url,body===undefined?{}:{method:'PUT',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});
    if(!response.ok)throw new Error(await response.text());
    return response.json();
  },{url,body});
}
async function navigate(page:Page,name:string){
  const expand=page.getByRole('button',{name:'Expand sidebar',exact:true});
  if(await expand.isVisible())await expand.click();
  await page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name,exact:true}).click();
}

test('note editing preserves unfinished work and makes saves and conflicts clear',async({page})=>{
  await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  await expect(page.getByLabel('Message or goal')).toBeVisible();
  await api(page,'/knowledge',{path:'notes/lantern.md',content:'Original lantern note.',version:'absent'});
  await api(page,'/knowledge',{path:'notes/garden.md',content:'Original garden note.',version:'absent'});
  await navigate(page,'Artifacts');
  await page.getByRole('button',{name:'Notes & memory',exact:true}).click();
  const editor=page.getByLabel('Markdown editor');
  const note=(name:string)=>page.locator('.page-list').getByRole('button',{name,exact:true});
  await note('notes/lantern.md').click();
  await expect(editor).toHaveValue('Original lantern note.');
  await editor.fill('Keep this unfinished lantern draft.');
  await note('notes/garden.md').click();
  const discard=page.getByRole('dialog',{name:'Discard unsaved note changes?'});
  await expect(discard).toBeVisible();
  await discard.getByRole('button',{name:'Keep editing',exact:true}).click();
  await expect(editor).toHaveValue('Keep this unfinished lantern draft.');
  expect((await api(page,'/knowledge?path=notes/lantern.md')).content).toBe('Original lantern note.');

  await navigate(page,'Chat');await page.getByLabel('Message or goal').fill('An independent chat draft.');
  await navigate(page,'Artifacts');
  await expect(editor).toHaveValue('Keep this unfinished lantern draft.');
  await page.getByRole('button',{name:'New note',exact:true}).click();
  await expect(discard).toBeVisible();await discard.getByRole('button',{name:'Keep editing',exact:true}).click();
  await expect(page.locator('.note-save-status')).toHaveText('Unsaved changes');
  await page.getByRole('button',{name:'Save my edits',exact:true}).click();
  await expect(page.locator('.note-save-status')).toHaveText('Saved');
  expect((await api(page,'/knowledge?path=notes/lantern.md')).content).toBe('Keep this unfinished lantern draft.');

  // A stale editor must keep its draft when another writer has already saved.
  const stored=await api(page,'/knowledge?path=notes/lantern.md');
  await editor.fill('My draft after another writer.');
  await api(page,'/knowledge',{...stored,content:'A newer version from another writer.'});
  await page.getByRole('button',{name:'Save my edits',exact:true}).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(editor).toHaveValue('My draft after another writer.');
  expect((await api(page,'/knowledge?path=notes/lantern.md')).content).toBe('A newer version from another writer.');

  await note('notes/garden.md').click();
  await page.route('**/api/knowledge?*',route=>route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Fictional note loading failure.'})}));
  await discard.getByRole('button',{name:'Discard changes',exact:true}).click();
  await expect(discard.getByRole('button',{name:'Discard changes',exact:true})).toBeEnabled();
  await expect(editor).toHaveValue('My draft after another writer.');
  await expect(discard).toBeVisible();
  await page.unroute('**/api/knowledge?*');
  await discard.getByRole('button',{name:'Discard changes',exact:true}).click();
  await expect(editor).toHaveValue('Original garden note.');
  await page.getByRole('button',{name:'New note',exact:true}).click();
  await page.getByLabel('Page path',{exact:true}).fill('');
  await expect(page.getByRole('heading',{name:'Untitled note',exact:true})).toBeVisible();
  await page.getByLabel('Page path',{exact:true}).fill('notes/unfinished.md');
  await editor.fill('A brand-new note deserves the same protection.');
  await note('notes/lantern.md').click();await expect(discard).toBeVisible();
  await discard.getByRole('button',{name:'Keep editing',exact:true}).click();
  await expect(page.getByLabel('Page path',{exact:true})).toHaveValue('notes/unfinished.md');
  const reloadDialog=page.waitForEvent('dialog');
  await page.evaluate(()=>setTimeout(()=>location.reload(),0));
  const dialog=await reloadDialog;expect(dialog.type()).toBe('beforeunload');await dialog.dismiss();
  await expect(editor).toHaveValue('A brand-new note deserves the same protection.');
  await page.getByRole('button',{name:'Save my edits',exact:true}).click();await expect(page.locator('.note-save-status')).toHaveText('Saved');
  await page.reload();await expect(page.getByLabel('Message or goal')).toHaveValue('An independent chat draft.');
  await navigate(page,'Artifacts');await page.getByRole('button',{name:'Notes & memory',exact:true}).click();
  await note('notes/unfinished.md').click();await expect(editor).toHaveValue('A brand-new note deserves the same protection.');
  await page.setViewportSize({width:390,height:844});
  const closeSidebar=page.getByRole('button',{name:'Close sidebar',exact:true});if(await closeSidebar.isVisible())await closeSidebar.click();
  await editor.fill('Mobile unfinished note.');await note('notes/garden.md').click();await expect(discard).toBeVisible();
  await discard.getByRole('button',{name:'Keep editing',exact:true}).click();await expect(editor).toHaveValue('Mobile unfinished note.');
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);
  const screenshots=process.env.THADDEUS_SCREENSHOTS!;fs.mkdirSync(screenshots,{recursive:true});
  await page.screenshot({path:path.join(screenshots,'notes-mobile.png'),fullPage:true});
  const state=await api(page,'/state');
  // Manual saves have audit entries; they must never dispatch inference.
  expect(state.runs.every((run:{modelCalls:number;modelDispatches:unknown[]})=>run.modelCalls===0&&run.modelDispatches.length===0)).toBe(true);
  expect(state.search.budget.used).toBe(0);
});
