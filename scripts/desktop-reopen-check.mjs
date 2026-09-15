import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {openSync,closeSync} from 'node:fs';
import {mkdir,readFile,writeFile,stat} from 'node:fs/promises';
import {createServer,createConnection} from 'node:net';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import {requireArtifactSpace,cleanArtifactPaths} from './artifact-storage.mjs';

assert.equal(process.platform,'win32','This fixture supplies Windows native evidence only.');
const [packageArgument,name]=process.argv.slice(2);
assert.ok(packageArgument&&/^[a-zA-Z0-9-]{1,50}$/.test(name??''),'Use HOST_PACKAGE FRESH-NAME.');
const packagePath=path.resolve(packageArgument),root=path.resolve('artifacts',`desktop-reopen-check-${name}`);
await mkdir(root);await requireArtifactSpace(root,32*1024**2,'Native desktop reopen');
const hash=value=>createHash('sha256').update(value).digest('hex');
const manifestBytes=await readFile(path.join(packagePath,'package-manifest.json')),manifest=JSON.parse(manifestBytes);
assert.equal(manifest.runtime,'win-x64');
for(const file of manifest.files)assert.equal(hash(await readFile(path.join(packagePath,file.path))),file.sha256,file.path);
const sourceHashes=[];
for(const file of ['scripts/desktop-reopen-check.mjs','src/Thaddeus.Host/DesktopReopen.cs','src/Thaddeus.Host/DesktopLaunch.cs','src/Thaddeus.Host/Program.cs'])sourceHashes.push({path:file,sha256:hash(await readFile(file))});
const data=path.join(root,'study'),executable=path.join(packagePath,'Thaddeus.Host.exe'),owned=new Set(),steps=[];
const environment=Object.fromEntries(Object.entries(process.env).filter(([key])=>!key.toLowerCase().startsWith('thaddeus')));
async function port(){const listener=createServer();await new Promise(resolve=>listener.listen(0,'127.0.0.1',resolve));const value=listener.address().port;await new Promise(resolve=>listener.close(resolve));return value;}
const origin=`http://127.0.0.1:${await port()}`,workerPort=await port();
assert.notEqual(new URL(origin).port,String(workerPort));
const settings={schemaVersion:1,dataDirectory:data,localOrigin:origin,workerPort};
function run(profile,label){
 const out=openSync(path.join(root,label+'.stdout.log'),'wx'),err=openSync(path.join(root,label+'.stderr.log'),'wx');
 let child;try{child=spawn(executable,['--desktop','--no-browser','--launch-profile',profile],{cwd:packagePath,env:environment,windowsHide:true,stdio:['ignore',out,err]});}finally{closeSync(out);closeSync(err);}
 owned.add(child);child.finished=new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',(code,signal)=>{owned.delete(child);resolve({code,signal});});});return child;
}
async function exit(child){let timer;try{return await Promise.race([child.finished,new Promise((_,reject)=>timer=setTimeout(()=>reject(new Error('Owned launch did not finish in time.')),12_000))]);}finally{clearTimeout(timer);}}
async function profile(value,label){const file=path.join(root,label+'.json');await writeFile(file,JSON.stringify(value));return file;}
function dotnetJson(value){return JSON.stringify(value).replace(/[<>&+'\u007f-\uffff]/g,character=>'\\u'+character.charCodeAt(0).toString(16).padStart(4,'0').toUpperCase());}
const identity={package:packagePath.toUpperCase(),data:data.toUpperCase(),origin,workerPort,installation:null,user:os.userInfo().username};
const pipe='\\\\.\\pipe\\thaddeus-open-'+hash(dotnetJson(identity));
async function ticket(){
 return new Promise((resolve,reject)=>{
  const socket=createConnection(pipe),chunks=[];socket.setTimeout(4000,()=>socket.destroy(new Error('IPC reply deadline exceeded.')));
  socket.once('connect',()=>socket.write(Buffer.from([1])));socket.on('data',chunk=>{
   try{chunks.push(chunk);const bytes=Buffer.concat(chunks);if(bytes.length>=49){assert.equal(bytes.length,49);assert.equal(bytes[0],1);socket.end();resolve(bytes.subarray(1).toString('ascii'));}}
   catch(error){socket.destroy(error);}
  });
  socket.once('error',reject);socket.once('end',()=>{if(Buffer.concat(chunks).length<49)reject(new Error('Incomplete IPC ticket.'));});
 });
}
let active,fake,passed=false,errorText;
try{
 const selected=await profile(settings,'launch');active=run(selected,'first');
 const expected=await readFile(path.join(packagePath,'wwwroot/index.html'),'utf8');let ready=false;
 for(let attempt=0;attempt<100;attempt++){
  assert.ok(owned.has(active),'Initial host exited.');
  try{const response=await fetch(origin,{signal:AbortSignal.timeout(500)});ready=response.ok&&await response.text()===expected;}catch{}
  if(ready)break;await new Promise(resolve=>setTimeout(resolve,100));
 }
 assert.ok(ready,'The exact packaged host did not become ready.');
 const repeated=run(selected,'second');assert.equal((await exit(repeated)).code,0);assert.ok(owned.has(active));
 assert.match(await readFile(path.join(root,'second.stdout.log'),'utf8'),/no second host was started/);
 steps.push('Second native launch succeeds while the original process and exact web shell remain active.');
 const token=await ticket();assert.match(token,/^[a-f0-9]{48}$/);
 const headers={Origin:origin,'Content-Type':'application/json'};
 const claim=await fetch(origin+'/api/auth/claim-launch',{method:'POST',headers,body:JSON.stringify({ticket:token})});
 assert.equal(claim.status,200);const session=await claim.json();assert.equal(session.owner,true);
 const ownerHeaders={...headers,Cookie:claim.headers.get('set-cookie').split(';')[0],'X-CSRF':session.csrf};
 assert.equal((await fetch(origin+'/api/auth/claim-launch',{method:'POST',headers,body:JSON.stringify({ticket:token})})).status,401);
 const before=await (await fetch(origin+'/api/state',{headers:ownerHeaders})).json();
 assert.deepEqual(before.runs,[]);assert.deepEqual(before.chats,[]);
 steps.push('IPC issues a real single-use owner session without reading or sending the durable host key.');
 const other={...settings,dataDirectory:path.join(root,'other-study')};
 const conflict=run(await profile(other,'other-launch'),'other');assert.notEqual((await exit(conflict)).code,0);
 await assert.rejects(stat(other.dataDirectory),{code:'ENOENT'});assert.ok(owned.has(active));
 assert.deepEqual(await (await fetch(origin+'/api/state',{headers:ownerHeaders})).json(),before);
 steps.push('Different study on occupied ports is refused before creating data; original state is unchanged.');
 let requests=0;fake=http.createServer((request,response)=>{requests++;response.end('Unrelated listener');});
 await new Promise(resolve=>fake.listen(0,'127.0.0.1',resolve));
 const impostor={...settings,dataDirectory:path.join(root,'unrelated-study'),localOrigin:`http://127.0.0.1:${fake.address().port}`,workerPort:await port()};
 const refused=run(await profile(impostor,'unrelated-launch'),'unrelated');assert.notEqual((await exit(refused)).code,0);
 assert.equal(requests,0);await assert.rejects(stat(impostor.dataDirectory),{code:'ENOENT'});
 steps.push('An unrelated HTTP listener receives no probes or credentials and is left running.');
 passed=true;
}catch(error){errorText=error.message;throw error;}
finally{
 if(fake)await new Promise(resolve=>fake.close(resolve));
 const cleanupErrors=[];
 for(const child of [...owned]){try{child.kill('SIGTERM');await exit(child);}catch(error){cleanupErrors.push(error.message);}}
 let removed=[];if(owned.size===0&&cleanupErrors.length===0)removed=await cleanArtifactPaths(root,['study','other-study','unrelated-study']);
 const result={passed:passed&&owned.size===0&&cleanupErrors.length===0,package:packagePath,hostManifestSha256:hash(manifestBytes),sourceHashes,steps,error:errorText,
  hostPid:active?.pid,ownedProcessesRemaining:owned.size,cleanupErrors,removed,liveModelCalls:0,liveSearchCalls:0,workerStarts:0,browserShellOpened:false};
 await writeFile(path.join(root,'verified.json'),JSON.stringify(result,null,2)+'\n');
 assert.equal(cleanupErrors.length,0);assert.equal(owned.size,0);
}
console.log('Native desktop reopening passed. The raven reopened one study and swept up the fixture.');
