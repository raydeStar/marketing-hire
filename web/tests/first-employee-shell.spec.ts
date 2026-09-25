import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

// The redesigned shell. Marketing ledger responses are fictional; wiki, employee files and pages use the real host.
const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,view='today'){
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/?view=${view}#launch=${(await issued.json()).ticket}`);
}

function fixture(){
  const profile={id:'marketing',display_name:'Marketing agent',product_summary:'',audience:'',voice:'',goals:'',guardrails:'No outbound sends',channels:'',claims:'',examples:'',version:1,updated_at:1780000000};
  const draft={id:7,channel:'LinkedIn',destination:'https://example.org/post',content:'A fictional post about slow mornings.',rationale:'Matches the brief',rules_url:'UNVERIFIED',status:'pending',revision:1,digest:'b'.repeat(64),decided_by:null};
  const task={id:'c'.repeat(32),title:'Pick the holiday offer',status:'needs_you',priority:'high',next_action:'Choose between two offers',action_state:'user_waiting',blocker:'Which offer should lead?',conversation_key:'agent:main:marketing-task-'+'c'.repeat(32),version:1,updated_at:1780000000};
  const messages:{id:string;sessionKey:string;role:string;content:string;createdAt:number}[]=[];
  const chats:string[]=[],decisions:unknown[]=[];
  return {profile,draft,task,messages,chats,decisions};
}

async function mockMarketing(page:Page,data:ReturnType<typeof fixture>,reply:(content:string)=>string){
  await page.route('**/api/marketing/**',async route=>{
    const url=new URL(route.request().url()),method=route.request().method();
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:data.profile.display_name,model:'fixture',sessionKey:'agent:main:marketing-business-main'},
      connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,businessBriefEvidenceEnabled:true,chatBlockedReason:null,profile:data.profile,
      drafts:[data.draft],evidence:[],ownerDecisions:[],tasks:[data.task],activity:[],messages:data.messages,requests:[],runway:null}});
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    if(url.pathname==='/api/marketing/usage')return route.fulfill({json:{chat:[],autonomous:null,autonomousAvailable:false,fixture:true,updatedAt:new Date().toISOString()}});
    if(url.pathname==='/api/marketing/allowance')return route.fulfill({json:{configured:false,latest:null,samples:[],lastAttemptAt:null,error:null,stale:false,pollSeconds:300}});
    if(url.pathname==='/api/marketing/profile'&&method==='PUT'){const change=route.request().postDataJSON();Object.assign(data.profile,change,{version:data.profile.version+1});return route.fulfill({json:data.profile});}
    if(url.pathname==='/api/marketing/chat'){
      const body=route.request().postDataJSON();data.chats.push(body.content);
      const answer=reply(body.content),now=Date.now()/1000;
      data.messages.push({id:body.requestId+':user',sessionKey:'agent:main:marketing-business-main',role:'user',content:body.content,createdAt:now},{id:body.requestId,sessionKey:'agent:main:marketing-business-main',role:'assistant',content:answer,createdAt:now+1});
      return route.fulfill({json:{requestId:body.requestId,status:'succeeded',reply:answer}});
    }
    if(url.pathname==='/api/marketing/drafts/7/decision'){data.decisions.push(route.request().postDataJSON());Object.assign(data.draft,{status:'approved'});return route.fulfill({json:data.draft});}
    return route.fulfill({status:404,json:{error:'Unexpected marketing request '+url.pathname}});
  });
}

