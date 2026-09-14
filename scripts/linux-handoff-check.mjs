import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,readdir,stat,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {requireArtifactSpace,cleanArtifactPaths,cleanBuildIntermediates} from './artifact-storage.mjs';

// This is a native Linux process check, not a desktop, worker or VM qualification.
const [name,...extra]=process.argv.slice(2);
assert.match(name??'',/^[a-z0-9-]{1,45}$/);assert.equal(extra.length,0);
const root=path.resolve('artifacts',`linux-handoff-${name}`);await mkdir(root);
const source=path.join(root,'source'),packagePath=path.join(root,'package');
const image='sha256:091661a81cd600896c635dd7fa70f6b28773ee3bbbe15316c7efde5029e4a413';
const owner='thaddeus-handoff-'+randomUUID().replaceAll('-','');
const receipt={owner,image,commands:[],sources:[],builtOn:process.platform,executedOn:'linux-x64',
  scope:'native Linux container; same-build study handoff; no desktop or worker qualification',
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
try{
  receipt.storage=await requireArtifactSpace(root,2*1024**3,'Native Linux handoff check');
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
  if(process.platform==='win32'){
    await run('web-install',process.env.ComSpec??'cmd.exe',['/d','/s','/c','npm --prefix web ci'],120,source);
    await run('web-build',process.env.ComSpec??'cmd.exe',['/d','/s','/c','npm --prefix web run build'],120,source);
  }else{
    await run('web-install','npm',['--prefix','web','ci'],120,source);await run('web-build','npm',['--prefix','web','run','build'],120,source);
  }
  await run('restore','dotnet',['restore','src/Thaddeus.Host','--locked-mode'],120,source);
  await run('publish','dotnet',['publish','src/Thaddeus.Host','-c','Release','-r','linux-x64','--self-contained','true','-p:ContinuousIntegrationBuild=true','-o',packagePath],180,source);
  // RID restoration may alter staged lockfiles. Preserve both inputs and resolved
  // build locks, just as the native portable publisher does; neither is a runtime edit.
  receipt.resolvedLocks=await Promise.all(receipt.sources.filter(file=>file.path.endsWith('/packages.lock.json'))
    .map(async file=>({path:file.path,sha256:await hash(path.join(source,file.path))})));
  await copyFile(path.join(source,'scripts/Start Thaddeus.command'),path.join(packagePath,'start-thaddeus.sh'));
  const manifest={schemaVersion:1,kind:'portable-development-package',runtime:'linux-x64',
    sourceHead:await run('head','git',['rev-parse','HEAD']),published:new Date().toISOString(),
    signedRelease:false,isolationQualified:false,sourceFiles:receipt.sources,resolvedLocks:receipt.resolvedLocks,files:await inventory(packagePath)};
  await writeFile(path.join(root,'manifest-input.json'),JSON.stringify(manifest,null,2)+'\n');
  created=true; // An uncertain create acknowledgement must still enter owned-name cleanup.
  await run('create','docker',['create','--name',owner,'--network','none','--cpus','2','--memory','2g','--pids-limit','128',
    '--security-opt','no-new-privileges','--mount',`type=bind,source=${root},target=/evidence`,
    '--mount',`type=bind,source=${packagePath},target=/package,readonly`,
    '--entrypoint','/usr/bin/python3',image,'/evidence/source/fixtures/linux-handoff/check.py'],30);
  await run('native','docker',['start','--attach',owner],210);
  const native=JSON.parse(await readFile(path.join(root,'native.json'),'utf8'));receipt.native=native;
  assert.equal(native.passed,true);assert.equal(native.cleanup.confirmed,true);
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
    receipt.removedScratch=await cleanArtifactPaths(root,['package']);
  }catch(error){receipt.cleanupError=error.message;receipt.passed=false;process.exitCode=1;}
  await writeFile(path.join(root,'verified.json'),JSON.stringify(receipt,null,2)+'\n');
}
console.log(JSON.stringify({passed:receipt.passed,root,error:receipt.error}));
