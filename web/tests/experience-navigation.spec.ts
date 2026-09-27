import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

async function api(page:Page,url:string,body?:unknown,method='POST'){
 return page.evaluate(async({url,body,method})=>{const session=await(await fetch('/api/session')).json();const response=await fetch('/api'+url,body===undefined?{}:{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});if(!response.ok)throw new Error(await response.text());return response.json();},{url,body,method});
}
async function nav(page:Page,name:string){const toggle=page.getByRole('button',{name:'Expand sidebar',exact:true});if(await toggle.count())await toggle.click();await page.getByRole('navigation',{name:'Study navigation'}).getByRole('button',{name,exact:true}).click();}

test('recurring completion reopens correctly and discussing items preserves drafts and attachments',async({page})=>{
 test.setTimeout(60000);page.setDefaultTimeout(10000);fs.mkdirSync(process.env.THADDEUS_SCREENSHOTS!,{recursive:true});await page.setViewportSize({width:1440,height:1000});await page.goto('/');
 await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Open workspace',exact:true}).click();await expect(page.getByLabel('Message or goal')).toBeVisible();
 await nav(page,'To-do');await page.getByRole('button',{name:'Add to Tracked',exact:true}).click();const editor=page.getByRole('form',{name:'Edit to-do'});
 await editor.getByLabel('Title',{exact:true}).fill('Fictional walks');await editor.getByLabel('Next step',{exact:true}).fill('Pick a path');await editor.getByRole('button',{name:'Save item',exact:true}).click();
 await page.getByRole('button',{name:'Check in: Fictional walks',exact:true}).click();await expect(page.getByText(/checked in today/)).toBeVisible();
 const checked=(await api(page,'/state')).library.find((i:any)=>i.title==='Fictional walks');
 await page.getByRole('button',{name:'Finish tracking',exact:true}).click();await page.getByRole('navigation',{name:'Task status'}).getByRole('button',{name:/Completed/}).click();
 await page.getByRole('button',{name:'Mark unfinished: Fictional walks',exact:true}).click();await page.getByRole('navigation',{name:'Task status'}).getByRole('button',{name:/Active/}).click();await expect(page.getByRole('heading',{name:'Fictional walks',exact:true})).toBeVisible();
 const reopened=(await api(page,'/state')).library.find((i:any)=>i.id===checked.id);expect(reopened.status).toBe('open');expect(reopened.tracking).toEqual(checked.tracking);
 await page.getByRole('button',{name:'Archive',exact:true}).click();await page.getByRole('navigation',{name:'Task status'}).getByRole('button',{name:/Archived/}).click();await expect(page.getByRole('button',{name:'Finish tracking',exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Restore',exact:true}).click();
 await page.getByRole('navigation',{name:'Task status'}).getByRole('button',{name:/Active/}).click();await page.getByRole('button',{name:'Edit Fictional walks',exact:true}).click();await editor.getByRole('textbox',{name:'Notes',exact:true}).fill('My unfinished edit');
 const before=(await api(page,'/state')).library.find((i:any)=>i.id===checked.id);await api(page,'/library/'+before.id,{...before,content:'Changed in another window'},'PUT');
 await expect(editor.getByRole('button',{name:'Save item',exact:true})).toBeDisabled();await expect(editor.getByRole('textbox',{name:'Notes',exact:true})).toHaveValue('My unfinished edit');await editor.getByRole('button',{name:'Reload saved item',exact:true}).click();await expect(editor.getByRole('textbox',{name:'Notes',exact:true})).toHaveValue('Changed in another window');await editor.getByRole('button',{name:'Close item editor',exact:true}).click();

 await api(page,'/library/cccccccccccccccccccccccccccccccc',{id:'cccccccccccccccccccccccccccccccc',kind:'feed',title:'A fictional reading',content:'Notes for a quiet afternoon.',status:'open',url:'https://example.org/reading',due:null,version:'absent'},'PUT');
 await nav(page,'Chat');const composer=page.getByLabel('Message or goal');await composer.fill('Keep my unfinished thought.');
 await page.locator('.conversation-compose input[type=file]').setInputFiles({name:'clipping.txt',mimeType:'text/plain',buffer:Buffer.from('A fictional clipping.')});await expect(page.getByText('clipping.txt',{exact:true})).toBeVisible();
 await nav(page,'Feed');await page.getByRole('button',{name:/^Saved links/}).click();await page.getByRole('button',{name:'Discuss',exact:true}).click();
 const dialog=page.getByRole('dialog',{name:'Add this to your draft?'});await expect(dialog).toContainText('attached files');await expect(dialog).toContainText('A fictional reading');await dialog.getByRole('button',{name:'Keep current draft',exact:true}).click();await expect(page.getByRole('heading',{name:'Feed',exact:true})).toBeVisible();
 await nav(page,'Chat');await expect(composer).toHaveValue('Keep my unfinished thought.');await expect(page.getByText('clipping.txt',{exact:true})).toBeVisible();
 await nav(page,'Feed');await page.getByRole('button',{name:/^Saved links/}).click();await page.getByRole('button',{name:'Discuss',exact:true}).click();await page.setViewportSize({width:390,height:900});await expect(dialog.getByRole('button',{name:'Add to draft',exact:true})).toBeInViewport();
 await page.screenshot({animations:'disabled',path:path.join(process.env.THADDEUS_SCREENSHOTS!,'discussion-mobile.png')});await dialog.getByRole('button',{name:'Add to draft',exact:true}).click();await expect(composer).toHaveValue('Keep my unfinished thought.\n\nA fictional reading\n\nNotes for a quiet afternoon.\n\nhttps://example.org/reading');await expect(page.getByText('clipping.txt',{exact:true})).toBeVisible();
 await nav(page,'Ideas');await page.getByRole('button',{name:/^Make a little room in your day/}).click();await expect(dialog).toBeVisible();await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await expect(page.getByRole('heading',{name:'Ideas',exact:true})).toBeVisible();
 const final=await api(page,'/state');expect(final.runs).toHaveLength(0);expect(final.search.budget.used).toBe(0);
});
