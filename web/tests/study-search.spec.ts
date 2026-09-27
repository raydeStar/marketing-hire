import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
async function api(page:Page,url:string,body?:unknown,method='PUT'){
 return page.evaluate(async({url,body,method})=>{const session=await(await fetch('/api/session')).json();const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});if(!response.ok)throw new Error(await response.text());return response.json();},{url,body,method});
}
async function search(page:Page,query:string){
 const expand=page.getByRole('button',{name:'Expand sidebar',exact:true});if(await expand.isVisible())await expand.click();
 await page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name:'Search',exact:true}).click();await page.getByLabel('Search your study').fill(query);
}
test('study search opens saved apps and file previews and locates files in Trash without model calls',async({page})=>{
 await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Open workspace',exact:true}).click();
 const composer=page.getByLabel('Message or goal');await expect(composer).toBeVisible();await composer.fill('Preserve this draft while I look for my work');
 const appId=crypto.randomUUID().replaceAll('-','');await api(page,'/artifacts/'+appId,{operationId:crypto.randomUUID().replaceAll('-',''),version:'absent',definition:{title:'Lantern checklist',description:'Fictional packing for a quiet trip',fields:[{key:'task',label:'Task',kind:'text'}],summaries:[]},upserts:[]});
 await page.locator('.conversation-compose input[type=file]').setInputFiles({name:'lantern-notes.txt',mimeType:'text/plain',buffer:Buffer.from('A small fictional note for preview.')});await expect(page.getByRole('button',{name:'Remove attachment lantern-notes.txt'})).toBeVisible();
 await search(page,'quiet trip');await expect(page.locator('.search-results')).toContainText('Lantern checklist');await page.locator('.search-results button').filter({hasText:'Lantern checklist'}).click();await expect(page.getByRole('heading',{name:'Lantern checklist',exact:true})).toBeVisible();await page.getByRole('button',{name:'Close app',exact:true}).click();await expect(page.getByLabel('Search your study')).toHaveValue('quiet trip');
 await search(page,'lantern-notes');await page.locator('.search-results button').filter({hasText:'lantern-notes.txt'}).click();const preview=page.getByRole('dialog',{name:'lantern-notes.txt'});await expect(preview).toContainText('A small fictional note for preview.');await page.keyboard.press('Escape');await expect(preview).toHaveCount(0);
 const file=(await api(page,'/state')).uploads[0];await api(page,'/uploads/'+file.id,{version:file.version,archived:true});
 await search(page,'lantern-notes');await expect(page.locator('.search-results')).toContainText('in Trash');await page.locator('.search-results button').click();await expect(preview).toHaveCount(0);await expect(page.getByRole('region',{name:'Uploaded files'}).getByRole('button',{name:'Restore',exact:true})).toBeVisible();
 await page.getByRole('region',{name:'Uploaded files'}).getByRole('button',{name:'Restore',exact:true}).click();await search(page,'lantern-notes');await expect(page.locator('.search-results')).not.toContainText('in Trash');
 const expand=page.getByRole('button',{name:'Expand sidebar',exact:true});if(await expand.isVisible())await expand.click();await page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name:'Chat',exact:true}).click();await expect(composer).toHaveValue('Preserve this draft while I look for my work');await expect(page.getByRole('button',{name:'Remove attachment lantern-notes.txt'})).toBeVisible();
 const state=await api(page,'/state');expect(state.runs).toHaveLength(0);expect(state.search.budget.used).toBe(0);
});
