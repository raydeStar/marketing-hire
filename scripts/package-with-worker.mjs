import assert from 'node:assert/strict';
import {spawn,spawnSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {cleanArtifactPaths,requireArtifactSpace} from './artifact-storage.mjs';

const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const [hostArgument,installationArgument,name,...extra]=process.argv.slice(2);
assert.ok(hostArgument&&installationArgument&&!extra.length&&/^[a-zA-Z0-9-]{1,55}$/.test(name??''),'Use: node scripts/package-with-worker.mjs HOST_PACKAGE PINNED_INSTALLATION FRESH-NAME');
const runtime=`${{win32:'win',linux:'linux'}[process.platform]??'unsupported'}-${process.arch}`;
assert.ok(['win-x64','linux-x64'].includes(runtime),'Combined workers currently require a native Windows x64 or Linux x64 publisher.');
const host=path.resolve(hostArgument),installation=path.resolve(installationArgument);
const manifest=JSON.parse(await readFile(path.join(host,'package-manifest.json'),'utf8'));assert.equal(manifest.runtime,runtime);
const root=path.join(repository,'artifacts',`portable-combined-${name}`);await mkdir(root);
const archive=path.join(root,`thaddeus-${runtime}.zip`),build=path.join(root,'builder');
const steps=[];let failure;
function git(args){const result=spawnSync('git',args,{cwd:repository,encoding:'utf8',windowsHide:true});assert.equal(result.status,0,result.stderr);return result.stdout.trim();}
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
async function fileHash(file){const digest=createHash('sha256');for await(const chunk of createReadStream(file))digest.update(chunk);return digest.digest('hex');}
async function inputs(){
  const names=git(['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--','src/Thaddeus.Core','src/Thaddeus.Infrastructure','tools/Thaddeus.WorkerBundle','Directory.Build.props','global.json','scripts/package-with-worker.mjs','scripts/artifact-storage.mjs']).split('\0').filter(Boolean).sort();
  return Promise.all(names.map(async name=>({path:name,sha256:hash(await readFile(path.join(repository,name)))})));
}
async function run(label,file,args){
  const out=openSync(path.join(root,label+'.stdout.log'),'wx'),err=openSync(path.join(root,label+'.stderr.log'),'wx');let child;
  try{child=spawn(file,args,{cwd:repository,windowsHide:true,stdio:['ignore',out,err]});}finally{closeSync(out);closeSync(err);}
  const result=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',(code,signal)=>resolve({code,signal}));});
  steps.push({label,file,args,...result});assert.equal(result.code,0,`${label} failed; inspect ${root}.`);
}
let before;
try{
  await requireArtifactSpace(root,256*1024**2,'Combined archive builder');before=await inputs();
  // Only the small packaging tool is compiled. Reuse the already checked host and immutable worker inputs.
  await run('restore','dotnet',['restore','tools/Thaddeus.WorkerBundle','--locked-mode']);
  await run('build','dotnet',['build','tools/Thaddeus.WorkerBundle','--no-restore','--configuration','Release','--output',build]);
  await run('archive','dotnet',[path.join(build,'Thaddeus.WorkerBundle.dll'),'archive',installation,host,archive,runtime]);
  const receipt=JSON.parse(await readFile(archive+'.receipt.json','utf8'));
  assert.equal(receipt.archiveSha256,await fileHash(archive));
  assert.equal(receipt.combinedManifestSha256,await fileHash(archive+'.manifest.json'));
  assert.equal(receipt.hostManifestSha256,await fileHash(path.join(host,'package-manifest.json')));
  assert.deepEqual(await inputs(),before,'Packaging sources changed during execution.');
  await writeFile(path.join(root,'SHA256SUMS'),`${receipt.archiveSha256}  ${path.basename(archive)}\n`);
  await writeFile(path.join(root,'published.json'),JSON.stringify({archive,manifest:archive+'.manifest.json',sourceHostPackage:host,sourceHead:manifest.sourceHead,runtime,
    includesWorker:true,signedRelease:false,verifiedOnTarget:false,archiveVerified:true,workerExecutionVerified:false},null,2)+'\n');
  console.log(`Combined ${runtime} archive verified. No staged worker copy, VM, model or hosted runner was used. ${archive}`);
}catch(error){failure=error.message;throw error;}
finally{
  const removed=await cleanArtifactPaths(root,['builder']);
  await writeFile(path.join(root,'packaging-receipt.json'),JSON.stringify({passed:!failure,failure,sourceFiles:before,steps,
    removed,hostRebuilt:false,workerStagingCopies:0,modelCalls:0,workerStarts:0,githubActions:0},null,2)+'\n');
}
