import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('Work shows the simulated campaign from brief through proposed lesson',async({page,request,baseURL})=>{
  const origin=baseURL!;
  const key=fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim();
  const issued=await request.post(origin+'/api/auth/launch',{headers:{Origin:origin},data:{key}});
  expect(issued.status()).toBe(200);
  const {ticket}=await issued.json();
  const source='fixture://source/founder-time';
  const projectId='f'.repeat(32),sourceId='a'.repeat(32),assetId='b'.repeat(32);
  const artifacts=[
    {id:sourceId,step_id:'1'.repeat(32),kind:'audience_note',content:JSON.stringify({audience:'Founders',problem:'Marketing time',evidence:[{sourceUrl:source,quote:'Fixture quote',inference:'Synthetic only'}],limitations:'Fixture evidence only'}),digest:'a'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000001},
    {id:assetId,step_id:'2'.repeat(32),kind:'post_angles',content:JSON.stringify({angles:[{title:'Controls',hook:'Review the work',why:'Fixture concern',claimLimit:'No outcome claim',sourceUrl:source}]}),digest:'b'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000002},
    {id:'c'.repeat(32),step_id:'3'.repeat(32),kind:'review_packet',content:JSON.stringify({summary:'Fixture packet',unsupportedClaims:['Demand is proven'],nextOwnerDecision:'Review draft',recommendation:'Keep it internal'}),digest:'c'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000003}
  ];
  const profile={id:'marketing',display_name:'Marketing employee',product_summary:'Configurable marketing agents',audience:'Founders',goals:'Learn from a draft',voice:'',guardrails:'Internal only',channels:'',version:1,updated_at:1780000000};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Customer growth'}],agents:[{id:'marketing-main',name:'Marketing employee',role:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  const runway:any={project:{id:projectId,goal:'SIMULATED campaign',status:'needs_review',version:4,run_count:3,max_runs:6,token_limit:150000,token_used:0,token_reserved:0,wait_reason:'Owner review needed',deadline_at:Date.now()/1000+600},steps:[],artifacts,source_metadata:[{url:source,digest:'a'.repeat(64),captured_at:1780000001}],inputs:[],reviews:[],campaign:null,campaign_revisions:[],campaign_actions:[],executions:[]};
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/meetings',route=>route.fulfill({json:[]}));
  await page.route('**/api/devices',route=>route.fulfill({json:{devices:[]}}));
  await page.route('**/api/marketing/**',async route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:profile.display_name,model:'fixture',sessionKey:'fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,runwayLiveEnabled:false,runwayArchiveEnabled:true,campaignBriefEnabled:true,fixtureCampaignEnabled:true,deferredRevisionEnabled:false,sharedGatewayEnabled:false,profile,drafts:[],evidence:[],ownerDecisions:[],tasks:[],activity:[],messages:[],requests:[],runway}});
    if(url.pathname==='/api/marketing/runways')return route.fulfill({json:{projects:[]}});
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    if(url.pathname.endsWith('/campaign-brief')){
      const body=route.request().postDataJSON();
      expect(body.projectVersion).toBe(runway.project.version);
      runway.campaign={runway_id:projectId,version:1,stage:'align',mode:'fixture',owner_verified:true,owner_actor:'fixture-owner',source_artifact_id:sourceId,source_artifact_digest:'a'.repeat(64),asset_artifact_id:assetId,asset_artifact_digest:'b'.repeat(64),brief_json:JSON.stringify(body.brief),experiment_json:JSON.stringify(body.experiment),created_at:1780000004,updated_at:1780000004};
      runway.campaign_revisions=[{id:'d'.repeat(32),version:1,actor_id:'fixture-owner',source_artifact_id:sourceId,source_artifact_digest:'a'.repeat(64),created_at:1780000004}];
      return route.fulfill({json:runway});
    }
    if(url.pathname.endsWith('/review')){
      const body=route.request().postDataJSON();
      runway.reviews.push({id:'e'.repeat(32),artifact_id:body.artifactId,artifact_digest:body.digest,decision:body.decision,instruction:'',actor_name:'Fixture owner',created_at:1780000005});
      runway.project.version++;runway.project.status='done';
      return route.fulfill({json:runway});
    }
    if(url.pathname.endsWith('/campaign-action')){
      const body=route.request().postDataJSON();
      expect(body.version).toBe(runway.campaign.version);
      expect(body.projectVersion).toBe(runway.project.version);
      const stage:Record<string,string>={align:'launch',launch:'measure',measure:'measure',decide:'learn',learn:'complete'};
      runway.campaign.version++;runway.campaign.stage=stage[body.action];
      runway.campaign_actions.push({id:String(runway.campaign.version).repeat(32).slice(0,32),version:runway.campaign.version,action:body.action,status:body.action==='launch'?'simulated':'recorded',actor_id:'fixture-owner',payload_json:JSON.stringify({...body.payload,brief_revision:1,...(body.action==='launch'?{receipt:'SIMULATED_ONLY',external_effect:false}:{})}),created_at:1780000004+runway.campaign.version});
      return route.fulfill({json:runway});
    }
    return route.fulfill({status:404,json:{error:'Unexpected fixture request'}});
  });
  await page.setViewportSize({width:1280,height:900});
  await page.goto('/#launch='+ticket);
  await page.getByRole('button',{name:'Work',exact:true}).click();
  const panel=page.getByRole('region',{name:'Standing marketing assignment'});
  await expect(panel.getByText(/Isolated fixture ledger/)).toBeVisible();
  await panel.getByRole('button',{name:'Edit campaign brief'}).click();
  for(const [label,value] of Object.entries({'Audience':'Founders','Customer problem':'Marketing time','Opportunity hypothesis':'A bounded draft may clarify the offer','Proposition to test':'Configurable marketing employee','Desired customer behavior':'Ask for a demo','Selected channel':'Owner reviewed draft','Primary outcome metric':'Qualified replies','How the metric is counted':'Count relevant replies','Claim or conduct guardrail':'No outcome guarantee','Intervention':'One fixture draft','Target population':'Founders','Observation window and timezone':'Seven days','Source of observations':'Fixture observation'}))await panel.getByLabel(label,{exact:true}).fill(value);
  await panel.getByLabel('Decision rule').selectOption('minimum_sample');
  await panel.getByLabel('Minimum observations').fill('1');
  await panel.getByRole('button',{name:'Save campaign brief'}).click();
  await expect(panel.getByText('Campaign workflow · align')).toBeVisible();
  await panel.getByRole('button',{name:'Review draft angles'}).click();
  await panel.getByRole('button',{name:'Approve exact draft'}).click();
  await panel.getByRole('button',{name:'Align approved fixture draft'}).click();
  await expect(panel.getByText('Campaign workflow · launch')).toBeVisible();
  await panel.getByRole('button',{name:'Record fake launch'}).click();
  await expect(panel.getByText('Campaign workflow · measure')).toBeVisible();
  await panel.getByLabel('Numerator').fill('1');
  await panel.getByLabel('Denominator').fill('1');
  await panel.getByRole('button',{name:'Record fixture observation'}).click();
  await panel.getByRole('button',{name:'Record fixture decision'}).click();
  await expect(panel.getByText('Campaign workflow · learn')).toBeVisible();
  await panel.getByLabel('Proposed lesson').fill('Controls may improve clarity');
  await panel.getByLabel('Context',{exact:true}).fill('Founders, one synthetic draft');
  await panel.getByLabel('Uncertainty').fill('No real audience response');
  await panel.getByLabel('Revisit when').fill('Real evidence arrives');
  await panel.getByLabel('Next action').fill('Remain paused');
  await panel.getByRole('button',{name:'Save proposed lesson'}).click();
  await expect(panel.getByText('Campaign workflow · complete')).toBeVisible();
  await panel.getByText('Campaign decisions and receipts · 5').click();
  await expect(panel.getByText(/SIMULATED_ONLY/)).toBeVisible();
  await page.reload();await page.getByRole('button',{name:'Work',exact:true}).click();
  await expect(panel.getByText('Campaign workflow · complete')).toBeVisible();
});
