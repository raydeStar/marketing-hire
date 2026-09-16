import {chooseMessageMode,navigateStudy,openLog,openSettings} from './navigation';
import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';

test('native research through question, exact import approval and reviewed workspace removal',async({page})=>{
  test.skip(process.env.THADDEUS_NATIVE_RESEARCH!=='1','Explicit owned VM fixture only; no automatic virtualization or model dispatch.');
  test.setTimeout(600000);
  const root=path.resolve(process.env.THADDEUS_TEST_DATA!);
  const publicSearch=process.env.THADDEUS_PUBLIC_SEARCH==='1';
  const setupNetwork:{event:string;at:string;method:string;path:string;status?:number;error?:string}[]=[];
  const recordSetup=(event:string,request:any,extra:object={})=>{
    const pathname=new URL(request.url()).pathname;
    if(!pathname.startsWith('/api/settings/worker'))return;
    setupNetwork.push({event,at:new Date().toISOString(),method:request.method(),path:pathname,...extra});
    fs.writeFileSync(path.join(root,'setup-network.json'),JSON.stringify(setupNetwork,null,2));
  };
  page.on('request',request=>recordSetup('request',request));
  page.on('response',response=>recordSetup('response',response.request(),{status:response.status()}));
  page.on('requestfailed',request=>recordSetup('failed',request,{error:request.failure()?.errorText}));
  const hostKey=fs.readFileSync(path.join(root,'host-key.txt'),'utf8').trim();
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(hostKey);
  await page.getByRole('button',{name:'Unlock study'}).click();
  await openSettings(page);
  if(publicSearch){
    const search=page.getByRole('region',{name:'Public search connection',exact:true});
    await search.getByLabel('Search key storage',{exact:true}).selectOption('session');
    await search.getByLabel('Brave Search API key',{exact:true}).fill('fictional-native-search-key');
    await search.getByRole('checkbox',{name:'My search plan permits retaining API results',exact:false}).check();
    await search.getByRole('button',{name:'Save search connection',exact:true}).click();
    await expect(search.getByText('Search connection saved. No query was sent; the provider has not verified this key yet.',{exact:true})).toBeVisible();
  }
  await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Research worker',exact:true}).click();
  const setup=page.getByRole('region',{name:'Host research setup'});
  await expect(setup.getByRole('button',{name:'Enable research on this host'})).toBeDisabled();
  await setup.getByRole('button',{name:'Check installed worker'}).click();
  await expect(setup.getByRole('button',{name:'Enable research on this host'})).toBeEnabled({timeout:60000});
  const checked=await page.evaluate(async()=>(await fetch('/api/state')).json());
  expect(checked.research.enabled).toBe(false);expect(checked.runs).toHaveLength(0);
  await setup.getByRole('button',{name:'Enable research on this host'}).click();
  await expect(setup.getByRole('button',{name:'Disable new research'})).toBeEnabled();
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});await setup.screenshot({path:path.join(root,`host-setup-${width}.png`)});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  }
  await page.reload();await openSettings(page);
  await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Research worker',exact:true}).click();
  await expect(page.getByRole('region',{name:'Host research setup'}).getByRole('button',{name:'Disable new research'})).toBeEnabled();
  await page.getByRole('button',{name:'Artifacts',exact:true}).click();
  const memory=page.getByRole('group',{name:'Remembered context'});await memory.locator('summary').first().click();
  await memory.getByLabel('Source note',{exact:true}).selectOption('notes/memory-source.md');
  await memory.getByLabel('Remembered statement').fill('Use cobalt workshop handouts.');
  await memory.getByLabel('Exact source quotation').fill('The workshop handout color is cobalt.');
  await memory.getByRole('button',{name:'Remember this statement'}).click();await expect(memory.locator('[data-memory-id]')).toHaveCount(1);
  await memory.getByLabel('Remembered statement').fill('The spare notebook is jade.');
  await memory.getByLabel('Exact source quotation').fill('The spare notebook is jade.');
  await memory.getByRole('button',{name:'Remember this statement'}).click();await expect(memory.locator('[data-memory-id]')).toHaveCount(2);
  await navigateStudy(page,'Chat');
  await chooseMessageMode(page,'research');
  await expect(page.getByRole('region',{name:'Research scope'})).toBeVisible();
  await expect(page.getByRole('checkbox',{name:'notes/source.md'})).toBeChecked();
  await page.getByRole('checkbox',{name:'notes/memory-source.md'}).uncheck();
  await page.getByRole('checkbox',{name:'Use cobalt workshop handouts.',exact:true}).check();
  await expect(page.getByRole('checkbox',{name:'The spare notebook is jade.',exact:true})).not.toBeChecked();
  await page.getByLabel('Public source websites (optional)').fill(publicSearch?'':'docs.docker.com');
  if(publicSearch){
    await page.getByRole('checkbox',{name:'Search the public web with Brave',exact:true}).check();
    await page.getByLabel('Search request allowance',{exact:true}).selectOption('1');
    await expect(page.getByRole('checkbox',{name:'Allow opening the returned result pages',exact:true})).toBeChecked();
  }
  await page.getByText('Resource limits & provider guarantees',{exact:true}).click();
  await page.getByLabel('Model calls',{exact:true}).fill('8'); // Explicit fixture allowance; product defaults remain unchanged.
  await page.getByLabel('Message or goal').fill('Read notes/source.md and https://docs.docker.com/ai/sandboxes/faq/. Ask which workshop audience to use, then write summary.md and request its import to plans/summary.md with captured citations. This is a fictional integration fixture.');
  await page.screenshot({path:path.join(root,'research-composer.png'),fullPage:true});
  await page.getByRole('button',{name:'Start research'}).click();
  const progress=page.getByRole('region',{name:'Research progress'});
  if(process.env.THADDEUS_GUIDANCE==='1'){
    const card=page.getByRole('region',{name:'Task guidance'});
    await expect(card.getByRole('button',{name:'Send guidance',exact:true})).toBeVisible({timeout:180000});
    await expect.poll(()=>fs.existsSync(path.join(root,'guidance-model-waiting.json'))).toBe(true);
    const waiting=JSON.parse(fs.readFileSync(path.join(root,'guidance-model-waiting.json'),'utf8'));
    const before=(await page.evaluate(async()=>(await fetch('/api/state')).json())).runs[0];
    expect(before.modelCalls).toBe(1);expect(before.reservedTokens).toBeGreaterThan(0);
    const message='Keep the workshop summary concise and use the selected source quotations.';
    await card.getByLabel('Additional guidance',{exact:true}).fill(message);
    await card.getByRole('button',{name:'Send guidance',exact:true}).click();
    await expect(card.getByText('Received by the worker · outcome still needs review',{exact:true})).toBeVisible();
    const admitted=(await page.evaluate(async()=>(await fetch('/api/state')).json())).runs[0];
    expect(admitted.modelCalls).toBe(1);expect(admitted.reservedTokens).toBe(waiting.reservedTokens);
    expect(admitted.executionDeadlineStart).toBe(waiting.executionDeadlineStart);
    expect(admitted.execution.runtimeRunId).toBe(before.execution.runtimeRunId);
    expect(admitted.goal).toEqual(before.goal);expect(admitted.preparedContext).toEqual(before.preparedContext);
    const commands=admitted.executionCommands.filter((command:any)=>command.kind==='steer');
    expect(commands).toHaveLength(1);expect(commands[0].message).toBe(message);
    const duplicate=await page.evaluate(async({id,operationId,message})=>{
      const session=await (await fetch('/api/session')).json();
      const response=await fetch('/api/runs/'+id+'/guidance',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify({operationId,message})});
      return {status:response.status,body:await response.json()};
    },{id:admitted.id,operationId:commands[0].id.slice(6),message});
    expect(duplicate.status).toBe(200);expect(duplicate.body.executionCommands).toEqual(admitted.executionCommands);
    await page.reload();await page.getByLabel('Message mode',{exact:true}).selectOption('guidance');
    await expect(card.getByText(message,{exact:true})).toBeVisible();
    for(const width of [1440,390]){
      await page.setViewportSize({width,height:1000});await card.scrollIntoViewIfNeeded();
      await page.screenshot({path:path.join(root,`guidance-${width}.png`),fullPage:true});
      expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    }
    fs.writeFileSync(path.join(root,'browser-guidance.json'),JSON.stringify({before,admitted,duplicate,waiting},null,2));
    fs.writeFileSync(path.join(root,'release-guidance-model.json'),JSON.stringify({guidanceVerified:true}));
    await openLog(page);await page.locator(`[data-run-id="${admitted.id}"]`).click();
  }
  if(process.env.THADDEUS_CHECKPOINT_RECOVERY==='1'){
    const recovery=page.getByRole('region',{name:'Saved checkpoint recovery'});
    await expect(recovery.getByRole('button',{name:'Inspect saved worker',exact:true})).toBeVisible({timeout:180000});
    await expect(page.getByRole('button',{name:'Developers',exact:true})).toBeDisabled();
    const before=(await page.evaluate(async()=>(await fetch('/api/state')).json())).runs[0];
    const gap=JSON.parse(fs.readFileSync(path.join(root,'checkpoint-gap.json'),'utf8'));
    expect(gap.actualVmStopped).toBe(true);expect(gap.worker.status).toBe('stopped');
    expect(before.state).toBe('needsAttention');expect(before.question.answer).toBeNull();
    await page.reload();await openLog(page);await page.locator(`[data-run-id="${before.id}"]`).click();
    await recovery.getByRole('button',{name:'Inspect saved worker',exact:true}).click();
    const restore=recovery.getByRole('button',{name:'Restore saved checkpoint',exact:true});
    await expect(restore).toBeEnabled({timeout:120000});
    await expect(recovery.getByText('Saved worker confirmed stopped',{exact:true})).toBeVisible();
    const physical=JSON.parse(fs.readFileSync(path.join(root,'checkpoint-inspection.json'),'utf8'));
    expect(physical.status).toBe('stopped');expect(physical.overlayUnchanged).toBe(true);
    expect(physical.booted).toBe(false);expect(physical.replayedCommands).toBe(false);
    const inspected=(await page.evaluate(async()=>(await fetch('/api/state')).json())).runs[0];
    expect(inspected.state).toBe('needsAttention');expect(inspected.modelCalls).toBe(before.modelCalls);
    expect(inspected.executionCommands).toEqual(before.executionCommands);expect(inspected.question).toEqual(before.question);
    for(const width of [1440,390]){
      await page.setViewportSize({width,height:1000});await recovery.scrollIntoViewIfNeeded();
      await page.screenshot({path:path.join(root,`checkpoint-review-${width}.png`),fullPage:true});
      expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
    }
    await restore.click();await expect(recovery).toHaveCount(0);
    const restored=(await page.evaluate(async()=>(await fetch('/api/state')).json())).runs[0];
    expect(restored.state).toBe('awaitingInput');expect(restored.research.phase).toBe('awaiting-input');
    expect(restored.modelCalls).toBe(before.modelCalls);expect(restored.modelDispatches).toEqual(before.modelDispatches);
    expect(restored.executionCommands).toEqual(before.executionCommands);expect(restored.question).toEqual(before.question);
    expect(restored.preparedContext).toEqual(before.preparedContext);expect(restored.chargedTokens).toBe(before.chargedTokens);
    fs.writeFileSync(path.join(root,'browser-recovery.json'),JSON.stringify({before,inspected,restored,physical},null,2));
  }
  const question=page.getByRole('heading',{name:'A detail before I continue.'});
  const questionDeadline=Date.now()+180000;
  while(!await question.isVisible()&&Date.now()<questionDeadline){
    if(await page.getByText('Needs attention',{exact:true}).isVisible())
      throw new Error('Research stopped before its question: '+await progress.innerText());
    await new Promise(resolve=>setTimeout(resolve,250));
  }
  await expect(question).toBeVisible();
  const answer=page.getByRole('button',{name:'Send answer & continue'});
  await expect(page.getByRole('button',{name:'Developers',exact:true})).toBeEnabled({timeout:180000});
  await page.reload(); // Durable question and stopped worker are recovered by the normal UI state request.
  await openLog(page);
  await page.locator('[data-run-id]').first().click();
  if(publicSearch){
    await expect(page.getByRole('group',{name:'Token usage'}).locator('summary').first()).toContainText('1 search attempt');
    const receipts=page.getByRole('region',{name:'Public search receipts'});
    await expect(receipts).toContainText('1 search attempt used / 1 allowed');
    await receipts.locator('summary').click();
    await expect(receipts.getByRole('link',{name:'Docker Sandboxes FAQ',exact:true})).toHaveAttribute('href','https://docs.docker.com/ai/sandboxes/faq/');
    await expect(receipts).toContainText('broker-observed');
    for(const width of [1440,390]){
      await page.setViewportSize({width,height:1000});await receipts.screenshot({path:path.join(root,`search-receipts-${width}.png`)});
      expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    }
  }
  await page.getByRole('button',{name:'Developers',exact:true}).click();
  await page.screenshot({path:path.join(root,'research-question.png'),fullPage:true});
  await answer.click();
  await expect(page.getByRole('button',{name:'Approve exact write'})).toBeEnabled({timeout:180000});
  await expect(progress.getByText('Artifact readback matched: summary.md',{exact:true})).toBeVisible();
  const quotationChecks=page.getByRole('region',{name:'Source quotation checks'});
  await expect(quotationChecks.getByText('Proposal 1 · Correction requested',{exact:true})).toBeVisible();
  await expect(quotationChecks.getByText('Proposal 2 · Quotation checks passed',{exact:true})).toBeVisible();
  const captures=page.getByRole('region',{name:'Captured worker files'});
  await expect(captures.getByText('Source correction sent to OpenClaw',{exact:false})).toBeVisible();
  await expect(captures.getByText('Captured file ready for your approval',{exact:false})).toBeVisible();
  const before=await page.evaluate(async()=>({status:(await fetch('/api/knowledge?path=plans/summary.md')).status}));
  expect(before.status).toBe(404);
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    await page.screenshot({path:path.join(root,`research-approval-${width}.png`),fullPage:true});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  }
  await page.getByRole('button',{name:'Approve exact write'}).click();
  await expect(progress.getByText('Worker stopped · private workspace retained for inspection',{exact:true})).toBeVisible({timeout:60000});
  await page.getByRole('button',{name:'Open editable plan'}).click();
  await expect(page.getByLabel('Markdown editor')).toContainText('Audience: Developers');
  const exported=await page.evaluate(async()=>(await fetch('/api/export')).json());
  expect(exported.runs[0].research.phase).toBe('finished');
  expect(exported.runs[0].research.review.artifact).toBe('summary.md');
  expect(exported.runs[0].goal.criteria.some((criterion:any)=>criterion.status==='unverified')).toBeTruthy();
  expect(exported.runs[0].modelCalls).toBe(publicSearch?8:7);
  if(publicSearch){
    expect(exported.runs[0].goal.web.hosts).toEqual([]);
    expect(exported.runs[0].goal.web.search).toMatchObject({provider:'brave',maxQueries:1,openResults:true});
    expect(exported.runs[0].capabilities.filter((call:any)=>call.name==='thaddeus_search_public_web')).toHaveLength(1);
    expect(JSON.stringify(exported)).not.toContain('fictional-native-search-key');
  }
  expect(exported.runs[0].repairs).toBe(1);
  expect(exported.runs[0].nativeProposals.map((review:any)=>review.status)).toEqual(['repair-requested','passed']);
  expect(exported.runs[0].nativeProposals[1].contentHash).toBe(exported.runs[0].research.review.sha256);
  expect(exported.runs[0].profile.proposalEvidenceVersion).toBe(2);
  expect(exported.runs[0].artifactImports.map((item:any)=>item.status)).toEqual(['repair-dispatched','ready-for-approval']);
  expect(exported.runs[0].artifactImports[0].approvalId).toBeNull();
  expect(exported.runs[0].artifactImports[1].sha256).toBe(exported.runs[0].research.review.sha256);
  expect(exported.runs[0].executionCommands.filter((command:any)=>command.kind==='artifact-repair')).toHaveLength(1);
  expect(exported.runs[0].preparedContext.memories).toHaveLength(1);
  expect(exported.runs[0].preparedContext.memories[0].statement).toBe('Use cobalt workshop handouts.');
  fs.writeFileSync(path.join(root,'browser-export.json'),JSON.stringify(exported,null,2));
  await openSettings(page);
  await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
  const storage=page.getByRole('region',{name:'Stored research workspaces'});
  await storage.getByRole('button',{name:'Inspect stored workspace',exact:true}).click();
  const review=storage.getByRole('region',{name:'Workspace removal review'});
  await expect(review.getByRole('heading')).toHaveText(exported.runs[0].goal.objective);
  const remove=review.getByRole('button',{name:'Remove reviewed workspace'});
  await expect(remove).toBeDisabled();
  await review.getByLabel('Confirm workspace removal').fill('REMOVE'); await expect(remove).toBeDisabled();
  await review.getByLabel('Confirm workspace removal').fill('REMOVE WORKSPACE');
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    await review.scrollIntoViewIfNeeded();
    await page.screenshot({path:path.join(root,`workspace-review-${width}.png`),fullPage:true});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  }
  await remove.click();
  await expect(storage.getByText('Private workspace removal is verified. Imported notes and task receipts remain.',{exact:true})).toBeVisible();
  await expect(storage.getByText('No private research workspaces are retained.',{exact:true})).toBeVisible();
  const after=await page.evaluate(async()=>(await fetch('/api/export')).json());
  expect(after.runs[0].research.workerRetained).toBe(false);
  expect(after.runs[0].state).toBe('succeeded');
  expect(after.pages.find((entry:any)=>entry.path==='plans/summary.md').content).toContain('Audience: Developers');
  expect(fs.existsSync(path.join(root,'qemu-'+exported.runs[0].execution.sandboxId))).toBe(false);
  fs.writeFileSync(path.join(root,'browser-export-after-removal.json'),JSON.stringify(after,null,2));
  await page.reload(); await openSettings(page);
  await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
  await expect(storage.getByText('No private research workspaces are retained.',{exact:true})).toBeVisible();
  fs.writeFileSync(path.join(root,'browser-finished.json'),JSON.stringify({passed:true,syntheticModel:true}));
});
