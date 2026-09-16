import {useEffect,useState} from 'react';
import {api} from '../api';
import type {Provider} from '../types';

type Setup={worker?:{backend:string;name:string;installationDigest:string;developmentOnly:boolean};enabled:boolean;canEnable:boolean;status:string;summary:string;
 lastCheck?:{checkedAt:string;passed:boolean;summary:string;checks:{id:string;state:string;detail:string}[]};
 progress?:{id:string;startedAt:string;step:{stage:string;verifiedFiles:number;totalFiles:number}}};
type Requirements={platform:string;architecture:string;passed:boolean;summary:string;checkedAt:string;checks:{id:string;name:string;state:string;detail:string;nextStep?:string}[]};

export function HostWorkerSettings({online,provider,onChanged,onConnectModel}:{online:boolean;provider?:Provider;onChanged:()=>Promise<unknown>;onConnectModel:()=>void}){
 const [setup,setSetup]=useState<Setup|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const [requirements,setRequirements]=useState<Requirements|null>(null);
 const [cancelling,setCancelling]=useState(false);
 const working=busy||!!setup?.progress;
 useEffect(()=>{let stale=false;api<Setup>('/settings/worker').then(value=>{if(!stale)setSetup(value);}).catch(error=>{if(!stale)setError(error.message);});return()=>{stale=true;};},[]);
 useEffect(()=>{
  if(!working)return;
  let stale=false,inFlight=false;
  const timer=setInterval(async()=>{if(inFlight)return;inFlight=true;try{const value=await api<Setup>('/settings/worker');if(!stale)setSetup(value);}catch(error){if(!stale)setError((error as Error).message);}finally{inFlight=false;}},1000);
  return()=>{stale=true;clearInterval(timer);};
 },[working,setup?.progress?.id]);
 async function change(check=false){
  setBusy(true);setError('');
  try{
   const value=check?await api<Setup>('/settings/worker/check',{}):await api<Setup>('/settings/worker',{installationDigest:setup?.worker?.installationDigest,enabled:!setup?.enabled});
   setSetup(value);await onChanged();
  }catch(error){setError((error as Error).message);}finally{setBusy(false);}
 }
 async function checkComputer(){
  setBusy(true);setError('');
  try{setRequirements(await api<Requirements>('/settings/worker/requirements',{}));}
  catch(error){setError((error as Error).message);}finally{setBusy(false);}
 }
 async function cancelCheck(){
  if(!setup?.progress)return;
  setCancelling(true);setError('');
  try{setSetup(await api<Setup>('/settings/worker/cancel',{checkId:setup.progress.id}));}
  catch(error){setError((error as Error).message);}finally{setCancelling(false);}
 }
 const stage=setup?.progress?.step;
 const stageText=stage?.stage==='runtime-files'?`Verified ${stage.verifiedFiles.toLocaleString()} of ${stage.totalFiles.toLocaleString()} runtime files.`:
  stage?.stage==='guest-image'?'Checking the Linux worker image. This is the largest file.':
  stage?.stage==='runtime-version'?'Checking the installed runtime version.':
  stage?.stage==='kernel'||stage?.stage==='initrd'?'Checking worker boot files.':'Checking the installed worker files.';
 return <section aria-label="Host research setup">
  <h2>Set up this host</h2>
  <p>Your browser can connect from any device. This computer runs Thaddeus’s isolated worker and must stay awake while it works.</p>
  <ol className="host-setup-steps">
   <li><h3>Check this computer</h3>
    <p>Check whether this host can support the worker. This reads system capabilities without installing software, starting a VM or using a model.</p>
    <button disabled={!online||working} onClick={checkComputer}>Check this computer</button>
    {requirements&&<div role="region" aria-label="Computer requirements">
     <p role="status">{requirements.summary}</p>
     <ul>{requirements.checks.map(check=><li key={check.id}><strong>{check.name} · {check.state==='passed'?'Ready':check.state==='failed'?'Needs attention':'Could not verify'}</strong>
      <p>{check.detail}</p>{check.nextStep&&<p>{check.nextStep}</p>}</li>)}</ul>
     <p className="muted">Checked {new Date(requirements.checkedAt).toLocaleString()} · {requirements.platform} {requirements.architecture}. No system setting was changed.</p>
    </div>}
   </li>
   <li><h3>Set up the worker package</h3>
    <p role="status">{setup?.summary||'Reading host setup…'}</p>
    {setup&&!setup.worker&&<p>Use an application package with an included worker for this computer. Keep its worker folder beside the app, reopen Thaddeus, then check and enable it here.</p>}
    {setup?.worker&&<p><strong>{setup.worker.name}</strong>{setup.worker.developmentOnly&&' · Development preview'}</p>}
    {setup?.worker?.developmentOnly&&<p className="muted">This preview uses a private Linux VM with two virtual CPUs and a 5 GiB host memory limit. Broader release qualification is still in progress.</p>}
    <div className="host-setup-actions">
     <button disabled={!online||working||!setup?.worker} onClick={()=>change(true)}>{setup?.progress?'Checking worker…':'Check installed worker'}</button>
     {setup?.worker&&<button className={setup.enabled?'':'primary'} disabled={!online||working||(!setup.enabled&&!setup.canEnable)} onClick={()=>change()}>
      {setup.enabled?'Disable new research':'Enable research on this host'}
     </button>}
    </div>
    {setup?.progress&&<div role="region" aria-label="Worker verification progress">
     <p role="status">{stageText}</p>
     <p className="muted">Elapsed {Math.max(0,Math.floor((Date.now()-Date.parse(setup.progress.startedAt))/1000))} seconds. This reads files only; no VM or model is running.</p>
     <button disabled={!online||cancelling} onClick={cancelCheck}>{cancelling?'Cancelling…':'Cancel worker check'}</button>
    </div>}
    {setup?.lastCheck&&<><p role="status">{setup.lastCheck.summary}</p><details><summary>Package check · {new Date(setup.lastCheck.checkedAt).toLocaleString()}</summary>
     <ul>{setup.lastCheck.checks.map(check=><li key={check.id}>{check.state==='passed'?'Verified':check.state==='failed'?'Needs attention':'Still unverified'}: {check.detail}</li>)}</ul>
    </details></>}
   </li>
   <li><h3>Connect a model</h3><p>{provider?.kind==='compatible'?`Selected model: ${provider.model}.`:'Connect a model before starting research.'}</p><button type="button" onClick={onConnectModel}>Open model connection settings</button></li>
   <li><h3>Keep usage and permissions visible</h3><p>Research handles one task at a time. Set its call, token and time limits before sending it. Usage stays visible in the header and task details; saving a result into your notes requires exact approval.</p></li>
  </ol>
  {error&&<p role="alert" className="error">{error}</p>}
 </section>;
}
