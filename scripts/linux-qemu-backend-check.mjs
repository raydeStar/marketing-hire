import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {createReadStream,openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
const [name,reusePath]=process.argv.slice(2);assert.match(name??'',/^[a-z0-9-]{1,45}$/);
const root=path.resolve('artifacts',`linux-backend-${name}`);await mkdir(root);
const assets=path.resolve('artifacts/linux-qemu-session-20260913-b');
const original=JSON.parse(await readFile(path.join(assets,'verified.json'),'utf8'));
const runtime=JSON.parse(await readFile('artifacts/qemu-linux-runtime-20260913-b/runtime-reference.json','utf8'));
const inputs=path.resolve('artifacts/qemu-inputs-script-check');
const owner='thaddeus-backend-'+randomUUID().replaceAll('-','');
const receipt={commands:[],sources:[],owner,modelCalls:0,gpuDevices:0,githubActionsStarted:0};const created=[];
async function hash(file){const h=createHash('sha256');for await(const b of createReadStream(file))h.update(b);return h.digest('hex');}
async function run(label,executable,args,seconds=120,cwd=process.cwd()){
 console.log(`Checking ${label}. This estate has a spare key, not a spare writer.`);
 const out=openSync(path.join(root,label+'.stdout.log'),'wx'),err=openSync(path.join(root,label+'.stderr.log'),'wx');
 const child=spawn(executable,args,{cwd,windowsHide:true,stdio:['ignore',out,err]});closeSync(out);closeSync(err);
 const entry={label,executable,args};receipt.commands.push(entry);const timer=setTimeout(()=>{entry.timeout=true;child.kill();},seconds*1000);
 entry.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});clearTimeout(timer);
 assert.ok(entry.code===0&&!entry.timeout,`${label} failed; inspect ${root}`);
 return (await readFile(path.join(root,label+'.stdout.log'),'utf8')).trim();
}
try{
 assert.equal(original.passed,true);assert.equal(await hash(path.join(assets,'payload.ext4')),original.payloadSha256);receipt.assetsSha256=original.payloadSha256;
 const manifest=JSON.parse(await readFile(runtime.manifest.path,'utf8'));assert.equal(await hash(runtime.manifest.path),runtime.manifest.sha256);
 for(const file of manifest.files)assert.equal(await hash(path.join(runtime.root,file.path)),file.sha256);
 receipt.runtimeManifestSha256=runtime.manifest.sha256;
 const source=path.join(root,'source');await mkdir(source);
 const files=(await run('sources','git',['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--','src/Thaddeus.Core','src/Thaddeus.Infrastructure','tools/Thaddeus.LinuxProcessCheck','Directory.Build.props','global.json'])).split('\0').filter(Boolean);
 for(const file of files){const target=path.join(source,file);await mkdir(path.dirname(target),{recursive:true});await copyFile(file,target);receipt.sources.push({path:file,sha256:await hash(target)});}
 await copyFile('scripts/linux-qemu-backend-check.mjs',path.join(root,'runner.mjs'));
 await run('restore','dotnet',['restore','tools/Thaddeus.LinuxProcessCheck','--locked-mode'],120,source);
 await run('publish','dotnet',['publish','tools/Thaddeus.LinuxProcessCheck','-c','Release','-r','linux-x64','--self-contained','true','-o',path.join(root,'tools')],180,source);
 if(reusePath){
  const reuse=path.resolve(reusePath);assert.ok(reuse.startsWith(path.resolve('artifacts')+path.sep));
  const saved=JSON.parse(await readFile(path.join(reuse,'verified.json'),'utf8'));assert.equal(await hash(path.join(reuse,'root.ext4')),saved.rootSha256);
  await copyFile(path.join(reuse,'root.ext4'),path.join(root,'root.ext4'));receipt.reusedBase=reuse;
 }
 const buildScript=`set -euo pipefail\nif [ ! -f /output/root.ext4 ]; then\n mkdir /rootfs; tar --numeric-owner -xf /assets/rootfs.tar -C /rootfs\n sed -i 's|^ExecStart=.*|ExecStart=/opt/probe/tools/Thaddeus.LinuxProcessCheck --backend-check /opt/probe/bin/installation.json /home/thaddeuscheck/backend-check|;s|^RequiresMountsFor=.*|RequiresMountsFor=/opt/probe/bin /opt/probe/tools|' /rootfs/etc/systemd/system/thaddeus-check.service\n mkdir -p /rootfs/opt/probe/tools; printf '/dev/vdc /opt/probe/tools ext4 ro,nodev,nosuid 0 0\\n' >> /rootfs/etc/fstab\n truncate -s 4G /tmp/root.ext4; mke2fs -q -t ext4 -F -m 0 -d /rootfs /tmp/root.ext4; e2fsck -fn /tmp/root.ext4; cp --sparse=always /tmp/root.ext4 /output/root.ext4\nfi\ncp -a /output/tools /tmp/tools; chmod +x /tmp/tools/Thaddeus.LinuxProcessCheck; truncate -s 256M /tmp/tools.ext4; mke2fs -q -t ext4 -F -m 0 -d /tmp/tools /tmp/tools.ext4; e2fsck -fn /tmp/tools.ext4; cp --sparse=always /tmp/tools.ext4 /output/tools.ext4\n`;
 await writeFile(path.join(root,'disks.sh'),buildScript);receipt.diskScriptSha256=await hash(path.join(root,'disks.sh'));
 created.push(owner+'-disk');
 await run('disks','docker',['run','--name',owner+'-disk','--network','none','--cpus','1','--memory','1g','--pids-limit','64',
  '--mount',`type=bind,source=${root},target=/output`,'--mount',`type=bind,source=${assets},target=/assets,readonly`,
  '--entrypoint','/bin/bash',original.fixtureImage,'/output/disks.sh'],180);
 receipt.rootSha256=await hash(path.join(root,'root.ext4'));receipt.toolsSha256=await hash(path.join(root,'tools.ext4'));
 const prefix=['/runtime/lib/ld-linux-x86-64.so.2','--inhibit-cache','--library-path','/runtime/lib'];
 const args=['/runtime/bin/qemu-system-x86_64','-no-user-config','-L','/runtime/share/qemu','-name',owner,'-machine','q35','-accel','kvm','-cpu','host','-smp','2','-m','4096',
 '-nodefaults','-nic','none','-display','none','-monitor','none','-serial','stdio','-no-reboot','-kernel','/inputs/alpine/boot/vmlinuz-virt','-initrd','/inputs/alpine/boot/initramfs-virt',
 '-append','console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/sbin/init quiet',
 '-drive','file=/output/root.qcow2,format=qcow2,if=virtio','-drive','file=/assets/payload.ext4,format=raw,if=virtio,readonly=on','-drive','file=/output/tools.ext4,format=raw,if=virtio,readonly=on'];
 await writeFile(path.join(root,'outer.py'),`import subprocess\nenv={'HOME':'/tmp','TMPDIR':'/tmp','LC_ALL':'C','QEMU_MODULE_DIR':'/disabled'}\nsubprocess.run(${JSON.stringify([...prefix,'/runtime/bin/qemu-img','create','-f','qcow2','-F','raw','-b','/output/root.ext4','/output/root.qcow2'])},env=env,check=True,timeout=20)\nresult=subprocess.run(${JSON.stringify([...prefix,...args])},env=env,timeout=285)\nraise SystemExit(result.returncode)\n`);
 created.push(owner+'-boot');
 await run('boot','docker',['run','--name',owner+'-boot','--network','none','--read-only','--tmpfs','/tmp:rw,size=64m','--cpus','2','--memory','5g','--pids-limit','128',
 '--cap-drop','ALL','--security-opt','no-new-privileges','--device','/dev/kvm','--mount',`type=bind,source=${root},target=/output`,
 '--mount',`type=bind,source=${runtime.root},target=/runtime,readonly`,'--mount',`type=bind,source=${assets},target=/assets,readonly`,
 '--mount',`type=bind,source=${inputs},target=/inputs,readonly`,'--entrypoint','/usr/bin/timeout',original.fixtureImage,'--signal=KILL','305','/usr/bin/python3','/output/outer.py'],320);
 const consoleText=await readFile(path.join(root,'boot.stdout.log'),'utf8');const line=consoleText.split(/\r?\n/).find(line=>line.includes('BACKEND_VERIFIED '));
 assert.ok(line,'No native backend receipt.');receipt.native=JSON.parse(line.slice(line.indexOf('BACKEND_VERIFIED ')+17));assert.equal(receipt.native.passed,true);receipt.passed=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{for(const name of created.reverse())try{await run('remove-'+name.slice(-4),'docker',['rm','--force',name]);}catch{}await writeFile(path.join(root,'verified.json'),JSON.stringify(receipt,null,2)+'\n');}
console.log(JSON.stringify({passed:receipt.passed,root,error:receipt.error}));
