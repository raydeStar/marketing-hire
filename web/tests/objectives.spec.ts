import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query:string){
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/?${query}#launch=${(await issued.json()).ticket}`);
}

test('the owner sets a north star tied to the scorecard and the cockpit tracks it',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'pane=work');
  // A scorecard with a daily trial-starts series.
  const scorecard=page.getByRole('region',{name:'Scorecard'});
  await scorecard.getByRole('button',{name:'Import data'}).first().click();
  const lines=['date,Trial starts'];for(let back=29;back>=0;back--)lines.push(`${new Date(Date.now()-back*86400000).toISOString().slice(0,10)},10`);
  await page.getByRole('dialog',{name:'Import scorecard data'}).getByLabel('CSV',{exact:true}).fill(lines.join('\n'));
  await page.getByRole('dialog',{name:'Import scorecard data'}).getByRole('button',{name:'Import'}).click();
  await expect(scorecard.getByRole('status')).toContainText('Imported');

  const cockpit=page.getByRole('complementary',{name:'Cockpit'});
  await cockpit.getByRole('button',{name:/Set your main goal/}).click();
  const window=page.locator('.fe-window');
  await window.getByRole('button',{name:'Set objectives'}).click();
  const form=page.getByRole('form',{name:'Edit objectives'});
  await form.getByLabel('Name',{exact:true}).fill('Trial starts');
  await form.getByLabel('Scorecard metric').selectOption({label:'Trial starts'});
  await form.getByLabel('Target').fill('600');
  await form.getByLabel('Unit').fill('per month');
  await form.getByLabel('Objective 1',{exact:true}).fill('Recover signup conversion');
  await form.getByLabel('Key results 1',{exact:true}).fill('Signup conversion back above 5%');
  await form.getByLabel("Who it’s for").fill('Founders of small B2B software companies');
  await form.getByRole('button',{name:'Add',exact:true}).nth(1).click();
  await form.getByLabel('Proof points 1',{exact:true}).fill('Every draft needs owner approval');
  await form.getByLabel('Current focus').fill('The signup funnel');
  await form.getByRole('group',{name:'Listening'}).getByRole('button',{name:'Add',exact:true}).first().click();
  await form.getByLabel('Topic to watch 1',{exact:true}).fill('First Employee');
  await form.getByRole('group',{name:'Research sites'}).getByRole('button',{name:'Add',exact:true}).first().click();
  await form.getByLabel('Research site 1',{exact:true}).fill('https://www.competitor-example.com/pricing');
  await form.getByRole('button',{name:'Save objectives'}).click();
  await expect(window.getByRole('status')).toContainText('The next shift cycle works from these');
  await expect(window).toContainText('Recover signup conversion');
  await expect(window.getByRole('heading',{name:'Research sites'}).locator('..')).toContainText('competitor-example.com');
  // 30 days × 10 = 300 of a 600-a-month target.
  await expect(cockpit.getByRole('button',{name:'Main goal: Trial starts'})).toContainText('50%');
  // Listening shows the topic, and says plainly when the community search can't be reached (no employee container here).
  await page.getByRole('button',{name:'Close'}).click();
  const listening=page.getByRole('region',{name:'Listening'});
  await expect(listening.getByRole('row').filter({hasText:'First Employee'})).toBeVisible();
  await listening.getByRole('button',{name:'Listen now'}).click();
  await expect(listening.getByRole('status')).toContainText('0 new mentions');
  await expect(listening).toContainText("container isn't reachable");
  // It is filed in the Library under Company.
  await page.getByRole('complementary',{name:'Main navigation'}).getByRole('button',{name:'Library'}).click();
  await page.getByRole('treeitem',{name:/Company/}).first().getByRole('button',{name:/Company/}).click();
  await expect(page.getByRole('row').filter({hasText:'Objectives & positioning'})).toBeVisible();
});
