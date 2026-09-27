import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {openSettings} from './navigation';

test('owner reviews maintenance, sees a verified backup, reloads and reopens unchanged history',async({page})=>{
  const data=path.resolve(process.env.THADDEUS_TEST_DATA||'../.data');
  const images=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(images,{recursive:true});
  if(!process.env.CI&&(!process.env.THADDEUS_TEST_ORIGIN||!data.startsWith(path.resolve('../artifacts')+path.sep)))
    throw new Error('Maintenance browser checks require an explicitly configured disposable host and data folder under artifacts.');
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(data,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Open workspace',exact:true}).click();await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  const before=await page.evaluate(async()=>(await fetch('/api/export')).json());
  // An ordinary file where the backup directory should be makes copying fail without modifying source data.
  const backupRoot=data+'-backups';expect(fs.existsSync(backupRoot)).toBe(false);
  fs.writeFileSync(backupRoot,'Fictional blocked backup location.',{flag:'wx'});
  await openSettings(page);await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
  const section=page.getByRole('region',{name:'Backups and shutdown'});
  await section.getByRole('button',{name:'Review maintenance',exact:true}).click();
  await expect(section.getByRole('heading',{name:'Put the study in order'})).toBeVisible();
  await section.getByRole('button',{name:'Keep working',exact:true}).click();
  await expect(section.getByRole('heading',{name:'Put the study in order'})).toHaveCount(0);
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
  await section.getByRole('button',{name:'Review maintenance',exact:true}).click();
  await section.getByLabel('Maintenance action').selectOption('backup');
  await section.getByRole('button',{name:'Back up and close study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Study maintenance',exact:true})).toBeVisible();
  await expect(page.getByRole('status')).toContainText('A completed backup could not be confirmed',{timeout:20000});
  await expect(page.getByRole('heading',{name:'Backup verified',exact:true})).toHaveCount(0);
  await page.getByRole('button',{name:'Reopen study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible({timeout:20000});
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
  fs.renameSync(backupRoot,path.join(images,'blocked-backup-location-'+Date.now()+'.fixture'));
  await openSettings(page);await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
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
  const edit=await page.evaluate(async()=>{
    const session=await(await fetch('/api/session')).json();
    const response=await fetch('/api/knowledge',{method:'PUT',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},
      body:JSON.stringify({path:'notes/restore-browser.md',content:'Keep this newer original edit.',version:'absent'})});
    return response.status;
  });expect(edit).toBe(200);
  const newer=await page.evaluate(async()=>(await fetch('/api/export')).json());
  await openSettings(page);await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
  await section.getByRole('button',{name:'Review maintenance',exact:true}).click();
  await section.getByRole('button',{name:'Back up and close study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Study maintenance',exact:true})).toBeVisible();
  await expect(page.getByRole('heading',{name:'Backup verified',exact:true})).toBeVisible({timeout:20000});
  const second=await page.evaluate(async()=>(await fetch('/api/maintenance')).json());
  expect(second.version).not.toBe(verified.version);expect(second.receipt.directory).not.toBe(verified.receipt.directory);
  const restore=page.getByRole('region',{name:'Restore a backup',exact:true});
  await restore.getByRole('button',{name:'Find saved backups',exact:true}).click();
  await restore.getByLabel('Recorded backup',{exact:true}).selectOption(verified.version);
  if(process.env.THADDEUS_TEST_PACKAGE){
    await restore.getByRole('checkbox',{name:'Use a different application version'}).check();
    await restore.getByLabel('Extracted application folder').fill(process.env.THADDEUS_TEST_PACKAGE);
  }
  await restore.getByRole('button',{name:'Review selected backup',exact:true}).click();
  await expect(restore.getByRole('heading',{name:'Review the separate copy',exact:true})).toBeVisible();
  const reviewed=await page.evaluate(async()=>(await fetch('/api/maintenance/restore')).json());
  expect(reviewed.review.backupDirectory).toBe(verified.receipt.directory);
  if(process.env.THADDEUS_TEST_PACKAGE){
    expect(reviewed.review.application.directory).toBe(process.env.THADDEUS_TEST_PACKAGE);
    expect(reviewed.review.application.publisherVerified).toBe(false);
    await expect(restore.getByRole('region',{name:'Selected application',exact:true})).toBeVisible();
    for(const width of [1440,390]){
      await page.setViewportSize({width,height:1000});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
      await page.screenshot({path:path.join(images,`application-review-${width}.png`),fullPage:true});
    }
  }
  expect(fs.existsSync(reviewed.review.destination)).toBe(false);
  await restore.getByRole('button',{name:'Restore as a separate study',exact:true}).click();
  await expect(restore.getByRole('heading',{name:'Restored study verified',exact:true})).toBeVisible({timeout:20000});
  const restored=await page.evaluate(async()=>(await fetch('/api/maintenance/restore')).json());
  expect(restored.receipt.manifestSha256).toBe(verified.receipt.manifestSha256);
  if(process.env.THADDEUS_TEST_PACKAGE){
    expect(restored.returnLauncher.package).toBe(process.env.THADDEUS_TEST_PACKAGE);
    await expect(restore.getByRole('region',{name:'Return to original study',exact:true})).toBeVisible();
    expect(JSON.parse(fs.readFileSync(restored.returnLauncher.profile,'utf8')).dataDirectory).toBe(data);
  }
  expect(fs.existsSync(path.join(restored.receipt.directory,'knowledge/notes/restore-browser.md'))).toBe(false);
  expect(fs.readFileSync(path.join(data,'knowledge/notes/restore-browser.md'),'utf8')).toBe('Keep this newer original edit.');
  if(reviewed.review.canPrepareLauncher){
    expect(fs.existsSync(restored.launcher.entryPoint)).toBe(true);
    await expect(restore.getByRole('button',{name:'Copy launcher folder location',exact:true})).toBeVisible();
  }
  await page.reload();await expect(restore.getByRole('heading',{name:'Restored study verified',exact:true})).toBeVisible();
  expect((await page.evaluate(async()=>(await fetch('/api/maintenance/restore')).json())).receipt).toEqual(restored.receipt);
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await page.screenshot({path:path.join(images,`restore-${width}.png`),fullPage:true});
  }
  await page.getByRole('button',{name:'Reopen study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible({timeout:20000});
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(newer);
});
