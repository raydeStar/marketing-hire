import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('marketing views and task selection read state without invoking the employee',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  const task={id:'a'.repeat(32),title:'Prepare launch brief',status:'ready',priority:'high',next_action:'Outline the audience',action_state:'agent_ready',blocker:null,conversation_key:'agent:main:marketing-task-'+('a'.repeat(32)),version:1,updated_at:1780000000};
  const profile={id:'marketing',display_name:'Marketing agent',product_summary:'Configurable marketing agent',audience:'',voice:'',goals:'',guardrails:'No outbound sends',channels:'',version:1,updated_at:1780000000};
  const draft={id:12,channel:'local-test',destination:'https://example.org/thread',content:'Reviewable test draft. Do not post.',rationale:'Acceptance fixture',rules_url:'UNVERIFIED',status:'pending',revision:1,digest:'a'.repeat(64),decided_by:null};
  const evidence:{id:string;task_id:string;url:string;title:string;note:string;query:string;source:string;created_at:number}[]=[];
  const ownerDecisions:{requestId:string;draftId:number;decision:string;revision:number;digest:string;status:string;createdAt:string}[]=[];
  let turns=0;
  let decisions=0;
  let connectionStatus='connected';
  await page.route('**/api/marketing/**',async route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:profile.display_name,model:'openai/test',sessionKey:'agent:main:marketing-business-main'},connection:{status:connectionStatus},taskStoreAvailable:true,canConfigure:true,profile,drafts:[draft],evidence,ownerDecisions,tasks:[task],messages:[],requests:[]}});
    if(url.pathname==='/api/marketing/profile'&&route.request().method()==='PUT'){
      const change=route.request().postDataJSON();Object.assign(profile,{audience:change.audience,version:profile.version+1});
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
  await page.goto('/#launch='+ticket);
  await expect(page.getByRole('heading',{name:'Conversation'})).toBeVisible();
  await page.getByRole('button',{name:'Marketing',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Talk with your marketing employee'})).toBeVisible();
  await page.getByRole('button',{name:'Work',exact:false}).click();
  await page.getByRole('button',{name:/Prepare launch brief/}).click();
  await expect(page.getByRole('heading',{name:'Prepare launch brief'}).first()).toBeVisible();
  await page.getByRole('button',{name:'Edit brief'}).click();
  await page.getByLabel('Audience').fill('Small teams');
  await page.getByRole('button',{name:'Save brief'}).click();
  await expect(page.getByText(/Audience: Small teams/)).toBeVisible();
  await page.getByRole('button',{name:'Add source'}).click();
  await page.getByLabel('HTTPS source URL').fill('https://example.org/research');
  await page.getByLabel('Source name').fill('Example');
  await page.getByLabel('Title',{exact:true}).last().fill('Research source');
  await page.getByLabel('What this source supports').fill('The page discusses a marketing problem.');
  await page.getByRole('button',{name:'Attach source'}).click();
  await expect(page.getByRole('link',{name:'https://example.org/research'})).toBeVisible();
  await page.getByRole('button',{name:'Approve exact draft'}).click();
  await expect(page.getByText('No draft is waiting for approval.')).toBeVisible();
  await page.getByText('Recent decisions').click();
  await expect(page.getByText(/owner decision verified/)).toBeVisible();
  Object.assign(draft,{status:'rejected'});
  await page.getByRole('button',{name:'Refresh marketing state'}).click();
  await expect(page.getByText(/unverified ledger status/)).toBeVisible();
  await page.getByRole('combobox',{name:'Status'}).selectOption('working');
  await expect(page.getByRole('combobox',{name:'Status'})).toHaveValue('working');
  connectionStatus='auth_required';
  await page.getByRole('button',{name:'Refresh marketing state'}).click();
  await expect(page.getByText('Authentication needed')).toBeVisible();
  await expect(page.getByRole('combobox',{name:'Priority'})).toBeEnabled();
  await page.getByRole('combobox',{name:'Priority'}).selectOption('low');
  await expect(page.getByRole('combobox',{name:'Priority'})).toHaveValue('low');
  await page.getByRole('button',{name:'Chat',exact:true}).click();
  await expect(page.getByRole('textbox',{name:'Message to marketing employee'})).toBeDisabled();
  expect(turns).toBe(0);
  expect(decisions).toBe(1);
  await expect(page.getByText('SCRIPTED DEMO')).toHaveCount(0);
});
