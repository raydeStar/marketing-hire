import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,query=''){
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/${query?"?"+query:""}#launch=${(await issued.json()).ticket}`);
}

test('marketing views and task selection read state without invoking the employee',async({page,request,baseURL})=>{
  test.setTimeout(90000);
  const task={id:'a'.repeat(32),title:'Prepare launch brief',status:'ready',priority:'high',next_action:'Outline the audience',action_state:'agent_ready',blocker:null,conversation_key:'agent:main:marketing-task-'+('a'.repeat(32)),version:1,updated_at:1780000000};
  const paused={...task,id:'b'.repeat(32),title:'Deferred campaign',status:'paused'};
  const legacyHold={...task,id:'c'.repeat(32),title:'Earlier campaign (on hold)',status:'needs_you',action_state:'blocked'};
  const profile={id:'marketing',display_name:'Marketing agent',product_summary:'Configurable marketing agent',audience:'',voice:'',goals:'',guardrails:'No outbound sends',channels:'',version:1,updated_at:1780000000};
  const draft={id:12,channel:'local-test',destination:'https://example.org/thread',content:'Reviewable test draft. Do not post.',rationale:'Acceptance fixture',rules_url:'UNVERIFIED',status:'pending',revision:1,digest:'a'.repeat(64),decided_by:null};
  const evidence:{id:string;task_id:string;url:string;title:string;note:string;query:string;source:string;created_at:number}[]=[];
  const ownerDecisions:{requestId:string;draftId:number;decision:string;revision:number;digest:string;status:string;createdAt:string}[]=[];
  let turns=0;
  let decisions=0;
  let connectionStatus='connected';
  let chatBlockedReason:string|null=null;
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Market our agents'},{id:'ops',name:'Operations',purpose:'Delivery'}],agents:[{id:'marketing-main',name:'Marketing agent',role:'Research and drafts',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'},{id:'ops-agent',name:'Operations agent',role:'Delivery',departmentId:'ops',kind:'employee',runtimeKey:null}]};
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/marketing/**',async route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:profile.display_name,model:'openai/test',sessionKey:'agent:main:marketing-business-main'},connection:{status:connectionStatus},taskStoreAvailable:true,canConfigure:true,businessBriefEvidenceEnabled:true,chatBlockedReason,profile,drafts:[draft],evidence,ownerDecisions,tasks:[task,paused,legacyHold],activity:[{id:1,ts:1780000000,kind:'task',title:'Task created: Prepare launch brief',data:{id:task.id,status:'ready'}}],messages:[{id:'fixture-user',sessionKey:'agent:main:marketing-business-main',role:'user',content:'What should we focus on first?',createdAt:1780000000},{id:'fixture-assistant',sessionKey:'agent:main:marketing-business-main',role:'assistant',content:'Start with a clear offer for small teams.\n\nI can prepare the positioning brief, collect supporting evidence, and bring a bounded proposal to the CEO for review.',createdAt:1780000020}],requests:[]}});
    if(url.pathname==='/api/marketing/profile'&&route.request().method()==='PUT'){
      const change=route.request().postDataJSON();Object.assign(profile,{audience:change.audience,claims:change.claims,examples:change.examples,version:profile.version+1});
      return route.fulfill({json:profile});
    }
    if(url.pathname==='/api/marketing/drafts/12/decision'){
      const decision=route.request().postDataJSON();
      expect(decision).toMatchObject({decision:'approved',revision:1,digest:'a'.repeat(64)});
      expect(decision.requestId).toBeTruthy();decisions++;Object.assign(draft,{status:'approved',decided_by:'Local owner'});
      ownerDecisions.push({requestId:decision.requestId,draftId:12,decision:'approved',revision:1,digest:draft.digest,status:'confirmed',createdAt:'2026-09-23T00:00:00Z'});
      return route.fulfill({json:draft});
    }
    if(url.pathname==='/api/marketing/tasks/'+task.id+'/evidence'){
      const source=route.request().postDataJSON();
      expect(source).toMatchObject({url:'https://example.org/research',title:'Research source',source:'Example',note:'The page discusses a marketing problem.'});
      expect(source.requestId).toBeTruthy();
      const item={id:'e'.repeat(32),task_id:task.id,...source,created_at:1780000000};
      evidence.push(item);
      return route.fulfill({json:item});
    }
    if(url.pathname==='/api/marketing/tasks/'+task.id&&route.request().method()==='PUT'){
      const change=route.request().postDataJSON();
      Object.assign(task,{status:change.status??task.status,priority:change.priority??task.priority,version:task.version+1});
      return route.fulfill({json:task});
    }
    if(url.pathname==='/api/marketing/chat'){turns++;return route.fulfill({json:{requestId:'test',status:'succeeded',reply:'Ready.',sessionKey:'agent:main:marketing-business-main'}});}
    return route.fulfill({status:404,json:{error:'Unexpected marketing request'}});
  });
  await page.setViewportSize({width:1440,height:1000});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
  await launch(page,request,baseURL!);
  const rail=page.getByRole('complementary',{name:'Main navigation'});
  const board=page.getByRole('region',{name:'Team tasks'});
  // Work is the second tab of home; the rail's Chat button returns home from any view.
  const work=async()=>{await rail.getByRole('button',{name:/^Chat/}).click();await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Work'}).click();};
  await expect(page.getByRole('region',{name:'Conversation with Marketing agent'})).toBeVisible();
  await expect(page.getByRole('button',{name:'Study',exact:true})).toHaveCount(0);
  await expect(page.getByText('What should we focus on first?',{exact:true})).toBeVisible();
  await page.screenshot({path:'../artifacts/business-chat-desktop.png',fullPage:true});

  await work();
  await expect(page.getByRole('main',{name:'Work'}).getByRole('region',{name:'Team tasks'})).toBeVisible();
  await expect(board.getByRole('region',{name:'Needs decision',exact:true})).toContainText('Nothing waiting on you');
  await expect(board.getByRole('button',{name:/Deferred campaign/})).toBeHidden();
  await board.getByText('Paused work',{exact:true}).click();
  await expect(board.getByRole('button',{name:/Deferred campaign/})).toBeVisible();
  await expect(board.getByRole('button',{name:/Earlier campaign/})).toBeVisible();
  await expect(page.getByRole('region',{name:'Recent activity'})).toContainText('Task created: Prepare launch brief');
  await page.screenshot({path:'../artifacts/business-work-board.png',fullPage:true});

  // The business brief is edited from the employee's profile in Team.
  await rail.getByRole('button',{name:'Team'}).click();
  await page.getByRole('navigation',{name:'Team sections'}).getByRole('button',{name:'AI employees'}).click();
  await page.getByRole('region',{name:'AI employees'}).getByRole('button',{name:/Marketing agent/}).click();
  await page.getByRole('navigation',{name:'Employee views'}).getByRole('button',{name:'Business brief'}).click();
  await page.getByRole('region',{name:'Business brief'}).getByRole('button',{name:'Edit'}).click();
  const brief=page.getByRole('form',{name:'Edit business brief'});
  await brief.getByLabel('Who it’s for').fill('Small teams');
  await brief.getByLabel('What we can truthfully claim').fill('No proven revenue uplift');
  await brief.getByLabel('Examples to learn from').fill('Owner writing sample');
  await brief.getByRole('button',{name:'Save brief'}).click();
  const saved=page.getByRole('region',{name:'Business brief'});
  await expect(saved.getByText('Small teams',{exact:true})).toBeVisible();
  await expect(saved.getByText('No proven revenue uplift',{exact:true})).toBeVisible();
  expect(profile.version).toBe(2);

  // A task opens in the work window: sources, then status.
  await work();
  await board.getByRole('button',{name:/Prepare launch brief/}).first().click();
  const window=page.getByRole('region',{name:'Prepare launch brief'});
  await expect(window).toBeVisible();
  await window.getByRole('button',{name:'Sources',exact:true}).click();
  await window.getByRole('button',{name:'Add source'}).click();
  await window.getByLabel('HTTPS source URL').fill('https://example.org/research');
  await window.getByLabel('Source name').fill('Example');
  await window.getByLabel('Title',{exact:true}).fill('Research source');
  await window.getByLabel('What this source supports').fill('The page discusses a marketing problem.');
  await window.getByRole('button',{name:'Attach source'}).click();
  await expect(window.getByRole('link',{name:'https://example.org/research'})).toBeVisible();
  await window.getByRole('button',{name:'Details',exact:true}).click();
  await window.getByRole('combobox',{name:'Status',exact:true}).selectOption('working');
  await expect(window.getByRole('combobox',{name:'Status',exact:true})).toHaveValue('working');
  await expect(page.getByRole('complementary',{name:'Cockpit'}).getByRole('region',{name:'In progress'})).toContainText('Prepare launch brief');
  await window.getByRole('button',{name:'Close',exact:true}).click();
  await expect(window).toHaveCount(0);

  // Draft approval moved from the Inbox to the cockpit's decisions.
  const decide=page.getByRole('complementary',{name:'Cockpit'}).getByRole('region',{name:'Needs your decision'});
  await decide.getByRole('button',{name:/Draft for local-test/}).click();
  const draftCard=page.getByRole('article',{name:'Draft 12'});
  await draftCard.getByRole('button',{name:'Approve',exact:true}).click();
  await expect(decide).not.toContainText('Draft for local-test');
  // The decided draft cannot be decided again from the open window.
  await expect(draftCard.getByRole('button',{name:'Approve',exact:true})).toBeDisabled();
  await expect(draftCard.getByRole('button',{name:'Reject',exact:true})).toBeDisabled();
  await expect(draftCard).toContainText('Decision recorded: approved.');

  // Sources saved on a task are Library items under Research.
  await rail.getByRole('button',{name:'Library'}).click();
  await page.getByRole('tree',{name:'Folders'}).getByRole('button',{name:/^Research/}).click();
  await page.getByRole('row',{name:/Research source/}).click();
  const sourceItem=page.getByRole('region',{name:'Research source'});
  await expect(sourceItem).toContainText('The page discusses a marketing problem.');
  await expect(sourceItem.locator('.fe-window-title small')).toHaveText('Source · Research / Sources');
  await sourceItem.getByRole('button',{name:/Open the task it supports/}).click();
  await expect(page.getByRole('region',{name:'Prepare launch brief'})).toBeVisible();

  // An unsent message survives leaving the chat.
  await rail.getByRole('button',{name:/^Chat/}).click();
  await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Chat'}).click();
  const composer=page.getByRole('textbox',{name:'Message to marketing employee'});
  await composer.fill('Keep this unsent direction.');
  await rail.getByRole('button',{name:'Library'}).click();
  await rail.getByRole('button',{name:/^Chat/}).click();
  await expect(composer).toHaveValue('Keep this unsent direction.');
  await page.setViewportSize({width:390,height:844});
  await expect(composer).toBeInViewport();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Work'}).click();
  await expect(page.getByRole('main',{name:'Work'}).getByRole('region',{name:'Team tasks'})).toBeVisible();
  await page.screenshot({path:'../artifacts/business-work-mobile.png',fullPage:true});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.setViewportSize({width:1440,height:1000});

  chatBlockedReason='The earlier employee request needs recovery.';
  await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Chat'}).click();
  await page.reload();
  // The notice shows the host's own reason and keeps the draft.
  await expect(page.getByText('Chat is paused for now')).toBeVisible();
  await expect(page.getByText('The earlier employee request needs recovery.',{exact:false})).toBeVisible();
  await composer.fill('Keep my draft while recovery is pending');
  await expect(page.getByRole('button',{name:'Send',exact:true})).toBeDisabled();
  await page.reload();
  await expect(composer).toHaveValue('Keep my draft while recovery is pending');
  chatBlockedReason=null;
  connectionStatus='auth_required';
  await page.reload();
  await expect(page.getByRole('banner').or(page.locator('.fe-topbar'))).toContainText('Marketing agent · Needs sign-in');
  await expect(composer).toBeDisabled();
  await expect(page.getByText('Chat is unavailable until the employee reconnects. Your draft is saved.')).toBeVisible();
  await page.evaluate(()=>localStorage.setItem('thaddeus-theme','light'));
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme','light');
  await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Work'}).click();
  await expect(page.getByRole('main',{name:'Work'}).getByRole('region',{name:'Team tasks'})).toBeVisible();
  await page.screenshot({path:'../artifacts/business-work-light.png',fullPage:true});
  expect(turns).toBe(0); expect(decisions).toBe(1);
  await expect(page.getByText('SCRIPTED DEMO')).toHaveCount(0);
});
