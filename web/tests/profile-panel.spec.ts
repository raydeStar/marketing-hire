import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {openLog,resizeLog} from './navigation';

test('right rail opens versioned Identity, Soul, and User documents and links to memory',async({page})=>{
  const screenshots=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(screenshots,{recursive:true});
  await page.setViewportSize({width:1440,height:1000});
  await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  await openLog(page);await page.getByRole('button',{name:'Profile',exact:true}).click();

  const profile=page.getByRole('region',{name:'Thaddeus profile',exact:true});
  await expect(profile).toBeVisible();await expect(profile.getByText('Connected',{exact:true})).toBeVisible();
  for(const name of ['Identity','Soul','User'])await expect(profile.getByRole('button',{name:new RegExp('^'+name)})).toBeVisible();
  await page.screenshot({path:path.join(screenshots,'profile-rail-1440.png'),fullPage:true});

  await profile.getByRole('button',{name:/^Identity/}).click();
  const editor=page.getByRole('region',{name:'IDENTITY.md editor',exact:true});
  const textarea=editor.getByLabel('IDENTITY.md',{exact:true});
  await expect(textarea).toHaveValue(/Thaddeus/);
  const original=await textarea.inputValue(),marker='\n\n**Test note:** The right-rail editor preserves exact Markdown.';
  await textarea.fill(original+marker);await editor.getByRole('button',{name:'Save changes',exact:true}).click();
  await expect(editor.getByText('Saved to IDENTITY.md.',{exact:true})).toBeVisible();
  await page.reload();await openLog(page);await page.getByRole('button',{name:'Profile',exact:true}).click();
  await page.getByRole('region',{name:'Thaddeus profile',exact:true}).getByRole('button',{name:/^Identity/}).click();
  await expect(page.getByRole('region',{name:'IDENTITY.md editor',exact:true}).getByLabel('IDENTITY.md',{exact:true})).toHaveValue(new RegExp('right-rail editor'));
  await page.getByRole('region',{name:'IDENTITY.md editor',exact:true}).getByRole('button',{name:'Profile',exact:true}).click();

  await resizeLog(page,390,900);
  await page.getByRole('button',{name:'Profile',exact:true}).click();
  await expect(page.getByRole('region',{name:'Thaddeus profile',exact:true})).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await page.screenshot({path:path.join(screenshots,'profile-rail-390.png'),fullPage:true});
  await page.getByRole('region',{name:'Thaddeus profile',exact:true}).getByRole('button',{name:/Notes & memory/}).click();
  await expect(page.getByRole('heading',{name:'Notes & memory',exact:true})).toBeVisible();
  await page.getByText(/Remembered context ·/).click();
  await expect(page.getByText(/One main memory note is a good starting point/)).toBeVisible();
});
