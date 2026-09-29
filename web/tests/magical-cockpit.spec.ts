import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {layoutFaults} from '../tools/layout-faults.mjs';

const taskId='a'.repeat(32),pageId='b'.repeat(16),campaignId='launch-week';
function fiction(){
  const profile={id:'marketing',display_name:'Chip',product_summary:'A marketing employee for solo founders.',audience:'Solo founders',goals:'Start qualified conversations',voice:'Concrete and direct',guardrails:'Draft only',channels:'LinkedIn, Email',claims:'Owner reviews each draft',examples:'Show useful work',version:1,updated_at:1780000000};
  const draft=(id:number,content:string,rationale:string)=>({id,channel:'LinkedIn',destination:'https://example.org/post',content,rationale,rules_url:'UNVERIFIED',status:'pending',revision:1,digest:'c'.repeat(64)});
  const drafts=[draft(7,'A tool that creates posts for your business.','Week: 1\nClaims: Owner reviews each draft.\nMarketing rubric C, first version.'),draft(8,'Meet the marketing employee that brings you finished work—and a clear decision.','A polished version of draft #7.\nWeek: 1\nClaims: Owner reviews each draft.\nMarketing rubric C → A over 2 passes, revised.')];
  const tasks=[{id:taskId,title:'Get the customer quote approved',status:'needs_you',priority:'high',next_action:'Week: 2\nChannel: Email\nGet permission for the quote.',action_state:'user_waiting',blocker:'Customer has not approved the quote.',conversation_key:'fixture',version:1,updated_at:1780000000}];
  const wiki=(id:string,title:string,body:string)=>({id,title,body,version:2,scope:'company',scopeId:'company',kind:'hypothesis',status:'draft',digest:'d'.repeat(64),author:'Marketing employee (shift)',createdAt:'2026-09-26T12:00:00Z',updatedAt:'2026-09-26T13:00:00Z'});
  const docs=[wiki('plan','A concrete introduction','# A concrete introduction\n\nAngle: Show the work before explaining the technology.\nGoal: Start five qualified conversations.\n\nFirst, show a prepared package. Then invite founders to review one example.'),wiki('email','Customer objection answer','Week: 2\nChannel: Email\nClaims: Owner reviews each draft.\n\nYou stay in control. Chip brings you work worth reviewing.\n\n---\n\n_Marketing rubric B, revised._')];
  const campaign={id:campaignId,name:'A concrete introduction',goal:'Start five qualified conversations',status:'planned',starts:'2026-09-28',ends:'2026-10-09',channels:['LinkedIn','Email'],moves:'Qualified conversations',planWikiId:'plan',createdBy:'Owner',createdAt:'2026-09-26T12:00:00Z',updatedAt:'2026-09-26T12:00:00Z'};
  const items=Object.fromEntries(['wiki:plan','wiki:email','draft:8','task:'+taskId,'pagecopy:'+pageId].map(key=>[key,campaignId]));
  const opportunity={id:'prepared-launch',headline:'Lead the launch with finished work',why:'The launch is next week. Founders need one concrete example.',recommendation:'Show the result first; keep the machinery in the supporting document.',prepared:[{key:'draft:8',kind:'draft',title:'The LinkedIn opening'},{key:'wiki:email',kind:'wiki',title:'The customer objection answer'}],evidence:[{title:'Owner-reviewed claim',key:'wiki:plan'},{title:'A fictional customer conversation',url:'https://example.org/evidence'}],decisions:[{id:'review',label:'Review the package',primary:true},{id:'change',label:'Change direction'},{id:'park',label:'Park it'}]};
  const today=Array.from({length:3},(_,index)=>({id:'today-'+index,kind:'document',title:'Today decision '+(index+1),detail:index===0?"Doesn't meet: owner asked for a concrete example.":'A concrete decision ready for review.',target:'wiki:email'}));
  const later=[{id:'later-1',kind:'task',title:'Later customer interview',detail:'After launch review.',target:'task:'+taskId}];
  return {state:{employee:{name:'Chip',model:'scripted fixture',sessionKey:'agent:main:marketing-business-main'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,access:'owner',profile,drafts,tasks,evidence:[],ownerDecisions:[],messages:[],requests:[],runway:null,activity:[]},docs,campaign,ledger:{version:1,campaigns:[campaign],items},today:{opportunity,today,later} as any,decisions:[] as any[],writes:[] as any[]};
}
async function mock(page:Page,data:ReturnType<typeof fiction>,unavailable=false){
  await page.route('**/api/continuity',route=>route.fulfill({status:404,json:{error:'Older host fixture'}}));
  await page.route(/\/api\/today(?:\/.*)?$/,async route=>{if(unavailable)return route.fulfill({status:404,json:{error:'Older host'}});if(route.request().method()==='POST'){const decision=route.request().postDataJSON();data.decisions.push(decision);if(decision.decision!=='review')data.today={...data.today,opportunity:null};}return route.fulfill({json:data.today});});
  await page.route('**/api/marketing/state',route=>route.fulfill({json:data.state}));
  await page.route('**/api/marketing/history*',route=>route.fulfill({json:{items:[],nextCursor:null}}));
  await page.route('**/api/marketing/drafts/*/decision',route=>{const id=Number(/drafts\/(\d+)/.exec(route.request().url())![1]),body=route.request().postDataJSON();data.writes.push(body);data.state.drafts.find(item=>item.id===id)!.status=body.decision;return route.fulfill({json:data.state.drafts.find(item=>item.id===id)});});
  await page.route('**/api/campaigns',route=>route.fulfill({json:data.ledger}));
  await page.route('**/api/campaigns/*/pieces',route=>route.fulfill({json:{campaignId,angle:'Show the work before explaining the technology.',pieces:[{key:'draft:8',week:'2026-09-28',channel:'LinkedIn',grade:'A',claims:[{text:'Owner reviews each draft.',url:'https://example.org/evidence'}],blockers:[]}]}}));
  await page.route('**/api/company-wiki',route=>{if(route.request().method()==='PUT'){const body=route.request().postDataJSON(),doc=data.docs.find(item=>item.id===body.id)!;data.writes.push(body);Object.assign(doc,body,{version:doc.version+1});return route.fulfill({json:doc});}return route.fulfill({json:data.docs});});
  await page.route('**/api/company-wiki/*/history',route=>{const doc=data.docs.find(item=>route.request().url().includes('/'+item.id+'/history'))!;return route.fulfill({json:[{...doc,version:1,body:doc.id==='email'?'You get automated posts.':doc.body},...(doc.version>2?[{...doc,version:2,status:'draft'}]:[]),doc]});});
  await page.route('**/api/page-proposals',route=>route.fulfill({json:{ownSite:'https://example.org',proposals:[{id:pageId,url:'https://example.org',title:'The offer headline',before:'Automate your posts',after:'Week: 1\nChannel: Website\nReview finished work from your marketing employee.',rationale:'Claims: Owner reviews each draft.\nMarketing rubric A, kept.',status:'pending',createdAt:'2026-09-26T12:00:00Z',by:'Employee'}]}}));
  await page.route('**/api/drafts/media',route=>route.fulfill({json:{'8':[{id:'preview-image',name:'Launch illustration',mediaType:'image/png',bytes:68}]}}));
  await page.route('**/api/state',async route=>{const body=await (await route.fetch()).json();body.uploads=[{id:'preview-image',name:'Launch illustration',mediaType:'image/png',bytes:68,sha256:'e'.repeat(64),created:1780000000,version:1,archived:false}];return route.fulfill({json:body});});
  await page.route('**/api/uploads/preview-image/content',route=>route.fulfill({contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j5L8AAAAASUVORK5CYII=','base64')}));
  await page.route('**/api/attention',route=>route.fulfill({json:{items:[]}}));
  await page.route('**/api/rubric',route=>route.fulfill({json:{categories:[],focus:[],entries:[],focusBar:3.5}}));
  await page.route('**/api/feedback',route=>route.fulfill({json:{feedback:[],notebook:{}}}));
  await page.route('**/api/experience',route=>route.fulfill({json:{ledger:{recommendations:[]},feedback:[],revisions:[],notebook:{wikiId:null},outcomes:{ratedUseful:0,ratedNotUseful:0,reportedMinutesSaved:null,timeReports:0,revisionsCompleted:0}}}));
  await page.route('**/api/redrafts',route=>{data.writes.push(route.request().postDataJSON());return route.fulfill({json:{taskId:'fictional',queued:true,message:'Sent back. The next authorized shift will revise it.'}});});
}
async function launch(page:Page,request:APIRequestContext,origin:string,query='pane=work'){
  await page.addInitScript(()=>{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');localStorage.setItem('fe-cockpit-open','yes');});
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim()}});expect(issued.status()).toBe(200);
  await page.goto('/?'+query+'#launch='+(await issued.json()).ticket);await expect(page.locator('.fe-app')).toBeVisible();
}
const screenshots=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/magical-web/screenshots');
async function shot(page:Page,name:string){fs.mkdirSync(screenshots,{recursive:true});await page.screenshot({path:path.join(screenshots,name+'.png'),animations:'disabled'});const faults=await page.evaluate(layoutFaults);fs.writeFileSync(path.join(screenshots,name+'-layout.json'),JSON.stringify(faults,null,2));expect(faults).toEqual([]);}

test('one lead opportunity, three Today items and collapsed Later work, with real decision shapes',async({page,request,baseURL})=>{
  const data=fiction();await mock(page,data);await page.setViewportSize({width:1440,height:1000});await launch(page,request,baseURL!);
  const card=page.getByRole('region',{name:'Prepared opportunity'});await expect(card).toHaveCount(1);await expect(card).toContainText(data.today.opportunity.headline);
  const today=page.getByRole('complementary',{name:'Cockpit'}).getByRole('region',{name:'Today',exact:true});await expect(today.locator('.fe-cockpit-list').first().getByRole('button')).toHaveCount(3);
  await expect(today).toContainText("Doesn't meet: owner asked for a concrete example.");
  await expect(today.getByText('Later customer interview')).toBeHidden();await today.getByText('Later (1)',{exact:true}).click();await expect(today.getByText('Later customer interview')).toBeVisible();
  await card.getByText('Sources (2)').click();await expect(card.getByRole('link',{name:'A fictional customer conversation'})).toHaveAttribute('href','https://example.org/evidence');
  await page.locator('.fe-cockpit-body').evaluate(el=>{el.scrollTop=0;});await shot(page,'opportunity-desktop');
  await card.getByRole('button',{name:'The customer objection answer'}).click();await expect(page.locator('.fe-window')).toContainText('Customer objection answer');
  await card.getByRole('button',{name:'Review the package'}).click();await expect(page.getByRole('region',{name:'Campaign package'})).toBeVisible();expect(data.decisions[0]).toEqual({decision:'review'});
  await page.setViewportSize({width:390,height:844});await page.getByRole('button',{name:/Show cockpit/}).click();await expect(card).toBeVisible();await shot(page,'opportunity-phone');
  await card.getByRole('button',{name:'Change direction'}).click();await expect(card.getByRole('button',{name:'Save direction'})).toBeDisabled();await card.getByLabel('Which direction should the employee take?').fill('Show the customer objection before the launch announcement.');await card.getByRole('button',{name:'Save direction'}).click();await expect(card).toHaveCount(0);expect(data.decisions.at(-1)).toEqual({decision:'change',note:'Show the customer objection before the launch announcement.'});
});

test('continuity puts the hypothesis beside the recorded result and preserves a failed refresh',async({page,request,baseURL})=>{
  await page.clock.install();
  const data=fiction();await mock(page,data);
  const continuity={finished:['Saved the concrete introduction'],changedMind:['After your note, I removed the unsupported customer quote.'],needsYou:1,needsYouTop:['Review the concrete introduction'],next:'Review the owner’s pending decisions, then continue the queue.',since:'2026-09-26T12:00:00Z',bets:[{id:'prepared-launch',title:'Show the useful work',hypothesis:'A concrete example could start more conversations.',measurement:'Qualified conversations after the first week.',status:'ready',result:'Not published yet; no result to read.',uncertainty:'No customer response has been measured.'}]};
  let fail=false;await page.route('**/api/continuity',route=>fail?route.fulfill({status:503,json:{error:'Temporarily unavailable'}}):route.fulfill({json:continuity}));
  await launch(page,request,baseURL!);const standing=page.getByRole('region',{name:'Working on'});await standing.getByText('Bets and results (1)',{exact:true}).click();await expect(standing).toContainText(continuity.bets[0].hypothesis);await expect(standing).toContainText(continuity.bets[0].result);await expect(standing).toContainText(continuity.bets[0].uncertainty);
  await page.setViewportSize({width:390,height:844});await page.getByRole('button',{name:/Show cockpit/}).click();if(!await standing.getByRole('button',{name:'Inspect the prepared work'}).isVisible())await standing.getByText('Bets and results (1)',{exact:true}).click();await standing.scrollIntoViewIfNeeded();await shot(page,'continuity-bet-phone');
  fail=true;await page.clock.fastForward(16000);await expect(standing.getByRole('status')).toContainText('last saved response');await expect(standing).toContainText(continuity.bets[0].result);
  await standing.getByRole('button',{name:'Inspect the prepared work'}).click();await expect(page).toHaveURL(/open=recommendation%3Aprepared-launch/);
});

test('parking failures remain visible and older hosts keep the inbox fallback',async({page,request,baseURL})=>{
  const data=fiction();await mock(page,data);await launch(page,request,baseURL!);const card=page.getByRole('region',{name:'Prepared opportunity'});
  await card.getByRole('button',{name:'Park it'}).click();await card.getByLabel('Reason to park').fill('Wait for quote permission.');
  await page.route('**/api/today/*/decision',route=>route.fulfill({status:409,json:{error:'The opportunity changed; review it again.'}}));await card.getByRole('button',{name:'Park opportunity'}).click();await expect(card.getByRole('alert')).toContainText('opportunity changed');
  await page.unroute('**/api/today/*/decision');await card.getByRole('button',{name:'Park opportunity'}).click();await expect(card).toHaveCount(0);expect(data.decisions.at(-1)).toEqual({decision:'park',note:'Wait for quote permission.'});
  await page.route(/\/api\/today(?:\/.*)?$/,route=>route.fulfill({status:404,json:{error:'Older host'}}));await page.reload();await expect(page.getByRole('region',{name:'Needs your decision'})).toContainText('Draft for LinkedIn');await expect(card).toHaveCount(0);
  data.state.tasks[0].status='ready';await page.route(/\/api\/today(?:\/.*)?$/,route=>route.fulfill({json:{opportunity:null,today:[],later:[]}}));await page.reload();const empty=page.getByRole('region',{name:'Today',exact:true});await expect(empty).toContainText('Nothing needs you right now');await expect(empty).toContainText('next shift you start');
});

test('campaign pieces group by recorded week and channel and stay reviewable in the package',async({page,request,baseURL})=>{
  const data=fiction();await mock(page,data);await page.setViewportSize({width:1440,height:1000});await launch(page,request,baseURL!,'pane=work&open=campaign:'+campaignId);
  await expect(page.getByRole('region',{name:'Campaign angle'})).toContainText('Show the work before explaining');const pack=page.getByRole('region',{name:'Campaign package'});
  await expect(pack.getByRole('region',{name:'Week of 2026-09-28',exact:true})).toContainText('LinkedIn');await expect(pack.getByRole('region',{name:'Week 2',exact:true})).toContainText('Customer has not approved the quote');
  const draft=pack.getByRole('article',{name:'LinkedIn draft #8'});await expect(draft).toContainText('GradeA');await expect(draft).toContainText('Owner reviews each draft');await expect(pack.getByRole('article',{name:'The offer headline'})).toBeVisible();await shot(page,'campaign-package-desktop');
  await expect(pack.getByRole('article',{name:'Launch illustration'})).toBeVisible();
  await expect(draft.getByRole('link',{name:'Source'})).toHaveAttribute('href','https://example.org/evidence');
  await pack.scrollIntoViewIfNeeded();await shot(page,'campaign-pieces-desktop');
  await draft.getByRole('button',{name:'Review',exact:true}).click();const desk=page.getByRole('region',{name:'Piece review'});await desk.getByText('Compare the polished draft with #7').click();await expect(desk.getByRole('region',{name:'Original draft #7'})).toContainText('A tool that creates posts');await expect(desk.getByRole('region',{name:'Polished draft #8'})).toContainText('finished work');
  await desk.locator('.fe-artifact-compare').scrollIntoViewIfNeeded();await shot(page,'polished-comparison-desktop');
  await desk.getByRole('button',{name:'Approve',exact:true}).click();await expect(desk).toContainText('Decision recorded: approved');expect(data.writes.at(-2)||data.writes.at(-1)).toBeTruthy();expect(data.writes.some(item=>item.decision==='approved'&&item.revision===1&&item.digest==='c'.repeat(64))).toBe(true);
  await pack.getByRole('article',{name:'Customer objection answer'}).getByRole('button',{name:'Review',exact:true}).click();await desk.getByLabel('What should change?').fill('Lead with the owner control example.');await desk.getByRole('button',{name:'Send back',exact:true}).click();await expect(desk).toContainText('Sent back.');expect(data.writes.at(-1)).toMatchObject({key:'wiki:email',feedback:'Lead with the owner control example.'});
  await desk.getByRole('button',{name:'Approve document'}).click();await expect(desk).toContainText('Document shared with the employee');expect(data.writes.at(-1)).toMatchObject({id:'email',version:2,status:'active'});
  await pack.getByRole('article',{name:'Get the customer quote approved'}).getByRole('button',{name:'Open',exact:true}).click();await expect(desk.getByRole('combobox',{name:'Status',exact:true})).toHaveValue('needs_you');await expect(desk).toContainText('Customer has not approved the quote');
  await page.setViewportSize({width:390,height:844});await desk.getByRole('button',{name:'Close review'}).click();await page.locator('.fe-window-body').evaluate(el=>{el.scrollTop=0;});await shot(page,'campaign-package-phone');
  await pack.getByRole('article',{name:'LinkedIn draft #8'}).scrollIntoViewIfNeeded();await shot(page,'campaign-pieces-phone');
  await pack.getByRole('article',{name:'Customer objection answer'}).getByRole('button',{name:'Open work'}).click();await page.getByText('Version history (3)',{exact:true}).click();await expect(page.getByRole('region',{name:'Version 1',exact:true})).toContainText('automated posts');await expect(page.getByRole('region',{name:'Version 3',exact:true})).toContainText('work worth reviewing');await page.getByLabel('Earlier version to compare').selectOption('2');await expect(page.getByText('The text is unchanged.',{exact:true})).toBeVisible();
  await page.getByLabel('Earlier version to compare').selectOption('1');await page.locator('.fe-artifact-compare').scrollIntoViewIfNeeded();await shot(page,'document-comparison-phone');
});
