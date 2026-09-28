import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

// A shift on the real fixture host with the scripted stand-in model: no model budget is spent.
const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query='pane=work'){
  if(process.env.THADDEUS_TEST_PLOW==='1'){await page.goto('/?'+query);return;}
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/?${query}#launch=${(await issued.json()).ticket}`);
}
function csv(){
  const lines=['date,Signups,Cost per signup'];
  for(let back=20;back>=0;back--){const day=new Date(Date.now()-back*86400000).toISOString().slice(0,10);lines.push(`${day},${back===0?40:100+back%3},12`);}
  return lines.join('\n');
}

test('the owner imports a scorecard, starts a shift, watches the loop run and stops it with a report',async({page,request,baseURL})=>{
  test.setTimeout(90000);
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!);

  // Scorecard: paste a CSV; the sharp drop is flagged as a material move.
  const scorecard=page.getByRole('region',{name:'Scorecard'});
  await scorecard.getByRole('button',{name:'Import data'}).first().click();
  const importer=page.getByRole('dialog',{name:'Import scorecard data'});
  await importer.getByLabel('CSV',{exact:true}).fill(csv());
  await importer.getByLabel('Source name (optional)').fill('Test export');
  await importer.getByRole('button',{name:'Import'}).click();
  await expect(scorecard.getByRole('status')).toContainText('across 2 metrics');
  await expect(scorecard.getByRole('row').filter({hasText:'Signups'})).toContainText('Major move');
  // Data connections: Google waits for the one-time app setup; Plausible takes an API key (not submitted here).
  await scorecard.getByRole('button',{name:'Connect data'}).click();
  const connect=page.getByRole('dialog',{name:'Connect data'});
  await expect(connect.getByRole('button',{name:/Google Analytics/})).toBeDisabled();
  await expect(connect).toContainText('Settings → Google app');
  await connect.getByRole('button',{name:/Plausible/}).click();
  await expect(connect.getByRole('form',{name:'Connect Plausible'}).getByLabel('API key')).toHaveAttribute('type','password');
  await connect.getByRole('button',{name:'Close dialog'}).click();

  // Start an 8-hour shift from the cockpit.
  const cockpit=page.getByRole('complementary',{name:'Cockpit'});
  const shift=cockpit.getByRole('region',{name:'Shift'});
  await expect(shift).toContainText(/Off shift/);
  await shift.getByRole('button',{name:'Start shift'}).click();
  const start=page.getByRole('dialog',{name:'Start a shift'});
  await expect(start).toContainText('scripted stand-in');
  await start.getByRole('button',{name:'Start 8-hour shift'}).click();
  await expect(shift).toContainText('On shift');

  // Run a cycle now: the loop senses the drop and writes an analysis.
  await shift.getByRole('button',{name:'Run a cycle now'}).click();
  await expect(shift.getByRole('list',{name:'Operating loop'}).getByText('Sense')).toHaveClass(/done/);
  await expect(shift).toContainText('Cycle 1 done');
  const log=page.getByRole('region',{name:'Shift log'});
  await expect(log).toContainText('Explain the move in Signups');
  await log.getByRole('button',{name:/Explain the move in Signups/}).click();
  const window=page.locator('.fe-window');
  await expect(window.getByRole('heading',{name:'Explain the move in Signups'})).toBeVisible();
  await expect(window).toContainText('What we know');
  // The owner tells the employee the analysis missed the point; the verdict is kept with the reason.
  const rate=window.getByRole('region',{name:'Feedback for the employee'});
  await rate.getByRole('button',{name:'Not useful'}).click();
  await rate.getByLabel(/Why\?/).fill('Check the tracking change first.');
  await rate.getByRole('button',{name:'Send feedback'}).click();
  await expect(rate).toContainText('Marked not useful: “Check the tracking change first.”');

  // Pause, resume, then stop: the report lands in the Library.
  await page.getByRole('button',{name:'Close'}).click();
  await shift.getByRole('button',{name:'Pause shift'}).click();
  await expect(shift).toContainText('Shift paused');
  await shift.getByRole('button',{name:'Resume shift'}).click();
  await expect(shift).toContainText('On shift');
  page.once('dialog',dialog=>void dialog.accept());
  await shift.getByRole('button',{name:'Stop shift'}).click();
  await expect(shift).toContainText('Off shift');
  await shift.getByRole('button',{name:'Read the last shift report'}).click();
  await expect(page.locator('.fe-window')).toContainText('Cycle log');
  await expect(page.locator('.fe-window')).toContainText('Shift reports');
  await expect(page.locator('.fe-window')).toContainText('Updated the Marketing notebook');

  // Publishing channels live in Settings; connecting one asks for an app password, never the account password.
  await page.goto('/?view=settings');
  const channels=page.getByRole('region',{name:'Publishing'});
  await expect(channels).toContainText('approving a draft never posts it');
  await channels.getByRole('button',{name:'Connect a channel'}).click();
  const picker=page.getByRole('dialog',{name:'Connect a channel'});
  await picker.getByRole('button',{name:/Bluesky/}).click();
  await expect(picker.getByLabel('App password')).toHaveAttribute('type','password');
  await picker.getByRole('button',{name:'Back'}).click();
  await picker.getByRole('button',{name:/LinkedIn/}).click();
  await expect(picker).toContainText('/api/publishing/oauth/callback');
  await picker.getByRole('button',{name:'Back'}).click();
  await expect(picker.getByRole('button',{name:/Email \(Gmail drafts\)/})).toContainText('Gmail drafts');
  await picker.getByRole('button',{name:'Close dialog'}).click();
});

test('a four-hour shift starts directly with a readable desktop and phone dialog',async({page,request,baseURL})=>{
  await page.route('**/*',route=>new URL(route.request().url()).origin===baseURL?route.continue():route.abort());
  await page.addInitScript(()=>{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');localStorage.setItem('fe-cockpit-open','yes');});
  await page.setViewportSize({width:1440,height:1000});await launch(page,request,baseURL!);
  const shift=page.getByRole('region',{name:'Shift',exact:true});await expect(shift).toContainText('Off shift');
  await shift.getByRole('button',{name:'Start shift'}).click();
  const dialog=page.getByRole('dialog',{name:'Start a shift'});
  await dialog.getByRole('radio',{name:'4 hours',exact:true}).check();
  for(const [name,width,height] of [['desktop',1440,1000],['phone',390,844]] as const){
    await page.setViewportSize({width,height});await expect(dialog.getByRole('button',{name:'Start 4-hour shift'})).toBeVisible();
    expect(await dialog.evaluate(el=>el.scrollWidth-el.clientWidth)).toBeLessThanOrEqual(1);
    if(process.env.THADDEUS_SCREENSHOTS){fs.mkdirSync(process.env.THADDEUS_SCREENSHOTS,{recursive:true});await page.screenshot({path:path.join(process.env.THADDEUS_SCREENSHOTS,`four-hour-${name}.png`)});}
  }
  const started=page.waitForResponse(response=>response.url().endsWith('/api/shifts')&&response.request().method()==='POST');
  await dialog.getByRole('button',{name:'Start 4-hour shift'}).click();
  const receipt=await (await started).json();expect(Date.parse(receipt.endsAt)-Date.parse(receipt.startedAt)).toBe(4*60*60*1000);
  await page.setViewportSize({width:1440,height:1000});await expect(shift).toContainText('4h · ends');
  page.once('dialog',dialog=>void dialog.accept());await shift.getByRole('button',{name:'Stop shift'}).click();await expect(shift).toContainText('Off shift');
});
