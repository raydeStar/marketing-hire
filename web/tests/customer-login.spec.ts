import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
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

test('customer sign-in offers only configured providers and retains local recovery',async({page})=>{
  const calls:string[]=[];
  await page.route('**/api/**',route=>{
    const path=new URL(route.request().url()).pathname;calls.push(path);
    if(path==='/api/auth/customer')return route.fulfill({json:{enabled:true,origin:'https://workspace.example.test',providers:['google']}});
    return route.fulfill({status:401,json:{error:'Sign in first.'}});
  });
  await page.goto('/');
  const google=page.getByRole('link',{name:'Continue with Google'});
  await expect(google).toHaveAttribute('href','https://workspace.example.test/api/auth/customer/login?provider=google');
  await expect(page.getByRole('link',{name:'Continue with Microsoft'})).toHaveCount(0);
  await page.getByRole('button',{name:'Other workspace access'}).click();
  await expect(google).toHaveCount(0);
  expect(calls.every(path=>!path.startsWith('/api/marketing/'))).toBe(true);
});

test('customer sign-in displays safe callback failure without provider details',async({page})=>{
  await page.route('**/api/**',route=>new URL(route.request().url()).pathname==='/api/auth/customer'
    ?route.fulfill({json:{enabled:true,origin:'https://workspace.example.test',providers:['google','microsoft']}})
    :route.fulfill({status:401,json:{error:'Sign in first.'}}));
  await page.goto('/#sign-in-error=not-completed');
  await expect(page.getByRole('link',{name:'Continue with Microsoft'})).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('Sign-in wasn’t completed');
});

for(const targetKind of ['email','account'] as const)test(`${targetKind} invitation survives sign-in, requires explicit acceptance and opens its campaign`,async({page})=>{
  const token='a'.repeat(64),project='b'.repeat(32);
  let signedIn=false,accepted=0;
  const calls:string[]=[];
  await page.setViewportSize({width:390,height:850});
  await page.route('**/api/**',async route=>{
    const request=route.request(),path=new URL(request.url()).pathname;calls.push(request.url());
    if(path==='/api/auth/customer')return route.fulfill({json:{enabled:true,origin:'https://workspace.example.test',providers:['google']}});
    if(path==='/api/session'&&signedIn)return route.fulfill({json:{id:'browser-fixture',accountId:'person-fixture',principalId:'person-fixture',csrf:'fixture-csrf',owner:false,name:'Reviewer'}});
    if(path==='/api/marketing/campaigns/invitations/preview'){
      expect(request.postDataJSON()).toEqual({token});expect(request.headers()['x-csrf']).toBe('fixture-csrf');
      return route.fulfill({json:{projectId:project,campaignName:'Founder campaign',email:'reviewer@example.test',recipient:targetKind==='account'?'Microsoft reviewer':'reviewer@example.test',targetKind,provider:targetKind==='account'?'microsoft':'google',expiresAt:new Date(Date.now()+86400000).toISOString(),scope:'Read the shared draft, comment and request changes.'}});
    }
    if(path==='/api/marketing/campaigns/invitations/accept'){
      accepted++;expect(request.postDataJSON()).toEqual({token});return route.fulfill({json:{projectId:project}});
    }
    return route.fulfill({status:401,json:{error:'Fixture route is unavailable.'}});
  });
  await page.goto('/#invite='+token);
  await expect(page.getByRole('link',{name:'Continue with Google'})).toBeVisible();
  expect(new URL(page.url()).hash).toBe('');
  signedIn=true;await page.reload();
  await expect(page.getByRole('heading',{name:'Founder campaign'})).toBeVisible();
  if(targetKind==='account'){
    await expect(page.getByText('Microsoft reviewer',{exact:true})).toBeVisible();
    await expect(page.getByText(/bound to your exact sign-in account/)).toBeVisible();
  }
  expect(accepted).toBe(0);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await page.getByRole('button',{name:'Accept and open campaign'}).click();
  await expect(page).toHaveURL(new RegExp('#campaign='+project+'$'));
  expect(accepted).toBe(1);expect(calls.every(url=>!url.includes(token))).toBe(true);
  expect(await page.evaluate(()=>sessionStorage.getItem('first-employee-campaign-invitation'))).toBeNull();
});

test('invalid invitation provides account switch without an accept button',async({page})=>{
  await page.route('**/api/**',route=>{
    const path=new URL(route.request().url()).pathname;
    if(path==='/api/auth/customer')return route.fulfill({json:{enabled:true,origin:'https://workspace.example.test',providers:['google']}});
    if(path==='/api/session')return route.fulfill({json:{id:'browser-fixture',accountId:'person-fixture',principalId:'person-fixture',csrf:'fixture-csrf',owner:false}});
    if(path==='/api/marketing/campaigns/invitations/preview')return route.fulfill({status:409,json:{error:'This invitation is expired or belongs to a different verified sign-in account.'}});
    return route.fulfill({status:401,json:{error:'Fixture route unavailable.'}});
  });
  await page.goto('/#invite='+'c'.repeat(64));
  await expect(page.getByRole('alert')).toContainText('different verified sign-in account');
  await expect(page.getByRole('button',{name:'Accept and open campaign'})).toHaveCount(0);
  await expect(page.getByRole('link',{name:'Sign in with another Google account'})).toBeVisible();
});

