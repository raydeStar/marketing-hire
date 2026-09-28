import {test,expect,type Page} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {layoutFaults} from '../tools/layout-faults.mjs';

// Real host routes and saved fictional work. The runner disables the pump and uses ScriptedShiftRuntime.
async function write(page:Page,route:string,body:unknown,method='POST'){
  return page.evaluate(async({route,body,method})=>{
    const session=await (await fetch('/api/session')).json();
    const result=await fetch(route,{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});
    if(!result.ok)throw new Error(route+' '+result.status+' '+await result.text());
    return result.json();
  },{route,body,method});
}
async function read(page:Page,route:string){return page.evaluate(async route=>{const result=await fetch(route);if(!result.ok)throw new Error(route+' '+result.status);return result.json();},route);}
async function shot(page:Page,name:string){
  const folder=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(folder,{recursive:true});
  await page.screenshot({path:path.join(folder,name+'.png'),animations:'disabled'});
  const faults=await page.evaluate(layoutFaults);fs.writeFileSync(path.join(folder,name+'-layout.json'),JSON.stringify(faults,null,2));expect(faults).toEqual([]);
}

test('first win needs an explicit shift, then real Today, campaign pieces and continuity open the saved work',async({page,request,baseURL})=>{
  test.setTimeout(120000);
  const shiftRequests:unknown[]=[];
  page.on('request',request=>{if(new URL(request.url()).pathname==='/api/shifts'&&request.method()==='POST')shiftRequests.push(request.postDataJSON());});
  // A test browser must never reach an external provider or a real customer site.
  await page.route('**/*',route=>new URL(route.request().url()).origin===baseURL?route.continue():route.abort());
  await page.addInitScript(()=>{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');localStorage.setItem('fe-cockpit-open','yes');localStorage.setItem('thaddeus-theme','light');});
  if(process.env.THADDEUS_TEST_PLOW==='1')await page.goto('/?pane=work');
  else{
    const issued=await request.post(baseURL+'/api/auth/launch',{headers:{Origin:baseURL!},data:{key:fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim()}});expect(issued.status()).toBe(200);
    await page.goto('/?pane=work#launch='+(await issued.json()).ticket);
  }
  await expect(page.locator('.fe-app')).toBeVisible();
  const state=await read(page,'/api/marketing/state');expect(state.taskStoreAvailable).toBe(true);
  await write(page,'/api/marketing/profile',{requestId:crypto.randomUUID(),version:state.profile.version,display_name:'Chip',product_summary:'A marketing employee that prepares useful work.',audience:'Solo founders',goals:'Start five qualified conversations',voice:'Concrete and calm',guardrails:'Draft only; no publishing.',claims:'The owner reviews every draft.',channels:'LinkedIn'},'PUT');
  await page.reload();
  const first=page.getByRole('region',{name:'Your first useful win'}).first();
  await expect(first.getByRole('button',{name:'Prepare my first win'})).toBeVisible();
  expect((await read(page,'/api/shifts')).recent).toHaveLength(0);
  await first.getByRole('button',{name:'Prepare my first win'}).click();
  await expect(first).toContainText('Assignment saved');await expect(first.getByRole('button',{name:'Start a 30-minute shift now'})).toBeVisible();
  const queued=await read(page,'/api/marketing/state');const task=queued.tasks.find((item:any)=>item.title==='Prepare my first useful win');expect(task).toBeTruthy();
  expect((await read(page,'/api/shifts')).recent).toHaveLength(0);expect((await read(page,'/api/experience')).ledger.recommendations).toHaveLength(0);expect(shiftRequests).toHaveLength(0);
  await page.reload();await expect(first.getByRole('button',{name:'Start a 30-minute shift now'})).toBeVisible();
  expect((await read(page,'/api/marketing/state')).tasks.filter((item:any)=>item.title===task.title)).toHaveLength(1);
  const plan=await write(page,'/api/company-wiki',{requestId:crypto.randomUUID(),scope:'company',scopeId:'company',title:'A useful introduction',body:'# A useful introduction\n\nGoal: Start five qualified conversations.\nAngle: Show useful work before explaining the machinery.\n\nPrepare one concrete offer improvement and review it with the owner.',kind:'policy',status:'active'},'PUT');
  const campaign=await write(page,'/api/campaigns/from-plan',{expectedVersion:(await read(page,'/api/campaigns')).version,wikiId:plan.id});
  await write(page,'/api/campaigns/assign',{expectedVersion:campaign.ledger.version,key:'task:'+task.id,campaignId:campaign.campaign.id});
  await first.getByRole('button',{name:'Start a 30-minute shift now'}).click();
  await expect(first).toContainText('Simulated shift');expect(shiftRequests).toHaveLength(1);expect(shiftRequests[0]).toMatchObject({hours:1,durationMinutes:30,cycleMinutes:30,turnBudget:30});
  const shift=(await read(page,'/api/shifts')).current;expect(shift.runtime).toBe('scripted');expect(shift.turnsUsed).toBe(0);expect(new Date(shift.endsAt).getTime()-new Date(shift.startedAt).getTime()).toBe(30*60000);
  await write(page,'/api/shifts/'+shift.id+'/cycle',{});
  await expect(first.getByRole('list',{name:'What the employee is doing'})).toBeVisible({timeout:15000});
  // The first shift's results in one place: what it prepared, graded, and the site's fixes (or how to get them).
  await expect(first.locator('.fe-first-shift-results')).toContainText('What it prepared',{timeout:15000});await expect(first.locator('.fe-first-shift-results button.fe-link').first()).toBeVisible();
  await shot(page,'real-first-shift-desktop');
  const today=await read(page,'/api/today');expect(today.opportunity.prepared.length).toBeGreaterThan(0);expect(today.today.length).toBeLessThanOrEqual(3);
  const pieces=await read(page,'/api/campaigns/'+campaign.campaign.id+'/pieces');expect(pieces.pieces.some((piece:any)=>piece.key===today.opportunity.prepared[0].key&&/^\d{4}-\d{2}-\d{2}$/.test(piece.week)&&piece.grade)).toBe(true);
  const continuity=await read(page,'/api/continuity');expect(continuity.finished.length).toBeGreaterThan(0);expect(continuity.needsYou).toBeGreaterThan(0);
  await page.reload();const card=page.getByRole('region',{name:'Prepared opportunity'});await expect(card).toContainText(today.opportunity.headline);
  await shot(page,'real-opportunity-desktop');
  await page.setViewportSize({width:390,height:844});await page.getByRole('button',{name:/Show cockpit/}).click();await expect(card).toBeVisible();await shot(page,'real-opportunity-phone');
  await page.setViewportSize({width:1440,height:1000});
  await card.getByRole('button',{name:'Review the package'}).click();const pack=page.getByRole('region',{name:'Campaign package'});await expect(pack).toContainText(today.opportunity.headline);await expect(page.getByRole('region',{name:'Campaign angle'})).toContainText(pieces.angle);
  const doc=pack.getByRole('article',{name:today.opportunity.headline,exact:true}).filter({has:page.getByText('Document',{exact:true})});
  await shot(page,'real-campaign-desktop');await doc.scrollIntoViewIfNeeded();await shot(page,'real-campaign-pieces-desktop');
  await page.setViewportSize({width:390,height:844});await page.locator('.fe-window-body').evaluate(el=>{el.scrollTop=0;});await shot(page,'real-campaign-phone');await doc.scrollIntoViewIfNeeded();await shot(page,'real-campaign-pieces-phone');
  await page.setViewportSize({width:1440,height:1000});
  await doc.getByRole('button',{name:'Review piece'}).click();await expect(page.getByRole('region',{name:'Piece review'})).toContainText('Marketing rubric');
  await write(page,'/api/scorecard/import',{requestId:crypto.randomUUID(),csv:'date,Qualified conversations\n2026-09-01,4\n2026-09-02,5',source:'Fictional owner test'});
  const score=await read(page,'/api/scorecard');const exp=await write(page,'/api/scorecard/experiments',{requestId:crypto.randomUUID(),title:'Offer clarity test',hypothesis:'A concrete example starts more qualified conversations.',metric:score.metrics[0].key,startDate:'2026-09-28',reviewDate:'2026-10-12',direction:'up',thresholdPercent:10});
  await write(page,'/api/campaigns/assign',{expectedVersion:(await read(page,'/api/campaigns')).version,key:'exp:'+exp.id,campaignId:campaign.campaign.id});
  await page.reload();const experiment=pack.getByRole('article',{name:'Offer clarity test'});await expect(experiment).toContainText('Experiment');await experiment.getByRole('button',{name:'Review piece'}).click();await expect(page.getByRole('region',{name:'Piece review'})).toContainText('No measured change yet');await experiment.getByRole('button',{name:'Open work'}).click();await expect(page.getByRole('region',{name:'Scorecard',exact:true})).toContainText('Offer clarity test');
  await write(page,'/api/shifts/'+shift.id+'/stop',{});
  await page.goto('/?pane=work');await expect(card).toBeVisible();await card.getByRole('button',{name:'Change direction'}).click();await card.getByLabel('Which direction should the employee take?').fill('Use one concrete offer example instead of an internal plan.');const redirected=await card.locator('h3').first().innerText();await card.getByRole('button',{name:'Save direction'}).click();await expect(card).not.toContainText(redirected);
  const changed=await read(page,'/api/continuity');expect(changed.changedMind.some((text:string)=>text.includes('concrete offer example'))).toBe(true);
  await page.reload();const standing=page.getByRole('region',{name:'Where we stand'});await standing.getByText(/What changed my mind/).click();await expect(standing).toContainText('concrete offer example');await standing.getByText(/Finished \(/).click();await expect(standing).toContainText(today.opportunity.headline);await standing.scrollIntoViewIfNeeded();await shot(page,'real-continuity-desktop');
  fs.writeFileSync(path.join(process.env.THADDEUS_SCREENSHOTS!,'real-contracts.json'),JSON.stringify({today,pieces,continuity,changed,shiftRequest:shiftRequests[0],simulated:true,apiRoutesMocked:false},null,2));
});
