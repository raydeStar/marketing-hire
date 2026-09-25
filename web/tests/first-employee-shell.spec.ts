import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

// The cockpit shell. Marketing ledger responses are fictional; the Library (wiki, pages, uploads), employee files and publishing use the real host.
const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  for(let round=0;;round++){
    // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
    let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
    for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
    expect(issued.status()).toBe(200);
    await page.goto(`/${query?'?'+query:''}#launch=${(await issued.json()).ticket}`);
    // Sign-ins are rate limited per address; if the claim was shed, wait out the window and launch again.
    const signIn=page.getByRole('heading',{name:'Welcome back'});
    await expect(page.locator('.fe-app').or(signIn)).toBeVisible({timeout:30000});
    if(!await signIn.isVisible()||round>=2)return;
    await page.waitForTimeout(20000);
  }
}

function fixture(){
  const profile={id:'marketing',display_name:'Marketing agent',product_summary:'',audience:'',voice:'',goals:'',guardrails:'No outbound sends',channels:'',claims:'',examples:'',version:1,updated_at:1780000000};
  const draft={id:7,channel:'LinkedIn',destination:'https://example.org/post',content:'A fictional post about slow mornings.',rationale:'Matches the brief',rules_url:'UNVERIFIED',status:'pending',revision:1,digest:'b'.repeat(64),decided_by:null};
  const task={id:'c'.repeat(32),title:'Pick the holiday offer',status:'needs_you',priority:'high',next_action:'Choose between two offers',action_state:'user_waiting',blocker:'Which offer should lead?',conversation_key:'agent:main:marketing-task-'+'c'.repeat(32),version:1,updated_at:1780000000};
  const messages:{id:string;sessionKey:string;role:string;content:string;createdAt:number}[]=[];
  const chats:string[]=[],decisions:unknown[]=[],tasks:typeof task[]=[task];
  const runway:any=null;
  return {profile,draft,task,messages,chats,decisions,tasks,runway};
}

async function mockMarketing(page:Page,data:ReturnType<typeof fixture>,reply:(content:string)=>string){
  await page.route('**/api/marketing/**',async route=>{
    const url=new URL(route.request().url()),method=route.request().method();
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:data.profile.display_name,model:'fixture',sessionKey:'agent:main:marketing-business-main'},
      connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,access:'owner',businessBriefEvidenceEnabled:true,chatBlockedReason:null,profile:data.profile,
      drafts:[data.draft],evidence:[],ownerDecisions:[],tasks:data.tasks,activity:[],messages:data.messages,requests:[],runway:data.runway}});
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
    const taskUpdate=/^\/api\/marketing\/tasks\/([a-f0-9]{32})$/.exec(url.pathname);
    if(taskUpdate&&method==='PUT'){const body=route.request().postDataJSON();const found=data.tasks.find(item=>item.id===taskUpdate[1])!;expect(body.version).toBe(found.version);Object.assign(found,{status:body.status??found.status,version:found.version+1});return route.fulfill({json:found});}
    if(url.pathname==='/api/marketing/tasks'&&method==='POST'){const body=route.request().postDataJSON();const created={...data.task,id:'d'.repeat(32),title:body.title,status:'ready',blocker:null as unknown as string,next_action:body.next_action,conversation_key:'k',version:1};data.tasks.push(created);return route.fulfill({json:created});}
    if(url.pathname==='/api/marketing/drafts/7/decision'){data.decisions.push(route.request().postDataJSON());Object.assign(data.draft,{status:'approved'});return route.fulfill({json:data.draft});}
    return route.fulfill({status:404,json:{error:'Unexpected marketing request '+url.pathname}});
  });
}

/** A real host write from the signed-in page, with its CSRF token. */
async function hostWrite<T=any>(page:Page,method:string,route:string,body:unknown):Promise<T>{
  return page.evaluate(async({method,route,body})=>{
    const session=await (await fetch('/api/session')).json() as {csrf:string};
    const response=await fetch(route,{method,headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify(body)});
    if(!response.ok)throw new Error(route+' '+response.status+' '+await response.text());
    return response.json();
  },{method,route,body});
}

