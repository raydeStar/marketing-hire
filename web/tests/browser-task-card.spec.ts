import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('Chrome chat review shows exact authority and sends pause, takeover, resume and close controls',async({page})=>{
  // A UI fixture only: runtime approval/dispatch tests separately exercise the actual host boundary.
  test.setTimeout(45000);await page.setViewportSize({width:1440,height:1000});
  const now=new Date().toISOString(),id='c'.repeat(32),commands:string[]=[];
  const limits={modelCalls:8,toolCalls:12,maxOutputTokens:4096,seconds:600,repairs:0,maxTotalTokens:64000,requireCertifiedTokenBound:false};
  const run:any={id,version:1,state:'awaitingApproval',summary:'Review the website task and its allowance before Chrome opens',created:now,updated:now,modelCalls:1,toolCalls:0,repairs:0,evidence:[],goal:{kind:'conversation',objective:'Open Chrome to inspect the fictional desk',provider:{kind:'scripted',model:'fixture',reasoning:'high'},limits,criteria:[],readScope:[]},approval:{id:'scope-review',digest:'fixture-scope',decision:'pending',expires:new Date(Date.now()+900000).toISOString(),action:{name:'browser_task',path:'',content:'{}'}},browser:{sessionId:id,phase:'review',epoch:0,scope:{objective:'Inspect the fictional desk',startUrl:'https://example.com/',hosts:['example.com'],limits},receipts:[],activeSeconds:0}};
  await page.route('**/api/state',async route=>{const response=await route.fetch();const state=await response.json();await route.fulfill({response,json:{...state,browserAvailable:true,runs:[run],chats:[{id:id+'-user',role:'user',content:run.goal.objective,created:now}]}});});
  await page.route('**/api/runs/'+id+'/approve',async route=>{
    const request=route.request().postDataJSON();expect(request.remember).toBeUndefined();expect(request.allow).toBe(true);
    commands.push(run.browser.phase==='review'?'scope-approved':'action-approved');
    run.browser.authorizedUntil=new Date(Date.now()+7200000).toISOString();run.browser.phase='working';run.state='running';run.approval.decision='consumed';run.summary='Inspecting the fictional desk';
    run.browser.page={url:'https://example.com/',title:'Fictional desk',snapshot:'',version:'current'};
    await route.fulfill({json:run});
  });
  await page.route('**/api/runs/'+id+'/browser/*',async route=>{
    const command=new URL(route.request().url()).pathname.split('/').at(-1)!;commands.push(command);
    run.browser.phase=command==='close'?'closed':command==='resume'?'working':command==='pause'?'paused':'takeover';run.state=command==='resume'?'running':command==='close'?'cancelled':'paused';run.summary=command==='resume'?'Inspecting the fictional desk':'AI is stopped';
    run.browser.pendingAction=null;run.approval.decision='cancelled';await route.fulfill({json:run});
  });
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA!,'host-key.txt'),'utf8').trim());await page.getByRole('button',{name:'Open workspace',exact:true}).click();
  const card=page.getByRole('region',{name:'Chrome task',exact:true});
  await expect(card.getByText('Two hours after approval, or when closed.',{exact:false})).toBeVisible();
  await expect(card.getByRole('button',{name:'Always allow this type'})).toHaveCount(0);
  await card.getByRole('button',{name:'Open Chrome & begin',exact:true}).click();
  await card.getByRole('button',{name:'Pause',exact:true}).click();await expect(card.getByRole('button',{name:'Resume AI',exact:true})).toBeVisible();
  run.chargedTokens=limits.maxTotalTokens;run.modelStages=[{call:2,reasoning:'high',purpose:'reply',status:'interrupted'}];
  await page.reload();
  await expect(card.getByRole('button',{name:'Resume AI',exact:true})).toHaveCount(0);
  await expect(card.getByText('An interrupted model call had unknown usage',{exact:false})).toBeVisible();
  run.chargedTokens=20;run.modelStages=[];await page.reload();
  await card.getByRole('button',{name:'Resume AI',exact:true}).click();await card.getByRole('button',{name:'Take over',exact:true}).click();
  await expect(card.getByText('AI is stopped. You can use Chrome yourself.',{exact:false})).toBeVisible();
  await card.getByRole('button',{name:'Resume AI',exact:true}).click();
  run.browser.phase='review-action';run.state='awaitingApproval';run.summary='Review this exact browser action';
  run.browser.pendingAction={kind:'type',pageVersion:'current',target:'e7',description:'Enter the exact fictional note',text:'Meet at the library at noon.'};
  run.approval={...run.approval,id:'action-review',digest:'fixture-action',decision:'pending',action:{name:'browser_action',path:'',content:JSON.stringify(run.browser.pendingAction)}};
  await page.reload();await expect(card.getByText('Meet at the library at noon.',{exact:true})).toBeVisible();await expect(card.getByText('e7',{exact:true})).toBeVisible();
  await expect(card.getByRole('button',{name:'Always allow this type'})).toHaveCount(0);
  fs.mkdirSync(process.env.THADDEUS_SCREENSHOTS!,{recursive:true});await page.screenshot({path:path.join(process.env.THADDEUS_SCREENSHOTS!,'browser-review-desktop.png')});
  await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:path.join(process.env.THADDEUS_SCREENSHOTS!,'browser-review-mobile.png')});
  await card.getByRole('button',{name:'Approve this action',exact:true}).click();await card.getByRole('button',{name:'Close Chrome',exact:true}).click();
  await expect(card.getByRole('button',{name:'Resume AI',exact:true})).toHaveCount(0);
  expect(commands).toEqual(['scope-approved','pause','resume','takeover','resume','action-approved','close']);
});
