import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {openLog} from './navigation';

test('raven follows the selected task, has discrete working poses, and honors reduced motion throughout',async({page})=>{
  const directory=path.resolve(process.env.THADDEUS_SCREENSHOTS||'../artifacts/screenshots');fs.mkdirSync(directory,{recursive:true});
  await page.setViewportSize({width:1440,height:1000});await page.goto('/');
  await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.resolve(process.env.THADDEUS_TEST_DATA||'../.data','host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Unlock study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  // Create only a fictional plan. Presentation states below are never written to the backend.
  const created=await page.evaluate(async()=>{
    const current=await(await fetch('/api/state')).json();if(current.provider.kind!=='scripted')throw new Error('This check requires the fictional provider.');
    const session=await(await fetch('/api/session')).json();
    const headers={'Content-Type':'application/json','X-CSRF':session.csrf};
    const seed=await fetch('/api/demo/seed',{method:'POST',headers,body:'{}'});if(!seed.ok)throw new Error('Fictional notes could not be loaded.');
    const reply=await fetch('/api/runs',{method:'POST',headers,body:JSON.stringify({objective:'Raven presentation fixture',readScope:['notes/constraints.md']})});
    if(!reply.ok)throw new Error('Fictional plan could not be created.');return reply.json();
  });
  await expect.poll(async()=>page.evaluate(async id=>(await(await fetch('/api/runs/'+id)).json()).state,created.id)).toBe('awaitingApproval');
  const original=await page.evaluate(async()=>(await fetch('/api/state')).json());
  const source=original.runs.find((run:any)=>run.id===created.id);expect(source).toBeTruthy();
  const before=await page.evaluate(async()=>(await fetch('/api/export')).json());
  let projection={...original,runs:[]};
  await page.route('**/api/state',route=>route.fulfill({json:projection}));
  const writes:string[]=[];
  page.on('request',request=>{if(new URL(request.url()).pathname.startsWith('/api/')&&!['GET','HEAD'].includes(request.method()))writes.push(request.url());});
  await page.reload();await openLog(page);
  const companion=page.getByRole('complementary',{name:'Activity log'}).locator('.companion');
  await expect(companion.getByRole('img',{name:'Thaddeus raven: At your service',exact:true})).toBeVisible();
  await expect(companion.getByRole('button')).toHaveCount(0);
  const composer=page.getByLabel('Message or goal');
  await composer.fill('A friendly hello, left unsent.');
  await expect(companion.getByRole('img',{name:'Thaddeus raven: Ready when you are',exact:true})).toBeVisible();
  await companion.locator('.raven').screenshot({path:path.join(directory,'raven-listening.png'),animations:'allow'});
  await composer.fill('');
  const idle=companion.getByRole('img',{name:'Thaddeus raven: At your service',exact:true});
  await idle.hover();
  await idle.evaluate(element=>{for(const animation of element.getAnimations({subtree:true})){animation.pause();animation.currentTime=350;}});
  await idle.screenshot({path:path.join(directory,'raven-hello.png'),animations:'allow'});
  projection={...original,runs:[{...source,state:'running'}]};
  await page.reload();await openLog(page);
  const raven=companion.getByRole('button',{name:'Thaddeus raven: Working. Open task details',exact:true});
  await expect(raven).toBeVisible();
  const sample=async(time:number)=>raven.evaluate((element,time)=>{
    for(const animation of element.getAnimations({subtree:true})){animation.pause();animation.currentTime=time;}
    return {open:getComputedStyle(element.querySelector('.raven-wing-open')!).opacity,folded:getComputedStyle(element.querySelector('.raven-wing-folded')!).opacity};
  },time);
  expect(await sample(0)).toEqual({open:'0',folded:'1'});
  await raven.screenshot({path:path.join(directory,'raven-working-folded.png'),animations:'allow'});
  expect(await sample(1250)).toEqual({open:'1',folded:'0'});
  await raven.screenshot({path:path.join(directory,'raven-working-open.png'),animations:'allow'});
  await raven.focus();await page.keyboard.press('Enter');
  await expect(page.getByRole('heading',{name:source.goal.objective,exact:true})).toBeVisible();
  await page.emulateMedia({reducedMotion:'reduce'});
  const motion=await raven.evaluate(element=>[element,...element.querySelectorAll('*')].map(node=>getComputedStyle(node).animationName));
  expect(motion.every(name=>name==='none')).toBe(true);
  expect(await sample(1250)).toEqual({open:'0',folded:'1'});
  // Another pending task must not override the completed task the owner chose to inspect.
  projection={...original,runs:[{...source,id:'presentation-only-pending',state:'awaitingInput',goal:{...source.goal,objective:'Another pending presentation task'}},{...source,state:'succeeded'}]};
  await page.reload();await openLog(page);
  await page.locator(`[data-run-id="${source.id}"]`).click();
  await expect(companion.getByRole('button',{name:'Thaddeus raven: Completed. Open task details',exact:true})).toBeVisible();
  await expect(companion.getByText('Completed',{exact:true})).toBeVisible();
  await companion.locator('.raven').screenshot({path:path.join(directory,'raven-happy-reduced-motion.png')});
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    if(width===390){
      const headerCompanion=page.locator('.header-companion').getByRole('button',{name:'Thaddeus: open activity log',exact:true});
      await expect(headerCompanion).toBeVisible();await expect(headerCompanion.getByRole('img',{name:'Thaddeus raven: Completed',exact:true})).toBeVisible();
    }
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await page.screenshot({path:path.join(directory,`raven-task-${width}.png`),fullPage:true});
  }
  await page.evaluate(()=>window.dispatchEvent(new Event('offline')));
  await expect(page.locator('.header-companion').getByRole('img',{name:'Thaddeus raven: Disconnected',exact:true})).toBeVisible();
  expect(writes).toEqual([]);
  expect(await page.evaluate(async()=>(await fetch('/api/export')).json())).toEqual(before);
});