const rail=(page:Page)=>page.getByRole('complementary',{name:'Main navigation'});
const cockpit=(page:Page)=>page.getByRole('complementary',{name:'Cockpit'});
const libraryNew=async(page:Page,item:'Document'|'Page or app'|'Upload files'|'Folder')=>{
  await page.getByRole('main').getByRole('button',{name:'New',exact:true}).click();
  await page.getByRole('menuitem',{name:item}).click();
};

test('onboarding drafts a brief from links, the cockpit follows it, and nothing overflows on a phone',async({page,request,baseURL})=>{
  test.setTimeout(120000);
  const data=fixture(),stamp=Date.now().toString(36);
  await mockMarketing(page,data,content=>content.startsWith('Onboarding')
    ?'Here is what I learned.\n```json\n{"display_name":"Juno","product_summary":"Small-batch coffee subscriptions.","audience":"Home brewers (assumption)","goals":"Grow subscriptions before the holidays","voice":"Warm and curious","guardrails":"Never shame anyone’s coffee","ethos":"Good coffee should be simple '+stamp+'"}\n```'
    :content.startsWith('Morning meeting')?'**Today:** approve the LinkedIn draft, then pick the holiday offer.':'Noted.');
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.removeItem('fe-onboarding-dismissed');localStorage.removeItem('fe-cockpit-open');localStorage.setItem('thaddeus-theme','light');}catch{}});
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
  const files=await page.evaluate(async()=>(await fetch('/api/organization/agents/marketing-main/files')).json());
  expect(files.map((file:{name:string})=>file.name)).toContain('SOUL.md');
  await onboarding.getByRole('button',{name:'Go to chat'}).click();
  await expect(onboarding).toHaveCount(0);

  // Chat is home, under the employee's new name. Onboarding ran in the main conversation, so its turns are there.
  await expect(page.getByRole('region',{name:'Conversation with Juno'})).toContainText('Here is what I learned.');
  await expect(page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Chat'})).toHaveAttribute('aria-pressed','true');
  await expect(page.getByText('Juno · Online')).toBeVisible();

  // The pinned cockpit lists what needs the owner; with it visible the rail carries no count.
  const decide=cockpit(page).getByRole('region',{name:'Needs your decision'});
  await expect(decide.getByRole('button')).toHaveCount(2);
  await expect(decide).toContainText('Draft for LinkedIn');
  await expect(decide).toContainText('Pick the holiday offer');
  await expect(rail(page).getByRole('button',{name:'Chat',exact:true})).toBeVisible();
  await expect(page).toHaveTitle('(2) Chat · First Employee');
  await cockpit(page).getByRole('button',{name:'Start',exact:true}).click();
  await expect(page.getByRole('region',{name:'Conversation with Juno'}).getByText('approve the LinkedIn draft',{exact:false})).toBeVisible();
  expect(data.chats.at(-1)).toMatch(/^Morning meeting/);
  await expect(cockpit(page).getByRole('button',{name:'Run again',exact:true})).toBeVisible();

  // A draft opens beside the chat and is decided there.
  await decide.getByRole('button',{name:/Draft for LinkedIn/}).click();
  await expect(page).toHaveURL(/open=draft%3A7/);
  await expect(page.getByRole('region',{name:'Conversation with Juno'})).toBeVisible();
  const draft=page.getByRole('region',{name:'LinkedIn draft'}).getByRole('article',{name:'Draft 7'});
  await expect(draft).toContainText('slow mornings');
  await draft.getByRole('button',{name:'Approve'}).click();
  await expect.poll(()=>data.decisions.length).toBe(1);
  expect(data.decisions[0]).toMatchObject({decision:'approved',revision:1,digest:'b'.repeat(64)});
  await expect(decide.getByRole('button')).toHaveCount(1);
  await decide.getByRole('button',{name:/Pick the holiday offer/}).click();
  const task=page.getByRole('region',{name:'Pick the holiday offer'});
  await expect(task).toContainText('Which offer should lead?');
  await expect(task.getByRole('combobox',{name:'Status'})).toHaveValue('needs_you');
  await task.getByRole('button',{name:'Close',exact:true}).click();
  await expect(task).toHaveCount(0);
  await cockpit(page).getByRole('button',{name:'Hide cockpit'}).click();
  await expect(cockpit(page)).toHaveCount(0);
  await expect(rail(page).getByRole('button',{name:'Chat, 1 needs a decision'})).toBeVisible();
  await page.getByRole('button',{name:'Show cockpit, 1 needs a decision'}).click();
  await expect(cockpit(page)).toBeVisible();

  await page.setViewportSize({width:390,height:844});
  for(const query of ['','pane=work','view=library','view=team','view=settings','open=task:'+data.task.id,'pane=work&open=campaign:current']){
    await page.goto('/'+(query?'?'+query:''));
    await expect(page.locator('.fe-main')).toBeVisible();
    await expect(page.locator('.fe-loading')).toHaveCount(0);
    expect(await page.evaluate(()=>document.documentElement.scrollWidth),query||'home').toBeLessThanOrEqual(390);
  }
  // On a phone the rail is a bottom bar and the cockpit is a sheet.
  await expect(rail(page).getByRole('button',{name:'Library'})).toBeInViewport();
  await expect(rail(page).getByRole('button',{name:'Settings and account'})).toBeInViewport();
  await page.getByRole('button',{name:/^Show cockpit/}).click();
  const sheet=page.getByRole('dialog',{name:'Cockpit'});
  await expect(sheet.getByRole('region',{name:'Needs your decision'})).toBeInViewport();
  await sheet.getByRole('button',{name:'Hide cockpit'}).click();
  await expect(sheet).toHaveCount(0);
});

