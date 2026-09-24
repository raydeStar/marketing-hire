import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

function localInput(timestamp:number){
  const date=new Date(timestamp);
  const pad=(value:number)=>String(value).padStart(2,'0');
  return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

test('owner can record sourced internal context without advancing launch',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  const id='f'.repeat(32),sourceId='a'.repeat(32),assetId='b'.repeat(32);
  const profile={id:'marketing',display_name:'Marketing employee',product_summary:'Configurable marketing agents',audience:'Founders',goals:'Learn',voice:'',guardrails:'Internal only',channels:'',version:1,updated_at:1780000000};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Customer growth'}],agents:[{id:'marketing-main',name:'Marketing employee',role:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  const brief={audience:'Founders',problem:'Marketing time',hypothesis:'A small draft may clarify the offer',proposition:'Configurable marketing employee',desired_behavior:'Ask for a demo',channel:'Owner reviewed draft',primary_metric:'Qualified replies',metric_definition:'Count relevant replies',guardrail:'No outcome claim',review_timing:'At owner review',non_goals:'No publishing'};
  const experiment={intervention:'One internal draft',target_population:'Founders',observation_window:'One week',metric_source:'Owner notebook',decision_rule:'learning_only',minimum_sample:0};
  const runway:any={project:{id,goal:'Internal founder message',scope:'internal_research_draft',status:'needs_review',version:4,run_count:3,max_runs:6,token_limit:150000,token_used:300,token_reserved:0,wait_reason:'Owner review needed'},steps:[],artifacts:[
    {id:sourceId,step_id:'1'.repeat(32),kind:'audience_note',content:JSON.stringify({audience:'Founders',problem:'Marketing time',evidence:[],limitations:'Hypothesis only'}),digest:'a'.repeat(64),source_urls:'[]',created_at:1780000001},
    {id:assetId,step_id:'2'.repeat(32),kind:'post_angles',content:JSON.stringify({angles:[]}),digest:'b'.repeat(64),source_urls:'[]',created_at:1780000002}
  ],source_metadata:[],inputs:[],reviews:[],campaign:{runway_id:id,version:1,stage:'align',mode:'internal',owner_verified:true,owner_actor:'owner-fixture',source_artifact_id:sourceId,source_artifact_digest:'a'.repeat(64),asset_artifact_id:assetId,asset_artifact_digest:'b'.repeat(64),brief_json:JSON.stringify(brief),experiment_json:JSON.stringify(experiment),created_at:1780000003,updated_at:1780000003},campaign_revisions:[{id:'c'.repeat(32),version:1,actor_id:'owner-fixture',source_artifact_id:sourceId,source_artifact_digest:'a'.repeat(64),created_at:1780000003}],campaign_actions:[],executions:[]};
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/meetings',route=>route.fulfill({json:[]}));
  await page.route('**/api/devices',route=>route.fulfill({json:{devices:[]}}));
  await page.route('**/api/marketing/**',route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:'Marketing employee',model:'fixture',sessionKey:'fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,runwayLiveEnabled:false,runwayArchiveEnabled:false,campaignBriefEnabled:true,fixtureCampaignEnabled:false,deferredRevisionEnabled:true,sharedGatewayEnabled:false,profile,drafts:[],evidence:[],ownerDecisions:[],tasks:[],activity:[],messages:[],requests:[],runway}});
    if(url.pathname.endsWith('/campaign-observation')){
      const body=route.request().postDataJSON();
      expect(body.projectVersion).toBe(4);
      expect(body.version).toBe(1);
      expect(body.observation.source).toBe('Owner notebook');
      expect(body.observation.metric_definition).toBe('Count relevant replies');
      expect(body.observation.source_reference).toBe('Notebook entry 17');
      runway.campaign.version=2;
      runway.campaign_actions.push({id:'d'.repeat(32),version:2,action:'manual_observation',status:'owner_reported',actor_id:'owner-fixture',owner_verified:true,payload_json:JSON.stringify({...body.observation,brief_revision:1,launch_receipt:null,causality:'not_established'}),created_at:Date.now()/1000});
      return route.fulfill({json:runway});
    }
    return route.fulfill({status:404,json:{error:'Unexpected fixture request'}});
  });
  await page.goto('/#launch='+ticket);
  await page.getByRole('button',{name:'Work',exact:true}).click();
  const panel=page.getByRole('region',{name:'Standing marketing assignment'});
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
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await panel.getByText('Owner-reported observations · 1').click();
  await expect(panel.getByText(/Verified owner receipt · source Notebook entry 17/)).toBeVisible();
});
