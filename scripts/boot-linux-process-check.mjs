import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';

assert.equal(process.platform,'win32');
const [preparedPath,name]=process.argv.slice(2);assert.match(name??'',/^[a-z0-9-]{1,45}$/);
const prepared=path.resolve(preparedPath),root=path.join(prepared,`boot-${name}`);
await mkdir(root);
const preparation=JSON.parse(await readFile(path.join(prepared,'preparation.json'),'utf8'));
assert.equal(preparation.prepared,true);
const installationPath=path.resolve('artifacts/public-search-20260913/search-installation.json');
const {installation}=JSON.parse(await readFile(installationPath,'utf8'));
const owned=`thaddeus-linux-payload-${randomUUID().replaceAll('-','')}`;
const receipt={commands:[],prepared,modelCalls:0,gpuDevices:0,guestNetwork:false};
async function run(label,exe,args,seconds=90,cwd=process.cwd()){
  console.log(`Checking ${label}. The raven has reserved one disposable VM.`);
  const output=openSync(path.join(root,label+'.stdout.log'),'wx'),error=openSync(path.join(root,label+'.stderr.log'),'wx');
  const child=spawn(exe,args,{cwd,windowsHide:true,stdio:['ignore',output,error]});closeSync(output);closeSync(error);
  const record={label,exe,args};receipt.commands.push(record);
  // The VM itself is additionally owned by a Windows kill-on-close job.
  const timer=setTimeout(()=>{record.timeout=true;child.kill();},seconds*1000);
  record.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});clearTimeout(timer);
  assert.ok(record.code===0&&!record.timeout,`${label} failed; inspect ${root}`);
  return (await readFile(path.join(root,label+'.stdout.log'),'utf8')).trim();
}
async function hash(file){const result=createHash('sha256');for await(const bytes of createReadStream(file))result.update(bytes);return result.digest('hex');}
let payloadCreated=false;
try{
  assert.equal(await hash(path.join(prepared,'root.ext4')),preparation.diskSha256);
  assert.equal(await hash(installation.executable.path),installation.executable.sha256);
  assert.equal(await hash(installation.imageTool.path),installation.imageTool.sha256);
  const source=path.join(root,'source');await mkdir(source);
  const selected=(await run('source-files','git',['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--','src/Thaddeus.Core','src/Thaddeus.Infrastructure','tools/Thaddeus.LinuxProcessCheck','Directory.Build.props','global.json'])).split('\0').filter(Boolean);
  receipt.sources=[];
  for(const relative of selected){const target=path.join(source,relative);await mkdir(path.dirname(target),{recursive:true});await copyFile(relative,target);receipt.sources.push({path:relative,sha256:await hash(target)});}
  await run('restore','dotnet',['restore','tools/Thaddeus.LinuxProcessCheck','--locked-mode'],120,source);
  await run('publish','dotnet',['publish','tools/Thaddeus.LinuxProcessCheck','-c','Release','-r','linux-x64','--self-contained','true','-o',path.join(root,'payload-files')],180,source);
  const payload=path.join(root,'payload.ext4');
  payloadCreated=true;
  await run('payload','docker',['run','--name',owned,'--network','none','--read-only','--tmpfs','/tmp','--cpus','1','--memory','512m','--pids-limit','64',
    '--mount',`type=bind,source=${prepared},target=/output`,'--entrypoint','/bin/bash',preparation.fixtureImage,'-c',
    `set -euo pipefail; chmod +x /output/boot-${name}/payload-files/Thaddeus.LinuxProcessCheck; truncate -s 256M /output/boot-${name}/payload.ext4; mke2fs -q -t ext4 -F -m 0 -d /output/boot-${name}/payload-files /output/boot-${name}/payload.ext4`]);
  receipt.payloadSha256=await hash(payload);
  const overlay=path.join(root,'root.qcow2');
  await run('overlay',installation.imageTool.path,['create','-f','qcow2','-F','raw','-b',path.join(prepared,'root.ext4'),overlay]);
  const request={Executable:installation.executable.path,Arguments:[
    '-name','thaddeus-linux-process-check','-machine','q35','-accel','whpx','-cpu','qemu64,-svm','-m','1536','-smp','2','-nodefaults','-nic','none','-display','none','-monitor','none','-serial','stdio','-no-reboot',
    '-kernel',installation.kernel.path,'-initrd',installation.initrd.path,
    '-append','console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/usr/lib/systemd/systemd systemd.unit=multi-user.target systemd.log_target=console quiet',
    '-blockdev',JSON.stringify({driver:'file',filename:overlay,'node-name':'overlay-file'}),
    '-blockdev',JSON.stringify({driver:'qcow2',file:'overlay-file','node-name':'root'}),'-device','virtio-blk-pci,drive=root',
    '-blockdev',JSON.stringify({driver:'file',filename:payload,'node-name':'payload-file','read-only':true}),
    '-blockdev',JSON.stringify({driver:'raw',file:'payload-file','node-name':'payload','read-only':true}),'-device','virtio-blk-pci,drive=payload'],
    WorkingDirectory:root,Environment:{SystemRoot:process.env.SystemRoot,WINDIR:process.env.WINDIR,TEMP:root,TMP:root},Lifetime:'00:05:00',OutputLimit:1500000,
    Resources:{CommittedMemoryBytes:2684354560,CpuRate:625,ActiveProcesses:1}};
  await writeFile(path.join(root,'request.json'),JSON.stringify(request,null,2)+'\n');
  await run('vm-owner','dotnet',['run','--project','tools/Thaddeus.LinuxProcessCheck','-c','Release','--','--vm',path.join(root,'request.json'),installationPath],315,source);
  const output=await readFile(path.join(root,'vm.stdout.log'),'utf8');
  const line=output.split('\n').find(line=>line.includes('LINUX_VERIFIED '));assert.ok(line,'Guest did not produce a completed check report.');
  receipt.guest=JSON.parse(line.slice(line.indexOf('LINUX_VERIFIED ')+'LINUX_VERIFIED '.length).trim());
  assert.equal(receipt.guest.passed,true,receipt.guest.error);
  receipt.passed=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{
  if(payloadCreated)try{await run('remove-payload-builder','docker',['rm','--force',owned]);}catch{}
  await writeFile(path.join(root,'verified.json'),JSON.stringify(receipt,null,2)+'\n');
}
console.log(JSON.stringify({passed:receipt.passed,root,error:receipt.error}));