test('employee files, Library documents and campaign pages persist on the host',async({page,request,baseURL})=>{
  test.setTimeout(90000);
  const data=fixture(),stamp=Date.now().toString(36);
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1440,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'view=team');

  const openEmployee=async()=>{
    await page.getByRole('navigation',{name:'Team sections'}).getByRole('button',{name:'AI employees'}).click();
    await page.getByRole('region',{name:'AI employees'}).getByRole('button',{name:/Marketing agent/}).click();
  };
  await openEmployee();
  await expect(page.getByRole('navigation',{name:'Employee views'}).getByRole('button',{name:'Instructions'})).toHaveAttribute('aria-pressed','true');
  await page.getByRole('button',{name:'Add a file'}).click();
  await page.getByRole('dialog',{name:'Add a file'}).getByLabel('Or name your own').fill('PLAYBOOK-'+stamp);
  await page.getByRole('dialog',{name:'Add a file'}).getByRole('button',{name:'Create'}).click();
  const editor=page.getByLabel(`Contents of PLAYBOOK-${stamp}.md`);
  await editor.fill('# Playbook\nAlways cite sources. '+stamp);
  await page.getByRole('button',{name:'Save file'}).click();
  await expect(page.locator('.fe-file-editor').getByText('Version 1',{exact:true})).toBeVisible();
  await page.reload();
  await openEmployee();
  await page.getByRole('button',{name:new RegExp(`PLAYBOOK-${stamp}`)}).click();
  await expect(page.getByLabel(`Contents of PLAYBOOK-${stamp}.md`)).toHaveValue(new RegExp(stamp));

  // Documents (the former wiki) live in the Library and default to Company.
  await rail(page).getByRole('button',{name:'Library'}).click();
  await libraryNew(page,'Document');
  await page.getByRole('dialog',{name:'New document'}).getByRole('button',{name:/Launch checklist/}).click();
  const form=page.getByRole('form',{name:'New document'});
  await form.getByLabel('Title').fill('Launch checklist '+stamp);
  await form.getByRole('button',{name:'Create document'}).click();
  const doc=page.getByRole('region',{name:'Launch checklist '+stamp});
  await expect(doc).toContainText('Rollback criteria');
  await expect(doc.locator('.fe-window-title small')).toHaveText(/ · Company$/);
  await expect(page).toHaveURL(/view=library&open=wiki%3A/);

  // Pages default to Pages & apps.
  await doc.getByRole('button',{name:'Close',exact:true}).click();
  await libraryNew(page,'Page or app');
  await page.getByRole('dialog',{name:'New page or app'}).getByRole('button',{name:/Landing page/}).click();
  await page.getByRole('dialog',{name:'New landing page'}).getByLabel('Name').fill('Landing '+stamp);
  await page.getByRole('dialog',{name:'New landing page'}).getByRole('button',{name:'Create',exact:true}).click();
  const landing=page.getByRole('region',{name:'Landing '+stamp});
  await expect(landing).toContainText('Pages & apps');
  await expect(landing.frameLocator('.fe-page-frame iframe').getByRole('heading',{name:/what changes/})).toBeVisible();
  await landing.getByRole('button',{name:'Edit',exact:true}).click();
  await landing.getByLabel('HTML').fill('<h1>Edited headline '+stamp+'</h1>');
  await landing.getByRole('button',{name:'Save changes'}).click();
  await landing.getByRole('button',{name:'Preview',exact:true}).click();
  await expect(landing.frameLocator('.fe-page-frame iframe').getByRole('heading',{name:'Edited headline '+stamp})).toBeVisible();
  await landing.getByRole('button',{name:'Ask for feedback'}).click();
  await expect(page.getByLabel('Message to marketing employee')).toHaveValue(/Edited headline/);
  await rail(page).getByRole('button',{name:'Library'}).click();
  await page.getByRole('tree',{name:'Folders'}).getByRole('button',{name:/Pages & apps/}).click();
  await expect(page.getByRole('row',{name:new RegExp('Landing '+stamp)})).toBeVisible();
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
  // The interview is part of the main conversation, so it stays readable after onboarding closes.
  await onboarding.getByRole('button',{name:'Close onboarding'}).click();
  await expect(onboarding).toHaveCount(0);
  await expect(page.getByRole('region',{name:'Conversation with Marketing agent'})).toContainText('Who is it for?');
  expect(await page.evaluate(()=>localStorage.getItem('fe-onboarding-dismissed'))).toBe('yes');
});

