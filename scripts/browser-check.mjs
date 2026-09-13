import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {openSync,closeSync} from 'node:fs';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {createServer} from 'node:net';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const [packageArgument,evidenceArgument,...specs]=process.argv.slice(2);
assert.ok(packageArgument&&evidenceArgument,'Supply an existing package and a fresh evidence folder.');
const packagePath=path.resolve(packageArgument),evidence=path.resolve(evidenceArgument),privateRoot=path.join(repository,'artifacts')+path.sep;
assert.ok(packagePath.startsWith(privateRoot)&&evidence.startsWith(privateRoot),'Browser checks require a disposable package and data below repository artifacts.');
const manifest=JSON.parse(await readFile(path.join(packagePath,'package-manifest.json'),'utf8'));
const native=`${{win32:'win',darwin:'osx',linux:'linux'}[process.platform]}-${process.arch}`;
assert.equal(manifest.runtime,native);
const executable=path.join(packagePath,'Thaddeus.Host'+(process.platform==='win32'?'.exe':''));
const expectedPage=await readFile(path.join(packagePath,'wwwroot/index.html'),'utf8');
await mkdir(evidence);
const data=path.join(evidence,'study'),profile=path.join(evidence,'launch.json');
async function reserve(){const server=createServer();await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));return server;}
async function release(server){await new Promise((resolve,reject)=>server.close(error=>error?reject(error):resolve()));}
const front=await reserve(),worker=await reserve();
const origin=`http://127.0.0.1:${front.address().port}`,workerPort=worker.address().port;
await release(front);await release(worker);
await writeFile(profile,JSON.stringify({schemaVersion:1,dataDirectory:data,localOrigin:origin,workerPort}));
const environment=Object.fromEntries(Object.entries(process.env).filter(([key])=>!key.toLowerCase().startsWith('thaddeus')));
const children=new Set();
function run(file,args,label,env=environment,cwd=evidence){
  const out=openSync(path.join(evidence,label+'.stdout.log'),'wx'),err=openSync(path.join(evidence,label+'.stderr.log'),'wx');
  let child;
  try{child=spawn(file,args,{cwd,env,windowsHide:true,stdio:['ignore',out,err]});}finally{closeSync(out);closeSync(err);}
  children.add(child);child.finished=new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',(code,signal)=>{children.delete(child);resolve({code,signal});});});
  return child;
}
const delay=milliseconds=>new Promise(resolve=>setTimeout(resolve,milliseconds));
async function bounded(child,milliseconds){let timer;try{return await Promise.race([child.finished,new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error('Owned fixture deadline exceeded.')),milliseconds);})]);}finally{clearTimeout(timer);}}
try{
  const host=run(executable,['--desktop','--no-browser','--launch-profile',profile],'host');
  let ready=false;
  for(let attempt=0;attempt<150;attempt++){
    assert.ok(children.has(host),'The disposable host exited before readiness.');
    try{const response=await fetch(origin,{signal:AbortSignal.timeout(1000)});if(response.ok&&await response.text()===expectedPage){ready=true;break;}}catch{}
    await delay(200);
  }
  assert.ok(ready,'The exact packaged client did not become ready.');
  await writeFile(path.join(evidence,'fixture.json'),JSON.stringify({origin,workerPort,data,pid:host.pid,package:packagePath,sourceHead:manifest.sourceHead,sourceDirty:manifest.checkoutDirty},null,2)+'\n');
  const browser=run(process.execPath,[path.join(repository,'web/node_modules/@playwright/test/cli.js'),'test',...specs],'browser',
    {...environment,THADDEUS_TEST_ORIGIN:origin,THADDEUS_TEST_DATA:data,THADDEUS_SCREENSHOTS:path.join(evidence,'screenshots')},path.join(repository,'web'));
  const result=await bounded(browser,10*60_000);
  await writeFile(path.join(evidence,'browser-results.json'),await readFile(path.join(repository,'artifacts/browser-results.json')));
  assert.equal(result.code,0,'Browser checks failed; inspect their retained output.');
  await writeFile(path.join(evidence,'verified.json'),JSON.stringify({passed:true,origin,package:packagePath,sourceHead:manifest.sourceHead,sourceDirty:manifest.checkoutDirty,specs,mainStudyTouched:false},null,2)+'\n');
  console.log('Packaged browser checks passed. Only the fixture study was invited.');
}finally{
  for(const child of [...children]){child.kill(process.platform==='win32'?'SIGTERM':'SIGINT');await bounded(child,15_000);}
}
