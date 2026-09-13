import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {copyFile,cp,mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';

const [name,reusePath]=process.argv.slice(2);assert.match(name??'',/^[a-z0-9-]{1,45}$/);
const root=path.resolve('artifacts',`linux-qemu-${name}`);await mkdir(root);
const prepared=path.resolve('artifacts/linux-process-ownership-20260913-c');
const runtimeRoot=path.resolve('artifacts/qemu-linux-runtime-20260913-b');
const inputs=path.resolve('artifacts/qemu-inputs-script-check');
const preparation=JSON.parse(await readFile(path.join(prepared,'preparation.json'),'utf8'));
const runtimeReference=JSON.parse(await readFile(path.join(runtimeRoot,'runtime-reference.json'),'utf8'));
const {installation}=JSON.parse(await readFile('artifacts/public-search-20260913/search-installation.json','utf8'));
const owner=`thaddeus-linux-session-${randomUUID().replaceAll('-','')}`;
const receipt={commands:[],sources:[],owner,modelCalls:0,gpuDevices:0,githubActionsStarted:0,guestNetwork:false,hostConfigurationChanged:false};
async function hash(file){const h=createHash('sha256');for await(const b of createReadStream(file))h.update(b);return h.digest('hex');}
async function run(label,executable,args,seconds=120,cwd=process.cwd()){
 console.log(`Checking ${label}. The test estate has a closing time.`);
 const out=openSync(path.join(root,label+'.stdout.log'),'wx'),err=openSync(path.join(root,label+'.stderr.log'),'wx');
 const child=spawn(executable,args,{cwd,windowsHide:true,stdio:['ignore',out,err]});closeSync(out);closeSync(err);
 const record={label,executable,args};receipt.commands.push(record);
 const timer=setTimeout(()=>{record.timeout=true;child.kill();},seconds*1000);
 record.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});clearTimeout(timer);
 assert.ok(record.code===0&&!record.timeout,`${label} failed; inspect ${root}`);
 return (await readFile(path.join(root,label+'.stdout.log'),'utf8')).trim();
}
const created=[];
try{
 assert.equal(preparation.prepared,true);
 assert.equal(await hash(path.join(inputs,'downloads/alpine-virt-3.24.1-x86_64.iso')),'e73a6241bd5f3c5c2d4d38c02cc52c378c0415a7c888bd292066bf36e0f41a39');
 assert.equal(await hash(runtimeReference.manifest.path),runtimeReference.manifest.sha256);
 receipt.runtimeManifestSha256=runtimeReference.manifest.sha256;
 const source=path.join(root,'source');await mkdir(source);
 const selected=(await run('sources','git',['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--','src/Thaddeus.Core','src/Thaddeus.Infrastructure','tools/Thaddeus.LinuxProcessCheck','Directory.Build.props','global.json'])).split('\0').filter(Boolean);
 for(const relative of selected){const target=path.join(source,relative);await mkdir(path.dirname(target),{recursive:true});await copyFile(relative,target);receipt.sources.push({path:relative,sha256:await hash(target)});}
 await run('restore','dotnet',['restore','tools/Thaddeus.LinuxProcessCheck','--locked-mode'],120,source);
 const payload=path.join(root,'payload-files');
 await run('publish','dotnet',['publish','tools/Thaddeus.LinuxProcessCheck','-c','Release','-r','linux-x64','--self-contained','true','-o',payload],180,source);
 await cp(runtimeReference.root,path.join(payload,'runtime'),{recursive:true,errorOnExist:true,force:false});
 await copyFile(runtimeReference.manifest.path,path.join(payload,'runtime-manifest.json'));
 for(const [role,pin] of Object.entries({kernel:installation.kernel,initrd:installation.initrd,base:installation.baseDisk})){
  await copyFile(pin.path,path.join(payload,role));assert.equal(await hash(path.join(payload,role)),pin.sha256);
 }
 const linuxManifest=JSON.parse(await readFile(runtimeReference.manifest.path,'utf8'));
 for(const file of linuxManifest.files)assert.equal(await hash(path.join(runtimeReference.root,file.path)),file.sha256);
 const pin=relative=>({path:'/opt/probe/bin/runtime/'+relative,sha256:linuxManifest.files.find(f=>f.path===relative).sha256});
 const guestInstallation={installation:{executable:pin('bin/qemu-system-x86_64'),imageTool:pin('bin/qemu-img'),
  kernel:{path:'/opt/probe/bin/kernel',sha256:installation.kernel.sha256},initrd:{path:'/opt/probe/bin/initrd',sha256:installation.initrd.sha256},
  baseDisk:{path:'/opt/probe/bin/base',sha256:installation.baseDisk.sha256},runtimePackage:{root:'/opt/probe/bin/runtime',manifest:{path:'/opt/probe/bin/runtime-manifest.json',sha256:runtimeReference.manifest.sha256}}}};
 await writeFile(path.join(payload,'installation.json'),JSON.stringify(guestInstallation));
 await copyFile('fixtures/linux-qemu-vm/setup.sh',path.join(root,'setup.sh'));receipt.setupSha256=await hash(path.join(root,'setup.sh'));
 if(reusePath){
  const previous=path.resolve(reusePath);assert.ok(previous.startsWith(path.resolve('artifacts')+path.sep));
  const reuse=JSON.parse(await readFile(path.join(previous,'verified.json'),'utf8'));
  assert.equal(reuse.setupSha256,receipt.setupSha256,'The disposable base setup changed; prepare it again.');
  receipt.fixtureImage=reuse.fixtureImage;receipt.reusedBase=previous;
  if(reuse.rootSha256){
   assert.equal(await hash(path.join(previous,'root.ext4')),reuse.rootSha256);
   await copyFile(path.join(previous,'root.ext4'),path.join(root,'root.ext4'));
  }else{
   assert.match(reuse.fixtureImage??'',/^sha256:[a-f0-9]{64}$/);
   await run('reuse-image','docker',['create','--name',owner,'--network','none','--cpus','1','--memory','64m','--pids-limit','16','--entrypoint','/bin/true',reuse.fixtureImage]);created.push(owner);
   await run('export','docker',['export','--output',path.join(root,'rootfs.tar'),owner],120);
  }
 }else{
 await run('create','docker',['create','--name',owner,'--cpus','1','--memory','1g','--pids-limit','128',
  '--mount',`type=bind,source=${root},target=/output`,'--mount',`type=bind,source=${path.join(inputs,'downloads/alpine-virt-3.24.1-x86_64.iso')},target=/alpine.iso,readonly`,
  '--entrypoint','/bin/bash',preparation.fixtureImage,'/output/setup.sh']);created.push(owner);
 await run('modules','docker',['start','--attach',owner],240);
 receipt.fixtureImage=await run('snapshot','docker',['commit',owner,owner+':fixture']);
 await run('export','docker',['export','--output',path.join(root,'rootfs.tar'),owner],120);
 }
 created.push(owner+'-disk');
 await run('disks','docker',['run','--name',owner+'-disk','--network','none','--cpus','1','--memory','1g','--pids-limit','64',
  '--mount',`type=bind,source=${root},target=/output`,'--entrypoint','/bin/bash',receipt.fixtureImage,'-c',
  'set -euo pipefail; mkdir /imagework; if [ -f /output/rootfs.tar ]; then mkdir /rootfs; tar --numeric-owner -xf /output/rootfs.tar -C /rootfs; truncate -s 4G /imagework/root.ext4; mke2fs -q -t ext4 -F -m 0 -d /rootfs /imagework/root.ext4; e2fsck -fn /imagework/root.ext4; cp --sparse=always /imagework/root.ext4 /output/root.ext4; fi; cp -a /output/payload-files /imagework/payload; chmod +x /imagework/payload/Thaddeus.LinuxProcessCheck /imagework/payload/runtime/bin/* /imagework/payload/runtime/lib/ld-linux-x86-64.so.2; truncate -s 12G /imagework/payload.ext4; mke2fs -q -t ext4 -F -m 0 -d /imagework/payload /imagework/payload.ext4; e2fsck -fn /imagework/payload.ext4; cp --sparse=always /imagework/payload.ext4 /output/payload.ext4'],360);
 receipt.rootSha256=await hash(path.join(root,'root.ext4'));receipt.payloadSha256=await hash(path.join(root,'payload.ext4'));
 const loader='/runtime/lib/ld-linux-x86-64.so.2';
 const prefix=['--inhibit-cache','--library-path','/runtime/lib'];
 const outerArgs=['-no-user-config','-L','/runtime/share/qemu','-name',owner,'-machine','q35','-accel','kvm','-cpu','host','-smp','2','-m','4096',
  '-nodefaults','-nic','none','-display','none','-monitor','none','-serial','stdio','-no-reboot','-kernel','/inputs/alpine/boot/vmlinuz-virt','-initrd','/inputs/alpine/boot/initramfs-virt',
  '-append','console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/sbin/init quiet',
  '-drive','file=/output/root.qcow2,format=qcow2,if=virtio','-drive','file=/output/payload.ext4,format=raw,if=virtio,readonly=on'];
 await writeFile(path.join(root,'outer.py'),`import subprocess,json\nenv={'HOME':'/tmp','TMPDIR':'/tmp','LC_ALL':'C','QEMU_MODULE_DIR':'/disabled'}\nsubprocess.run(${JSON.stringify([loader,...prefix,'/runtime/bin/qemu-img','create','-f','qcow2','-F','raw','-b','/output/root.ext4','/output/root.qcow2'])},env=env,check=True,timeout=20)\nresult=subprocess.run(${JSON.stringify([loader,...prefix,'/runtime/bin/qemu-system-x86_64',...outerArgs])},env=env,timeout=290)\nraise SystemExit(result.returncode)\n`);
 created.push(owner+'-boot');
 await run('boot','docker',['run','--name',owner+'-boot','--network','none','--read-only','--tmpfs','/tmp:rw,size=64m',
  '--cpus','2','--memory','5g','--pids-limit','128','--cap-drop','ALL','--security-opt','no-new-privileges','--device','/dev/kvm',
  '--mount',`type=bind,source=${root},target=/output`,'--mount',`type=bind,source=${runtimeReference.root},target=/runtime,readonly`,
  '--mount',`type=bind,source=${inputs},target=/inputs,readonly`,'--entrypoint','/usr/bin/timeout',receipt.fixtureImage,'--signal=KILL','305',
  '/usr/bin/python3','/output/outer.py'],320);
 const consoleText=await readFile(path.join(root,'boot.stdout.log'),'utf8');
 const line=consoleText.split(/\r?\n/).find(line=>line.includes('SESSION_VERIFIED '));assert.ok(line,'No native session receipt was emitted.');
 receipt.native=JSON.parse(line.slice(line.indexOf('SESSION_VERIFIED ')+17));assert.equal(receipt.native.passed,true);
 receipt.passed=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{
 for(const owned of created.reverse())try{await run('remove-'+owned.slice(-5),'docker',['rm','--force',owned]);}catch{}
 await writeFile(path.join(root,'verified.json'),JSON.stringify(receipt,null,2)+'\n');
}
console.log(JSON.stringify({passed:receipt.passed,root,error:receipt.error}));