test('Ctrl+K searches the Library and tasks, and hands anything else to Marketing',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  const data=fixture(),stamp=Date.now().toString(36);
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!);
  await expect(page).toHaveTitle('(2) Chat · First Employee');
  const doc=await hostWrite<{id:string}>(page,'PUT','/api/company-wiki',{requestId:crypto.randomUUID(),id:null,version:0,scope:'company',scopeId:'company',
    title:'Audience interviews '+stamp,body:'# Audience interviews\nWhat buyers told us about slow mornings.',kind:'fact',status:'active'});
  await page.reload();
  await expect(page.getByRole('heading',{name:'What should Marketing agent work on?'})).toBeVisible();
  await page.keyboard.press('Control+k');
  const palette=page.getByRole('dialog',{name:'Search'});
  // "customer" also matches "audience".
  await palette.getByLabel('Search',{exact:true}).fill('customer interviews '+stamp);
  await expect(palette.getByRole('option').first()).toContainText('Audience interviews '+stamp);
  await palette.getByLabel('Search',{exact:true}).press('Enter');
  await expect(page).toHaveURL(new RegExp(`view=library&open=wiki%3A${doc.id}`));
  await expect(page.getByRole('region',{name:'Audience interviews '+stamp})).toContainText('What buyers told us');
  await page.keyboard.press('Control+k');
  await palette.getByLabel('Search',{exact:true}).fill('holiday offer');
  await palette.getByRole('option',{name:/Pick the holiday offer/}).click();
  await expect(page.getByRole('region',{name:'Pick the holiday offer'})).toContainText('Which offer should lead?');
  await rail(page).getByRole('button',{name:'Search'}).click();
  await palette.getByLabel('Search',{exact:true}).fill('Summarize our week');
  await palette.getByRole('option',{name:/Ask Marketing agent/}).click();
  await expect(page.getByLabel('Message to marketing employee')).toHaveValue('Summarize our week');
});

