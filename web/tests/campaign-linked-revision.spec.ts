import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('owner selects an approved linked revision into the internal source campaign',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  const sourceId='a'.repeat(32),revisionId='b'.repeat(32),audienceId='c'.repeat(32);
  const originalId='d'.repeat(32),revisedId='e'.repeat(32),approvalId='f'.repeat(32);
  const brief={audience:'Founders',problem:'Marketing time',hypothesis:'A concrete hook is clearer',priority_rationale:'Both checked founder notes describe the attention problem',proposition:'Configurable marketing employee',desired_behavior:'Ask for a demo',channel:'Owner reviewed draft',primary_metric:'Qualified replies',metric_definition:'Count relevant replies',guardrail:'No outcome claims',review_timing:'At owner review',non_goals:'No publishing'};
  const experiment={intervention:'One internal draft',target_population:'Founders',observation_window:'One week',metric_source:'Owner notes',decision_rule:'learning_only',minimum_sample:0};
  const profile={id:'marketing',display_name:'Marketing employee',product_summary:'Configurable marketing agents',audience:'Founders',goals:'Learn',voice:'',guardrails:'Internal only',channels:'',version:1,updated_at:1780000000};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Customer growth'}],agents:[{id:'marketing-main',name:'Marketing employee',role:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  const source:any={project:{id:sourceId,goal:'Internal founder message',scope:'internal_research_draft',status:'needs_review',version:4,run_count:3,max_runs:6,token_limit:150000,token_used:300,token_reserved:0,wait_reason:'Owner review needed',created_at:1780000000},steps:[],artifacts:[
    {id:audienceId,step_id:'1'.repeat(32),kind:'audience_note',content:JSON.stringify({audience:'Founders',problem:'Marketing time',evidence:[],limitations:'Anecdotes'}),digest:'c'.repeat(64),source_urls:'[]',created_at:1780000001},
    {id:originalId,step_id:'2'.repeat(32),kind:'post_angles',content:'Original draft',digest:'d'.repeat(64),source_urls:'[]',created_at:1780000002}
  ],source_metadata:[],inputs:[],reviews:[{id:'1'.repeat(32),artifact_id:originalId,artifact_digest:'d'.repeat(64),decision:'revision_requested',instruction:'Use a specific hook',actor_name:'Owner',created_at:1780000003}],campaign:{runway_id:sourceId,version:1,stage:'align',mode:'internal',owner_verified:true,owner_actor:'owner-fixture',source_artifact_id:audienceId,source_artifact_digest:'c'.repeat(64),asset_artifact_id:originalId,asset_artifact_digest:'d'.repeat(64),brief_json:JSON.stringify(brief),experiment_json:JSON.stringify(experiment),created_at:1780000004,updated_at:1780000004},campaign_revisions:[{id:'2'.repeat(32),version:1,actor_id:'owner-fixture',source_artifact_id:audienceId,source_artifact_digest:'c'.repeat(64),created_at:1780000004}],campaign_actions:[],revision_grants:[],executions:[]};
  const revision:any={project:{id:revisionId,goal:'Revise saved marketing draft angles',scope:'internal_revision_draft',status:'done',version:3,run_count:1,max_runs:1,token_limit:25000,token_used:300,token_reserved:0,wait_reason:'Exact draft approved',created_at:1780000010,source_runway_id:sourceId,source_review_id:'1'.repeat(32),source_artifact_id:originalId,source_artifact_digest:'d'.repeat(64)},steps:[],artifacts:[{id:revisedId,step_id:'3'.repeat(32),kind:'revision_angles',content:'Specific founder hook',digest:'e'.repeat(64),source_urls:'[]',created_at:1780000011}],source_metadata:[],inputs:[],reviews:[{id:approvalId,artifact_id:revisedId,artifact_digest:'e'.repeat(64),decision:'approved',instruction:'',actor_name:'Owner',created_at:1780000012}],campaign:null,campaign_revisions:[],campaign_actions:[],revision_grants:[],executions:[]};
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/meetings',route=>route.fulfill({json:[]}));
  await page.route('**/api/devices',route=>route.fulfill({json:{devices:[]}}));
  await page.route('**/api/marketing/**',route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:'Marketing employee',model:'fixture',sessionKey:'fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,runwayLiveEnabled:false,runwayArchiveEnabled:true,campaignBriefEnabled:true,fixtureCampaignEnabled:false,deferredRevisionEnabled:true,sharedGatewayEnabled:false,profile,drafts:[],evidence:[],ownerDecisions:[],tasks:[],activity:[],messages:[],requests:[],runway:revision}});
    if(url.pathname==='/api/marketing/runways')return route.fulfill({json:{projects:[{id:sourceId,goal:source.project.goal,status:source.project.status,created_at:1780000000,updated_at:1780000004,artifact_count:2},{id:revisionId,goal:revision.project.goal,status:'done',created_at:1780000010,updated_at:1780000012,artifact_count:1}]}});
    if(url.pathname===`/api/marketing/runways/${sourceId}`)return route.fulfill({json:source});
    if(url.pathname===`/api/marketing/runway/${sourceId}/campaign-adopt-revision`){
      const body=route.request().postDataJSON();
      expect(body).toMatchObject({projectVersion:4,version:1,revisionRunwayId:revisionId,revisionProjectVersion:3,revisionArtifactId:revisedId,revisionArtifactDigest:'e'.repeat(64),revisionReviewId:approvalId});
      source.campaign.version=2;source.campaign.asset_artifact_id=revisedId;source.campaign.asset_artifact_digest='e'.repeat(64);
      source.campaign_actions=[{id:'9'.repeat(32),version:2,action:'adopt_revision',status:'owner_selected_internal',actor_id:'owner-fixture',owner_verified:true,payload_json:JSON.stringify({revision_runway_id:revisionId,revision_artifact_id:revisedId,predecessor_id:originalId,brief_revision:1,external_effect:false,launch_authorized:false}),created_at:1780000013}];
      return route.fulfill({json:source});
    }
    return route.fulfill({status:404,json:{error:'Unexpected fixture request'}});
  });
  await page.goto('/#launch='+ticket);
  await page.getByRole('button',{name:'Work',exact:true}).click();
  const panel=page.getByRole('region',{name:'Standing marketing assignment'});
  await expect(panel.getByRole('button',{name:'Select for source campaign'})).toBeVisible();
  await panel.getByRole('button',{name:'Select for source campaign'}).click();
  await expect(panel.getByText('Approved revision selected for the original campaign')).toBeVisible();
  await expect(panel.getByText('Approved revision selected for this internal brief. Live launch remains blocked.')).toBeVisible();
  await panel.getByText('Campaign decisions and receipts · 1').click();
  await expect(panel.getByText(/Verified owner receipt · predecessor/)).toBeVisible();
});
