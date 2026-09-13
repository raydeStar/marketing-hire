import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';

assert.equal(process.platform,'win32','This fixture boots a Linux qualification guest using the existing Windows QEMU owner.');
const [name]=process.argv.slice(2);
assert.match(name??'',/^[a-z0-9-]{1,45}$/);
const root=path.resolve('artifacts',`linux-process-${name}`),owned=`thaddeus-linux-${randomUUID().replaceAll('-','')}`;
await mkdir(root);await mkdir(path.join(root,'context'));
const receipt={status:'progress',commands:[],owned,modelCalls:0,gpuDevices:0,guestNetwork:false};
async function run(label,exe,args,seconds=120,cwd=process.cwd()){
  console.log(`Checking ${label}. The raven is using the disposable estate.`);
  const output=openSync(path.join(root,label+'.stdout.log'),'wx'),error=openSync(path.join(root,label+'.stderr.log'),'wx');
  const child=spawn(exe,args,{cwd,windowsHide:true,stdio:['ignore',output,error]});closeSync(output);closeSync(error);
  const record={label,exe,args,started:new Date().toISOString()};receipt.commands.push(record);
  const timeout=setTimeout(()=>{record.timeout=true;child.kill();},seconds*1000);
  record.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});clearTimeout(timeout);
  assert.ok(record.code===0&&!record.timeout,`${label} failed; inspect retained logs in ${root}`);
  return (await readFile(path.join(root,label+'.stdout.log'),'utf8')).trim();
}
async function hash(file){const result=createHash('sha256');for await(const bytes of createReadStream(file))result.update(bytes);return result.digest('hex');}
let created=false,diskCreated=false;
try{
  receipt.sourceHead=await run('source-head','git',['rev-parse','HEAD']);
  const source=path.join(root,'source');await mkdir(source);
  const selected=(await run('source-files','git',['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--','src/Thaddeus.Core','src/Thaddeus.Infrastructure','tools/Thaddeus.LinuxProcessCheck','Directory.Build.props','global.json'])).split('\0').filter(Boolean);
  receipt.sources=[];
  for(const relative of selected){
    const target=path.join(source,relative);await mkdir(path.dirname(target),{recursive:true});await copyFile(relative,target);
    receipt.sources.push({path:relative,sha256:await hash(target)});
  }
  await copyFile('fixtures/linux-process-vm/setup.sh',path.join(root,'context','setup.sh'));
  receipt.setupSha256=await hash(path.join(root,'context','setup.sh'));
  await run('restore','dotnet',['restore','tools/Thaddeus.LinuxProcessCheck','--locked-mode'],120,source);
  // RID expansion changes only the captured lock files, as in the portable-host publisher.
  await run('publish','dotnet',['publish','tools/Thaddeus.LinuxProcessCheck','-c','Release','-r','linux-x64','--self-contained','true','-o',path.join(root,'context','bin')],180,source);
  receipt.baseImage=await run('base-image','docker',['image','inspect','ubuntu:24.04','--format','{{.Id}}']);
  assert.equal(receipt.baseImage,'sha256:561618e2c15bf2397621dd04f96926663a3b5616c189cf7e38db7e82f5c538ea');
  await run('create','docker',['create','--name',owned,'--cpus','1','--memory','768m','--pids-limit','128','--entrypoint','/bin/bash',receipt.baseImage,'/opt/probe/setup.sh']);created=true;
  await run('copy','docker',['cp',path.join(root,'context'),owned+':/opt/probe']);
  await run('prepare','docker',['start','--attach',owned],300);
  await run('packages','docker',['cp',owned+':/opt/probe/packages.txt',path.join(root,'packages.txt')]);
  receipt.fixtureImage=await run('snapshot','docker',['commit',owned,owned+':fixture']);
  await run('export','docker',['export','--output',path.join(root,'rootfs.tar'),owned],120);
  diskCreated=true;
  await run('disk','docker',['run','--name',owned+'-disk','--network','none','--cpus','1','--memory','1g','--pids-limit','64',
    '--mount',`type=bind,source=${root},target=/output`,'--entrypoint','/bin/bash',receipt.fixtureImage,'-c',
    'set -euo pipefail; mkdir /rootfs; tar --numeric-owner -xf /output/rootfs.tar -C /rootfs; truncate -s 4G /output/root.ext4; mke2fs -q -t ext4 -F -m 0 -L linux-check -d /rootfs /output/root.ext4; e2fsck -fn /output/root.ext4'],300);
  receipt.diskSha256=await hash(path.join(root,'root.ext4'));
  receipt.prepared=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{
  if(diskCreated)try{await run('remove-disk','docker',['rm','--force',owned+'-disk']);}catch{}
  if(created)try{await run('remove-builder','docker',['rm','--force',owned]);}catch{}
  await writeFile(path.join(root,'preparation.json'),JSON.stringify(receipt,null,2)+'\n');
}
console.log(JSON.stringify({prepared:receipt.prepared,root,error:receipt.error}));
