import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {randomUUID,createHash} from 'node:crypto';
import {openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
const [name]=process.argv.slice(2);assert.match(name??'',/^[a-z0-9-]{1,45}$/);
const root=path.resolve('artifacts',`qemu-linux-${name}`);await mkdir(root);
const owned=`thaddeus-linux-qemu-${randomUUID().replaceAll('-','')}`;
const image='sha256:561618e2c15bf2397621dd04f96926663a3b5616c189cf7e38db7e82f5c538ea';
await copyFile('workers/qemu/build-linux-runtime.sh',path.join(root,'build.sh'));
const receipt={owned,image,scriptSha256:createHash('sha256').update(await readFile(path.join(root,'build.sh'))).digest('hex'),commands:[],modelCalls:0,gpuDevices:0};
async function run(label,args,seconds=60){
 console.log(`Checking ${label}. The raven has capped the builder at two CPUs.`);
 const out=openSync(path.join(root,label+'.stdout.log'),'wx'),err=openSync(path.join(root,label+'.stderr.log'),'wx');
 const child=spawn('docker',args,{windowsHide:true,stdio:['ignore',out,err]});closeSync(out);closeSync(err);
 const record={label,args};receipt.commands.push(record);const timer=setTimeout(()=>{record.timeout=true;child.kill();},seconds*1000);
 record.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});clearTimeout(timer);
 assert.ok(record.code===0&&!record.timeout,`${label} failed; inspect ${root}`);
}
let created=false;
try{
 await run('create',['create','--name',owned,'--cpus','2','--memory','2g','--pids-limit','128','--mount',`type=bind,source=${root},target=/output`,'--entrypoint','/bin/bash',image,'/output/build.sh']);created=true;
 await run('build',['start','--attach',owned],1800);
 receipt.built=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{if(created)try{await run('remove-builder',['rm','--force',owned]);}catch{}await writeFile(path.join(root,'build.json'),JSON.stringify(receipt,null,2)+'\n');}
console.log(JSON.stringify({built:receipt.built,root,error:receipt.error}));
