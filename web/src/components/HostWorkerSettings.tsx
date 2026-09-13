import {useEffect,useState} from 'react';
import {api} from '../api';
import type {Provider} from '../types';

type Setup={worker?:{backend:string;name:string;installationDigest:string;developmentOnly:boolean};enabled:boolean;canEnable:boolean;status:string;summary:string;
 lastCheck?:{checkedAt:string;passed:boolean;summary:string;checks:{id:string;state:string;detail:string}[]}};

export function HostWorkerSettings({online,provider,onChanged}:{online:boolean;provider?:Provider;onChanged:()=>Promise<unknown>}){
 const [setup,setSetup]=useState<Setup|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 useEffect(()=>{let stale=false;api<Setup>('/settings/worker').then(value=>{if(!stale)setSetup(value);}).catch(error=>{if(!stale)setError(error.message);});return()=>{stale=true;};},[]);
 async function change(check=false){
  setBusy(true);setError('');
  try{
   const value=check?await api<Setup>('/settings/worker/check',{}):await api<Setup>('/settings/worker',{installationDigest:setup?.worker?.installationDigest,enabled:!setup?.enabled});
   setSetup(value);await onChanged();
  }catch(error){setError((error as Error).message);}finally{setBusy(false);}
 }
 return <section aria-label="Host research setup">
  <h2>Set up this host</h2>
  <p>Your browser can connect from any device. This computer runs Thaddeus’s isolated worker and must stay awake while it works.</p>
  <ol className="host-setup-steps">
   <li><h3>Choose the worker installed here</h3>
    <p role="status">{setup?.summary||'Reading host setup…'}</p>
    {setup?.worker&&<p><strong>{setup.worker.name}</strong>{setup.worker.developmentOnly&&' · Development preview'}</p>}
    {setup?.worker?.developmentOnly&&<p className="muted">This Windows preview uses a private Linux VM, up to 5 GiB of host committed memory and two virtual CPUs. Broader release qualification is still in progress.</p>}
    <div className="host-setup-actions">
     <button disabled={!online||busy||!setup?.worker} onClick={()=>change(true)}>{busy?'Checking or saving…':'Check installed worker'}</button>
     {setup?.worker&&<button className={setup.enabled?'':'primary'} disabled={!online||busy||(!setup.enabled&&!setup.canEnable)} onClick={()=>change()}>
      {setup.enabled?'Disable new research':'Enable research on this host'}
     </button>}
    </div>
    {setup?.lastCheck&&<><p role="status">{setup.lastCheck.summary}</p><details><summary>Package check · {new Date(setup.lastCheck.checkedAt).toLocaleString()}</summary>
     <ul>{setup.lastCheck.checks.map(check=><li key={check.id}>{check.state==='passed'?'Verified':check.state==='failed'?'Needs attention':'Still unverified'}: {check.detail}</li>)}</ul>
    </details></>}
   </li>
   <li><h3>Connect a model</h3><p>{provider?.kind==='compatible'?`Selected model: ${provider.model}. Use the model settings above to change it.`:'Choose a compatible model in the settings above before starting research.'}</p></li>
   <li><h3>Keep usage and permissions visible</h3><p>Research handles one task at a time. Set its call, token and time limits before sending it. Usage stays visible in the header and task details; saving a result into your notes requires exact approval.</p></li>
  </ol>
  {error&&<p role="alert" className="error">{error}</p>}
 </section>;
}
