import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('native research through the product composer, question and exact import approval',async({page})=>{
  test.skip(process.env.THADDEUS_NATIVE_RESEARCH!=='1','Explicit owned VM fixture only; no automatic virtualization or model dispatch.');
  test.setTimeout(600000);
  const root=path.resolve(process.env.THADDEUS_TEST_DATA!);
  const hostKey=fs.readFileSync(path.join(root,'host-key.txt'),'utf8').trim();
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(hostKey);
  await page.getByRole('button',{name:'Unlock study'}).click();
  await page.getByLabel('Message mode').selectOption('research');
  await expect(page.getByRole('region',{name:'Research scope'})).toBeVisible();
  await expect(page.getByRole('checkbox',{name:'notes/source.md'})).toBeChecked();
  await page.getByLabel('Public source websites (optional)').fill('docs.docker.com');
  await page.getByLabel('Message or goal').fill('Read notes/source.md and https://docs.docker.com/ai/sandboxes/faq/. Ask which workshop audience to use, then write summary.md and propose its exact contents for plans/summary.md. This is a fictional integration fixture.');
  await page.screenshot({path:path.join(root,'research-composer.png'),fullPage:true});
  await page.getByRole('button',{name:'Start research'}).click();
  const progress=page.getByRole('region',{name:'Research progress'});
  await expect(page.getByRole('heading',{name:'A detail before I continue.'})).toBeVisible({timeout:180000});
  const answer=page.getByRole('button',{name:'Send answer & continue'});
  await expect(page.getByRole('button',{name:'Developers',exact:true})).toBeEnabled({timeout:180000});
  await page.reload(); // Durable question and stopped worker are recovered by the normal UI state request.
  await page.getByRole('button',{name:'Tasks',exact:true}).click();
  await page.locator('[data-run-id]').first().click();
  await page.getByRole('button',{name:'Developers',exact:true}).click();
  await page.screenshot({path:path.join(root,'research-question.png'),fullPage:true});
  await answer.click();
  await expect(page.getByRole('button',{name:'Approve exact write'})).toBeEnabled({timeout:180000});
  await expect(progress.getByText('Artifact readback matched: summary.md',{exact:true})).toBeVisible();
  const before=await page.evaluate(async()=>({status:(await fetch('/api/knowledge?path=plans/summary.md')).status}));
  expect(before.status).toBe(404);
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    await page.screenshot({path:path.join(root,`research-approval-${width}.png`),fullPage:true});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  }
  await page.getByRole('button',{name:'Approve exact write'}).click();
  await expect(progress.getByText('Worker stopped · private workspace retained for inspection',{exact:true})).toBeVisible({timeout:60000});
  await page.getByRole('button',{name:'Open editable plan'}).click();
  await expect(page.getByLabel('Markdown editor')).toContainText('Audience: Developers');
  const exported=await page.evaluate(async()=>(await fetch('/api/export')).json());
  expect(exported.runs[0].research.phase).toBe('finished');
  expect(exported.runs[0].research.review.artifact).toBe('summary.md');
  expect(exported.runs[0].goal.criteria.some((criterion:any)=>criterion.status==='unverified')).toBeTruthy();
  expect(exported.runs[0].modelCalls).toBe(5);
  fs.writeFileSync(path.join(root,'browser-export.json'),JSON.stringify(exported,null,2));
  fs.writeFileSync(path.join(root,'browser-finished.json'),JSON.stringify({passed:true,syntheticModel:true}));
});
