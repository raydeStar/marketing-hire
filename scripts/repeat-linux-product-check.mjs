import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const [sourcePath,name,mode,...extra]=process.argv.slice(2);
assert.ok(extra.length===0&&(mode===undefined||mode==='--from-interrupted'),'Expected optional --from-interrupted only.');
assert.match(name??'',/^[a-z0-9-]{1,45}$/);
const source=path.resolve(sourcePath),artifacts=path.resolve('artifacts');
assert.ok(source.startsWith(artifacts+path.sep),'Use a retained local product fixture.');
const root=path.join(artifacts,'linux-product-repeat-'+name);await mkdir(root);
const saved=JSON.parse(await readFile(path.join(source,'verified.json'),'utf8'));
assert.equal(saved.fixtureLayout,'packaged-python-v1');
let sourceDisposition='passed',interruptionSha256;
if(mode==='--from-interrupted'){
 const file=path.join(source,'interruption.json'),bytes=await readFile(file),interruption=JSON.parse(bytes);
 assert.notEqual(saved.passed,true);assert.equal(interruption.classification,'owner-interrupted');
 assert.equal(interruption.container,saved.owner+'-boot');
 for(const label of ['publish','disks','remove-boot','remove-disk'])assert.ok(saved.commands.some(step=>step.label===label&&step.code===0&&!step.timeout),'Interrupted fixture was not prepared and contained: '+label);
 assert.ok(saved.commands.some(step=>step.label==='boot'&&step.code!==0),'Expected an interrupted boot.');
 sourceDisposition='owner-interrupted';interruptionSha256=createHash('sha256').update(bytes).digest('hex');
}else assert.equal(saved.passed,true);
const assets=path.join(artifacts,'linux-qemu-session-20260913-b');
const original=JSON.parse(await readFile(path.join(assets,'verified.json'),'utf8'));
const runtime=JSON.parse(await readFile(path.join(artifacts,'qemu-linux-runtime-20260913-b/runtime-reference.json'),'utf8'));
const rootDisk=saved.rootDisk?.path??path.join(source,'root.ext4'),sharedWorker=saved.sharedWorkerTools;
const rootFormat=saved.rootDisk?.format??'raw';assert.ok(['raw','qcow2'].includes(rootFormat));
assert.ok(rootDisk.startsWith(artifacts+path.sep));
if(sharedWorker)assert.ok(sharedWorker.path.startsWith(artifacts+path.sep));
const inputs=path.join(artifacts,'qemu-inputs-script-check'),owner='thaddeus-product-repeat-'+randomUUID().replaceAll('-','');
const receipt={passed:false,source,sourceDisposition,interruptionSha256,owner,commands:[],liveModelCalls:0,gpuDevices:0,githubActionsStarted:0,rebuilt:false,copiedBaseDisks:false};
let created=false;
async function hash(file){const digest=createHash('sha256');for await(const bytes of createReadStream(file))digest.update(bytes);return digest.digest('hex');}
async function run(label,args,seconds=665){
 const output=openSync(path.join(root,label+'.stdout.log'),'wx'),error=openSync(path.join(root,label+'.stderr.log'),'wx');
 const child=spawn('docker',args,{windowsHide:true,stdio:['ignore',output,error]});closeSync(output);closeSync(error);
 const step={label,args,started:new Date().toISOString()};receipt.commands.push(step);
 const timer=setTimeout(()=>{step.timeout=true;child.kill();},seconds*1000);
 try{step.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});}
 finally{clearTimeout(timer);step.finished=new Date().toISOString();}
 assert.ok(step.code===0&&!step.timeout,`${label} failed; inspect ${root}.`);
}
try{
 await copyFile(fileURLToPath(import.meta.url),path.join(root,'runner.mjs'));
 receipt.runnerSha256=await hash(path.join(root,'runner.mjs'));
 assert.equal(original.passed,true);
 for(const [file,expected] of [[rootDisk,saved.rootSha256],[path.join(source,'tools.ext4'),saved.toolsSha256],
  [path.join(assets,'payload.ext4'),saved.assetsSha256],[runtime.manifest.path,saved.runtimeManifestSha256]])
  assert.equal(await hash(file),expected,'Frozen fixture input changed: '+file);
 if(sharedWorker)assert.equal(await hash(sharedWorker.path),sharedWorker.sha256,'Shared worker tools changed.');
 const manifest=JSON.parse(await readFile(runtime.manifest.path,'utf8'));
 for(const entry of manifest.files)assert.equal(await hash(path.join(runtime.root,entry.path)),entry.sha256);
 const installation=JSON.parse(await readFile(path.join(assets,'payload-files/installation.json'),'utf8')).installation;
 // The diagnostic host boots with the same pinned kernel/initrd as the original session fixture.
 assert.equal(await hash(path.join(inputs,'alpine/boot/vmlinuz-virt')),installation.kernel.sha256);
 assert.equal(await hash(path.join(inputs,'alpine/boot/initramfs-virt')),installation.initrd.sha256);
 receipt.inputs={rootSha256:saved.rootSha256,toolsSha256:saved.toolsSha256,assetsSha256:saved.assetsSha256,runtimeManifestSha256:saved.runtimeManifestSha256,
  packageManifestSha256:saved.packageManifestSha256,kernelSha256:installation.kernel.sha256,initrdSha256:installation.initrd.sha256,rootDisk,rootFormat,sharedWorker};
 const prefix=['/runtime/lib/ld-linux-x86-64.so.2','--inhibit-cache','--library-path','/runtime/lib'];
 const args=['/runtime/bin/qemu-system-x86_64','-no-user-config','-L','/runtime/share/qemu','-name',owner,'-machine','q35','-accel','kvm','-cpu','host','-smp','2','-m','6144',
  '-nodefaults','-nic','none','-display','none','-monitor','none','-serial','stdio','-no-reboot','-kernel','/inputs/alpine/boot/vmlinuz-virt','-initrd','/inputs/alpine/boot/initramfs-virt',
  '-append','console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/sbin/init quiet'+(sharedWorker?' systemd.mount-extra=/dev/vdd:/opt/probe/worker-image:ext4:ro,nodev,nosuid':''),
  '-drive','file=/output/root.qcow2,format=qcow2,if=virtio','-drive','file=/assets/payload.ext4,format=raw,if=virtio,readonly=on',
  '-drive','file=/base/tools.ext4,format=raw,if=virtio,readonly=on',...(sharedWorker?['-drive','file=/shared-worker.ext4,format=raw,if=virtio,readonly=on']:[])];
 const program=`import subprocess\nenv={'HOME':'/tmp','TMPDIR':'/tmp','LC_ALL':'C','QEMU_MODULE_DIR':'/disabled'}\nsubprocess.run(${JSON.stringify([...prefix,'/runtime/bin/qemu-img','create','-f','qcow2','-F',rootFormat,'-b','/fixture-root.ext4','/output/root.qcow2'])},env=env,check=True,timeout=20)\nresult=subprocess.run(${JSON.stringify([...prefix,...args])},env=env,timeout=630)\nraise SystemExit(result.returncode)\n`;
 await writeFile(path.join(root,'outer.py'),program);receipt.outerSha256=await hash(path.join(root,'outer.py'));
 console.log('Repeating the frozen Linux product. The raven has kept the same instruments.');
 created=true;
 await run('boot',['run','--name',owner,'--network','none','--read-only','--tmpfs','/tmp:rw,size=64m','--cpus','2','--memory','7g','--pids-limit','128',
  '--cap-drop','ALL','--security-opt','no-new-privileges','--device','/dev/kvm','--mount',`type=bind,source=${root},target=/output`,
  '--mount',`type=bind,source=${rootDisk},target=/fixture-root.ext4,readonly`,
  ...(sharedWorker?['--mount',`type=bind,source=${sharedWorker.path},target=/shared-worker.ext4,readonly`]:[]),
  '--mount',`type=bind,source=${source},target=/base,readonly`,'--mount',`type=bind,source=${runtime.root},target=/runtime,readonly`,
  '--mount',`type=bind,source=${assets},target=/assets,readonly`,'--mount',`type=bind,source=${inputs},target=/inputs,readonly`,
  '--entrypoint','/usr/bin/timeout',original.fixtureImage,'--signal=KILL','650','/usr/bin/python3','/output/outer.py']);
 const consoleText=await readFile(path.join(root,'boot.stdout.log'),'utf8');const line=consoleText.split(/\r?\n/).find(line=>line.includes('PRODUCT_VERIFIED '));
 assert.ok(line,'No native product receipt.');receipt.native=JSON.parse(line.slice(line.indexOf('PRODUCT_VERIFIED ')+17));
 assert.equal(receipt.native.passed,true,'The unchanged Linux workflow failed. Retain its diagnostic receipt.');receipt.passed=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{
 if(created)try{await run('remove',['rm','--force',owner],30);}catch(error){receipt.cleanupError=error.message;receipt.passed=false;process.exitCode=1;}
 await writeFile(path.join(root,'verified.json'),JSON.stringify(receipt,null,2)+'\n');
}
console.log(JSON.stringify({passed:receipt.passed,root,error:receipt.error}));
