import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

async function navigate(page:Page,name:string){
  const expand=page.getByRole('button',{name:'Expand sidebar',exact:true});
  if(await expand.isVisible())await expand.click();
  await (name==='Settings'?page:page.getByRole('navigation',{name:'Study navigation'})).getByRole('button',{name,exact:true}).click();
}
async function maintenance(page:Page){
  await navigate(page,'Settings');
  await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
  const section=page.getByRole('region',{name:'Backups and shutdown'});
  await section.getByRole('button',{name:'Review maintenance',exact:true}).click();
  return section;
}

test('maintenance waits for the local note draft, then backs up saved work and reopens it',async({page})=>{
  const data=path.resolve(process.env.THADDEUS_TEST_DATA!);
  if(!data.startsWith(path.resolve('../artifacts')+path.sep))throw new Error('Use a disposable study. The private ledger is not a rehearsal prop.');
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(data,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  await page.getByLabel('Message or goal').fill('Keep this unsent chat draft through maintenance.');
  await navigate(page,'Artifacts');await page.getByRole('button',{name:'Notes & memory',exact:true}).click();
  await page.getByRole('button',{name:'New note',exact:true}).click();
  await page.getByLabel('Page path',{exact:true}).fill('notes/keep-the-lantern.md');
  const editor=page.getByLabel('Markdown editor');await editor.fill('A fictional note that must be saved before maintenance.');
  let starts=0;page.on('request',request=>{if(request.method()==='POST'&&new URL(request.url()).pathname==='/api/maintenance/start')starts++;});
  let section=await maintenance(page);
  await expect(section.getByRole('button',{name:'Back up and close study',exact:true})).toBeDisabled();
  await expect(section).toContainText('notes/keep-the-lantern.md');
  await section.getByLabel('Maintenance action').selectOption('stop');
  await expect(section.getByRole('button',{name:'Close study without a new backup',exact:true})).toBeDisabled();
  expect(starts).toBe(0);
  await page.setViewportSize({width:390,height:844});
  const close=page.getByRole('button',{name:'Close sidebar',exact:true});if(await close.isVisible())await close.click();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);
  const screenshots=process.env.THADDEUS_SCREENSHOTS!;fs.mkdirSync(screenshots,{recursive:true});
  await page.screenshot({path:path.join(screenshots,'maintenance-unsaved-note.png'),fullPage:true});
  await section.getByRole('button',{name:'Return to note',exact:true}).click();
  await expect(editor).toHaveValue('A fictional note that must be saved before maintenance.');
  await expect(editor).toBeFocused();
  await page.getByRole('button',{name:'Save my edits',exact:true}).click();await expect(page.locator('.note-save-status')).toHaveText('Saved');
  section=await maintenance(page);
  await expect(section.getByRole('button',{name:'Return to note',exact:true})).toHaveCount(0);
  await expect(section.getByRole('button',{name:'Back up and close study',exact:true})).toBeEnabled();
  await section.getByRole('button',{name:'Back up and close study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Backup verified',exact:true})).toBeVisible({timeout:20000});expect(starts).toBe(1);
  const receipt=await page.evaluate(async()=>(await fetch('/api/maintenance')).json());
  expect(receipt.phase).toBe('verified');
  expect(fs.existsSync(path.join(receipt.destination,'backup.json'))).toBe(true);
  await page.getByRole('button',{name:'Reopen study',exact:true}).click();
  await expect(page.getByLabel('Message or goal')).toHaveValue('Keep this unsent chat draft through maintenance.',{timeout:20000});
  await navigate(page,'Artifacts');await page.getByRole('button',{name:'Notes & memory',exact:true}).click();
  await page.locator('.page-list').getByRole('button',{name:'notes/keep-the-lantern.md',exact:true}).click();
  await expect(editor).toHaveValue('A fictional note that must be saved before maintenance.');
  await page.getByRole('button',{name:'New note',exact:true}).click();await page.getByLabel('Page path',{exact:true}).fill('');
  section=await maintenance(page);await expect(section).toContainText('Untitled note');
  await expect(section.getByRole('button',{name:'Back up and close study',exact:true})).toBeDisabled();
  await section.getByRole('button',{name:'Return to note',exact:true}).click();
  await page.locator('.page-list').getByRole('button',{name:'notes/keep-the-lantern.md',exact:true}).click();
  await page.getByRole('dialog',{name:'Discard unsaved note changes?'}).getByRole('button',{name:'Discard changes',exact:true}).click();
  section=await maintenance(page);await expect(section.getByRole('button',{name:'Back up and close study',exact:true})).toBeEnabled();expect(starts).toBe(1);
  const state=await page.evaluate(async()=>(await fetch('/api/state')).json());
  expect(state.runs.every((run:{modelCalls:number;modelDispatches:unknown[]})=>run.modelCalls===0&&run.modelDispatches.length===0)).toBe(true);
  expect(state.search.budget.used).toBe(0);
  fs.writeFileSync(path.join(screenshots,'maintenance-proof.json'),JSON.stringify({passed:true,maintenanceStarts:starts,savedNotePreserved:true,chatDraftPreserved:true,modelCalls:0,braveCalls:0,backupDirectory:receipt.destination},null,2));
});
