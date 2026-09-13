import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {openSync,closeSync} from 'node:fs';
import {copyFile,mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
assert.equal(process.platform,'win32');
const [name,mode='session']=process.argv.slice(2);assert.match(name??'',/^[a-z0-9-]{1,45}$/);assert.ok(['session','backend'].includes(mode));
const root=path.resolve('artifacts',`windows-qemu-${name}`);await mkdir(root);
const source=path.join(root,'source');await mkdir(source);
const receipt={commands:[],sources:[],modelCalls:0,gpuDevices:0,githubActionsStarted:0};
async function run(label,executable,args,seconds=120,cwd=process.cwd()){
 console.log(`Checking ${label}. The Windows estate retains its own steward.`);
 const out=openSync(path.join(root,label+'.stdout.log'),'wx'),err=openSync(path.join(root,label+'.stderr.log'),'wx');
 const child=spawn(executable,args,{cwd,windowsHide:true,stdio:['ignore',out,err]});closeSync(out);closeSync(err);
 const record={label,executable,args};receipt.commands.push(record);
 const timer=setTimeout(()=>{record.timeout=true;child.kill();},seconds*1000);
 record.code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});clearTimeout(timer);
 assert.ok(record.code===0&&!record.timeout,`${label} failed; inspect ${root}`);
 return (await readFile(path.join(root,label+'.stdout.log'),'utf8')).trim();
}
try{
 const selected=(await run('sources','git',['-c','core.quotepath=false','ls-files','--cached','--others','--exclude-standard','-z','--','src/Thaddeus.Core','src/Thaddeus.Infrastructure','tools/Thaddeus.LinuxProcessCheck','Directory.Build.props','global.json'])).split('\0').filter(Boolean);
 for(const relative of selected){const target=path.join(source,relative);await mkdir(path.dirname(target),{recursive:true});await copyFile(relative,target);receipt.sources.push({path:relative,sha256:createHash('sha256').update(await readFile(target)).digest('hex')});}
 await run('restore','dotnet',['restore','tools/Thaddeus.LinuxProcessCheck','--locked-mode'],120,source);
 const driver=path.join(root,'driver');
 await run('publish','dotnet',['publish','tools/Thaddeus.LinuxProcessCheck','-c','Release','-r','win-x64','--self-contained','true','-o',driver],180,source);
 await run('session',path.join(driver,'Thaddeus.LinuxProcessCheck.exe'),['--'+mode+'-check',path.resolve('artifacts/public-search-20260913/search-installation.json'),path.join(root,'session')],270);
 receipt.native=JSON.parse(await readFile(path.join(root,'session/verified.json'),'utf8'));assert.equal(receipt.native.passed,true);receipt.passed=true;
}catch(error){receipt.error=error.message;process.exitCode=1;}
finally{await writeFile(path.join(root,'verified.json'),JSON.stringify(receipt,null,2)+'\n');}
console.log(JSON.stringify({passed:receipt.passed,root,error:receipt.error}));
