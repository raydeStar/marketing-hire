import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {layoutFaults} from '../tools/layout-faults.mjs';

/** While you were away and one-tap approval on the Today desk: what was noticed comes with its one action, and a waiting draft is
 * approved and scheduled (or copied for the owner to post) without opening it. Synthetic records; nothing reaches a network. */
function fiction(){
  const draft=(id:number,channel:string,destination:string,content:string)=>({id,channel,destination,content,rationale:'Marketing rubric A.',rules_url:'UNVERIFIED',status:'pending',revision:1,digest:String(id).repeat(64).slice(0,64)});
  const drafts=[draft(8,'Mastodon','https://mastodon.example/','Four hours, eight cycles, nothing posted without us.'),draft(9,'LinkedIn','https://www.linkedin.com/feed/','Our FAQ said “Not yet.” That was wrong.')];
  const profile={id:'marketing',display_name:'Chip',product_summary:'A marketing employee for solo founders.',audience:'Solo founders',goals:'Beta sign-ups',voice:'Concrete',guardrails:'Draft only',channels:'LinkedIn, Mastodon',claims:'Owner reviews each draft',examples:'',version:1,updated_at:1780000000};
  const state={employee:{name:'Chip',model:'scripted fixture',sessionKey:'agent:main:marketing-business-main'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,access:'owner',profile,drafts,tasks:[],evidence:[],ownerDecisions:[],messages:[],requests:[],runway:null,activity:[]};
  const today={opportunity:null,today:[{id:'draft:8',kind:'draft',title:'Draft for Mastodon',detail:'Four hours, eight cycles, nothing posted without us.',target:'draft:8'},{id:'draft:9',kind:'draft',title:'Draft for LinkedIn',detail:'Our FAQ said “Not yet.” That was wrong.',target:'draft:9'}],later:[]};
  const away=[
    {id:'listen:question:m1',kind:'question',title:'A question on Bluesky: Anyone using an AI marketing employee? How do you stop it posting without approval?',detail:'Someone asked about “AI marketing employee”. A helpful reply puts you in the conversation.',url:'https://bsky.app/profile/sam.bsky.social/post/3kq',action:{label:'Draft a reply',kind:'assign'}},
    {id:'watch:https://jasper.example/pricing:2026-09-27',kind:'competitor',title:'Price change on jasper.example/pricing',detail:'Pro went from $49 to $69 a seat. The employee drafted a response.',url:'https://jasper.example/pricing',action:{label:'Review the response',kind:'open',key:'draft:9'}},
    {id:'post:p1',kind:'results',title:'Your Bluesky post brought in 12 likes, 3 reposts, 2 replies, 40 visits',detail:'Four hours, eight cycles, nothing posted without us.',url:'https://bsky.app/profile/hirezero/post/1',action:{label:'Write a follow-up',kind:'assign'}}];
  return {state,today,away,assigned:[] as string[],decisions:[] as any[],taps:[] as any[]};
}
async function mock(page:Page,data:ReturnType<typeof fiction>){
  await page.route('**/api/marketing/state',route=>route.fulfill({json:data.state}));
  await page.route(/\/api\/today$/,route=>route.fulfill({json:{...data.today,today:data.today.today.filter(item=>data.state.drafts.find(draft=>'draft:'+draft.id===item.target)?.status==='pending')}}));
  await page.route('**/api/continuity',route=>route.fulfill({status:404,json:{error:'fixture'}}));
  await page.route('**/api/attention',route=>route.fulfill({json:{items:[]}}));
  await page.route(/\/api\/away(?:\/.*)?$/,route=>{
    if(route.request().method()==='POST'){const id=decodeURIComponent(route.request().url().split('/api/away/')[1]);data.assigned.push(id);data.away=data.away.filter(item=>item.id!==id);}
    return route.fulfill({json:data.away});
  });
  await page.route(/\/api\/publishing\/drafts\/\d+\/one-tap$/,route=>{
    const id=Number(/drafts\/(\d+)/.exec(route.request().url())![1]);
    if(route.request().method()==='POST'){data.taps.push({id,...route.request().postDataJSON()});return route.fulfill({json:{id:'pub-'+id,status:id===8?'scheduled':'awaiting_link'}});}
    return route.fulfill({json:id===8?{action:'schedule',label:'Approve & schedule',connectionId:'c1',at:'2026-09-29T15:30:00Z',why:'A common starting point for Mastodon.'}:{action:'copy',label:'Approve & copy',connectionId:null,at:null,why:'No LinkedIn channel is connected.'}});
  });
  await page.route('**/api/marketing/drafts/*/decision',route=>{const id=Number(/drafts\/(\d+)/.exec(route.request().url())![1]),body=route.request().postDataJSON();data.decisions.push({id,...body});data.state.drafts.find(item=>item.id===id)!.status=body.decision;return route.fulfill({json:data.state.drafts.find(item=>item.id===id)});});
}
async function launch(page:Page,request:APIRequestContext,origin:string){
  await page.addInitScript(()=>{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');localStorage.setItem('fe-cockpit-open','yes');});
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:fs.readFileSync(path.join(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim()}});expect(issued.status()).toBe(200);
  await page.goto('/?pane=work#launch='+(await issued.json()).ticket);await expect(page.locator('.fe-app')).toBeVisible();
}
const screenshots=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/magical-web/screenshots');
async function shot(page:Page,name:string){fs.mkdirSync(screenshots,{recursive:true});await page.screenshot({path:path.join(screenshots,name+'.png'),animations:'disabled'});const faults=await page.evaluate(layoutFaults);fs.writeFileSync(path.join(screenshots,name+'-layout.json'),JSON.stringify(faults,null,2));expect(faults).toEqual([]);}

test('what it noticed while you were away comes with one action, and a draft is approved and scheduled in one tap',async({page,request,baseURL,context})=>{
  const data=fiction();await mock(page,data);await context.grantPermissions(['clipboard-read','clipboard-write']);
  await page.setViewportSize({width:1440,height:1000});await launch(page,request,baseURL!);
  const cockpit=page.getByRole('complementary',{name:'Cockpit'});
  const away=cockpit.getByRole('region',{name:'While you were away'});
  await expect(away.locator('.fe-away-item')).toHaveCount(3);
  await expect(away).toContainText('Price change on jasper.example/pricing');
  await expect(away.getByRole('link',{name:'Open the source'}).first()).toHaveAttribute('href','https://bsky.app/profile/sam.bsky.social/post/3kq');
  const today=cockpit.getByRole('region',{name:'Today',exact:true});
  await expect(today.getByRole('button',{name:/Approve & schedule · /})).toBeVisible();
  await expect(today.getByRole('button',{name:'Approve & copy'})).toBeVisible();
  await shot(page,'away-desktop');

  // One tap on the question: the reply becomes the employee's next task and the item leaves the list.
  await away.getByRole('button',{name:'Draft a reply'}).click();
  await expect(away).toContainText('Queued: Chip starts on it right away.');
  await expect(away.locator('.fe-away-item')).toHaveCount(2);
  expect(data.assigned).toEqual(['listen:question:m1']);
  // The competitor item opens the response the employee already drafted.
  await away.getByRole('button',{name:'Review the response'}).click();
  await expect(page.locator('.fe-window')).toContainText('That was wrong');
  await page.keyboard.press('Escape');

  // One tap on the Mastodon draft: approved at its revision and digest, then scheduled; nothing is posted now.
  await today.getByRole('button',{name:/Approve & schedule · /}).click();
  await expect(cockpit).toContainText('Approved and scheduled for');
  expect(data.decisions[0]).toMatchObject({id:8,decision:'approved',revision:1,digest:'8'.repeat(64)});
  expect(data.taps[0]).toMatchObject({id:8,digest:'8'.repeat(64)});

  await page.setViewportSize({width:390,height:844});await page.getByRole('button',{name:/Show cockpit/}).click();
  await expect(away).toBeVisible();await shot(page,'away-phone');
});
