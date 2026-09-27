import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?'?'+query:''}#launch=${(await issued.json()).ticket}`);
}
const file=(name:string,text='A fictional packing note.')=>({name,mimeType:'text/plain',buffer:Buffer.from(text)});

test('Library uploads show progress, keep the files that succeed and name the one that failed',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:1000});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  let release:()=>void=()=>{};const held=new Promise<void>(resolve=>{release=resolve;});let posts=0;
  await page.route('**/api/uploads',async route=>{if(route.request().method()==='POST'){posts++;if(posts===1)await held;}await route.continue();});
  await launch(page,request,baseURL!,'view=library');
  const library=page.getByRole('main',{name:'Library'});
  await expect(library.getByRole('heading',{level:1,name:'All items'})).toBeVisible();

  // Upload files comes from the New menu and opens the ordinary file picker.
  await library.getByRole('button',{name:'New',exact:true}).click();
  const chooser=page.waitForEvent('filechooser');
  await page.getByRole('menuitem',{name:'Upload files'}).click();
  try{
    await (await chooser).setFiles([file('first-note.txt'),file('empty-note.txt',''),file('last-note.txt')]);
    await expect(library.getByRole('status').filter({hasText:'Uploading 1 file…'})).toBeVisible();
  }finally{release();}
  await expect(library.getByRole('status').filter({hasText:/^Uploading/})).toHaveCount(0);

  // The empty file is refused before it is sent; the other two are kept and listed.
  await expect(library.getByRole('alert')).toContainText('empty-note.txt: Images and text files can be up to 2 MiB.');
  await expect(library.getByRole('row').filter({hasText:'first-note.txt'})).toBeVisible();
  await expect(library.getByRole('row').filter({hasText:'last-note.txt'})).toBeVisible();
  await expect(library.getByRole('row').filter({hasText:'empty-note.txt'})).toHaveCount(0);
  expect(posts).toBe(2);
  const uploads=await page.evaluate(async()=>(await(await fetch('/api/state')).json()).uploads.map((upload:any)=>upload.name).sort());
  expect(uploads).toEqual(['first-note.txt','last-note.txt']);

  // A later upload starts clean: the old failure is cleared.
  await library.getByRole('button',{name:'New',exact:true}).click();
  const again=page.waitForEvent('filechooser');
  await page.getByRole('menuitem',{name:'Upload files'}).click();
  await (await again).setFiles([file('third-note.txt')]);
  await expect(library.getByRole('row').filter({hasText:'third-note.txt'})).toBeVisible();
  await expect(library.getByRole('alert')).toHaveCount(0);

  await page.setViewportSize({width:390,height:844});
  await expect(library.getByRole('row').filter({hasText:'third-note.txt'})).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});
