import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

const key=()=>fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
async function launch(page:Page,request:APIRequestContext,origin:string,view='today'){
  // The host allows a few unclaimed launch links at a time; wait for one to expire rather than fail.
  let issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});
  for(let attempt=0;issued.status()===503&&attempt<15;attempt++){await page.waitForTimeout(5000);issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key:key()}});}
  expect(issued.status()).toBe(200);
  await page.goto(`/?view=${view}#launch=${(await issued.json()).ticket}`);
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
  await launch(page,request,baseURL!,'chat');
  const nav=page.getByRole('navigation',{name:'Main views'});
  const go=(name:string)=>nav.getByRole('button',{name:new RegExp('^'+name)}).click();
  await expect(page.getByRole('region',{name:'Conversation with Marketing agent'})).toBeVisible();
  await expect(page.getByRole('button',{name:'Study',exact:true})).toHaveCount(0);
  await expect(page.getByText('What should we focus on first?',{exact:true})).toBeVisible();
  await page.screenshot({path:'../artifacts/business-chat-desktop.png',fullPage:true});

  await go('Tasks');
  await expect(page.getByRole('main',{name:'Tasks'}).getByRole('region',{name:'Team tasks'})).toBeVisible();
  await expect(page.getByRole('region',{name:'Needs decision',exact:true})).toContainText('Nothing waiting on you');
  await expect(page.getByRole('button',{name:/Deferred campaign/})).toBeHidden();
  await page.getByText('Paused work',{exact:true}).click();
  await expect(page.getByRole('button',{name:/Deferred campaign/})).toBeVisible();
  await expect(page.getByRole('button',{name:/Earlier campaign/})).toBeVisible();
  await page.screenshot({path:'../artifacts/business-work-board.png',fullPage:true});

  // The business brief is edited from the employee's Team page.
  await go('Team');
  await page.locator('.fe-member-tile').filter({hasText:'Marketing agent'}).click();
  await page.getByRole('navigation',{name:'Member views'}).getByRole('button',{name:'Business brief'}).click();
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

  await go('Tasks');
  await page.getByRole('button',{name:/Prepare launch brief/}).first().click();
  const dialog=page.getByRole('dialog',{name:'Prepare launch brief'});
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button',{name:'Sources',exact:true}).click();
  await dialog.getByRole('button',{name:'Add source'}).click();
  await dialog.getByLabel('HTTPS source URL').fill('https://example.org/research');
  await dialog.getByLabel('Source name').fill('Example');
  await dialog.getByLabel('Title',{exact:true}).fill('Research source');
  await dialog.getByLabel('What this source supports').fill('The page discusses a marketing problem.');
  await dialog.getByRole('button',{name:'Attach source'}).click();
  await expect(dialog.getByRole('link',{name:'https://example.org/research'})).toBeVisible();
  await dialog.getByRole('button',{name:'Details',exact:true}).click();
  await dialog.getByRole('combobox',{name:'Status',exact:true}).selectOption('working');
  await expect(dialog.getByRole('combobox',{name:'Status',exact:true})).toHaveValue('working');
  await dialog.getByRole('button',{name:'Close dialog'}).click();

  // Draft approval moved from Work › Approvals to the Inbox.
  await go('Inbox');
  await page.getByRole('article',{name:'Draft 12'}).getByRole('button',{name:'Approve',exact:true}).click();
  await expect(page.getByRole('article',{name:'Draft 12'})).toHaveCount(0);

  await go('History');
  const historyViews=page.getByRole('navigation',{name:'History views'});
  await historyViews.getByRole('button',{name:'Activity log',exact:true}).click();
  await expect(page.getByRole('region',{name:'Activity log'})).toContainText('Task created: Prepare launch brief');
  await historyViews.getByRole('button',{name:'Records',exact:true}).click();
  const records=page.getByRole('complementary',{name:'Records'});
  const preview=page.getByRole('article',{name:'Record preview'});
  await records.getByRole('button',{name:/Draft approved/}).click();
  await expect(preview).toContainText('approved draft #12 (revision 1)');
  await expect(preview).toContainText('Confirmed');
  await page.getByLabel('Search records').fill('Research source');
  await records.getByRole('button',{name:/Research source/}).click();
  await expect(preview).toContainText('The page discusses a marketing problem.');

  await go('Chat');
  const composer=page.getByRole('textbox',{name:'Message to marketing employee'});
  await composer.fill('Keep this unsent direction.');
  await page.getByRole('button',{name:'Collapse sidebar'}).click();
  await expect(page.getByRole('button',{name:'Expand sidebar'})).toBeVisible();
  await expect(composer).toHaveValue('Keep this unsent direction.');
  await page.getByRole('button',{name:'Expand sidebar'}).click();
  await expect(nav.getByRole('button',{name:'Campaigns'})).toBeVisible();
  await page.setViewportSize({width:390,height:844});
  await expect(composer).toBeInViewport();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.getByRole('button',{name:'Open menu'}).click();
  await go('Tasks');
  await expect(page.getByRole('main',{name:'Tasks'}).getByRole('region',{name:'Team tasks'})).toBeVisible();
  await page.screenshot({path:'../artifacts/business-work-mobile.png',fullPage:true});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.setViewportSize({width:1440,height:1000});

  chatBlockedReason='The earlier employee request needs recovery.';
  await go('Chat');
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
  await expect(page.getByRole('region',{name:'Conversation with Marketing agent'})).toContainText('Needs sign-in');
  await expect(composer).toBeDisabled();
  await page.evaluate(()=>localStorage.setItem('thaddeus-theme','light'));
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme','light');
  await go('Tasks');
  await expect(page.getByRole('main',{name:'Tasks'}).getByRole('region',{name:'Team tasks'})).toBeVisible();
  await page.screenshot({path:'../artifacts/business-work-light.png',fullPage:true});
  expect(turns).toBe(0); expect(decisions).toBe(1);
  await expect(page.getByText('SCRIPTED DEMO')).toHaveCount(0);
});
