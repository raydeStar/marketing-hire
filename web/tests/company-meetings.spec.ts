import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('meeting agenda, resource approval, veto, notes, and responsive navigation',async({page,request,baseURL})=>{
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(baseURL+'/api/auth/launch',{headers:{Origin:baseURL!},data:{key}});
  const {ticket}=await issued.json();
  const meetings:any[]=[];let creates=0,turns=0;
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Build our brand'}],agents:[{id:'marketing-main',name:'Marketing agent',role:'Research and draft',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/marketing/**',route=>{
    const pathname=new URL(route.request().url()).pathname;
    if(pathname.endsWith('/history'))return route.fulfill({json:{items:[],nextCursor:null}});
    if(pathname.endsWith('/chat')){turns++;return route.fulfill({status:500});}
    return route.fulfill({json:{employee:{name:'Marketing agent',sessionKey:'main'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,profile:{display_name:'Marketing agent',product_summary:'Personal brand selling marketing agents',guardrails:'Evidence before claims; no external sends',version:1},tasks:[],messages:[],requests:[],evidence:[],drafts:[],ownerDecisions:[]}});
  });
  await page.route(/\/api\/meetings(?:\/[^?]+)?$/,route=>{
    if(route.request().method()==='GET')return route.fulfill({json:meetings});
    const body=route.request().postDataJSON();
    if(body.action==='create'){
      creates++;
      meetings.push({id:'meeting-1',version:1,title:body.title,agenda:body.agenda,ethos:body.ethos,participants:body.participants,stage:'discussion',messages:[],plan:null,review:null,approvedBy:null,approvedRevision:null,createdAt:'2026-09-23T20:00:00Z'});
    }else{
      const m=meetings[0];expect(body.requestId).toBeTruthy();m.version++;
      if(body.action==='ask-ceo')m.messages.push({id:'ceo-1',speaker:'CEO',content:'Which audience should we serve first, and what evidence would change your mind?',createdAt:m.createdAt});
      if(body.action==='propose'){
        m.plan={revision:1,summary:'Evaluate the pilot before committing resources.',requiresOwnerApproval:true,resources:'Substantial research time requested; owner approval needed.',actions:[{title:'Prepare the pilot proposal',outcome:'Document scope, evidence, and resource estimate.',state:'proposed',taskId:null}]};
        m.review={revision:1,verdict:'accept',rationale:'Aligned with evidence before claims, subject to resource approval.',questions:[]};m.stage='reviewed';
        m.messages.push({id:'marketing-1',speaker:'Marketing',content:'## Proposed pilot\n\nPrepare a scoped proposal before committing resources.',createdAt:m.createdAt});
      }
      if(body.action==='veto'){m.stage='vetoed';m.plan.actions[0].state='paused';m.messages.push({id:'owner-veto',speaker:'You',content:'Vetoed this plan. Pending work is stopped.',createdAt:m.createdAt});}
    }
    return route.fulfill({json:meetings[0]});
  });
  await page.setViewportSize({width:1440,height:1000});
  await page.goto('/#launch='+ticket);
  await expect(page.getByRole('button',{name:'Start meeting',exact:true})).toBeEnabled();
  await page.getByRole('button',{name:'All members',exact:true}).click();
  await expect(page.getByRole('button',{name:/CEO Chair meetings/})).toBeVisible();
  await page.getByRole('button',{name:'Start meeting',exact:true}).click();
  const form=page.getByRole('dialog',{name:'Start a meeting'});
  await form.getByLabel('Meeting title').fill('First customers');
  await form.getByLabel('Agenda').fill('Decide the first audience and a bounded pilot.');
  await form.getByLabel('Company ethos').fill('Evidence before claims. Protect owner time and resources.');
  await form.getByRole('button',{name:'Start meeting',exact:true}).click();
  await expect(page.getByRole('heading',{name:'First customers',exact:true}).first()).toBeVisible();
  await expect(page.getByText(/Which audience should we serve first/)).toBeVisible();
  await expect(page.getByRole('complementary',{name:'Company sidebar'})).toContainText('Decide the first audience and a bounded pilot.');
  await page.getByRole('button',{name:'Develop plan & review'}).click();
  await expect(page.getByText('Your approval required',{exact:true})).toBeVisible();
  await expect(page.getByRole('button',{name:'Approve plan & close meeting'})).toBeEnabled();
  await expect(page.getByText('Prepare the pilot proposal',{exact:true})).toBeVisible();
  await page.screenshot({path:'../artifacts/company-meeting-desktop.png',fullPage:true});
  await page.getByRole('button',{name:'Veto plan & stop pending work'}).click();
  await expect(page.getByText('Plan vetoed. Pending work is stopped.')).toBeVisible();
  await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Meeting notes'})).toBeVisible();
  await page.getByRole('button',{name:/First customers/}).first().click();
  await expect(page.getByText('Plan vetoed. Pending work is stopped.')).toBeVisible();
  await page.reload();
  await page.getByRole('button',{name:'Department conversations'}).click();
  await page.getByRole('button',{name:/First customers Vetoed/}).click();
  await expect(page.getByText('Plan vetoed. Pending work is stopped.')).toBeVisible();
  await page.setViewportSize({width:390,height:844});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.screenshot({path:'../artifacts/company-meeting-mobile.png',fullPage:true});
  expect(creates).toBe(1);expect(turns).toBe(0);
});
