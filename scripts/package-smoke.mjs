import { readFile } from 'node:fs/promises';
import path from 'node:path';
const origin = process.env.THADDEUS_TEST_ORIGIN;
const data = process.env.THADDEUS_TEST_DATA;
if (!origin || !data || !path.resolve(data).startsWith(path.resolve('artifacts') + path.sep))
  throw new Error('Package smoke requires an explicit disposable data directory under artifacts.');
const key = (await readFile(path.join(data, 'host-key.txt'), 'utf8')).trim();
const login = await fetch(origin + '/api/auth/login', { method:'POST', headers:{Origin:origin,'Content-Type':'application/json'}, body:JSON.stringify({key}) });
if (!login.ok) throw new Error('Packaged host login failed.');
const cookie = login.headers.get('set-cookie').split(';')[0], {csrf} = await login.json();
async function api(route, body) {
  const response = await fetch(origin + '/api' + route, { method:body ? 'POST':'GET', headers:{Cookie:cookie,Origin:origin,'Content-Type':'application/json','X-CSRF':csrf}, body:body ? JSON.stringify(body):undefined });
  if (!response.ok) throw new Error(route + ' failed with status ' + response.status);
  return response.json();
}
await api('/demo/seed', {});
let run = await api('/runs', {objective:'Fictional packaged-host verification',readScope:['notes/conflict.md']});
for(let step=0;step<50 && run.state!=='awaitingApproval';step++) {
  await new Promise(resolve=>setTimeout(resolve,100)); run=await api('/runs/'+run.id);
}
if(run.state!=='awaitingApproval')throw new Error('Packaged fixture did not reach exact approval.');
run = await api('/runs/'+run.id+'/approve',{approvalId:run.approval.id,digest:run.approval.digest,allow:true});
if(run.state!=='succeeded')throw new Error('Packaged exact write was not verified.');
const report=await api('/settings/sandbox/inspect',{});
const exported=await api('/export');
if(exported.schemaVersion!==4 || exported.databaseSchemaVersion!==4)throw new Error('Unexpected packaged export version.');
console.log(JSON.stringify({passed:true,provider:'scripted',isolatedAgentExecuted:false,fixtures:exported.pages.length,
  sandboxStatus:report.status,source:'self-contained published host',exportSchema:exported.schemaVersion}));