test('onboarding drafts a brief from links, Today and Inbox follow it, and nothing overflows on a phone',async({page,request,baseURL})=>{
  test.setTimeout(90000);
  const data=fixture(),stamp=Date.now().toString(36);
  await mockMarketing(page,data,content=>content.startsWith('Onboarding')
    ?'Here is what I learned.\n```json\n{"display_name":"Juno","product_summary":"Small-batch coffee subscriptions.","audience":"Home brewers (assumption)","goals":"Grow subscriptions before the holidays","voice":"Warm and curious","guardrails":"Never shame anyone’s coffee","ethos":"Good coffee should be simple '+stamp+'"}\n```'
    :content.startsWith('Morning meeting')?'**Today:** approve the LinkedIn draft, then pick the holiday offer.':'Noted.');
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.removeItem('fe-onboarding-dismissed');localStorage.setItem('thaddeus-theme','light');}catch{}});
  await launch(page,request,baseURL!);

  const onboarding=page.getByRole('dialog',{name:'Onboarding'});
  await expect(onboarding.getByRole('heading',{name:/up to speed/})).toBeVisible();
  await onboarding.getByRole('button',{name:/Learn from my website/}).click();
  await onboarding.getByLabel('Links').fill('https://example.org\nhttps://example.org/about');
  await onboarding.getByRole('button',{name:'Draft my brief'}).click();
  await expect(onboarding.getByRole('heading',{name:'Here’s what Marketing agent learned.'})).toBeVisible();
  expect(data.chats[0]).toContain('https://example.org/about');
  await expect(onboarding.getByLabel(/What you sell/)).toHaveValue('Small-batch coffee subscriptions.');
  await expect(onboarding.getByLabel('Ethos',{exact:true})).toHaveValue(/Good coffee should be simple/);
  await onboarding.getByRole('button',{name:'Save brief'}).click();
  await expect(onboarding.getByRole('heading',{name:'Juno is ready to work.'})).toBeVisible();
  const wiki=await page.evaluate(async()=>(await fetch('/api/company-wiki')).json());
  expect(wiki.some((item:{title:string;body:string})=>item.title==='Company ethos'&&item.body.includes(stamp))).toBe(true);
  await onboarding.getByRole('button',{name:'Go to Today'}).click();
  await expect(onboarding).toHaveCount(0);

  await expect(page.getByRole('heading',{name:/^Good (morning|afternoon|evening)/})).toBeVisible();
  await expect(page.getByRole('button',{name:/Get Juno up to speed/})).toHaveCount(0);
  await expect(page.getByRole('navigation',{name:'Main views'}).getByRole('button',{name:/Inbox/})).toContainText('2');
  await page.getByRole('button',{name:'Start the meeting'}).click();
  await expect(page.getByText('approve the LinkedIn draft',{exact:false})).toBeVisible();
  expect(data.chats.at(-1)).toMatch(/^Morning meeting/);
  await page.getByRole('button',{name:'Today',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Today’s brief'})).toBeVisible();

  await page.getByRole('button',{name:/Inbox/}).click();
  const draft=page.getByRole('article',{name:'Draft 7'});
  await expect(draft).toContainText('slow mornings');
  await draft.getByRole('button',{name:'Approve'}).click();
  await expect.poll(()=>data.decisions.length).toBe(1);
  expect(data.decisions[0]).toMatchObject({decision:'approved',revision:1,digest:'b'.repeat(64)});
  await page.getByRole('button',{name:/Pick the holiday offer/}).click();
  await expect(page.getByRole('dialog',{name:'Pick the holiday offer'})).toContainText('Which offer should lead?');
  await page.keyboard.press('Escape');

  await page.setViewportSize({width:390,height:844});
  for(const view of ['today','chat','inbox','assets','wiki','team','history','settings']){
    await page.goto('/?view='+view);
    await expect(page.locator('.fe-main')).toBeVisible();
    expect(await page.evaluate(()=>document.documentElement.scrollWidth),view).toBeLessThanOrEqual(390);
  }
  await page.getByRole('button',{name:'Open menu'}).click();
  await expect(page.getByRole('complementary',{name:'Main navigation'}).getByRole('button',{name:'Campaigns'})).toBeInViewport();
});

