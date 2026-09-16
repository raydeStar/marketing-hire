import assert from 'node:assert/strict';
import {spawn,spawnSync} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {openSync,closeSync} from 'node:fs';
import {mkdir,open,readFile,unlink,writeFile} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {requireArtifactSpace} from './artifact-storage.mjs';

const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const [mode,name]=process.argv.slice(2);
assert.ok(['core','package'].includes(mode)&&/^[a-zA-Z0-9-]{1,45}$/.test(name??''),'Use: node scripts/check-local.mjs core|package FRESH-NAME');
const rid=`${{win32:'win',darwin:'osx',linux:'linux'}[process.platform]}-${process.arch}`;
assert.ok(['win-x64','linux-x64','osx-x64','osx-arm64'].includes(rid),'Use a supported native host.');
const root=path.join(repository,'artifacts');await mkdir(root,{recursive:true});
const lockPath=path.join(root,'local-check.lock'),identity=randomUUID();
let lease;
try{lease=await open(lockPath,'wx');}catch{throw new Error('A local-check lock already exists. Inspect its process before removing it; no competing check was started.');}
await lease.writeFile(JSON.stringify({id:identity,pid:process.pid,host:os.hostname(),started:new Date().toISOString(),mode,name})+'\n');
const evidence=path.join(root,`local-check-${name}`),steps=[];
const hash=value=>createHash('sha256').update(value).digest('hex');
function git(args){const result=spawnSync('git',args,{cwd:repository,encoding:'utf8',windowsHide:true});assert.equal(result.status,0,result.stderr);return result.stdout.trim();}
async function inputs(){
  const names=git(['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--','src','web','tests','evals','tools','scripts','workers','fixtures','packaging','third-party','.github','.gitattributes','Directory.Build.props','global.json','Thaddeus.slnx']).split('\0').filter(Boolean).sort();
  return Promise.all(names.map(async name=>({path:name,sha256:hash(await readFile(path.join(repository,name)))})));
}
async function run(label,file,args){
  const started=new Date().toISOString();
  const stdout=openSync(path.join(evidence,label+'.stdout.log'),'wx'),stderr=openSync(path.join(evidence,label+'.stderr.log'),'wx');
  let child;
  console.log(`Checking ${label}. The raven has brought a local ledger.`);
  try{child=spawn(file,args,{cwd:repository,windowsHide:true,stdio:['ignore',stdout,stderr]});}finally{closeSync(stdout);closeSync(stderr);}
  const result=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',(code,signal)=>resolve({code,signal}));});
  steps.push({label,file,args,started,finished:new Date().toISOString(),...result});
  if(result.code!==0)throw new Error(`${label} failed; inspect ${evidence}. Later checks were not started.`);
}
let receipt;
try{
  await mkdir(evidence);
  await requireArtifactSpace(evidence,mode==='package'?2*1024**3:512*1024**2,'Local '+mode+' check');
  const sourceFiles=await inputs();
  receipt={passed:false,mode,runtime:rid,os:os.version(),sourceHead:git(['rev-parse','HEAD']),sourceDirty:!!git(['status','--porcelain']),sourceFiles,steps,
    githubActionsStarted:0,liveModelCalls:0,workerStarted:false,crossPlatformQualification:false};
  if(mode==='core'){
    await run('secret-scan',process.execPath,['scripts/scan-secrets.mjs']);
    await run('restore','dotnet',['restore','--locked-mode']);
    if(process.platform==='win32'){
      await run('notification-restore','dotnet',['restore','src/Thaddeus.Notifications/Thaddeus.Notifications.csproj','--locked-mode']);
      await run('notification-build','dotnet',['build','src/Thaddeus.Notifications/Thaddeus.Notifications.csproj','--no-restore','--configuration','Release']);
    }
    await run('backend','dotnet',['test','--no-restore','--configuration','Release','--logger','trx;LogFileName=backend.trx','--results-directory',evidence]);
    await run('protocols',process.execPath,['--test','scripts/luna-protocol.test.mjs','scripts/openclaw-gateway-control.test.mjs','workers/openclaw/configuration.test.mjs','scripts/artifact-storage.test.mjs','scripts/portable-cleanup.test.mjs','scripts/windows-installer.test.mjs']);
    if(process.platform==='win32')await run('web',process.env.ComSpec??'cmd.exe',['/d','/s','/c','npm --prefix web run build']);
    else await run('web','npm',['--prefix','web','run','build']);
  }else{
    const publication=path.join(root,`portable-local-${name}`),packagePath=path.join(publication,`thaddeus-${rid}`);
    await run('publish',process.execPath,['scripts/publish-portable.mjs',rid,`local-${name}`]);
    await run('native-package',process.execPath,['scripts/portable-check.mjs',publication,path.join(evidence,'native')]);
    await run('native-credentials',process.execPath,['scripts/credential-vault-check.mjs',path.join(packagePath,`Thaddeus.Host${process.platform==='win32'?'.exe':''}`),path.join(evidence,'credentials')]);
    await run('mcp-fixture-restore','dotnet',['restore','tools/Thaddeus.McpFixture/Thaddeus.McpFixture.csproj','--locked-mode']);
    await run('mcp-fixture-build','dotnet',['build','tools/Thaddeus.McpFixture/Thaddeus.McpFixture.csproj','--no-restore']);
    await run('browser',process.execPath,['scripts/browser-suite-check.mjs',packagePath,path.join(evidence,'browser')]);
  }
  assert.deepEqual(await inputs(),sourceFiles,'Source files changed during checks. Keep the evidence but do not promote it as one revision.');
  receipt.passed=true;
  console.log(`Local ${mode} checks passed. Evidence: ${evidence}. No GitHub runner was summoned.`);
}catch(error){if(receipt)receipt.error=error.message;throw error;}
finally{
  try{if(receipt)await writeFile(path.join(evidence,'verified.json'),JSON.stringify(receipt,null,2)+'\n');}
  finally{await lease.close();if(JSON.parse(await readFile(lockPath,'utf8')).id===identity)await unlink(lockPath);}
}
