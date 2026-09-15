import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,readdir,realpath,stat,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {requireArtifactSpace,cleanArtifactPaths,cleanBuildIntermediates} from './artifact-storage.mjs';

// This is a native Linux process check, not a desktop, worker or VM qualification.
const [name,baselineDirectory,...extra]=process.argv.slice(2);
assert.match(name??'',/^[a-z0-9-]{1,45}$/);assert.equal(extra.length,0);
const root=path.resolve('artifacts',`linux-handoff-${name}`);await mkdir(root);
const source=path.join(root,'source'),packagePath=path.join(root,'package');
const baselineSource=path.join(root,'baseline-source'),baselinePackage=path.join(root,'baseline-package');
const image='sha256:091661a81cd600896c635dd7fa70f6b28773ee3bbbe15316c7efde5029e4a413';
const owner='thaddeus-handoff-'+randomUUID().replaceAll('-','');
const receipt={owner,image,commands:[],sources:[],builtOn:process.platform,executedOn:'linux-x64',
  scope:`native Linux container; ${baselineDirectory?'different-build upgrade and rollback':'same-build study handoff'}; no desktop or worker qualification`,
  liveModelCalls:0,gpuDevices:0,githubActionsStarted:0};
let created=false;
async function hash(file){const h=createHash('sha256');for await(const b of createReadStream(file))h.update(b);return h.digest('hex');}
async function run(label,executable,args,seconds=120,cwd=process.cwd()){
  console.log(`Checking ${label}. The raven keeps his temporary quarters small.`);
  const out=openSync(path.join(root,label+'.stdout.log'),'wx'),err=openSync(path.join(root,label+'.stderr.log'),'wx');
  const child=spawn(executable,args,{cwd,windowsHide:true,stdio:['ignore',out,err]});closeSync(out);closeSync(err);
  const step={label,executable,args,started:new Date().toISOString()};receipt.commands.push(step);
  const timer=setTimeout(()=>{step.timeout=true;child.kill();},seconds*1000);
  try{step.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});}
  finally{clearTimeout(timer);step.finished=new Date().toISOString();}
  assert.ok(step.code===0&&!step.timeout,`${label} failed; inspect ${root}.`);
  return (await readFile(path.join(root,label+'.stdout.log'),'utf8')).trim();
}
async function inventory(directory,prefix=''){
  const files=[];
  for(const entry of await readdir(directory,{withFileTypes:true})){
    assert.ok(!entry.isSymbolicLink());const relative=prefix+entry.name,full=path.join(directory,entry.name);
    if(entry.isDirectory())files.push(...await inventory(full,relative+'/'));
    else files.push({path:relative,sha256:await hash(full),size:(await stat(full)).size});
  }return files.sort((a,b)=>a.path.localeCompare(b.path));
}
async function build(label,directory,destination){
  if(process.platform==='win32'){
    await run(label+'web-install',process.env.ComSpec??'cmd.exe',['/d','/s','/c','npm --prefix web ci'],120,directory);
    await run(label+'web-build',process.env.ComSpec??'cmd.exe',['/d','/s','/c','npm --prefix web run build'],120,directory);
  }else{
    await run(label+'web-install','npm',['--prefix','web','ci'],120,directory);
    await run(label+'web-build','npm',['--prefix','web','run','build'],120,directory);
  }
  await run(label+'restore','dotnet',['restore','src/Thaddeus.Host','--locked-mode'],120,directory);
  await run(label+'publish','dotnet',['publish','src/Thaddeus.Host','-c','Release','-r','linux-x64','--self-contained','true','-p:ContinuousIntegrationBuild=true','-o',destination],180,directory);
  await copyFile(path.join(directory,'scripts/Start Thaddeus.command'),path.join(destination,'start-thaddeus.sh'));
}
async function prepareBaseline(){
  const directory=path.resolve(baselineDirectory);
  assert.equal(await realpath(directory),directory,'Linked historical evidence is refused.');
  const old=JSON.parse(await readFile(path.join(directory,'verified.json'),'utf8'));
  const manifest=JSON.parse(await readFile(path.join(directory,'tested-package-manifest.json'),'utf8'));
  assert.ok(old.passed&&old.native?.passed&&old.containerRemoved,'Baseline must have completed native verification and cleanup.');
  assert.equal(old.executedOn,'linux-x64');assert.equal(manifest.runtime,'linux-x64');
  assert.deepEqual(old.sources,manifest.sourceFiles,'Historical package and source receipt differ.');
  assert.match(manifest.sourceHead,/^[0-9a-f]{40}$/);
  const names=new Set();
  await mkdir(baselineSource);await mkdir(baselinePackage);
  for(const file of old.sources){
    assert.ok(!path.isAbsolute(file.path)&&!file.path.includes('\\')&&!file.path.split('/').some(p=>!p||p==='.'||p==='..'));
    assert.ok(!names.has(file.path));names.add(file.path);assert.match(file.sha256,/^[0-9a-f]{64}$/);
    const captured=path.join(directory,'source',file.path),resolved=old.resolvedLocks?.find(entry=>entry.path===file.path);
    assert.equal(await realpath(captured),captured,'Linked historical source is refused.');
    assert.equal(await hash(captured),resolved?.sha256??file.sha256,'Historical source changed: '+file.path);
    const original=resolved?path.join(directory,'input-locks',file.path):captured;
    assert.equal(await realpath(original),original);assert.equal(await hash(original),file.sha256);
    const target=path.join(baselineSource,file.path);await mkdir(path.dirname(target),{recursive:true});await copyFile(original,target);
  }
  receipt.baseline={directory,receiptSha256:await hash(path.join(directory,'verified.json')),
    historicalManifestSha256:await hash(path.join(directory,'tested-package-manifest.json')),sourceHead:manifest.sourceHead,sources:old.sources,
    reproduction:'Rebuilt from the verified captured sources and original lockfiles; not the removed original binary.'};
  await build('baseline-',baselineSource,baselinePackage);
  const locks=await Promise.all(old.sources.filter(file=>file.path.endsWith('/packages.lock.json'))
    .map(async file=>({path:file.path,sha256:await hash(path.join(baselineSource,file.path))})));
  receipt.baseline.resolvedLocks=locks;
  const rebuilt={...manifest,published:new Date().toISOString(),resolvedLocks:locks,files:await inventory(baselinePackage)};
  delete rebuilt.application; // Obtain capabilities from the executable that is actually run.
  await writeFile(path.join(root,'baseline-manifest-input.json'),JSON.stringify(rebuilt,null,2)+'\n');
  return rebuilt;
}
try{
  receipt.storage=await requireArtifactSpace(root,(baselineDirectory?4:2)*1024**3,'Native Linux handoff check');
  // Refuse a missing image without pulling, installing or touching other containers.
  assert.equal(await run('image','docker',['image','inspect',image,'--format','{{.Id}}']),image);
  await mkdir(source);await mkdir(packagePath);
  const files=(await run('sources','git',['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--',
    'src','web','fixtures/notes','fixtures/linux-handoff/check.py','scripts/linux-handoff-check.mjs','scripts/artifact-storage.mjs',
    'scripts/Start Thaddeus.command','Directory.Build.props','global.json'])).split('\0').filter(Boolean).sort();
  for(const file of files){
    const target=path.join(source,file);await mkdir(path.dirname(target),{recursive:true});await copyFile(file,target);
    receipt.sources.push({path:file,sha256:await hash(target)});
    if(file.endsWith('/packages.lock.json')){
      const original=path.join(root,'input-locks',file);await mkdir(path.dirname(original),{recursive:true});await copyFile(target,original);
    }
  }
  await build('',source,packagePath);
  // RID restoration may alter staged lockfiles. Preserve both inputs and resolved
  // build locks, just as the native portable publisher does; neither is a runtime edit.
  receipt.resolvedLocks=await Promise.all(receipt.sources.filter(file=>file.path.endsWith('/packages.lock.json'))
    .map(async file=>({path:file.path,sha256:await hash(path.join(source,file.path))})));
  const manifest={schemaVersion:1,kind:'portable-development-package',runtime:'linux-x64',
    sourceHead:await run('head','git',['rev-parse','HEAD']),published:new Date().toISOString(),
    signedRelease:false,isolationQualified:false,sourceFiles:receipt.sources,resolvedLocks:receipt.resolvedLocks,files:await inventory(packagePath)};
  await writeFile(path.join(root,'manifest-input.json'),JSON.stringify(manifest,null,2)+'\n');
  const baseline=baselineDirectory?await prepareBaseline():null;
  if(baseline){
    assert.notEqual(baseline.sourceHead,manifest.sourceHead,'Choose a different source revision.');
    for(const name of ['Thaddeus.Host.dll','Thaddeus.Infrastructure.dll','wwwroot/index.html']){
      assert.notEqual(baseline.files.find(f=>f.path===name)?.sha256,manifest.files.find(f=>f.path===name)?.sha256,
        'Different-build verification requires changed application and client bytes: '+name);
    }
  }
  created=true; // An uncertain create acknowledgement must still enter owned-name cleanup.
  await run('create','docker',['create','--name',owner,'--network','none','--cpus','2','--memory','2g','--pids-limit','128',
    '--security-opt','no-new-privileges','--mount',`type=bind,source=${root},target=/evidence`,
    '--mount',`type=bind,source=${packagePath},target=/package,readonly`,
    ...(baseline?['--mount',`type=bind,source=${baselinePackage},target=/baseline,readonly`]:[]),
    '--entrypoint','/usr/bin/python3',image,'/evidence/source/fixtures/linux-handoff/check.py'],30);
  await run('native','docker',['start','--attach',owner],baseline?300:210);
  const native=JSON.parse(await readFile(path.join(root,'native.json'),'utf8'));receipt.native=native;
  assert.equal(native.passed,true);assert.equal(native.cleanup.confirmed,true);
  if(baseline){
    assert.equal(native.differentBuild,true);assert.equal(native.upgradeVerified,true);assert.equal(native.rollbackVerified,true);
    for(const file of receipt.baseline.sources){
      const resolved=receipt.baseline.resolvedLocks.find(entry=>entry.path===file.path);
      assert.equal(await hash(path.join(baselineSource,file.path)),resolved?.sha256??file.sha256,'Rebuilt baseline source changed.');
    }
    for(const file of baseline.files)assert.equal(await hash(path.join(baselinePackage,file.path)),file.sha256,'Baseline input changed during native check.');
  }
  for(const file of receipt.sources){
    const resolved=receipt.resolvedLocks.find(entry=>entry.path===file.path);
    assert.equal(await hash(path.join(source,file.path)),resolved?.sha256??file.sha256,'Captured source changed during native check: '+file.path);
    if(resolved)assert.equal(await hash(path.join(root,'input-locks',file.path)),file.sha256,'Original lockfile changed: '+file.path);
  }
  for(const file of manifest.files)assert.equal(await hash(path.join(packagePath,file.path)),file.sha256,'Published input changed during native check.');
  receipt.passed=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{
  let contained=true;
  if(created)try{
    await run('remove','docker',['rm','--force',owner],30);
    const names=await run('confirm-removed','docker',['ps','-a','--filter',`name=^/${owner}$`,'--format','{{.Names}}'],15);
    assert.equal(names,'','Owned container still exists.');
    receipt.containerRemoved=true;
  }catch(error){contained=false;receipt.cleanupError=error.message;receipt.passed=false;process.exitCode=1;}
  if(contained)try{
    if(await stat(source).catch(()=>null))receipt.removedBuildIntermediates=await cleanBuildIntermediates(source);
    if(await stat(baselineSource).catch(()=>null))receipt.removedBaselineIntermediates=await cleanBuildIntermediates(baselineSource);
    receipt.removedScratch=await cleanArtifactPaths(root,['package','baseline-package']);
  }catch(error){receipt.cleanupError=error.message;receipt.passed=false;process.exitCode=1;}
  await writeFile(path.join(root,'verified.json'),JSON.stringify(receipt,null,2)+'\n');
}
console.log(JSON.stringify({passed:receipt.passed,root,error:receipt.error}));
