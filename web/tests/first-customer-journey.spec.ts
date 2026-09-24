import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('fixture customer can save a brief, authorize work, review results, request revision, and approve exact text',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  const profile={id:'marketing',display_name:'Marketing employee',product_summary:'',audience:'',goals:'',voice:'',guardrails:'Internal drafts only',channels:'',version:1,updated_at:1780000000};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Customer growth'}],agents:[{id:'marketing-main',name:'Marketing employee',role:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  const pastTask={id:'7'.repeat(32),title:'Old meeting draft',status:'needs_you',priority:'normal',next_action:'Review the old meeting draft',action_state:'user_waiting',conversation_key:'meeting-fixture',version:1,updated_at:1780000000};
  const pastMeeting={id:'meeting-fixture',title:'Earlier pilot meeting',agenda:'Historical work',participants:['ceo','marketing'],stage:'closed',plan:{actions:[{taskId:pastTask.id,title:pastTask.title,kind:'local_draft',state:'done'}]},review:null,grant:null,artifacts:[],messages:[],createdAt:'2026-09-23T12:00:00Z'};
  let runway:any=null;
  let liveWorkEnabled=true;
  let deferredRevisionEnabled=false;
  let sharedGatewayEnabled=false;
  let archiveEnabled=false;
  let archiveProjects:any[]=[];
  let archivedRunway:any=null;
  const sharedState:any={available:false,suggestions:[]};
  const source='https://news.ycombinator.com/item?id=47667504';
  const angleContent=JSON.stringify({angles:[{title:'Keep control',hook:'Start with approval before publishing.',sourceUrl:source,why:'Owner wants control',claimLimit:'One anecdote'},{title:'Follow up after launch',hook:'Do not lose the next day.',sourceUrl:source,why:'Follow-up problem',claimLimit:'No demand claim'},{title:'Avoid generic output',hook:'Drafts still need review.',sourceUrl:source,why:'Quality concern',claimLimit:'No quality guarantee'}]});
  const revisedContent=angleContent.replace('Start with approval before publishing.','Start with a small, reviewable draft.');
  const artifacts=[
    {id:'a'.repeat(32),step_id:'1'.repeat(32),kind:'audience_note',content:JSON.stringify({audience:'Technical founders (assumption)',problem:'Time to market',evidence:[{sourceUrl:source,quote:'A short checked quote',inference:'Possible time cost'}],limitations:'Not demand proof'}),digest:'a'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000001},
    {id:'b'.repeat(32),step_id:'2'.repeat(32),kind:'post_angles',content:angleContent,digest:'b'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000002},
    {id:'c'.repeat(32),step_id:'3'.repeat(32),kind:'review_packet',content:JSON.stringify({summary:'A learning packet',unsupportedClaims:['This proves demand'],recommendation:'Test one angle manually',nextOwnerDecision:'Choose an angle'}),digest:'c'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000003}
  ];
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/meetings',route=>route.fulfill({json:[pastMeeting]}));
  await page.route('**/api/devices',route=>route.fulfill({json:{devices:[]}}));
  await page.route('**/api/marketing/**',async route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:profile.display_name,model:'openai/fixture',sessionKey:'agent:main:fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,runwayLiveEnabled:liveWorkEnabled,runwayArchiveEnabled:archiveEnabled,deferredRevisionEnabled,sharedGatewayEnabled,profile,drafts:[],evidence:[],ownerDecisions:[],tasks:[pastTask],activity:[],messages:[],requests:[],runway}});
    if(url.pathname==='/api/marketing/runways'&&route.request().method()==='GET')return route.fulfill({json:{projects:archiveProjects}});
    if(url.pathname==='/api/marketing/runways/'+ '1'.repeat(32)&&route.request().method()==='GET')return route.fulfill({json:archivedRunway});
    if(url.pathname.endsWith('/shared')&&route.request().method()==='GET')return route.fulfill({json:sharedState});
    if(url.pathname.endsWith('/shared/reconcile')&&route.request().method()==='POST'){
      const body=route.request().postDataJSON();expect(body.requestId).toBe('fixture-native-receipt');
      sharedState.suggestions[0].status='recorded';
      runway.inputs.push({id:'9'.repeat(32),actor_name:'Fixture collaborator',content:sharedState.suggestions[0].content,created_at:1780000010});
      return route.fulfill({json:{requestId:body.requestId,status:'recorded'}});
    }
    if(url.pathname==='/api/marketing/profile'&&route.request().method()==='PUT'){
      const change=route.request().postDataJSON();expect(change.version).toBe(profile.version);
      Object.assign(profile,{product_summary:change.product_summary,audience:change.audience,goals:change.goals,version:profile.version+1});
      return route.fulfill({json:profile});
    }
    if(url.pathname==='/api/marketing/runway'&&route.request().method()==='POST'){
      const body=route.request().postDataJSON();expect(body.goal).toContain('configurable marketing agents');
      runway={project:{id:'f'.repeat(32),goal:body.goal,status:'ready',version:1,run_count:0,max_runs:6,token_limit:150000,token_used:0,token_reserved:0,wait_reason:null,deadline_at:1780001800},steps:[1,2,3].map((n)=>({id:String(n).repeat(32),kind:['audience_note','post_angles','review_packet'][n-1],status:'ready',attempts:0})),artifacts:[],reviews:[],inputs:[],executions:[]};
      return route.fulfill({json:runway});
    }
    if(url.pathname.endsWith('/input')&&route.request().method()==='POST'){
      const body=route.request().postDataJSON();expect(body.version).toBe(runway.project.version);
      runway.inputs.push({id:crypto.randomUUID().replaceAll('-',''),actor_name:'Fixture owner',content:body.content,created_at:1780000009});
      runway.project.version++;
      return route.fulfill({json:runway});
    }
    if(url.pathname.endsWith('/review')&&route.request().method()==='POST'){
      const body=route.request().postDataJSON();expect(body.digest).toBe(body.artifactId.repeat(2));
      runway.reviews.push({id:crypto.randomUUID(),artifact_id:body.artifactId,artifact_digest:body.digest,decision:body.decision,instruction:body.instruction||'',actor_name:'Fixture owner',created_at:1780000005});
      runway.project.version++;
      if(body.decision==='revision_requested'){
        runway.project.status=liveWorkEnabled?'ready':'needs_review';
        runway.project.wait_reason=liveWorkEnabled?null:'Revision request saved; execution awaits a metered model route and a fresh owner grant';
        if(liveWorkEnabled)runway.steps.push({id:'4'.repeat(32),kind:'revision_angles',status:'ready',attempts:0});
      }else if(body.decision==='approved'){
        runway.project.status='done';runway.project.wait_reason='Exact draft approved for internal use; nothing was published';
      }
      return route.fulfill({json:runway});
    }
    return route.fulfill({status:404,json:{error:'Unexpected fixture request'}});
  });
  await page.setViewportSize({width:1280,height:900});
  await page.goto('/#launch='+ticket);
  await page.getByRole('button',{name:'Work',exact:true}).click();
  const panel=page.getByRole('region',{name:'Standing marketing assignment'});
  await expect(panel.getByText('Describe your offer to begin.')).toBeVisible();
  await expect(panel.getByRole('button',{name:'Start bounded work'})).toBeDisabled();
  await panel.getByRole('button',{name:'Edit',exact:true}).click();
  await panel.getByLabel('What does your business sell?').fill('Mark’s personal brand selling configurable marketing agents');
  await panel.getByLabel('Who is it for?').fill('');
  await panel.getByLabel('What is the immediate goal?').fill('learn which message is worth testing next');
  await panel.getByRole('button',{name:'Save business brief'}).click();
  await expect(panel.getByText('Mark’s personal brand selling configurable marketing agents')).toBeVisible();
  await expect(panel.getByRole('button',{name:'Start bounded work'})).toBeEnabled();
  await panel.getByRole('button',{name:'Start bounded work'}).click();
  await expect(panel.getByText('No active step')).toBeVisible();
  expect(runway.artifacts).toHaveLength(0);
  runway.project.status='needs_review';runway.project.wait_reason='All deliverables saved; owner review needed';runway.project.run_count=3;runway.project.token_used=8200;runway.project.version++;
  runway.steps.forEach((step:any)=>{step.status='done';step.attempts=1;});runway.artifacts=artifacts;
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(panel.getByText('8,200')).toBeVisible();
  const sidebar=page.getByRole('complementary',{name:'Company sidebar'});
  await expect(sidebar.getByRole('button',{name:/Review Marketing’s draft angles/})).toBeVisible();
  await expect(sidebar.getByText(pastTask.title)).toHaveCount(0);
  await sidebar.getByRole('button',{name:/Review Marketing’s draft angles/}).click();
  const pendingDraft=panel.locator('details.runway-artifact').filter({hasText:'Three draft post angles · saved'});
  await expect(pendingDraft).toHaveAttribute('open','');
  await pendingDraft.locator('summary').click();
  const board=page.getByRole('region',{name:'Team tasks'});
  await board.getByText('Past meeting work').click();
  await board.getByRole('button',{name:/Old meeting draft/}).click();
  const pastTaskDialog=page.getByRole('dialog',{name:pastTask.title});
  await expect(pastTaskDialog.getByText(/status is a historical record/)).toBeVisible();
  await expect(pastTaskDialog.getByLabel('Status')).toBeDisabled();
  await pastTaskDialog.getByRole('button',{name:'Close dialog'}).click();
  await panel.getByRole('button',{name:'Review draft angles'}).click();
  await expect(panel.locator('details.runway-artifact').filter({hasText:'Three draft post angles · saved'})).toHaveAttribute('open','');
  await expect(panel.getByText('Keep control')).toBeVisible();
  await panel.getByRole('button',{name:'Request revision'}).click();
  await panel.getByLabel('What should change?').fill('Make the first angle more specific and keep the claim limit.');
  await panel.getByRole('button',{name:'Save revision request'}).click();
  await expect(panel.getByText('Revised post angles',{exact:true})).toBeVisible();
  runway.project.status='needs_review';runway.project.wait_reason='All deliverables saved; owner review needed';runway.project.version++;
  runway.steps.at(-1).status='done';runway.steps.at(-1).attempts=1;
  runway.artifacts.push({...artifacts[1],id:'d'.repeat(32),step_id:'4'.repeat(32),kind:'revision_angles',content:revisedContent,digest:'d'.repeat(64),created_at:1780000006});
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await panel.getByText('Revised post angles · saved').click();
  await expect(panel.getByText('Start with a small, reviewable draft.')).toBeVisible();
  await panel.getByRole('button',{name:'Approve exact draft'}).last().click();
  await expect(panel.getByText('Exact draft approved for internal use; nothing was published')).toBeVisible();
  await page.screenshot({path:'../artifacts/overnight-journey-fixture.png',fullPage:true});
  liveWorkEnabled=false;deferredRevisionEnabled=true;
  runway.project.status='paused';runway.project.version++;
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(panel.getByRole('button',{name:'Resume project'})).toBeDisabled();
  runway.project.status='needs_review';runway.project.version++;
  runway.artifacts.push({...artifacts[1],id:'e'.repeat(32),step_id:'5'.repeat(32),kind:'revision_angles',digest:'e'.repeat(64),created_at:1780000007});
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await panel.getByText('Revised post angles · saved').last().click();
  await panel.getByRole('button',{name:'Request revision'}).click();
  await panel.getByLabel('What should change?').fill('Remove the unsupported performance claim.');
  const stepsBefore=runway.steps.length;
  await panel.getByRole('button',{name:'Save revision request'}).click();
  expect(runway.steps).toHaveLength(stepsBefore);
  await expect(panel.getByText('When a metered, linked revision grant is available')).toBeVisible();
  sharedGatewayEnabled=true;
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(panel.getByRole('button',{name:'Connect native conversation'})).toBeDisabled();
  await expect(panel.getByText(/this localhost URL cannot provide the client address/)).toBeVisible();
  sharedState.available=true;
  sharedState.suggestions=[{requestId:'fixture-native-receipt',actorName:'Fixture collaborator',content:'Keep the audience provisional.',status:'ledger_conflict',suggestionId:'8'.repeat(32),error:'Ledger temporarily unavailable',createdAt:'2026-09-24T04:00:00Z'}];
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(panel.getByRole('button',{name:'Reconcile saved receipt'})).toBeVisible();
  await expect(panel.getByText(/this localhost view can save project notes only/)).toBeVisible();
  await panel.getByLabel('Constraint or context for the next eligible step').fill('Keep this as a local project note.');
  await panel.getByRole('button',{name:'Add to project'}).click();
  await expect(panel.getByText('Keep this as a local project note.')).toBeVisible();
  await panel.getByRole('button',{name:'Reconcile saved receipt'}).click();
  await expect(panel.getByText('Keep the audience provisional.')).toBeVisible();
  await expect(panel.getByRole('button',{name:'Reconcile saved receipt'})).toHaveCount(0);
  archiveEnabled=true;
  archiveProjects=[{id:'1'.repeat(32),goal:'Earlier internal learning packet',status:'done',created_at:1780000000,updated_at:1780000100,artifact_count:1}];
  archivedRunway={project:{...runway.project,id:'1'.repeat(32),goal:'Earlier internal learning packet',status:'done',created_at:1780000000},steps:[],artifacts:[artifacts[0]],reviews:[],revision_grants:[{id:'8'.repeat(32),source_review_id:'9'.repeat(32),source_artifact_id:'a'.repeat(32),source_artifact_digest:'a'.repeat(64),scope:'internal_revision_draft',status:'held_for_metering',max_runs:1,max_model_requests:4,token_limit:25000,max_active_seconds:300,deadline_at:1780001800,created_at:1780000002}],inputs:[{id:'7'.repeat(32),actor_name:'Fixture owner',content:'Earlier project constraint',created_at:1780000001}],executions:[{id:'6'.repeat(32),status:'succeeded',reported_tokens:123,reserved_tokens:25000,started_at:1780000002}]};
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  const past=panel.locator('[aria-label="Previous marketing assignments"]');
  await expect(past.getByText('Earlier internal learning packet')).toBeVisible();
  await past.getByRole('button',{name:/1 saved result/}).click();
  await expect(past.getByText('Saved assignment')).toBeVisible();
  await past.getByText('Audience and problem note · saved').click();
  await expect(past.getByText('Technical founders (assumption)')).toBeVisible();
  await past.getByText('Project notes').click();
  await expect(past.getByText('Earlier project constraint')).toBeVisible();
  await past.getByText('Execution and usage receipts').click();
  await expect(past.getByText('123 reported tokens')).toBeVisible();
  await past.getByText('Linked revision grants').click();
  await expect(past.getByText('Saved authority only. No task or model request has been released.')).toBeVisible();
  runway.project.deadline_at=null;
  liveWorkEnabled=false;
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(panel.getByText('This legacy assignment has no recorded deadline. Its unused allowance does not authorize more work; a fresh owner grant is required.')).toBeVisible();
  await expect(panel.getByText('unused recorded tokens')).toBeVisible();
});
