// A bounded UI check: real host integration uses the scripted runtime; presentation specs mock work.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {openSync,closeSync} from 'node:fs';
import {mkdir,mkdtemp,readFile,writeFile,access,realpath,rm} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {requireArtifactSpace,cleanArtifactPaths} from '../../scripts/artifact-storage.mjs';

const repository=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const evidence=path.join(repository,'artifacts','magical-web-check-'+Date.now());
const website=path.join(repository,'artifacts','magical-web','website');
const dll=path.resolve(process.argv[2]||path.join(repository,'src','Thaddeus.Host','bin','Release','net10.0','Thaddeus.Host.dll'));
await access(dll);await access(path.join(website,'index.html'));
const space=await requireArtifactSpace(repository,256*1024**2,'Magical web browser check');
await mkdir(evidence);const study=path.join(evidence,'study'),origin='http://localhost:5183';
const ledger=await mkdtemp(path.join(os.tmpdir(),'magical-web-ledger-'));
const log=openSync(path.join(evidence,'host.log'),'w');let host;
const env=Object.fromEntries(Object.entries(process.env).filter(([key])=>!key.toLowerCase().startsWith('thaddeus')&&!key.toLowerCase().startsWith('marketing')));
async function run(file,args,environment,cwd){const child=spawn(file,args,{env:environment,cwd,windowsHide:true,stdio:'inherit'});return new Promise((resolve,reject)=>{child.on('error',reject);child.on('exit',code=>resolve(code));});}
try{
  try{const occupied=await fetch(origin,{signal:AbortSignal.timeout(500)});if(occupied)throw new Error('Port 5183 already belongs to a service; it is preserved.');}catch(error){if(!/fetch failed|timeout/i.test(error.message))throw error;}
  host=spawn('dotnet',[dll,'--contentRoot',repository,'--webroot',website],{cwd:repository,windowsHide:true,stdio:['ignore',log,log],env:{...env,Thaddeus__Data:study,Thaddeus__LocalOrigin:origin,Thaddeus__ApiRequestsPerMinute:'3000',Thaddeus__AuthRequestsPerMinute:'120',Marketing__FixtureLedger:ledger,Marketing__FixtureRunwayScript:path.join(repository,'business','agent','hire','bin','runway.py'),Marketing__Container:'nonexistent-magical-web-fixture',Marketing__SharedContainer:'nonexistent-magical-web-fixture',Marketing__ShiftRuntime:'scripted',Marketing__ShiftPump:'off',Marketing__BackgroundEnabled:'false',Logging__LogLevel__Default:'Warning'}});
  const exited=new Promise(resolve=>host.once('exit',resolve));host.finished=exited;host.on('error',error=>{throw error;});
  let ready=false;for(let index=0;index<100;index++){if(host.exitCode!==null)throw new Error('Owned host exited before readiness.');try{const response=await fetch(origin,{signal:AbortSignal.timeout(500)});if(response.ok){ready=true;break;}}catch{}await new Promise(resolve=>setTimeout(resolve,150));}
  assert.ok(ready,'Disposable host did not become ready.');
  await writeFile(path.join(evidence,'fixture.json'),JSON.stringify({origin,study,ledger,pid:host.pid,dll,website,space},null,2));
  const specs=process.argv.slice(3).length?process.argv.slice(3):['magical-cockpit.spec.ts','magical-host.spec.ts','first-employee-shell.spec.ts'];
  const code=await run(process.execPath,[path.join(repository,'web/node_modules/@playwright/test/cli.js'),'test',...specs,'--output',path.join(evidence,'test-results')],{...env,THADDEUS_TEST_ORIGIN:origin,THADDEUS_TEST_DATA:study,THADDEUS_SCREENSHOTS:path.join(evidence,'screenshots')},path.join(repository,'web'));
  await writeFile(path.join(evidence,'browser-results.json'),await readFile(path.join(repository,'artifacts','browser-results.json')));
  assert.equal(code,0,'Browser checks failed. Their evidence is retained.');
  await writeFile(path.join(evidence,'verified.json'),JSON.stringify({passed:true,specs,origin,realHostIntegration:specs.includes('magical-host.spec.ts'),presentationMocks:specs.filter(spec=>spec!=='magical-host.spec.ts'),runtime:'scripted',liveModelCalls:0,sharedFixtureTouched:false},null,2));
}finally{
  closeSync(log);
  if(host&&host.exitCode===null&&host.signalCode===null){host.kill();let timer;try{await Promise.race([host.finished,new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error('Owned host did not exit; study retained.')),10000);})]);}finally{clearTimeout(timer);}}
  const removed=await cleanArtifactPaths(evidence,['study']);
  const resolvedLedger=await realpath(ledger),resolvedTemp=await realpath(os.tmpdir());assert.equal(resolvedLedger,path.join(resolvedTemp,path.basename(ledger)));assert.ok(path.basename(resolvedLedger).startsWith('magical-web-ledger-'));await rm(resolvedLedger,{recursive:true,maxRetries:2});
  await writeFile(path.join(evidence,'cleanup.json'),JSON.stringify({removed,tempRemoved:[resolvedLedger],ownedHostExited:true,at:new Date().toISOString()},null,2));
  console.log('Browser evidence: '+evidence);
}
