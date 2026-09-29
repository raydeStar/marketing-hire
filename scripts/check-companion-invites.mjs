// Real portal, relay, connector and host; fictional identities, no SMS or models.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createServer} from 'node:net';
import {request as httpRequest} from 'node:http';
import {access,mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {companionConfig,runCompanion,checkCompanionHost} from '../packaging/plow/companion-connector.mjs';
import {cleanArtifactPaths,requireArtifactSpace} from './artifact-storage.mjs';

const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const landing=path.resolve(process.argv[2]),name=process.argv[3];
assert.match(name||'',/^companion-invites-[a-z0-9-]+$/);
const evidence=path.join(repo,'artifacts',name),scratch=path.join(evidence,'scratch');
await requireArtifactSpace(repo,128*1024**2,'Invitation acceptance check');
for(const port of [5183,5184]){
  const server=createServer();
  await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',resolve);});
  await new Promise(resolve=>server.close(resolve));
}
await mkdir(scratch,{recursive:true});
const children=[],logs=[],observations=[],controller=new AbortController();
let connection,failure;
function launch(command,args,env={}){
  const child=spawn(command,args,{cwd:repo,env:{...process.env,...env},windowsHide:true,stdio:['pipe','pipe','pipe']});
  child.stdout.on('data',x=>logs.push(x.toString()));child.stderr.on('data',x=>logs.push(x.toString()));
  children.push(child);return child;
}
try{
  const cms=launch('py',['-3.12',path.join(landing,'cms/tests/companion_fixture.py'),scratch]);
  const paired=await new Promise((resolve,reject)=>{
    let output='';const timer=setTimeout(()=>reject(new Error('Fixture did not start')),20000);
    cms.once('exit',()=>{clearTimeout(timer);reject(new Error('Fixture exited'));});
    cms.stdout.on('data',x=>{output+=x;if(output.includes('\n')){clearTimeout(timer);resolve(JSON.parse(output.split('\n')[0]));}});
  });
  const secret='fictional-local-companion-signing-secret-for-this-test';
  launch('dotnet',[path.join(repo,'src/Thaddeus.Host/bin/Debug/net10.0/Thaddeus.Host.dll'),'--contentRoot',path.join(repo,'src/Thaddeus.Host')],{
    TMP:scratch,TEMP:scratch,Thaddeus__Data:path.join(scratch,'host'),Thaddeus__LocalOrigin:'http://localhost:5184',Thaddeus__PhoneMode:'direct',
    Thaddeus__CompanionOrigin:paired.origin,Thaddeus__CompanionWorkspace:paired.workspace,Thaddeus__CompanionOwner:'owner-fixture',Thaddeus__CompanionSecret:secret,
    Marketing__FixtureLedger:path.join(scratch,'ledger'),Marketing__FixtureRunwayScript:path.join(repo,'business/agent/hire/bin/runway.py'),
    Marketing__ShiftPump:'off',Marketing__Container:'nonexistent-companion-fixture',Thaddeus__ApiRequestsPerMinute:'3000'});
  const config=companionConfig({brokerOrigin:'http://127.0.0.1:5183',workspaceOrigin:paired.origin,workspace:paired.workspace,credential:paired.token,ownerUid:'owner-fixture',ingressSecret:secret,development:true});
  for(let attempt=0;;attempt++){
    try{await checkCompanionHost(config);break;}catch(error){if(attempt>=60)throw error;await new Promise(r=>setTimeout(r,250));}
  }
  let connected;
  const ready=new Promise(resolve=>{connected=resolve;});
  connection=runCompanion(config,{signal:controller.signal,onStatus:status=>{if(status==='connected')connected();}});
  // The initial poll may wait for work; readiness is bounded by its 20-second poll.
  await ready;
  async function request(route,body,auth={},workspace=false){
    if(workspace)return new Promise((resolve,reject)=>{
      const content=body===undefined?'':JSON.stringify(body);
      const request=httpRequest({hostname:'127.0.0.1',port:5183,path:route,method:body===undefined?'GET':'POST',
        headers:{'Content-Type':'application/json','Content-Length':Buffer.byteLength(content),Origin:paired.origin,Host:new URL(paired.origin).host,...auth}},response=>{
        let text='';response.on('data',chunk=>text+=chunk);response.on('end',()=>{
          let data;try{data=JSON.parse(text);}catch{data=text;}
          resolve({status:response.statusCode,data,cookies:(response.headers['set-cookie']||[]).map(c=>c.split(';')[0])});
        });response.on('error',reject);
      });request.setTimeout(35000,()=>request.destroy(new Error('Fixture request timed out.')));request.on('error',reject);request.end(content);
    });
    const response=await fetch('http://127.0.0.1:5183'+route,{method:body===undefined?'GET':'POST',redirect:'manual',
      headers:{'Content-Type':'application/json',Origin:workspace?paired.origin:'http://127.0.0.1:5183',...(workspace?{Host:new URL(paired.origin).host}:{}),...auth},
      ...(body===undefined?{}:{body:JSON.stringify(body)}),signal:AbortSignal.timeout(35000)});
    const text=await response.text();let data;try{data=JSON.parse(text);}catch{data=text;}
    return {status:response.status,data,cookies:response.headers.getSetCookie().map(c=>c.split(';')[0])};
  }
  const post=(action,body,auth)=>request('/api/account/plow/'+action,body,auth);
  async function login(phone){
    let r=await request('/api/account/plow/session');const initial={Cookie:r.cookies.join('; '),'X-CSRF':r.data.csrf};
    assert.equal((await post('request',{phone},initial)).status,200);
    r=await post('verify',{code:'01234567'},initial);assert.equal(r.status,200);
    return {Cookie:r.cookies.join('; '),'X-CSRF':r.data.csrf};
  }
  const owner=await login('2025550101'),member=await login('2025550102'),other=await login('2025550103');
  let people=await post('companion-people',{id:paired.workspace},owner);
  assert.equal(people.status,200);assert.deepEqual(people.data.roles,['viewer','reviewer','contributor','manager']);
  let invite=await post('companion-invite',{id:paired.workspace,name:'Fictional reviewer',recipientMode:'link',role:'reviewer'},owner);
  assert.equal(invite.status,200);const token=new URL(invite.data.url).hash.slice('#invite='.length);
  assert.equal((await post('companion-accept',{token},member)).status,200);
  assert.equal((await post('companion-accept',{token},other)).status,404);
  people=await post('companion-people',{id:paired.workspace},member);
  assert.deepEqual(people.data.roles,['viewer','reviewer']);
  assert.equal((await post('companion-invite',{id:paired.workspace,name:'Escalation',role:'manager'},member)).status,403);
  invite=await post('companion-invite',{id:paired.workspace,name:'Fictional viewer',recipientMode:'phone',phone:'(202) 555-0103',role:'viewer'},member);
  assert.equal(invite.status,200);const viewerToken=new URL(invite.data.url).hash.slice('#invite='.length);
  assert.equal((await post('companion-accept',{token:viewerToken},owner)).status,404);
  assert.equal((await post('companion-accept',{token:viewerToken},other)).status,200);
  assert.equal((await post('companion-people',{id:paired.workspace},other)).status,403);
  assert.equal((await post('companion-invite',{id:paired.workspace,name:'Another viewer',role:'viewer'},other)).status,403);
  const opened=await post('companion-open',{id:paired.workspace},other);
  const connect=await request('/_hirezero/connect',{token:new URL(opened.data.url).hash.slice('#connect='.length)},{},true);
  assert.equal(connect.status,200);
  const hostSession=await request('/api/session',undefined,{Cookie:connect.cookies.join('; ')},true);
  assert.equal(hostSession.status,200);assert.equal(hostSession.data.owner,false);
  const hostCookie=[...connect.cookies.filter(c=>!c.startsWith('thaddeus-session=')),...hostSession.cookies].join('; ');
  assert.equal((await request('/api/company-wiki',undefined,{Cookie:hostCookie},true)).status,403);
  const ownerOpened=await post('companion-open',{id:paired.workspace},owner);
  const ownerConnect=await request('/_hirezero/connect',{token:new URL(ownerOpened.data.url).hash.slice('#connect='.length)},{},true);
  const ownerSession=await request('/api/session',undefined,{Cookie:ownerConnect.cookies.join('; ')},true);
  const ownerCookie=[...ownerConnect.cookies.filter(c=>!c.startsWith('thaddeus-session=')),...ownerSession.cookies].join('; ');
  const nativeRoles=await request('/api/team/roles',undefined,{Cookie:ownerCookie},true);
  assert.equal(nativeRoles.status,200);
  assert.equal(nativeRoles.data.find(r=>r.principalId===hostSession.data.principalId).role,'viewer');
  observations.push('Unbound link is single use; a phone-locked link rejects the wrong phone.','Reviewer invites a Viewer but cannot grant Manager.','Viewer cannot invite, has native viewer role, and cannot read private wiki.','Portal, connector and .NET host exchange actual HTTP requests; no external APIs.');
  if(process.argv.includes('--preview')){
    console.log('INVITATION_PREVIEW_READY http://127.0.0.1:5183/account/?workspace='+paired.workspace+'&people=1; fictional phone 2025550101, code 01234567');
    // Plain-pipe callers may have no writable stdin. A sentinel and a bounded
    // timeout still let the butler pack up every child process himself.
    await new Promise(resolve=>{
      const finish=()=>{clearInterval(poll);clearTimeout(timeout);process.stdin.removeListener('data',finish);resolve();};
      const poll=setInterval(()=>access(path.join(evidence,'finish-preview')).then(finish,()=>{}),500);
      const timeout=setTimeout(finish,300000);
      process.stdin.once('data',finish);process.stdin.resume();
    });process.stdin.pause();
  }
}catch(error){failure=error.stack;}
finally{
  controller.abort();await connection;
  for(const child of children.reverse())if(child.exitCode===null&&child.signalCode===null){
    const ended=new Promise(resolve=>child.once('exit',resolve));
    if(child.spawnargs[0]==='py')child.stdin.end('\n');else child.kill();await ended;
  }
  const removed=await cleanArtifactPaths(evidence,['scratch']);
  await writeFile(path.join(evidence,'receipt.json'),JSON.stringify({failure,observations,removed,livePlow:false,liveModels:false},null,2));
  await writeFile(path.join(evidence,'process.log'),logs.join('').replace(/#launch=\w+/g,'#launch=[fixture]').replace(/"token":\s*"[^"]+"/g,'"token":"[fixture]"'));
}
if(failure)throw new Error(failure);
console.log('Invitation check passed; the fictional guest list is packed away.');
