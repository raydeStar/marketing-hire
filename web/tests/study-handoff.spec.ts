import {test,expect} from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import {openSettings} from './navigation';

test('owner opens original and restored studies across real packages, with failed-start recovery',async({page})=>{
  test.skip(process.platform!=='win32'||!process.env.THADDEUS_HANDOFF_PACKAGE,'Requires an explicit prior Windows package.');
  test.setTimeout(180000);
  const data=path.resolve(process.env.THADDEUS_TEST_DATA!),evidence=path.dirname(data);
  const selected=path.resolve(process.env.THADDEUS_HANDOFF_PACKAGE!),current=path.resolve(process.env.THADDEUS_TEST_PACKAGE!);
  expect(data.startsWith(path.resolve('../artifacts')+path.sep)).toBe(true);
  expect(selected.startsWith(path.resolve('../artifacts')+path.sep)).toBe(true);
  expect(selected).not.toBe(current);
  expect(fs.readFileSync(path.join(selected,'Thaddeus.Host.dll')).equals(fs.readFileSync(path.join(current,'Thaddeus.Host.dll')))).toBe(false);
  const fixture=JSON.parse(fs.readFileSync(path.join(evidence,'fixture.json'),'utf8'));
  const targets:{profile:string;package:string;dataDirectory:string}[]=[];
  const images=path.resolve(process.env.THADDEUS_SCREENSHOTS!);fs.mkdirSync(images,{recursive:true});
  const alive=(pid:number)=>{try{process.kill(pid,0);return true;}catch{return false;}};
  const register=(launcher:any)=>{
    const launch=JSON.parse(fs.readFileSync(launcher.profile,'utf8'));
    expect(launch.dataDirectory.startsWith(evidence+path.sep)).toBe(true);
    targets.push({profile:launcher.profile,package:launcher.package,dataDirectory:launch.dataDirectory});
    fs.writeFileSync(path.join(evidence,'handoff-targets.json'),JSON.stringify(targets,null,2));
  };
  const read=async(route:string)=>page.evaluate(async route=>(await fetch('/api'+route)).json(),route);
  const post=async(route:string,body:any,csrf=true)=>page.evaluate(async({route,body,csrf})=>{
    const session=await(await fetch('/api/session')).json();
    const response=await fetch('/api'+route,{method:'POST',headers:{'Content-Type':'application/json',...(csrf?{'X-CSRF':session.csrf}:{})},body:JSON.stringify(body)});
    return {status:response.status,body:await response.json().catch(()=>null)};
  },{route,body,csrf});
  async function maintenance(mode:'backup'|'stop'){
    await openSettings(page);
    await page.getByRole('navigation',{name:'Settings sections'}).getByRole('button',{name:'Storage & backups',exact:true}).click();
    const section=page.getByRole('region',{name:'Backups and shutdown'});
    await section.getByRole('button',{name:'Review maintenance',exact:true}).click();
    await section.getByLabel('Maintenance action').selectOption(mode);
    await section.getByRole('button',{name:mode==='backup'?'Back up and close study':'Close study without a new backup',exact:true}).click();
    await expect(page.getByRole('heading',{name:'Study maintenance',exact:true})).toBeVisible();
    await expect.poll(async()=>{try{return(await read('/maintenance')).phase;}catch{return '';}}).toBe(mode==='backup'?'verified':'stopped');
    return read('/maintenance');
  }
  const restore=page.getByRole('region',{name:'Restore a backup',exact:true});
  async function restoreBackup(id:string){
    await restore.getByRole('button',{name:'Find saved backups',exact:true}).click();
    await restore.getByLabel('Recorded backup',{exact:true}).selectOption(id);
    await restore.getByRole('checkbox',{name:'Use a different application version'}).check();
    await restore.getByLabel('Extracted application folder').fill(selected);
    await restore.getByRole('button',{name:'Review selected backup',exact:true}).click();
    await restore.getByRole('button',{name:'Restore as a separate study',exact:true}).click();
    await expect(restore.getByRole('heading',{name:'Restored study verified',exact:true})).toBeVisible({timeout:20000});
    const result=await read('/maintenance/restore');register(result.launcher);register(result.returnLauncher);
    await restore.getByRole('checkbox',{name:'Open in my default browser'}).uncheck();
    return result;
  }
  async function completed(launcher:any){
    let receipt:any;
    await expect.poll(()=>{
      const files=fs.readdirSync(launcher.directory).filter(name=>/^open-[a-f0-9]+\.json$/.test(name));
      receipt=files.map(name=>JSON.parse(fs.readFileSync(path.join(launcher.directory,name),'utf8'))).find(record=>record.result.started);
      return !!receipt;
    },{timeout:30000}).toBe(true);
    expect(receipt.result.processStartTicks).toMatch(/^\d+$/);
    expect(alive(receipt.result.processId)).toBe(true);
    await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible({timeout:20000});
    return receipt;
  }
  await page.goto('/');await page.getByLabel('Host access key',{exact:true}).fill(fs.readFileSync(path.join(data,'host-key.txt'),'utf8').trim());
  await page.getByRole('button',{name:'Open workspace',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible();
  const before=await read('/export');
  const backup=await maintenance('backup');
  await page.getByRole('button',{name:'Reopen study',exact:true}).click();
  await expect(page.getByRole('heading',{name:'Conversation',exact:true})).toBeVisible({timeout:20000});
  expect(await page.evaluate(async()=>{
    const session=await(await fetch('/api/session')).json();
    return(await fetch('/api/knowledge',{method:'PUT',headers:{'Content-Type':'application/json','X-CSRF':session.csrf},body:JSON.stringify({path:'notes/newer.md',content:'The original keeps this newer edit.',version:'absent'})})).status;
  })).toBe(200);
  const newer=await read('/export');
  const stopped=await maintenance('stop');
  const first=await restoreBackup(backup.version);
  expect((await post('/maintenance/restore/open',{reviewId:first.review.id,target:'restored',openBrowser:false},false)).status).toBe(403);
  expect((await post('/maintenance/restore/open',{reviewId:'stale',target:'restored',openBrowser:false})).status).toBe(409);
  // A real Windows file handle prevents the selected desktop host acquiring its study lock.
  const held=fs.openSync(path.join(first.receipt.directory,'launcher.lock'),'w');
  try{
    await restore.getByRole('button',{name:'Open restored study',exact:true}).click();
    await expect.poll(async()=>{try{return(await read('/maintenance')).phase;}catch{return '';}},{timeout:30000}).toBe('failed');
    await expect(restore.getByRole('heading',{name:'Restored study verified',exact:true})).toBeVisible({timeout:20000});
    expect((await read('/maintenance')).version).toBe(stopped.version);
    expect((await read('/maintenance/restore')).receipt).toEqual(first.receipt);
    expect(fs.readdirSync(data+'-backups').filter(name=>/^[a-f0-9]+\.receipt.json$/.test(name))).toHaveLength(1);
  }finally{fs.closeSync(held);}
  await restore.getByRole('checkbox',{name:'Open in my default browser'}).uncheck();
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:1000});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await page.screenshot({path:path.join(images,`handoff-${width}.png`),fullPage:true});
  }
  await restore.getByRole('button',{name:'Open original study',exact:true}).click();
  const originalOpen=await completed(first.returnLauncher);
  await expect.poll(()=>alive(fixture.pid)).toBe(false);
  expect(await read('/export')).toEqual(newer);
  await maintenance('stop');
  const second=await restoreBackup(backup.version);
  await restore.getByRole('button',{name:'Open restored study',exact:true}).click();
  const restoredOpen=await completed(second.launcher);
  await expect.poll(()=>alive(originalOpen.result.processId)).toBe(false);
  expect(await read('/export')).toEqual(before);
  expect(fs.readFileSync(path.join(data,'knowledge/notes/newer.md'),'utf8')).toBe('The original keeps this newer edit.');
  const expectedHtml=fs.readFileSync(path.join(selected,'wwwroot/index.html'),'utf8');
  expect(new URL(page.url()).searchParams.get('study')).toMatch(/^[a-f0-9-]+$/);
  expect(await page.locator('script[src]').getAttribute('src')).toBe(expectedHtml.match(/<script[^>]+src="([^"]+)"/)![1]);
  expect(await page.evaluate(async()=>(await fetch('/?verify-handoff='+crypto.randomUUID(),{cache:'no-store'})).text())).toBe(expectedHtml);
  const final=await maintenance('stop');
  expect((await post('/maintenance/finish',{version:final.version,mode:'close'})).status).toBe(200);
  await expect.poll(()=>alive(restoredOpen.result.processId),{timeout:20000}).toBe(false);
  fs.writeFileSync(path.join(evidence,'handoff-verified.json'),JSON.stringify({passed:true,originalOpen,restoredOpen,originalPid:fixture.pid,failedStartupRecovered:true,newerOriginalPreserved:true,oldStudyMatchesBackup:true,differentHostAssembly:true,liveModelCalls:0},null,2));
});