test('team files, wiki playbooks and campaign pages persist on the host',async({page,request,baseURL})=>{
  test.setTimeout(90000);
  const data=fixture(),stamp=Date.now().toString(36);
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'team');

  await page.locator('.fe-member-tile').filter({hasText:'Marketing agent'}).click();
  await page.getByRole('button',{name:'Add a file'}).click();
  await page.getByRole('dialog',{name:'Add a file'}).getByLabel('Or name your own').fill('PLAYBOOK-'+stamp);
  await page.getByRole('dialog',{name:'Add a file'}).getByRole('button',{name:'Create'}).click();
  const editor=page.getByLabel(`Contents of PLAYBOOK-${stamp}.md`);
  await editor.fill('# Playbook\nAlways cite sources. '+stamp);
  await page.getByRole('button',{name:'Save file'}).click();
  await expect(page.locator('.fe-file-editor').getByText('Version 1',{exact:true})).toBeVisible();
  await page.reload();
  await page.locator('.fe-member-tile').filter({hasText:'Marketing agent'}).click();
  await page.getByRole('button',{name:new RegExp(`PLAYBOOK-${stamp}`)}).click();
  await expect(page.getByLabel(`Contents of PLAYBOOK-${stamp}.md`)).toHaveValue(new RegExp(stamp));

  await page.getByRole('button',{name:'Wiki',exact:true}).click();
  await page.getByRole('button',{name:'New page'}).click();
  await page.getByRole('dialog',{name:'New wiki page'}).getByRole('button',{name:/Launch checklist/}).click();
  await page.getByLabel('Title').fill('Launch checklist '+stamp);
  await page.getByRole('button',{name:'Save page'}).click();
  await expect(page.getByRole('heading',{name:'Launch checklist '+stamp})).toBeVisible();
  await expect(page.locator('.fe-reader')).toContainText('Rollback criteria');

  await page.getByRole('button',{name:'Assets',exact:true}).click();
  await page.getByRole('button',{name:'New page'}).click();
  await page.getByRole('dialog',{name:'Create'}).getByRole('button',{name:/Landing page/}).click();
  await page.getByLabel('Name').fill('Landing '+stamp);
  await page.getByRole('button',{name:'Create',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Landing '+stamp})).toBeVisible();
  await expect(page.frameLocator('.fe-page-frame iframe').getByRole('heading',{name:/what changes/})).toBeVisible();
  await page.getByRole('button',{name:'Edit'}).click();
  await page.getByLabel('HTML').fill('<h1>Edited headline '+stamp+'</h1>');
  await page.getByRole('button',{name:'Save changes'}).click();
  await page.getByRole('button',{name:'Preview'}).click();
  await expect(page.frameLocator('.fe-page-frame iframe').getByRole('heading',{name:'Edited headline '+stamp})).toBeVisible();
  await page.getByRole('button',{name:'Ask Marketing'}).click();
  await expect(page.getByLabel('Message to marketing employee')).toHaveValue(/Edited headline/);
});

test('onboarding can interview the owner, then drafts the brief from the conversation',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  const data=fixture();
  await mockMarketing(page,data,content=>content.startsWith('Onboarding: let')?'Great. First question: what do you sell?'
    :content.startsWith('Thanks. Now turn')?'```json\n{"product_summary":"Hand-thrown mugs","goals":"First 100 customers","voice":"Plain and kind"}\n```':'Got it. Who is it for?');
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{try{localStorage.removeItem('fe-onboarding-dismissed');}catch{}});
  await launch(page,request,baseURL!);
  const onboarding=page.getByRole('dialog',{name:'Onboarding'});
  await onboarding.getByRole('button',{name:/Talk it through/}).click();
  await expect(onboarding.getByText('First question: what do you sell?')).toBeVisible();
  expect(data.chats[0]).toMatch(/^Onboarding: let's get you up to speed/);
  await onboarding.getByLabel('Message to marketing employee').fill('Hand-thrown mugs from my studio.');
  await onboarding.getByLabel('Message to marketing employee').press('Enter');
  await expect(onboarding.getByText('Who is it for?')).toBeVisible();
  await onboarding.getByRole('button',{name:'Draft my brief'}).click();
  await expect(onboarding.getByLabel(/What you sell/)).toHaveValue('Hand-thrown mugs');
  await expect(onboarding.getByLabel(/What matters now/)).toHaveValue('First 100 customers');
  await onboarding.getByRole('button',{name:'Back'}).click();
  await expect(onboarding.getByText('Who is it for?')).toBeVisible();
});