test('a reply can be kept as a Library document or turned into a task',async({page,request,baseURL})=>{
  const data=fixture(),stamp=Date.now().toString(36);
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  await mockMarketing(page,data,()=>`Focus ${stamp}

Ship the holiday landing page first.`);
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!);
  await page.getByLabel('Message to marketing employee').fill('What first?');
  await page.getByLabel('Message to marketing employee').press('Enter');
  await expect(page.getByText('Ship the holiday landing page first.')).toBeVisible();
  await page.getByRole('button',{name:'Save to Library'}).click();
  await expect(page.getByRole('button',{name:'Saved to the Library'})).toBeVisible();
  const wiki=await page.evaluate(async()=>(await fetch('/api/company-wiki')).json());
  expect(wiki.some((item:{title:string;status:string})=>item.title===`Focus ${stamp}`&&item.status==='draft')).toBe(true);
  await page.getByRole('button',{name:'Make a task'}).click();
  await expect(page.getByRole('button',{name:'Task created'})).toBeVisible();
  expect(data.tasks.at(-1)).toMatchObject({title:`Focus ${stamp}`});
  await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Work'}).click();
  await expect(page.getByRole('region',{name:'Assigned',exact:true}).getByRole('button',{name:new RegExp(`Focus ${stamp}`)})).toBeVisible();
  // The saved reply is in the Library right away, filed under Company.
  await rail(page).getByRole('button',{name:'Library'}).click();
  await page.getByLabel('Search the library').fill('Focus '+stamp);
  await expect(page.getByRole('row',{name:new RegExp(`Focus ${stamp}`)})).toContainText('Company');
});

test('a campaign page publishes an exact version to a public link and can be taken down',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  const data=fixture(),stamp=Date.now().toString(36);
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'view=library');
  await libraryNew(page,'Page or app');
  await page.getByRole('dialog',{name:'New page or app'}).getByRole('button',{name:/Launch announcement/}).click();
  await page.getByRole('dialog',{name:'New launch announcement'}).getByLabel('Name').fill('Holiday '+stamp);
  await page.getByRole('dialog',{name:'New launch announcement'}).getByRole('button',{name:'Create',exact:true}).click();
  const window=page.getByRole('region',{name:'Holiday '+stamp});
  await window.getByRole('button',{name:'Publish',exact:true}).click();
  const dialog=page.getByRole('dialog',{name:'Publish page'});
  await expect(dialog.getByLabel('Page address')).toHaveValue('holiday-'+stamp);
  await dialog.getByRole('button',{name:'Publish',exact:true}).click();
  const live=page.getByRole('dialog',{name:'Published page'});
  await expect(live.getByText('Live at')).toBeVisible();
  const url=baseURL+'/p/holiday-'+stamp;
  const first=await request.get(url);
  expect(first.status()).toBe(200);
  expect(first.headers()['content-security-policy']).toMatch(/^sandbox allow-scripts/);
  expect(await first.text()).toContain("We're launching something new");
  await live.getByRole('button',{name:'Close dialog'}).click();
  await expect(window).toContainText('Page · Published');

  await window.getByRole('button',{name:'Edit',exact:true}).click();
  await window.getByLabel('HTML').fill('<h1>Second draft '+stamp+'</h1>');
  await window.getByRole('button',{name:'Save changes'}).click();
  await expect(window.getByText('Changed since publishing')).toBeVisible();
  expect(await (await request.get(url)).text()).not.toContain('Second draft');

  await window.getByRole('button',{name:'Publishing settings'}).click();
  await expect(live.getByText(/changed after it was published/)).toBeVisible();
  page.once('dialog',confirm=>void confirm.accept());
  await live.getByRole('button',{name:'Unpublish'}).click();
  await expect(page.getByRole('dialog',{name:'Publish page'})).toBeVisible();
  expect((await request.get(url)).status()).toBe(404);
});

