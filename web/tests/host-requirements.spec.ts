import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('owner can inspect this computer before configuring a worker',async({page})=>{
 await page.goto('/');
 await page.getByLabel('Host access key').fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
 await page.getByRole('button',{name:'Unlock study',exact:true}).click();
 await page.getByRole('button',{name:'Settings',exact:true}).click();
 const setup=page.getByRole('region',{name:'Host research setup'});
 const before=await page.evaluate(async()=>(await fetch('/api/state')).json());
 const observation=page.waitForResponse(response=>response.url().endsWith('/api/settings/worker/requirements'));
 await setup.getByRole('button',{name:'Check this computer',exact:true}).click();
 const response=await observation;expect(response.status()).toBe(200);const observed=await response.json();
 const report=setup.getByRole('region',{name:'Computer requirements'});
 await expect(report).toBeVisible();
 await expect(report).toContainText('No system setting was changed.');
 await expect(report).toContainText(observed.summary);
 for(const check of observed.checks){await expect(report).toContainText(check.detail);if(check.nextStep)await expect(report).toContainText(check.nextStep);}
 await expect(setup.getByRole('button',{name:'Check installed worker',exact:true})).toBeDisabled();
 await expect(setup.getByRole('button',{name:'Enable research on this host',exact:true})).toHaveCount(0);
 const after=await page.evaluate(async()=>(await fetch('/api/state')).json());
 expect(after.runs).toEqual(before.runs);expect(after.research).toEqual(before.research);expect(after.provider).toEqual(before.provider);
 const directory=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(directory,{recursive:true});
 fs.writeFileSync(path.join(directory,'host-requirements.json'),JSON.stringify(observed,null,2));
 for(const width of [1440,390]){
  await page.setViewportSize({width,height:1000});await setup.scrollIntoViewIfNeeded();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await setup.screenshot({path:path.join(directory,`host-requirements-${width}.png`)});
 }
});
