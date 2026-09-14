import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

// Explicit desktop acceptance: an external operator uses the actual native dialog.
// Normal suites skip it; no route stub or test-only selector substitutes for Windows UI.
test('native folder selection returns to maintenance and cancellation leaves no trusted review',async({page})=>{
 test.skip(process.env.THADDEUS_NATIVE_PICKER!=='1','Requires an explicit native desktop operator.');
 test.setTimeout(240_000);
 const data=path.resolve(process.env.THADDEUS_TEST_DATA!);
 const application=path.resolve(process.env.THADDEUS_TEST_PACKAGE!);
 const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);
 expect(data.startsWith(path.resolve('../artifacts')+path.sep)).toBe(true);
 const packageEntries=fs.readdirSync(application,{recursive:true}).sort();
 fs.mkdirSync(images,{recursive:true});
 await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(data,'host-key.txt'),'utf8').trim());
 await page.getByRole('button',{name:'Unlock study',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
 const before=await page.evaluate(async()=>(await fetch('/api/export')).json());
 await page.getByRole('button',{name:'Settings',exact:true}).click();
 await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
 await page.getByRole('button',{name:'Review maintenance',exact:true}).click();
 await page.getByRole('button',{name:'Back up and close study',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Backup verified',exact:true})).toBeVisible({timeout:20_000});
 await page.getByRole('button',{name:'Find saved backups',exact:true}).click();
 await page.getByRole('checkbox',{name:'Use a different application version'}).check();
 const input=page.getByLabel('Extracted application folder');
 await input.fill(application);
 await page.getByRole('button',{name:'Review selected backup',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Review the separate copy'})).toBeVisible();
 const review=await page.evaluate(async()=>(await fetch('/api/maintenance/restore')).json());
 const request=async(endpoint:string,body:unknown,csrf=true)=>page.evaluate(async({endpoint,body,csrf})=>{
  const session=await(await fetch('/api/session')).json();
  const response=await fetch('/api/maintenance/'+endpoint,{method:'POST',headers:{'Content-Type':'application/json',...(csrf?{'X-CSRF':session.csrf}:{})},body:JSON.stringify(body)});
  return response.status;
 },{endpoint,body,csrf});
 const maintenance=await page.evaluate(async()=>(await fetch('/api/maintenance')).json());
 expect(await request('application-folder/start',{version:maintenance.version,mode:'choose'},false)).toBe(403);
 expect(await request('application-folder/start',{version:'stale',mode:'choose'})).toBe(409);
 await page.getByRole('button',{name:'Browse for application folder'}).click();
 await expect(page.getByRole('button',{name:'Cancel folder selection'})).toBeVisible();
 const first=await page.evaluate(async()=>(await fetch('/api/maintenance/application-folder')).json());
 expect(first.phase).toBe('choosing');
 expect(await request('application-folder/start',{version:maintenance.version,mode:'choose'})).toBe(409);
 expect(await request('restore/start',{reviewId:review.review.id})).toBe(409);
 expect(await request('finish',{version:maintenance.version,mode:'close'})).toBe(409);
 await page.reload();await expect(page.getByRole('button',{name:'Cancel folder selection'})).toBeVisible();
 await expect(page.getByRole('button',{name:'Reopen study',exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'Cancel folder selection'}).click();
 await expect(page.getByRole('button',{name:'Cancel folder selection'})).toHaveCount(0);
 expect(await request('restore/start',{reviewId:review.review.id})).toBe(409);
 await page.getByRole('button',{name:'Find saved backups',exact:true}).click();
 await page.getByRole('checkbox',{name:'Use a different application version'}).check();
 await page.getByRole('button',{name:'Browse for application folder'}).click();
 const second=await page.evaluate(async()=>(await fetch('/api/maintenance/application-folder')).json());
 expect(second.id).not.toBe(first.id);
 expect(await request('application-folder/cancel',{id:first.id})).toBe(409);
 let interrupted=false;
 await page.route('**/api/maintenance/application-folder',async route=>{
  if(!interrupted){interrupted=true;await route.abort('connectionreset');}else await route.continue();
 });
 await expect(page.getByRole('alert')).toBeVisible();
 await expect(page.getByRole('alert')).toHaveCount(0,{timeout:10_000});
 expect(interrupted).toBe(true);
 await page.unroute('**/api/maintenance/application-folder');
 fs.writeFileSync(path.join(images,'picker-ready.json'),JSON.stringify({application,operation:second.id,phase:second.phase}),{flag:'wx'});
 await expect(input).toHaveValue(application,{timeout:180_000});
 await expect(page.getByRole('button',{name:'Cancel folder selection'})).toHaveCount(0);
 const selected=await page.evaluate(async()=>(await fetch('/api/maintenance/application-folder')).json());
 expect(selected.phase).toBe('selected');expect(selected.directory).toBe(application);
 await page.getByRole('button',{name:'Review selected backup',exact:true}).click();
 await expect(page.getByRole('region',{name:'Selected application',exact:true})).toBeVisible();
 for(const width of [1440,390]){
  await page.setViewportSize({width,height:1000});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await page.screenshot({path:path.join(images,`native-folder-${width}.png`),fullPage:true});
 }
 await page.reload();await expect(page.getByRole('region',{name:'Selected application',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Reopen study',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible({timeout:20_000});
 expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
 expect(fs.readdirSync(application,{recursive:true}).sort()).toEqual(packageEntries);
 fs.writeFileSync(path.join(images,'picker-verified.json'),JSON.stringify({selected,revokedReview:review.review.id,mainStudyTouched:false,modelCalls:0}),{flag:'wx'});
});
