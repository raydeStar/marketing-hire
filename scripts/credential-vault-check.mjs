import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';

const [executable,artifactPath]=process.argv.slice(2).map(value=>path.resolve(value));
const repository=process.cwd();
if(!executable?.startsWith(repository+path.sep)||!artifactPath?.startsWith(path.join(repository,'artifacts')+path.sep))throw new Error('Use a repository host executable and a fresh private artifact folder.');
await mkdir(artifactPath,{recursive:false});
const scope=randomUUID().replaceAll('-',''),first=randomUUID().replaceAll('-',''),second=randomUUID().replaceAll('-','');
async function operation(operation,id,value){
 const child=spawn(executable,['--credential-helper'],{stdio:['pipe','pipe','pipe'],windowsHide:true});
 let output='',error='';const timer=setTimeout(()=>child.kill(),15000);
 child.stdout.on('data',data=>{output+=data.toString('utf8');if(output.length>12000)child.kill();});
 child.stderr.on('data',data=>{error+=data.toString('utf8');if(error.length>12000)child.kill();});
 child.stdin.on('error',()=>{});child.stdin.end(JSON.stringify({operation,scope,id,value}));
 try{
  const code=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('exit',resolve);});
  assert.equal(code,0,'The native credential operation failed or timed out; no secret output is included.');
  assert.equal(error,'','Native helper wrote an unexpected diagnostic.');
  const reply=JSON.parse(output);assert.equal(reply.ok,true);return reply.value;
 }finally{clearTimeout(timer);}
}
const checks=[];
try{
 assert.equal(await operation('read',first),null);checks.push('Missing entry is distinct from native-store failure');
 const value=JSON.stringify({endpoint:'https://provider.invalid/v1/',key:'fictional-é-🪶-credential'});
 assert.equal(await operation('write',first,value),null);assert.equal(await operation('read',first),value);
 checks.push('Native store round-trips bounded Unicode bytes across separate helper processes');
 assert.equal(await operation('read',second),null);checks.push('Separate credential IDs remain isolated');
 const replaced=JSON.stringify({endpoint:'https://provider.invalid/v1/',key:'fictional-replacement'});
 await operation('write',first,replaced);assert.equal(await operation('read',first),replaced);
 checks.push('Explicit replacement returns exact new bytes');
 await operation('forget',first);assert.equal(await operation('read',first),null);await operation('forget',first);
 checks.push('Removal is verified through a fresh process and is idempotent');
}finally{await operation('forget',first);await operation('forget',second);}
await writeFile(path.join(artifactPath,'verified.json'),JSON.stringify({passed:true,os:os.platform(),architecture:os.arch(),release:os.release(),checks,
  backend:os.platform()==='win32'?'Windows Credential Manager':os.platform()==='darwin'?'macOS Keychain':'Linux Secret Service',fictionalCredentialsOnly:true,liveModelCalls:0,gpuInference:0,allCreatedEntriesRemoved:true},null,2)+'\n');
 console.log(`${checks.length} native credential checks passed. The keys stayed with the butler.`);