for(const targetKind of ['email','account'] as const)test(`owner creates and revokes a scoped ${targetKind} reviewer invitation from campaign work`,async({page,request,baseURL})=>{
  const origin=baseURL!;
  const id='d'.repeat(32),artifactId='e'.repeat(32),digest='f'.repeat(64);
  const invitationId='a'.repeat(32),link='https://workspace.example.test/#invite='+'b'.repeat(64);
  const invitations:any[]=[];let created=0,revoked=0;
  const accountId='c'.repeat(32);
  const profile={id:'marketing',display_name:'Marketing agent',product_summary:'Fixture offer',audience:'Founders',voice:'Plain',goals:'Learn',guardrails:'No publishing',channels:'',version:1,updated_at:1780000000};
  const runway={project:{id,goal:'Reviewer invitation fixture',criteria:'Saved draft',scope:'internal',status:'needs_review',version:1,run_count:1,max_runs:1,token_limit:1000,token_used:20,token_reserved:0,max_active_seconds:60,created_at:1780000000},
    artifacts:[{id:artifactId,kind:'post_angles',digest,content:'Fixture draft',source_urls:'[]',created_at:1780000000,step_id:'fixture'}],steps:[],inputs:[],reviews:[],executions:[],campaign_actions:[],
    campaign:{asset_artifact_id:artifactId,asset_artifact_digest:digest,version:1,stage:'align',mode:'fixture',brief_json:'{}',experiment_json:'{}'}};
  const directory={version:1,departments:[{id:'marketing',name:'Marketing'}],agents:[{id:'marketing-main',name:'Marketing agent',role:'Drafts',departmentId:'marketing',kind:'employee',runtimeKey:'marketing'}]};
  await page.route('**/api/auth/customer',route=>route.fulfill({json:{enabled:true,origin:'https://workspace.example.test',providers:['google']}}));
  await page.route('**/api/organization',route=>route.fulfill({json:{directory,canConfigure:true}}));
  await page.route('**/api/marketing/**',route=>{
    const pathname=new URL(route.request().url()).pathname;
    if(pathname==='/api/marketing/invitation-accounts')return route.fulfill({json:{accounts:[{id:accountId,name:'Microsoft reviewer',email:'reviewer@example.test',emailVerified:false,provider:'microsoft'}]}});
    if(pathname==='/api/marketing/state')return route.fulfill({json:{employee:{name:'Marketing agent',model:'fixture'},connection:{status:'connected'},taskStoreAvailable:true,canConfigure:true,profile,runway,drafts:[],tasks:[],activity:[],messages:[],requests:[]}});
    if(pathname.endsWith('/invitations')&&route.request().method()==='GET')return route.fulfill({json:{invitations}});
    if(pathname.endsWith('/invitations')){
      expect(route.request().postDataJSON()).toEqual(targetKind==='account'?{accountId,expiresInHours:24}:{email:'reviewer@example.test',provider:'google',expiresInHours:24});created++;
      const item={id:invitationId,email:'reviewer@example.test',recipient:targetKind==='account'?'Microsoft reviewer':'reviewer@example.test',targetKind,provider:targetKind==='account'?'microsoft':'google',expiresAt:new Date(Date.now()+86400000).toISOString(),status:'pending',acceptedBy:null};invitations.push(item);
      return route.fulfill({json:{...item,url:link,scope:'Review shared draft only'}});
    }
    if(pathname.endsWith('/revoke')){revoked++;invitations[0].status='revoked';return route.fulfill({json:{}});}
    if(pathname.endsWith('/access'))return route.fulfill({json:{members:[]}});
    if(pathname==='/api/marketing/history')return route.fulfill({json:{items:[],nextCursor:null}});
    return route.fulfill({status:404,json:{error:'Fixture route unavailable.'}});
  });
  try{
    await page.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');}catch{}});
    await launch(page,request,origin);
    await page.getByRole('navigation',{name:'Chat or work'}).getByRole('button',{name:'Work'}).click();
    await page.getByRole('region',{name:'Campaigns'}).getByRole('button',{name:/Reviewer invitation fixture/}).click();
    await page.getByRole('region',{name:'Campaign review workspace'}).getByRole('button',{name:'Activity & sharing',exact:true}).click();
    const panel=page.getByRole('region',{name:'Invite a campaign reviewer'});
    if(targetKind==='account'){
      await panel.getByLabel('Invite by').selectOption('account');
      await expect(panel.getByRole('button',{name:'Create invitation link'})).toBeDisabled();
      await expect(panel.getByLabel('Reviewer email')).toHaveCount(0);
      await panel.getByLabel('Reviewer account').selectOption(accountId);
    }else await panel.getByLabel('Reviewer email').fill('reviewer@example.test');
    await panel.getByLabel('Link expires after').selectOption('24');
    await panel.getByRole('button',{name:'Create invitation link'}).click();
    await expect(panel.getByLabel('Invitation link')).toHaveValue(link);expect(created).toBe(1);
    if(targetKind==='account')await expect(panel).toContainText('Exact account');
    await expect(panel).toContainText('You approve employee work.');
    await panel.getByRole('button',{name:'Revoke invitation'}).click();
    await expect(panel.getByLabel('Invitation link')).toHaveCount(0);
    await expect(panel).toContainText('revoked');expect(revoked).toBe(1);
  }finally{
    await page.evaluate(async()=>{const session=await fetch('/api/session').then(r=>r.json());await fetch('/api/devices/'+session.id+'/revoke',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:'{}'});}).catch(()=>{});
  }
});
