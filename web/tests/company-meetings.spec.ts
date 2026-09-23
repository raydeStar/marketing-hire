import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('meeting entry, exact grant, return recap, artifact review, and mobile layout use saved fixtures',async({page,request,baseURL})=>{
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(baseURL+'/api/auth/launch',{headers:{Origin:baseURL!},data:{key}});
  const {ticket}=await issued.json();
  const urls=['https://news.ycombinator.com/item?id=47667504','https://news.ycombinator.com/item?id=49703771'];
  const evidence=urls.map((url,index)=>({id:'source-'+index,task_id:'prior-task',url,title:index?'AI marketing skepticism':'Solo founder marketing question',note:'Previously checked source',query:'',source:'Hacker News',created_at:1}));
  const meetings:any[]=[];const tasks:any[]=[];let creates=0,turns=0;
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Build our brand'}],agents:[{id:'marketing-main',name:'Marketing agent',role:'Research and draft',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/marketing/**',route=>{
    const pathname=new URL(route.request().url()).pathname;
    if(pathname.endsWith('/history'))return route.fulfill({json:{items:[],nextCursor:null}});
    if(pathname.endsWith('/chat')){turns++;return route.fulfill({status:500});}
    return route.fulfill({json:{employee:{name:'Marketing agent',model:'openai/gpt-5.6-luna',sessionKey:'main'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,profile:{display_name:'Marketing agent',product_summary:'Personal brand selling marketing agents',guardrails:'Evidence before claims; no external sends',version:6},tasks,messages:[],requests:[],evidence,drafts:[],ownerDecisions:[]}});
  });
  await page.route(/\/api\/meetings(?:\/[^?]+)?$/,route=>{
    if(route.request().method()==='GET')return route.fulfill({json:meetings});
    const body=route.request().postDataJSON();
    if(body.action==='create'){
      creates++;
      meetings.push({id:'meeting-1',version:1,title:body.title,agenda:body.agenda,ethos:body.ethos,participants:body.participants,stage:'discussion',messages:[],plan:null,review:null,approvedBy:null,approvedRevision:null,grant:null,artifacts:null,proposalDigest:null,error:null,releaseAt:null,createdAt:'2026-09-23T20:00:00Z'});
    }else{
      const m=meetings[0];expect(body.requestId).toBeTruthy();m.version++;
      if(body.action==='ask-ceo')m.messages.push({id:'ceo-1',speaker:'CEO',content:'Which audience should we serve first, and what evidence would change your mind?',createdAt:m.createdAt});
      if(body.action==='propose'){
        m.plan={revision:1,profile:'personal_brand_content_pilot_v1',summary:'Use two supplied discussions to find angles and create one local draft.',requiresOwnerApproval:true,resources:'Two worker turns on existing subscription; no external actions.',actions:[{kind:'evidence_brief',title:'Prepare evidence brief',outcome:'Three sourced content angles.',state:'proposed',taskId:null},{kind:'local_draft',title:'Write local draft',outcome:'One reviewable draft.',state:'proposed',taskId:null}]};
        m.proposalDigest='a'.repeat(64);m.review={revision:1,verdict:'accept',rationale:'Recommended, subject to exact owner grant.',questions:[]};m.stage='reviewed';
        m.messages.push({id:'marketing-1',speaker:'Marketing',content:'## Proposed local job',createdAt:m.createdAt});
      }
      if(body.action==='approve'){
        expect(body.planDigest).toBe(m.proposalDigest);expect(body.sourceUrls).toEqual(urls);
        m.approvedBy='Owner session fixture';m.approvedRevision=1;m.stage='closed';
        m.grant={id:'grant-1',approver:m.approvedBy,authoritySource:'owner-session',approvedAt:m.createdAt,expiresAt:m.createdAt,planRevision:1,planDigest:m.proposalDigest,sourceUrls:urls,allowedActionTypes:['evidence_brief','local_draft'],capabilities:['public-read-exact-urls','local-meeting-artifact','scoped-task-update'],artifactDestination:'meeting-ledger',maxAssignedTasks:2,maxDispatchAttempts:2,dispatchAttempts:2,executionDeadline:m.createdAt,modelRoute:'openai/gpt-5.6-luna',allowFallback:false,revoked:false,taskIds:['a'.repeat(32),'b'.repeat(32)],dispatchIds:['one','two']};
        m.plan.actions[0].taskId='a'.repeat(32);m.plan.actions[0].state='produced';m.plan.actions[1].taskId='b'.repeat(32);m.plan.actions[1].state='produced';
        m.artifacts=[{id:'brief-1',taskId:m.plan.actions[0].taskId,kind:'evidence_brief',content:'# Three angles\n\nSource-backed brief.',digest:'1'.repeat(64),producedAt:m.createdAt,ownerAccepted:false,sourceUrls:urls,evidenceIds:['e1','e2'],acceptedBy:null,acceptedAt:null},{id:'draft-1',taskId:m.plan.actions[1].taskId,kind:'local_draft',content:'# Local draft\n\nA practical first post.',digest:'2'.repeat(64),producedAt:m.createdAt,ownerAccepted:false,sourceUrls:urls,evidenceIds:[],acceptedBy:null,acceptedAt:null}];
        tasks.push({id:m.plan.actions[0].taskId,title:'Prepare evidence brief',status:'done',priority:'normal',next_action:'Saved',action_state:'none',conversation_key:'task-a',version:2,updated_at:1},{id:m.plan.actions[1].taskId,title:'Write local draft',status:'needs_you',priority:'normal',next_action:'Review draft',action_state:'user_waiting',conversation_key:'task-b',version:2,updated_at:1});
      }
      if(body.action==='accept-artifact'){
        expect(body.content).toBe('draft-1');m.artifacts[1].ownerAccepted=true;m.artifacts[1].acceptedBy='Owner session fixture';m.artifacts[1].acceptanceSynced=true;
        tasks[1].status='done';tasks[1].next_action='Owner accepted saved local draft';
      }
    }
    return route.fulfill({json:meetings[0]});
  });
  await page.setViewportSize({width:1440,height:1000});
  await page.goto('/#launch='+ticket);
  await expect(page.getByRole('button',{name:'Start meeting',exact:true})).toBeEnabled();
  await expect(page.getByRole('button',{name:'Add to chat',exact:true})).toHaveCount(0);
  await page.getByRole('button',{name:'Start meeting',exact:true}).click();
  const form=page.getByRole('dialog',{name:'Start a meeting'});
  await expect(form).toContainText('Direct chat history is not copied');
  await expect(form).toContainText('openai/gpt-5.6-luna');
  await form.getByLabel('Meeting title').fill('First customers');
  await form.getByLabel('Agenda').fill('Find three angles and draft one local post.');
  await form.getByRole('button',{name:'Start meeting',exact:true}).click();
  await expect(page.getByText(/Which audience should we serve first/)).toBeVisible();
  await page.getByRole('button',{name:'Develop plan & review'}).click();
  await expect(page.getByLabel('Meeting return recap')).toContainText('CEO recommended plan v1');
  await expect(page.getByRole('button',{name:'Approve scope & assign work'})).toBeDisabled();
  await page.getByRole('group',{name:'Select two existing source records'}).getByRole('checkbox').first().check();
  await page.getByRole('group',{name:'Select two existing source records'}).getByRole('checkbox').last().check();
  await expect(page.getByRole('button',{name:'Approve scope & assign work'})).toBeEnabled();
  await page.getByRole('button',{name:'Work',exact:true}).click();
  await page.getByRole('button',{name:/^Approvals/}).click();
  await page.getByRole('region',{name:'Meeting approvals'}).getByRole('button',{name:/First customers/}).click();
  await page.getByRole('button',{name:'Approve scope & assign work'}).click();
  await expect(page.getByLabel('Meeting return recap')).toContainText('Local draft · produced for review');
  await expect(page.getByLabel('Meeting return recap')).toContainText('Review local draft');
  await expect(page.getByRole('button',{name:'Accept this local draft'})).toBeVisible();
  await page.getByRole('button',{name:'Accept this local draft'}).click();
  await expect(page.getByLabel('Meeting return recap')).toContainText('owner accepted');
  await expect(page.getByLabel('Meeting return recap')).toContainText('Write local draft · done');
  await page.screenshot({path:'../artifacts/company-meeting-desktop-sprint02.png',fullPage:true});
  await page.reload();
  await page.getByRole('button',{name:'Department conversations'}).click();
  await page.getByRole('button',{name:/First customers Meeting closed/}).click();
  await expect(page.getByLabel('Meeting return recap')).toContainText('owner accepted');
  await page.setViewportSize({width:390,height:844});
  await page.getByRole('button',{name:'Close company panel'}).click();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await page.screenshot({path:'../artifacts/company-meeting-mobile-sprint02.png',fullPage:true});
  expect(creates).toBe(1);expect(turns).toBe(0);
});
