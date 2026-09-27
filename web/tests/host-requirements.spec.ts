import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {closeSidebarOverlay,openSettings} from './navigation';

test('owner can inspect this computer before configuring a worker',async({page})=>{
 await page.goto('/');
 await page.getByLabel('Host access key').fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
 await page.getByRole('button',{name:'Open workspace',exact:true}).click();
 await openSettings(page);await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Research worker',exact:true}).click();
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

test('worker verification shows observed progress and cancels without enabling research',async({page})=>{
 let checking=false,cancels=0,release=()=>{};
 const completed=new Promise<void>(resolve=>{release=resolve;});
 const id='fixture-check-identity';
 const view=()=>({worker:{backend:'qemu-whpx',name:'Fixture worker',installationDigest:'a'.repeat(64),developmentOnly:true},enabled:false,canEnable:false,
  status:checking?'checking':'check-required',summary:'Check the installed worker, then enable research on this host.',
  progress:checking?{id,startedAt:new Date(Date.now()-15000).toISOString(),step:{stage:'runtime-files',verifiedFiles:2,totalFiles:4}}:null,
  lastCheck:cancels?{checkedAt:new Date().toISOString(),passed:false,summary:'Verification stopped before it finished.',checks:[]}:null});
 await page.route('**/api/settings/worker',route=>route.fulfill({json:view()}));
 await page.route('**/api/settings/worker/check',async route=>{checking=true;await completed;await route.fulfill({json:view()});});
 await page.route('**/api/settings/worker/cancel',async route=>{
  expect(route.request().postDataJSON()).toEqual({checkId:id});cancels++;checking=false;release();await route.fulfill({json:view()});
 });
 await page.goto('/');
 await page.getByLabel('Host access key').fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
 await page.getByRole('button',{name:'Open workspace',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
 const before=await page.evaluate(async()=>(await fetch('/api/state')).json());
 await openSettings(page);await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Research worker',exact:true}).click();
 const setup=page.getByRole('region',{name:'Host research setup'});
 await setup.getByRole('button',{name:'Check installed worker',exact:true}).click();
 const progress=setup.getByRole('region',{name:'Worker verification progress'});
 await expect(progress).toContainText('Verified 2 of 4 runtime files.');
 await expect(progress).toContainText('no VM or model is running');
 await expect(setup.getByRole('button',{name:'Enable research on this host',exact:true})).toBeDisabled();
 await page.setViewportSize({width:390,height:900});await closeSidebarOverlay(page);await progress.scrollIntoViewIfNeeded();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
 await progress.getByRole('button',{name:'Cancel worker check',exact:true}).click();
 await expect(progress).toHaveCount(0);await expect(setup).toContainText('Verification stopped before it finished.');
 await expect(setup.getByRole('button',{name:'Enable research on this host',exact:true})).toBeDisabled();
 expect(cancels).toBe(1);
 const after=await page.evaluate(async()=>(await fetch('/api/state')).json());
 expect(after.runs).toEqual(before.runs);expect(after.provider).toEqual(before.provider);
});
