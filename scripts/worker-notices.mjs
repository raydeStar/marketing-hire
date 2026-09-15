import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream} from 'node:fs';
import {mkdir,readFile,writeFile,lstat,realpath,readdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {requireArtifactSpace} from './artifact-storage.mjs';

const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const [installationArgument,name,...extra]=process.argv.slice(2);
assert.ok(installationArgument&&!extra.length&&/^[a-zA-Z0-9-]{1,45}$/.test(name??''),'Use: node scripts/worker-notices.mjs PINNED_INSTALLATION FRESH-NAME');
const root=path.join(repository,'artifacts','worker-notices-'+name);
const image='sha256:d3fef0da199e9b1006d150950668bcb8580f9b7bd386152eb8674b66837cd2e8';
const container='thaddeus-notices-'+randomUUID().replaceAll('-','');
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
async function fileHash(file){const digest=createHash('sha256');for await(const bytes of createReadStream(file))digest.update(bytes);return digest.digest('hex');}
async function noLinks(file){const full=path.resolve(file);assert.equal(await realpath(full),full,'Linked input paths are refused.');assert.equal((await lstat(full)).isSymbolicLink(),false);return full;}
const sources=['scripts/worker-notices.mjs','scripts/artifact-storage.mjs','tools/worker-notices/collect.py','tools/worker-notices/test_collect.py'];
const sourceFiles=await Promise.all(sources.map(async file=>({path:file,sha256:hash(await readFile(path.join(repository,file)))})));
await mkdir(root);await mkdir(path.join(root,'guest'));await mkdir(path.join(root,'runtime'));
const steps=[];let failure,created=false,removed=false;
async function run(label,args,seconds=60){
  const record={label,args,stdout:'',stderr:''};steps.push(record);
  const child=spawn('docker',args,{cwd:repository,windowsHide:true,stdio:['ignore','pipe','pipe']});
  const timer=setTimeout(()=>{record.timeout=true;child.kill();},seconds*1000);
  child.stdout.on('data',data=>{record.stdout+=data;if(record.stdout.length>100_000){record.overflow=true;child.kill();}});
  child.stderr.on('data',data=>{record.stderr+=data;if(record.stderr.length>100_000){record.overflow=true;child.kill();}});
  try{record.exitCode=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});}
  finally{clearTimeout(timer);}
  if(record.exitCode!==0||record.timeout||record.overflow)throw new Error(label+' failed; inspect the bounded receipt.');
  return record.stdout;
}
let report;
try{
  const storage=await requireArtifactSpace(root,64*1024**2,'Read-only worker notice inspection');
  const installationBytes=await readFile(await noLinks(installationArgument));assert.ok(installationBytes.length<64_000);
  const descriptor=JSON.parse(installationBytes.toString('utf8').replace(/^\uFEFF/,''));assert.equal(descriptor.kind,'qemu');
  const installation=descriptor.installation,disk=await noLinks(installation.baseDisk.path);
  assert.match(installation.baseDisk.sha256,/^[a-f0-9]{64}$/);
  const runtime=installation.runtimePackage,manifestPath=await noLinks(runtime.manifest.path);
  const manifestBytes=await readFile(manifestPath);assert.equal(hash(manifestBytes),runtime.manifest.sha256);
  const manifest=JSON.parse(manifestBytes);assert.equal(manifest.kind,'qemu-windows-runtime');assert.ok(manifest.files.length<=4096);
  await run('engine',['version','--format','{{.Server.Version}}']);
  assert.equal((await run('inspector-image',['image','inspect',image,'--format','{{.Id}}'])).trim(),image);
  const scripts=await noLinks(path.join(repository,'tools/worker-notices'));
  created=true;
  await run('create',['create','--name',container,'--label','thaddeus.notice-owner='+container,'--pull','never','--network','none','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges',
    '--pids-limit','32','--memory','384m','--cpus','1','--user','0','--env','PYTHONDONTWRITEBYTECODE=1',
    '--mount','type=bind,source='+disk+',target=/input/root.ext4,readonly',
    '--mount','type=bind,source='+scripts+',target=/inspector,readonly',
    '--mount','type=bind,source='+path.join(root,'guest')+',target=/output',
    '--entrypoint','/usr/bin/python3',image,'-c',
    "import subprocess,sys; subprocess.run(['/usr/bin/python3','-m','unittest','discover','-s','/inspector','-p','test_collect.py','-v'],check=True); subprocess.run(['/usr/bin/python3','/inspector/collect.py','/input/root.ext4',sys.argv[1],'/output'],check=True)",installation.baseDisk.sha256]);
  console.log('Inspecting the immutable worker disk. No guest programs, VM or model are started.');
  await run('collect',['start','--attach',container],600);
  const state=JSON.parse(await run('state',['inspect',container,'--format','{{json .State}}']));
  assert.equal(state.Running,false);assert.equal(state.ExitCode,0);
  await run('remove',['rm',container]);removed=true;
  const guest=JSON.parse(await readFile(path.join(root,'guest/inventory.json'),'utf8'));
  assert.equal(guest.inspectionPassed,true);assert.equal(guest.diskSha256,installation.baseDisk.sha256);
  const runtimeRoot=await noLinks(runtime.root),expectedNames=new Set(),notices=[];
  for(const file of manifest.files){
    assert.ok(typeof file.path==='string'&&!file.path.includes('\\')&&!file.path.split('/').some(p=>!p||p==='.'||p==='..'||p.includes(':')));
    assert.ok(!expectedNames.has(file.path.toLowerCase()));expectedNames.add(file.path.toLowerCase());
    const original=await noLinks(path.join(runtimeRoot,file.path));assert.ok(original.startsWith(runtimeRoot+path.sep));
    assert.equal(await fileHash(original),file.sha256,file.path);
    if(/(^|\/)(copying(?:\.lib)?|.*licenses?\.(txt|html))$/i.test(file.path)){
      const bytes=await readFile(original);assert.ok(bytes.length<=4_000_000);
      const target=file.sha256+'.txt';await writeFile(path.join(root,'runtime',target),bytes);
      notices.push({path:file.path,sha256:file.sha256,bytes:bytes.length,file:'runtime/'+target});
    }
  }
  const actual=[];
  async function inventory(directory,relative=''){for(const entry of await readdir(directory,{withFileTypes:true})){
    assert.equal(entry.isSymbolicLink(),false);const name=relative+entry.name;
    if(entry.isDirectory())await inventory(path.join(directory,entry.name),name+'/');else{assert.ok(entry.isFile());actual.push(name);}
  }}
  await inventory(runtimeRoot);assert.deepEqual(actual.sort(),manifest.files.map(file=>file.path).sort());
  const guestAssets=[];
  for(const name of ['kernel','initrd']){const pin=installation[name];assert.equal(await fileHash(await noLinks(pin.path)),pin.sha256);guestAssets.push({kind:name,sha256:pin.sha256,noticesVerified:false,sourceArchiveVerified:false});}
  for(const file of sourceFiles)assert.equal(hash(await readFile(path.join(repository,file.path))),file.sha256,'Inspector source changed during execution.');
  report={inspectionPassed:true,redistributionComplete:false,storage,sourceFiles,installationSha256:hash(installationBytes),
    inspectorImage:image,guestDiskSha256:guest.diskSha256,
    guest:{components:guest.components.length,dpkg:guest.components.filter(c=>c.ecosystem==='dpkg').length,
      npm:guest.components.filter(c=>c.ecosystem==='npm').length,gaps:guest.gaps.length,textFiles:guest.textFiles,capturedBytes:guest.capturedBytes,
      inventorySha256:await fileHash(path.join(root,'guest/inventory.json'))},
    runtime:{manifestSha256:runtime.manifest.sha256,files:manifest.files.length,archive:manifest.archive,notices,
      completeDependencyMapping:false,sourceArchiveVerified:false},guestAssets,
    vmStarts:0,liveModelCalls:0,gpuUse:false,githubActions:0,
    limitations:['Candidate notice text is not a complete license review.','Source archives, firmware and non-dpkg/non-npm guest components require separate verified coverage.']};
  await writeFile(path.join(root,'inventory.json'),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify({inspectionPassed:true,redistributionComplete:false,guest:report.guest,runtimeFiles:manifest.files.length,root}));
}catch(error){failure=error.message;throw error;}
finally{
  if(created&&!removed){try{
    const owned=(await run('cleanup-owner',['inspect',container,'--format','{{index .Config.Labels "thaddeus.notice-owner"}}'])).trim();
    assert.equal(owned,container,'Cleanup ownership could not be verified.');
    await run('remove-after-failure',['rm','--force',container]);removed=true;
  }catch(error){failure=(failure??'')+' Cleanup: '+error.message;}}
  const cleanup={passed:!failure,inspectionPassed:!!report,containerRemoved:removed,failure,steps,sourceFiles,
    guestMounted:false,guestExecuted:false,largeCopies:0,retained:'Compact inventories, original candidate notice texts and inspector receipts; pinned inputs and active app untouched.'};
  await writeFile(path.join(root,'receipt.json'),JSON.stringify(cleanup,null,2)+'\n');
  if(!removed)process.exitCode=1;
}
