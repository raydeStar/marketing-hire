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
const savedWork=(panel:Locator,summary:RegExp)=>panel.locator('details.runway-artifact').filter({has:panel.page().locator(':scope > summary',{hasText:summary})});

test('the Work campaign row opens the simulated campaign from brief through proposed lesson',async({page,request,baseURL})=>{
  const source='fixture://source/founder-time';
  const projectId='f'.repeat(32),sourceId='a'.repeat(32),assetId='b'.repeat(32);
  const artifacts=[
    {id:sourceId,step_id:'1'.repeat(32),kind:'audience_note',content:JSON.stringify({audience:'Founders',problem:'Marketing time',evidence:[{sourceUrl:source,quote:'Fixture quote',inference:'Synthetic only'}],limitations:'Fixture evidence only'}),digest:'a'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000001},
    {id:assetId,step_id:'2'.repeat(32),kind:'post_angles',content:JSON.stringify({angles:[{title:'Controls',hook:'Review the work',why:'Fixture concern',claimLimit:'No outcome claim',sourceUrl:source}]}),digest:'b'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000002},
    {id:'c'.repeat(32),step_id:'3'.repeat(32),kind:'review_packet',content:JSON.stringify({summary:'Fixture packet',unsupportedClaims:['Demand is proven'],qualitativeReview:{audienceFit:'Provisional founder fit',clarity:'One clear opening',productTruth:'No outcome proof',channelSuitability:'Internal draft only',desiredAction:'Request owner review'},nextOwnerDecision:'Review draft',recommendation:'Keep it internal'}),digest:'c'.repeat(64),source_urls:JSON.stringify([source]),created_at:1780000003}
  ];
  const profile={id:'marketing',display_name:'Marketing employee',product_summary:'Configurable marketing agents',audience:'Founders',goals:'Learn from a draft',voice:'',guardrails:'Internal only',channels:'',version:1,updated_at:1780000000};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing',purpose:'Customer growth'}],agents:[{id:'marketing-main',name:'Marketing employee',role:'Marketing',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  const runway:any={project:{id:projectId,goal:'SIMULATED campaign',status:'needs_review',version:4,run_count:3,max_runs:6,token_limit:150000,token_used:0,token_reserved:0,wait_reason:'Owner review needed',deadline_at:Date.now()/1000+600},steps:[],artifacts,source_metadata:[{url:source,digest:'a'.repeat(64),captured_at:1780000001}],inputs:[],reviews:[],campaign:null,campaign_revisions:[],campaign_actions:[],executions:[]};
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/devices',route=>route.fulfill({json:{devices:[]}}));
  await page.route('**/api/marketing/**',async route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:profile.display_name,model:'fixture',sessionKey:'fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,runwayLiveEnabled:false,runwayArchiveEnabled:true,campaignBriefEnabled:true,fixtureCampaignEnabled:true,deferredRevisionEnabled:true,sharedGatewayEnabled:false,profile,drafts:[],evidence:[],ownerDecisions:[],tasks:[],activity:[],messages:[],requests:[],runway}});
    if(url.pathname==='/api/marketing/runways')return route.fulfill({json:{projects:[]}});
    if(url.pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    if(url.pathname==='/api/marketing/runway/fixture/lessons'){
      const body=route.request().postDataJSON();
      expect(body).toEqual({audience:'Founders',excludeCampaignId:projectId});
      return route.fulfill({json:{lessons:[{campaign_id:'e'.repeat(32),action_id:'7'.repeat(32),created_at:1780000000,
        lesson:{lesson:'Reviewable controls may clarify the offer',context:'Prior founder fixture',uncertainty:'No real audience response',revisit_condition:'Real evidence arrives',next_action:'Remain paused'},
        brief:{audience:'Founders'},decision:{decision:'pause',rationale:'Small synthetic sample',actual_sample:2,required_sample:2},
        observations:[{action_id:'8'.repeat(32),source:'Fixture observation',captured_at:1780000000,period_start:1779996400,period_end:1779999970,timezone:'America/Denver',metric_definition:'Count relevant replies',value_type:'actual',numerator:1,denominator:2,attribution_limitations:'Synthetic only'}]}]}});
    }
    if(url.pathname.endsWith('/campaign-brief')){
      const body=route.request().postDataJSON();
      expect(body.projectVersion).toBe(runway.project.version);
      runway.campaign={runway_id:projectId,version:1,stage:'align',mode:'fixture',owner_verified:true,owner_actor:'fixture-owner',source_artifact_id:sourceId,source_artifact_digest:'a'.repeat(64),asset_artifact_id:assetId,asset_artifact_digest:'b'.repeat(64),brief_json:JSON.stringify(body.brief),experiment_json:JSON.stringify(body.experiment),created_at:1780000004,updated_at:1780000004};
      runway.campaign_revisions=[{id:'d'.repeat(32),version:1,actor_id:'fixture-owner',source_artifact_id:sourceId,source_artifact_digest:'a'.repeat(64),created_at:1780000004}];
      return route.fulfill({json:runway});
    }
    if(url.pathname.endsWith('/review')&&route.request().method()==='POST'){
      const body=route.request().postDataJSON();
      runway.reviews.push({id:String(runway.reviews.length+5).repeat(32).slice(0,32),artifact_id:body.artifactId,artifact_digest:body.digest,decision:body.decision,instruction:body.instruction||'',actor_name:'Fixture owner',created_at:1780000005+runway.reviews.length});
      runway.project.version++;runway.project.status=body.decision==='approved'?'done':'needs_review';
      return route.fulfill({json:runway});
    }
    if(url.pathname.endsWith('/campaign-action')){
      const body=route.request().postDataJSON();
      expect(body.version).toBe(runway.campaign.version);
      expect(body.projectVersion).toBe(runway.project.version);
      const stage:Record<string,string>={revise_asset:'align',align:'launch',launch:'measure',measure:'measure',decide:'learn',learn:'complete'};
      if(body.action==='revise_asset'){
        const revisedId='d'.repeat(32),revisedDigest='d'.repeat(64);
        runway.artifacts.push({id:revisedId,step_id:'4'.repeat(32),kind:'revision_angles',content:JSON.stringify({angles:[{title:'Specific controls',hook:'Ask what the agent can do',why:'Fixture revision',claimLimit:'No outcome claim',sourceUrl:source}],revisionOf:assetId,qa:{threeSourcedAngles:true}}),digest:revisedDigest,source_urls:JSON.stringify([source]),created_at:1780000006});
        runway.campaign.asset_artifact_id=revisedId;runway.campaign.asset_artifact_digest=revisedDigest;
      }
      runway.campaign.version++;runway.campaign.stage=stage[body.action];
      runway.campaign_actions.push({id:String(runway.campaign.version).repeat(32).slice(0,32),version:runway.campaign.version,action:body.action,status:body.action==='launch'?'simulated':'recorded',actor_id:'fixture-owner',payload_json:JSON.stringify({...body.payload,brief_revision:1,...(body.action==='launch'?{receipt:'SIMULATED_ONLY',external_effect:false}:{})}),created_at:1780000004+runway.campaign.version});
      return route.fulfill({json:runway});
    }
    return route.fulfill({status:404,json:{error:'Unexpected fixture request'}});
  });
  await page.setViewportSize({width:1280,height:900});
  await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed:*','yes');}catch{}});
  await launch(page,request,baseURL!,'pane=work');
  // Work lists the campaign; its row opens the owner's review desk in the work window.
  await page.getByRole('tab',{name:'Campaigns',exact:true}).click();
  const row=page.getByRole('region',{name:'Campaigns'}).getByRole('button',{name:/SIMULATED campaign/});
  await expect(row).toContainText('3 deliverables · 0 reviews');
  await expect(row).toContainText('Waiting for review');
  await row.click();
  await expect(page).toHaveURL(/pane=work&open=campaign%3Acurrent/);
  await expect(page.getByRole('region',{name:'SIMULATED campaign'}).locator('.fe-window-title small')).toHaveText('Campaign');
  const panel=page.getByRole('region',{name:'Standing marketing assignment'});
  const desk=page.getByRole('region',{name:'Campaign review workspace'});
  await expect(desk.getByRole('heading',{name:'Three draft post angles'})).toBeVisible();
  await expect(desk.getByRole('heading',{name:'Marketing’s self-review'})).toBeVisible();
  await expect(desk).toContainText('No outcome proof');
  await openManagement(panel);
  await expect(panel.getByText(/Demo workspace: sample data, no model calls/)).toBeVisible();
  const packet=savedWork(panel,/Owner review packet · saved/);
  await packet.locator('summary').click();
  await expect(packet.getByText('Employee qualitative assessment · owner review pending')).toBeVisible();
  await expect(packet.getByText('No outcome proof')).toBeVisible();
  await panel.getByRole('button',{name:'Edit campaign brief'}).click();
  for(const [label,value] of Object.entries({'Audience':'Founders','Customer problem':'Marketing time','Opportunity hypothesis':'A bounded draft may clarify the offer','Why prioritize this opportunity':'Founders in both checked notes raised this problem','Proposition to test':'Configurable marketing employee','Desired customer behavior':'Ask for a demo','Selected channel':'Owner reviewed draft','Primary outcome metric':'Qualified replies','How the metric is counted':'Count relevant replies','Claim or conduct guardrail':'No outcome guarantee','Intervention':'One fixture draft','Target population':'Founders','Observation window and timezone':'Seven days','Source of observations':'Fixture observation'}))await panel.getByLabel(label,{exact:true}).fill(value);
  await panel.getByLabel('Decision rule').selectOption('minimum_sample');
  await panel.getByLabel('Minimum observations').fill('1');
  await panel.getByRole('button',{name:'Save campaign brief'}).click();
  await expect(panel.getByText('Campaign workflow · align')).toBeVisible();
  await panel.getByText('Relevant prior simulated learning · 1').click();
  await expect(panel.getByText('SIMULATED ONLY · Reviewable controls may clarify the offer')).toBeVisible();
  await expect(panel.getByText(/Prior founder fixture/)).toBeVisible();
  await expect(panel.getByText(/actual 1\/2 · Count relevant replies/)).toBeVisible();
  await panel.getByRole('button',{name:'Review draft angles'}).click();
  const original=savedWork(panel,/Three draft post angles · saved/);
  await expect(original).toHaveAttribute('open','');
  await original.getByRole('button',{name:'Request changes',exact:true}).click();
  await original.getByLabel('What should change?').fill('Make the first hook specific.');
  await original.getByRole('button',{name:'Save revision request'}).click();
  await expect(panel.getByRole('button',{name:'Create simulated asset revision'})).toBeVisible();
  await panel.getByRole('button',{name:'Create simulated asset revision'}).click();
  const revised=savedWork(panel,/Revised post angles · saved/);
  await revised.locator('summary').click();
  await revised.getByRole('button',{name:'Approve',exact:true}).click();
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
  await panel.getByText('Campaign decisions and receipts · 6').click();
  await expect(panel.getByText(/SIMULATED_ONLY/)).toBeVisible();
  await page.reload();await openManagement(panel);
  await expect(panel.getByText('Campaign workflow · complete')).toBeVisible();
});
