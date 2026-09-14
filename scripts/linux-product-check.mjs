import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,readdir,stat,writeFile} from 'node:fs/promises';
import path from 'node:path';

const [name,reusePath,workerDiskPath,sharedWorkerCase,...extra]=process.argv.slice(2);assert.match(name??'',/^[a-z0-9-]{1,45}$/);
assert.equal(extra.length,0);assert.ok(!sharedWorkerCase||(reusePath&&workerDiskPath),'Sharing requires a captured root and the exact prepared worker image.');
const root=path.resolve('artifacts',`linux-product-${name}`);await mkdir(root);
const assets=path.resolve('artifacts/linux-qemu-session-20260913-b');
const original=JSON.parse(await readFile(path.join(assets,'verified.json'),'utf8'));
const runtime=JSON.parse(await readFile('artifacts/qemu-linux-runtime-20260913-b/runtime-reference.json','utf8'));
const inputs=path.resolve('artifacts/qemu-inputs-script-check');
const owner='thaddeus-product-'+randomUUID().replaceAll('-','');
const receipt={commands:[],sources:[],owner,liveModelCalls:0,gpuDevices:0,githubActionsStarted:0,builtOn:process.platform,executedOn:'linux-x64'};
const created=[];
const workerDisk=workerDiskPath?path.resolve(workerDiskPath):null;
async function hash(file){const h=createHash('sha256');for await(const b of createReadStream(file))h.update(b);return h.digest('hex');}
async function run(label,executable,args,seconds=120,cwd=process.cwd()){
 console.log(`Checking ${label}. The packaged steward must earn his keys.`);
 const out=openSync(path.join(root,label+'.stdout.log'),'wx'),err=openSync(path.join(root,label+'.stderr.log'),'wx');
 const child=spawn(executable,args,{cwd,windowsHide:true,stdio:['ignore',out,err]});closeSync(out);closeSync(err);
 const entry={label,executable,args};receipt.commands.push(entry);const timer=setTimeout(()=>{entry.timeout=true;child.kill();},seconds*1000);
 entry.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});clearTimeout(timer);
 assert.ok(entry.code===0&&!entry.timeout,`${label} failed; inspect ${root}`);
 return (await readFile(path.join(root,label+'.stdout.log'),'utf8')).trim();
}
async function inventory(directory,prefix=''){
 const files=[];
 for(const entry of await readdir(directory,{withFileTypes:true})){
  assert.ok(!entry.isSymbolicLink());const relative=prefix+entry.name,full=path.join(directory,entry.name);
  if(entry.isDirectory())files.push(...await inventory(full,relative+'/'));else files.push({path:relative,sha256:await hash(full)});
 }return files.sort((a,b)=>a.path.localeCompare(b.path));
}
try{
 assert.equal(original.passed,true);assert.equal(await hash(path.join(assets,'payload.ext4')),original.payloadSha256);receipt.assetsSha256=original.payloadSha256;
 const manifest=JSON.parse(await readFile(runtime.manifest.path,'utf8'));assert.equal(await hash(runtime.manifest.path),runtime.manifest.sha256);
 for(const file of manifest.files)assert.equal(await hash(path.join(runtime.root,file.path)),file.sha256);
 receipt.runtimeManifestSha256=runtime.manifest.sha256;
 if(workerDisk){
  assert.ok(workerDisk.startsWith(path.resolve('artifacts')+path.sep),'Use a prepared local worker image.');
  const info=await stat(workerDisk);assert.ok(info.isFile()&&info.size===8*1024**3,'Expected the prepared 8 GiB raw worker disk.');
  receipt.workerDisk={source:workerDisk,sha256:await hash(workerDisk),guestPath:'/opt/probe/tools/worker-base.ext4'};
 }
 if(sharedWorkerCase){
  const shared=path.resolve(sharedWorkerCase);assert.ok(shared.startsWith(path.resolve('artifacts')+path.sep));
  const saved=JSON.parse(await readFile(path.join(shared,'verified.json'),'utf8'));
  assert.equal(saved.fixtureLayout,'packaged-python-v1');assert.equal(saved.workerDisk?.sha256,receipt.workerDisk.sha256);
  assert.equal(saved.workerDisk.guestPath,'/opt/probe/tools/worker-base.ext4','Use a captured tools disk containing its own worker base.');
  assert.ok(saved.commands.some(step=>step.label==='disks'&&step.code===0&&!step.timeout));
  const disk=path.join(shared,'tools.ext4');assert.equal(await hash(disk),saved.toolsSha256);
  receipt.sharedWorkerTools={path:disk,sha256:saved.toolsSha256,sourceCase:shared};
  receipt.workerDisk.guestPath='/opt/probe/worker-image/worker-base.ext4';
 }
 const source=path.join(root,'source');await mkdir(source);
 const files=(await run('sources','git',['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--','src','web','fixtures/notes','fixtures/linux-qemu-vm/product-check.py','scripts/Start Thaddeus.command','Directory.Build.props','global.json'])).split('\0').filter(Boolean);
 for(const file of files){const target=path.join(source,file);await mkdir(path.dirname(target),{recursive:true});await copyFile(file,target);receipt.sources.push({path:file,sha256:await hash(target)});}
 await copyFile('scripts/linux-product-check.mjs',path.join(root,'runner.mjs'));
 if(process.platform==='win32'){
  await run('web-install',process.env.ComSpec??'cmd.exe',['/d','/s','/c','npm --prefix web ci'],120,source);
  await run('web-build',process.env.ComSpec??'cmd.exe',['/d','/s','/c','npm --prefix web run build'],120,source);
 }else{
  await run('web-install','npm',['--prefix','web','ci'],120,source);await run('web-build','npm',['--prefix','web','run','build'],120,source);
 }
 const payload=path.join(root,'tools'),packagePath=path.join(payload,'package');await mkdir(payload);
 await run('restore','dotnet',['restore','src/Thaddeus.Host','--locked-mode'],120,source);
 await run('publish','dotnet',['publish','src/Thaddeus.Host','-c','Release','-r','linux-x64','--self-contained','true','-p:ContinuousIntegrationBuild=true','-o',packagePath],180,source);
 await copyFile(path.join(source,'scripts/Start Thaddeus.command'),path.join(packagePath,'start-thaddeus.sh'));
 const packageManifest={schemaVersion:1,kind:'cross-published-development-fixture',runtime:'linux-x64',sourceHead:await run('head','git',['rev-parse','HEAD']),
  signedRelease:false,isolationQualified:false,sourceFiles:receipt.sources,files:await inventory(packagePath)};
 await writeFile(path.join(packagePath,'package-manifest.json'),JSON.stringify(packageManifest,null,2)+'\n');
 receipt.packageManifestSha256=await hash(path.join(packagePath,'package-manifest.json'));
 await copyFile(path.join(source,'fixtures/linux-qemu-vm/product-check.py'),path.join(payload,'check.py'));
 const installation=JSON.parse(await readFile(path.join(assets,'payload-files/installation.json'),'utf8'));
 for(const [file,pin] of [['alpine/boot/vmlinuz-virt',installation.installation.kernel],['alpine/boot/initramfs-virt',installation.installation.initrd]])
  assert.equal(await hash(path.join(inputs,file)),pin.sha256);
 receipt.kernelSha256=installation.installation.kernel.sha256;receipt.initrdSha256=installation.installation.initrd.sha256;
 if(workerDisk)installation.installation.baseDisk={path:receipt.workerDisk.guestPath,sha256:receipt.workerDisk.sha256};
 await writeFile(path.join(payload,'installation.json'),JSON.stringify({kind:'qemu',...installation}));
 if(reusePath){
  const reuse=path.resolve(reusePath);assert.ok(reuse.startsWith(path.resolve('artifacts')+path.sep));
  const saved=JSON.parse(await readFile(path.join(reuse,'verified.json'),'utf8'));assert.equal(saved.fixtureLayout,'packaged-python-v1');
  const backing=saved.rootDisk?.path??path.join(reuse,'root.ext4');assert.ok(backing.startsWith(path.resolve('artifacts')+path.sep));
  assert.equal(await hash(backing),saved.rootSha256);
  if(sharedWorkerCase)receipt.rootDisk={path:backing,sha256:saved.rootSha256};else await copyFile(backing,path.join(root,'root.ext4'));
  receipt.reusedBase=reuse;
 }
 receipt.fixtureLayout='packaged-python-v1';
 const buildScript=`set -euo pipefail
if [ ! -f /output/root.ext4 ] && [ ${sharedWorkerCase?'1':'0'} != 1 ]; then
 mkdir /rootfs; tar --numeric-owner -xf /assets/rootfs.tar -C /rootfs
 sed -i 's|^ExecStart=.*|ExecStart=/usr/bin/python3 /opt/probe/tools/check.py|;s|^RequiresMountsFor=.*|RequiresMountsFor=/opt/probe/bin /opt/probe/tools|;s|^TimeoutStartSec=.*|TimeoutStartSec=600|' /rootfs/etc/systemd/system/thaddeus-check.service
 mkdir -p /rootfs/opt/probe/tools; printf '/dev/vdc /opt/probe/tools ext4 ro,nodev,nosuid 0 0\n' >> /rootfs/etc/fstab
 truncate -s 4G /tmp/root.ext4; mke2fs -q -t ext4 -F -m 0 -d /rootfs /tmp/root.ext4; e2fsck -fn /tmp/root.ext4; cp --sparse=always /tmp/root.ext4 /output/root.ext4
fi
cp -a /output/tools /tmp/tools; chmod +x /tmp/tools/package/Thaddeus.Host /tmp/tools/package/start-thaddeus.sh
${workerDisk&&!sharedWorkerCase?'cp --sparse=always /worker-base /tmp/tools/worker-base.ext4':''}
truncate -s ${workerDisk&&!sharedWorkerCase?'9G':'384M'} /tmp/tools.ext4; mke2fs -q -t ext4 -F -m 0 -d /tmp/tools /tmp/tools.ext4; e2fsck -fn /tmp/tools.ext4; cp --sparse=always /tmp/tools.ext4 /output/tools.ext4
`;
 await writeFile(path.join(root,'disks.sh'),buildScript);receipt.diskScriptSha256=await hash(path.join(root,'disks.sh'));
 created.push(owner+'-disk');
 await run('disks','docker',['run','--name',owner+'-disk','--network','none','--cpus','1','--memory','1g','--pids-limit','64',
  '--mount',`type=bind,source=${root},target=/output`,'--mount',`type=bind,source=${assets},target=/assets,readonly`,
  ...(workerDisk&&!sharedWorkerCase?['--mount',`type=bind,source=${workerDisk},target=/worker-base,readonly`]:[]),
  '--entrypoint','/bin/bash',original.fixtureImage,'/output/disks.sh'],180);
 if(workerDisk)assert.equal(await hash(workerDisk),receipt.workerDisk.sha256,'Prepared worker image changed during fixture build.');
 receipt.rootSha256=await hash(receipt.rootDisk?.path??path.join(root,'root.ext4'));receipt.toolsSha256=await hash(path.join(root,'tools.ext4'));
 const prefix=['/runtime/lib/ld-linux-x86-64.so.2','--inhibit-cache','--library-path','/runtime/lib'];
 const args=['/runtime/bin/qemu-system-x86_64','-no-user-config','-L','/runtime/share/qemu','-name',owner,'-machine','q35','-accel','kvm','-cpu','host','-smp','2','-m','6144',
 '-nodefaults','-nic','none','-display','none','-monitor','none','-serial','stdio','-no-reboot','-kernel','/inputs/alpine/boot/vmlinuz-virt','-initrd','/inputs/alpine/boot/initramfs-virt',
 '-append','console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/sbin/init quiet'+(sharedWorkerCase?' systemd.mount-extra=/dev/vdd:/opt/probe/worker-image:ext4:ro,nodev,nosuid':''),
 '-drive','file=/output/root.qcow2,format=qcow2,if=virtio','-drive','file=/assets/payload.ext4,format=raw,if=virtio,readonly=on','-drive','file=/output/tools.ext4,format=raw,if=virtio,readonly=on',
 ...(sharedWorkerCase?['-drive','file=/shared-worker.ext4,format=raw,if=virtio,readonly=on']:[])];
 await writeFile(path.join(root,'outer.py'),`import subprocess\nenv={'HOME':'/tmp','TMPDIR':'/tmp','LC_ALL':'C','QEMU_MODULE_DIR':'/disabled'}\nsubprocess.run(${JSON.stringify([...prefix,'/runtime/bin/qemu-img','create','-f','qcow2','-F','raw','-b',receipt.rootDisk?'/fixture-root.ext4':'/output/root.ext4','/output/root.qcow2'])},env=env,check=True,timeout=20)\nresult=subprocess.run(${JSON.stringify([...prefix,...args])},env=env,timeout=630)\nraise SystemExit(result.returncode)\n`);
 created.push(owner+'-boot');
 await run('boot','docker',['run','--name',owner+'-boot','--network','none','--read-only','--tmpfs','/tmp:rw,size=64m','--cpus','2','--memory','7g','--pids-limit','128',
 '--cap-drop','ALL','--security-opt','no-new-privileges','--device','/dev/kvm','--mount',`type=bind,source=${root},target=/output`,
 ...(receipt.rootDisk?['--mount',`type=bind,source=${receipt.rootDisk.path},target=/fixture-root.ext4,readonly`]:[]),
 ...(receipt.sharedWorkerTools?['--mount',`type=bind,source=${receipt.sharedWorkerTools.path},target=/shared-worker.ext4,readonly`]:[]),
 '--mount',`type=bind,source=${runtime.root},target=/runtime,readonly`,'--mount',`type=bind,source=${assets},target=/assets,readonly`,
 '--mount',`type=bind,source=${inputs},target=/inputs,readonly`,'--entrypoint','/usr/bin/timeout',original.fixtureImage,'--signal=KILL','650','/usr/bin/python3','/output/outer.py'],665);
 const consoleText=await readFile(path.join(root,'boot.stdout.log'),'utf8');const line=consoleText.split(/\r?\n/).find(line=>line.includes('PRODUCT_VERIFIED '));
 assert.ok(line,'No native product receipt.');receipt.native=JSON.parse(line.slice(line.indexOf('PRODUCT_VERIFIED ')+17));assert.equal(receipt.native.passed,true);receipt.passed=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{for(const name of created.reverse())try{await run('remove-'+name.slice(-4),'docker',['rm','--force',name]);}catch{}await writeFile(path.join(root,'verified.json'),JSON.stringify(receipt,null,2)+'\n');}
console.log(JSON.stringify({passed:receipt.passed,root,error:receipt.error}));
