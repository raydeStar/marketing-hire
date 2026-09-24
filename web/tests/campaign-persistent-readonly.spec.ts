import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const projectId=process.env.MARKETING_PERSISTENT_PROJECT_ID;
test.skip(!projectId,'Set MARKETING_PERSISTENT_PROJECT_ID for a read-only owner acceptance check.');

test('owner can reopen a persisted internal campaign from Work',async({page,request,baseURL})=>{
  await page.setViewportSize({width:1440,height:900});
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  await page.goto('/#launch='+ticket);

  async function inspectWork(){
    await page.getByRole('button',{name:'Work',exact:true}).click();
    const panel=page.getByRole('region',{name:'Standing marketing assignment'});
    await expect(panel).toBeVisible();
    const selection=await page.evaluate(async target=>{
      const [stateResponse,archiveResponse]=await Promise.all([
        fetch('/api/marketing/state'),fetch('/api/marketing/runways')
      ]);
      if(!stateResponse.ok||!archiveResponse.ok)throw new Error('Owner Work records are unavailable.');
      const state=await stateResponse.json();
      const archive=await archiveResponse.json();
      return (archive.projects as {id:string}[]).filter(item=>item.id!==state.runway?.project?.id)
        .findIndex(item=>item.id===target);
    },projectId);
    expect(selection).toBeGreaterThanOrEqual(0);
    const archive=panel.locator('#previous-marketing-assignments');
    await archive.locator(':scope > .runway-artifact > button').nth(selection).click();
    const campaign=archive.getByRole('region',{name:'Campaign workflow'});
    await expect(campaign.getByRole('heading',{name:'Campaign workflow · align'})).toBeVisible();
    await expect(campaign).toContainText('verified by the local host');
    await expect(campaign).toContainText('Learn and collect evidence; no continuation threshold yet.');
    await expect(campaign).toContainText('Specific unanswered buyer questions identified in owner review');
    await expect(campaign).toContainText('Two saved Hacker News comments');
    await expect(campaign).toContainText('no live publishing');
  }

  await inspectWork();
  if(process.env.MARKETING_BASELINE_SCREENSHOT){
    const output=path.resolve('../artifacts/enterprise-review/before-owner-1440.png');
    fs.mkdirSync(path.dirname(output),{recursive:true});
    await page.screenshot({path:output,fullPage:true});
  }
  await page.reload();
  await inspectWork();
});
