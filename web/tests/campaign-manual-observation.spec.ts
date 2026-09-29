import {test,expect,type Page,type APIRequestContext,type Locator} from '@playwright/test';
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
// Assignment management is a collapsed <details> once a project exists.
async function openManagement(panel:Locator){
  const details=panel.locator('details.runway-management');
  await expect(details).toBeVisible();
  if(await details.getAttribute('open')===null)await details.locator(':scope > summary').click();
  await expect(details).toHaveAttribute('open','');
}

function localInput(timestamp:number){
  const date=new Date(timestamp);
  const pad=(value:number)=>String(value).padStart(2,'0');
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

test('owner can record sourced internal context without advancing launch',async({page,request,baseURL})=>{
  const id='f'.repeat(32),sourceId='a'.repeat(32),assetId='b'.repeat(32);
  const profile={id:'marketing',display_name:'Marketing employee',product_summary:'Configurable marketing agents',audience:'Founders',goals:'Learn',voice:'',guardrails:'Internal only',channels:'',version:1,updated_at:1780000000};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Customer growth'}],agents:[{id:'marketing-main',name:'Marketing employee',role:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  const brief={audience:'Founders',problem:'Marketing time',hypothesis:'A small draft may clarify the offer',priority_rationale:'Both checked founder notes describe the attention problem',proposition:'Configurable marketing employee',desired_behavior:'Ask for a demo',channel:'Owner reviewed draft',primary_metric:'Qualified replies',metric_definition:'Count relevant replies',guardrail:'No outcome claim',review_timing:'At owner review',non_goals:'No publishing'};
  const experiment={intervention:'One internal draft',target_population:'Founders',observation_window:'One week',metric_source:'Owner notebook',decision_rule:'learning_only',minimum_sample:0};
  const runway:any={project:{id,goal:'Internal founder message',scope:'internal_research_draft',status:'needs_review',version:4,run_count:3,max_runs:6,token_limit:150000,token_used:300,token_reserved:0,wait_reason:'Owner review needed'},steps:[],artifacts:[
    {id:sourceId,step_id:'1'.repeat(32),kind:'audience_note',content:JSON.stringify({audience:'Founders',problem:'Marketing time',evidence:[],limitations:'Hypothesis only'}),digest:'a'.repeat(64),source_urls:'[]',created_at:1780000001},
    {id:assetId,step_id:'2'.repeat(32),kind:'post_angles',content:JSON.stringify({angles:[]}),digest:'b'.repeat(64),source_urls:'[]',created_at:1780000002}
  ],source_metadata:[],inputs:[],reviews:[],campaign:{runway_id:id,version:1,stage:'align',mode:'internal',owner_verified:true,owner_actor:'owner-fixture',source_artifact_id:sourceId,source_artifact_digest:'a'.repeat(64),asset_artifact_id:assetId,asset_artifact_digest:'b'.repeat(64),brief_json:JSON.stringify(brief),experiment_json:JSON.stringify(experiment),created_at:1780000003,updated_at:1780000003},campaign_revisions:[{id:'c'.repeat(32),version:1,actor_id:'owner-fixture',source_artifact_id:sourceId,source_artifact_digest:'a'.repeat(64),created_at:1780000003}],campaign_actions:[],executions:[]};
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/devices',route=>route.fulfill({json:{devices:[]}}));
  await page.route('**/api/marketing/**',route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/campaign-lessons'){
      expect(url.searchParams.get('audience')).toBe('Founders');
      expect(url.searchParams.get('excludeCampaignId')).toBe(id);
      return route.fulfill({json:{lessons:[{campaign_id:'8'.repeat(32),action_id:'7'.repeat(32),created_at:1780000000,
        lesson:{lesson:'Ask founders about controls first',context:'Earlier owner interview',uncertainty:'One anecdote',revisit_condition:'A new authorized test',next_action:'Keep the draft internal',evidence_type:'owner_reported',causality:'not_established'},
        brief:{audience:'Founders (provisional)'},decision:{decision:'pause',rationale:'Insufficient evidence'},
        observations:[{source_reference:'Interview note 4',value_type:'actual',attribution_limitations:'No campaign launch'}]}]}});
    }
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:'Marketing employee',model:'fixture',sessionKey:'fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,runwayLiveEnabled:false,runwayArchiveEnabled:false,campaignBriefEnabled:true,fixtureCampaignEnabled:false,deferredRevisionEnabled:true,sharedGatewayEnabled:false,profile,drafts:[],evidence:[],ownerDecisions:[],tasks:[],activity:[],messages:[],requests:[],runway}});
    if(url.pathname.endsWith('/campaign-observation')){
      const body=route.request().postDataJSON();
      expect(body.projectVersion).toBe(4);
      expect(body.version).toBe(1);
      expect(body.observation.source).toBe('Owner notebook');
      expect(body.observation.metric_definition).toBe('Count relevant replies');
      expect(body.observation.source_reference).toBe('Notebook entry 17');
      runway.campaign.version=2;
      runway.campaign_actions.push({id:'d'.repeat(32),version:2,action:'manual_observation',status:'owner_reported',actor_id:'owner-fixture',owner_verified:true,payload_json:JSON.stringify({...body.observation,brief_revision:1,asset_id:assetId,asset_digest:'b'.repeat(64),launch_receipt:null,causality:'not_established'}),created_at:Date.now()/1000});
      return route.fulfill({json:runway});
    }
    if(url.pathname.endsWith('/campaign-internal-action')){
      const body=route.request().postDataJSON();
      expect(body.projectVersion).toBe(4);
      expect(body.version).toBe(runway.campaign.version);
      if(body.action==='internal_decision'){
        expect(body.payload).toMatchObject({decision:'pause',rationale:'One notebook note cannot prove campaign impact'});
        runway.campaign.version=3;runway.campaign.stage='learn';
        runway.campaign_actions.push({id:'e'.repeat(32),version:3,action:'internal_decision',status:'owner_reported_decision',actor_id:'owner-fixture',owner_verified:true,payload_json:JSON.stringify({decision:'pause',rationale:body.payload.rationale,actual_sample:2,required_sample:0,inconclusive:false,brief_revision:1,observation_action_ids:['d'.repeat(32)],execution_granted:false,causality:'not_established'}),created_at:Date.now()/1000});
      }else if(body.action==='internal_lesson'){
        expect(body.payload.decisionId).toBe('e'.repeat(32));
        runway.campaign.version=4;runway.campaign.stage='complete';
        runway.campaign_actions.push({id:'f'.repeat(32),version:4,action:'internal_lesson',status:'proposed_lesson',actor_id:'owner-fixture',owner_verified:true,payload_json:JSON.stringify({lesson:body.payload.lesson,context:body.payload.context,uncertainty:body.payload.uncertainty,revisit_condition:body.payload.revisitCondition,next_action:body.payload.nextAction,decision_id:'e'.repeat(32),brief_revision:1,causality:'not_established'}),created_at:Date.now()/1000});
      }else{
        expect(body.action).toBe('capability_request');
        expect(body.payload).toMatchObject({requiredScope:'One named channel and account',costStatus:'unknown'});
        runway.campaign.version=5;
        runway.campaign_actions.push({id:'9'.repeat(32),version:5,action:'capability_request',status:'request_only',actor_id:'owner-fixture',owner_verified:true,payload_json:JSON.stringify({blocked_task:body.payload.blockedTask,required_scope:body.payload.requiredScope,expected_benefit:body.payload.expectedBenefit,cost_status:body.payload.costStatus,cost_note:body.payload.costNote,brief_revision:1,capability_granted:false,external_effect:false}),created_at:Date.now()/1000});
      }
      return route.fulfill({json:runway});
    }
    return route.fulfill({status:404,json:{error:'Unexpected fixture request'}});
  });
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');}catch{}});
  await launch(page,request,baseURL!,'pane=work&open=campaign:current');
  const panel=page.getByRole('region',{name:'Standing marketing assignment'});
  await openManagement(panel);
  await panel.getByText('Relevant prior proposed learning · 1').click();
  await expect(panel.getByText('Ask founders about controls first')).toBeVisible();
  await expect(panel.getByText(/Interview note 4/)).toBeVisible();
  await panel.getByText('Owner-reported observations · 0').click();
  await panel.getByLabel('Source record or URL').fill('Notebook entry 17');
  await panel.getByLabel('Measurement period starts').fill(localInput(Date.now()-7200000));
  await panel.getByLabel('Measurement period ends').fill(localInput(Date.now()-3600000));
  await panel.getByLabel('Numerator').fill('3');
  await panel.getByLabel('Denominator').fill('2');
  await expect(panel.getByRole('button',{name:'Record owner observation'})).toBeDisabled();
  await panel.getByLabel('Numerator').fill('1');
  await panel.getByLabel('What you observed and how you interpret it').fill('One relevant reply, without launch attribution');
  await panel.getByLabel('Attribution limits').fill('No campaign launch or control group');
  await panel.getByRole('button',{name:'Record owner observation'}).click();
  await expect(panel.getByText('Owner-reported observations · 1')).toBeVisible();
  await expect(panel.getByText(/Verified owner receipt · source Notebook entry 17/)).toBeVisible();
  await expect(panel.getByText('Campaign workflow · align')).toBeVisible();
  await page.reload();await openManagement(panel);
  await panel.getByText('Owner-reported observations · 1').click();
  await expect(panel.getByText(/Verified owner receipt · source Notebook entry 17/)).toBeVisible();
  await panel.getByLabel('Reason for this decision').fill('One notebook note cannot prove campaign impact');
  await panel.getByRole('button',{name:'Record internal decision'}).click();
  await expect(panel.getByText('Campaign workflow · learn')).toBeVisible();
  await panel.getByLabel('Proposed lesson').fill('Ask about controls before making outcomes claims');
  await panel.getByLabel('Context',{exact:true}).fill('One owner notebook entry');
  await panel.getByLabel('Uncertainty',{exact:true}).fill('No launch or control group');
  await panel.getByLabel('Revisit when').fill('A separate authorized test yields evidence');
  await panel.getByLabel('Proposed next action').fill('Keep the draft internal');
  await panel.getByRole('button',{name:'Save proposed lesson'}).click();
  await expect(panel.getByText('Campaign workflow · complete')).toBeVisible();
  await panel.getByText('Campaign decisions and receipts · 3').click();
  await expect(panel.getByText(/Verified owner receipt · pause/)).toBeVisible();
  await expect(panel.getByText(/Ask about controls before making outcomes claims/)).toBeVisible();
  await panel.getByText('Launch readiness · blocked').click();
  await panel.getByLabel('Required action and destination scope').fill('One named channel and account');
  await panel.getByLabel('Expected benefit').fill('Learn from a bounded release');
  await panel.getByLabel('Cost source or uncertainty').fill('No account or price verified');
  await panel.getByRole('button',{name:'Record capability request'}).click();
  await expect(panel.getByText(/Verified owner receipt · scope One named channel and account/)).toBeVisible();
  await expect(panel.getByText('Campaign workflow · complete')).toBeVisible();
});