test('an uploaded image files into Media, goes into a page and ships with the published copy',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  const data=fixture(),stamp=Date.now().toString(36);
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'view=library');
  const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=','base64');
  const chooser=page.waitForEvent('filechooser');
  await libraryNew(page,'Upload files');
  await (await chooser).setFiles({name:`hero-${stamp}.png`,mimeType:'image/png',buffer:png});
  const row=page.getByRole('row',{name:new RegExp(`hero-${stamp}\\.png`)});
  await expect(row).toContainText('Image');
  await expect(row).toContainText('Media');
  await row.click();
  await expect(page.getByRole('region',{name:`hero-${stamp}.png`}).getByRole('img',{name:`hero-${stamp}.png`})).toBeVisible();
  await page.getByRole('region',{name:`hero-${stamp}.png`}).getByRole('button',{name:'Close',exact:true}).click();

  await libraryNew(page,'Page or app');
  await page.getByRole('dialog',{name:'New page or app'}).getByRole('button',{name:/Blank page/}).click();
  await page.getByRole('dialog',{name:'New blank page'}).getByLabel('Name').fill('Hero '+stamp);
  await page.getByRole('dialog',{name:'New blank page'}).getByRole('button',{name:'Create',exact:true}).click();
  const window=page.getByRole('region',{name:'Hero '+stamp});
  await window.getByRole('button',{name:'Edit',exact:true}).click();
  await window.getByLabel('HTML').fill('<h1>With a hero</h1>\n');
  await window.getByRole('button',{name:'Insert image'}).click();
  await page.getByRole('dialog',{name:'Insert an image'}).getByRole('button',{name:new RegExp(`hero-${stamp}.png`)}).click();
  await expect(window.getByLabel('HTML')).toHaveValue(/<img src="media:[a-f0-9]{32}" alt="hero-/);
  await window.getByRole('button',{name:'Save changes'}).click();
  await window.getByRole('button',{name:'Preview',exact:true}).click();
  await expect(window.frameLocator('.fe-page-frame iframe').getByRole('img')).toBeVisible();
  await window.getByRole('button',{name:'Publish',exact:true}).click();
  await page.getByRole('dialog',{name:'Publish page'}).getByRole('button',{name:'Publish',exact:true}).click();
  await expect(page.getByRole('dialog',{name:'Published page'}).getByText('Live at')).toBeVisible();
  const html=await (await request.get(baseURL+'/p/hero-'+stamp)).text();
  expect(html).toContain('src="data:image/png;base64,');
});

test('Marketing’s draft angles become a social mockup page in one step',async({page,request,baseURL})=>{
  const data=fixture(),stamp=Date.now().toString(36);
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  data.runway={project:{id:'f'.repeat(32),goal:`Holiday push ${stamp}. More detail.`,status:'done',version:3,run_count:1,max_runs:1,token_limit:1,token_used:0,token_reserved:0,max_active_seconds:60,created_at:1780000000},
    steps:[],reviews:[],inputs:[],executions:[],artifacts:[{id:'a'.repeat(32),kind:'post_angles',step_id:'s',digest:'d'.repeat(64),source_urls:'[]',created_at:1780000000,
      content:JSON.stringify({angles:[{title:'Slow mornings',hook:`Your coffee should wait for you ${stamp}`,why:'Calm beats rush',claimLimit:'No health claims',sourceUrl:'https://example.org'}]})}]};
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'view=library');
  // Campaign deliverables are filed under Campaigns.
  await page.getByRole('tree',{name:'Folders'}).getByRole('button',{name:/^Campaigns/}).click();
  await page.getByRole('row',{name:/Draft post angles/}).click();
  await page.getByRole('region',{name:'Draft post angles'}).getByRole('button',{name:'Make social mockups'}).click();
  const mockups=page.getByRole('region',{name:`Holiday push ${stamp} · post mockups`});
  await expect(mockups.frameLocator('.fe-page-frame iframe').getByText(`Your coffee should wait for you ${stamp}`)).toBeVisible();
  await expect(mockups.frameLocator('.fe-page-frame iframe').getByText(/Claim limit: No health claims/)).toBeVisible();
});

