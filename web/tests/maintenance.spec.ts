import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('owner reviews maintenance, sees a verified backup, reloads and reopens unchanged history',async({page})=>{
  const data=path.resolve(process.env.THADDEUS_TEST_DATA||'../.data');
  const images=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(images,{recursive:true});
  if(!process.env.CI&&(!process.env.THADDEUS_TEST_ORIGIN||!data.startsWith(path.resolve('../artifacts')+path.sep)))
    throw new Error('Maintenance browser checks require an explicitly configured disposable host and data folder under artifacts.');
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(data,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  const before=await page.evaluate(async()=>(await fetch('/api/export')).json());
  // An ordinary file where the backup directory should be makes copying fail without modifying source data.
  const backupRoot=data+'-backups';expect(fs.existsSync(backupRoot)).toBe(false);
  fs.writeFileSync(backupRoot,'Fictional blocked backup location.',{flag:'wx'});
  await page.getByRole('button',{name:'Settings',exact:true}).click();
  const section=page.getByRole('region',{name:'Backups and shutdown'});
  await section.getByRole('button',{name:'Review maintenance',exact:true}).click();
  await expect(section.getByRole('heading',{name:'Put the study in order'})).toBeVisible();
  await section.getByRole('button',{name:'Keep working',exact:true}).click();
  await expect(section.getByRole('heading',{name:'Put the study in order'})).toHaveCount(0);
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
  await section.getByRole('button',{name:'Review maintenance',exact:true}).click();
  await section.getByLabel('Maintenance action').selectOption('backup');
  await section.getByRole('button',{name:'Back up and close study',exact:true}).click();
  await expect(page.getByRole('status')).toContainText('A completed backup could not be confirmed',{timeout:20000});
  await expect(page.getByRole('heading',{name:'Backup verified',exact:true})).toHaveCount(0);
  await page.getByRole('button',{name:'Reopen study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible({timeout:20000});
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
  fs.renameSync(backupRoot,path.join(images,'blocked-backup-location-'+Date.now()+'.fixture'));
  await page.getByRole('button',{name:'Settings',exact:true}).click();
  await section.getByRole('button',{name:'Review maintenance',exact:true}).click();
  await section.getByRole('button',{name:'Back up and close study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Study maintenance',exact:true})).toBeVisible();
  await expect(page.getByRole('heading',{name:'Backup verified',exact:true})).toBeVisible({timeout:20000});
  await expect(page.getByRole('button',{name:'Send message',exact:true})).toHaveCount(0);
  const verified=await page.evaluate(async()=>(await fetch('/api/maintenance')).json());
  expect(verified.phase).toBe('verified');expect(verified.receipt.files).toBeGreaterThan(0);
  expect(fs.existsSync(path.join(verified.destination,'backup.json'))).toBe(true);
  await page.reload();await expect(page.getByRole('heading',{name:'Backup verified',exact:true})).toBeVisible();
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await page.screenshot({path:path.join(images,`maintenance-${width}.png`),fullPage:true});
  }
  await page.getByRole('button',{name:'Reopen study',exact:true}).focus();await page.keyboard.press('Enter');
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible({timeout:20000});
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
});