test('dragging a task card to another lane changes its status',async({page,request,baseURL})=>{
  const data=fixture();
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'pane=work');
  const board=page.getByRole('region',{name:'Team tasks'});
  const card=board.getByRole('region',{name:'Needs decision',exact:true}).getByRole('button',{name:/Pick the holiday offer/});
  await card.dragTo(board.getByRole('region',{name:'In progress',exact:true}));
  await expect.poll(()=>data.tasks[0].status).toBe('working');
  await expect(board.getByRole('region',{name:'In progress',exact:true}).getByRole('button',{name:/Pick the holiday offer/})).toBeVisible();
  // The cockpit follows: the task moves from decisions to in progress.
  await expect(cockpit(page).getByRole('region',{name:'In progress'}).getByRole('button',{name:/Pick the holiday offer/})).toBeVisible();
  await expect(cockpit(page).getByRole('region',{name:'Needs your decision'})).not.toContainText('Pick the holiday offer');
});

test('permissions are decided once and saved as the employee’s PERMISSIONS.md',async({page,request,baseURL})=>{
  const data=fixture(),stamp=Date.now().toString(36);
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!,'view=team');
  const openPermissions=async()=>{
    await page.getByRole('navigation',{name:'Team sections'}).getByRole('button',{name:'AI employees'}).click();
    await page.getByRole('region',{name:'AI employees'}).getByRole('button',{name:/Marketing agent/}).click();
    await page.getByRole('navigation',{name:'Employee views'}).getByRole('button',{name:'Permissions',exact:true}).click();
  };
  await openPermissions();
  const ask=page.getByRole('region',{name:'Asks you first'});
  await expect(ask).toContainText('Publish or post anything');
  await ask.getByLabel('Add to Asks you first').fill('Reply to comments '+stamp);
  await ask.getByRole('button',{name:'Add rule to Asks you first'}).click();
  await page.getByRole('button',{name:/^Save (these )?permissions$/}).click();
  await expect(page.getByText(/as PERMISSIONS\.md · version \d+/)).toBeVisible();
  const files=await page.evaluate(async()=>(await fetch('/api/organization/agents/marketing-main/files')).json());
  const saved=files.find((file:{name:string})=>file.name==='PERMISSIONS.md');
  expect(saved.content).toMatch(new RegExp(`## Asks you first[\\s\\S]*- Reply to comments ${stamp}[\\s\\S]*## Never`));
  await page.reload();
  await openPermissions();
  await expect(page.getByRole('region',{name:'Asks you first'})).toContainText('Reply to comments '+stamp);
});

test('with notifications on, a new decision notifies a background tab',async({page,request,baseURL})=>{
  test.setTimeout(60000);
  const data=fixture();
  Object.assign(data.profile,{product_summary:'Coffee',goals:'Grow'});
  data.tasks.length=0;data.draft.status='approved';
  await mockMarketing(page,data,()=>'Noted.');
  await page.setViewportSize({width:1280,height:860});
  await page.addInitScript(()=>{
    try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}
    const notes:unknown[]=[];(window as any).__notes=notes;
    (window as any).Notification=class{static permission='granted';static async requestPermission(){return 'granted';}onclick:unknown=null;constructor(title:string,options:unknown){notes.push({title,options});}close(){}};
  });
  await launch(page,request,baseURL!,'view=settings');
  await page.getByRole('region',{name:'Notifications'}).getByRole('button',{name:'Turn on'}).click();
  await expect(page.getByRole('region',{name:'Notifications'}).getByRole('button',{name:'Turn off'})).toBeVisible();
  await page.evaluate(()=>{Object.defineProperty(document,'hidden',{configurable:true,get:()=>true});Object.defineProperty(document,'visibilityState',{configurable:true,get:()=>'hidden'});});
  data.tasks.push({...data.task,id:'e'.repeat(32),title:'Approve the holiday budget'});
  await expect.poll(()=>page.evaluate(()=>(window as any).__notes.length),{timeout:45000}).toBe(1);
  expect(await page.evaluate(()=>(window as any).__notes[0])).toMatchObject({title:'Marketing agent needs a decision',options:{body:'Approve the holiday budget'}});
});
