import {useEffect,useState} from 'react';
import {Archive,ArrowUpRight,Check,Power,RotateCcw} from 'lucide-react';
import {api,setCsrf} from '../api';
import {Raven} from './Raven';

type Receipt={directory:string;files:number;bytes:number;databaseSchemaVersion:number;manifestSha256:string};
export type MaintenanceView={phase:string;version:string;source:string;backupRoot:string;destination:string|null;message:string;canStart:boolean;receipt?:Receipt};

export function MaintenanceSettings({online,onStarted}:{online:boolean;onStarted:(view:MaintenanceView)=>void}){
  const [view,setView]=useState<MaintenanceView|null>(null),[review,setReview]=useState(false),[mode,setMode]=useState('backup'),[busy,setBusy]=useState(false),[error,setError]=useState('');
  async function inspect(){
    setBusy(true);setError('');
    try{setView(await api<MaintenanceView>('/maintenance'));setReview(true);}
    catch(error){setError((error as Error).message);}finally{setBusy(false);}
  }
  async function start(){
    if(!view)return;setBusy(true);setError('');
    try{onStarted(await api<MaintenanceView>('/maintenance/start',{version:view.version,mode}));}
    catch(error){setError((error as Error).message);}finally{setBusy(false);}
  }
  return <section aria-label="Backups and shutdown"><h2>Backups & shutdown</h2>
    <p>Close this study safely before an upgrade. The maintenance screen stays open while your backup is checked.</p>
    <button disabled={!online||busy} onClick={inspect}><Archive size={16}/> Review maintenance</button>
    {review&&view&&<div className="scope-card maintenance-review"><h3>Put the study in order</h3><p>{view.message}</p>
      <label>Before closing<select aria-label="Maintenance action" value={mode} onChange={event=>setMode(event.target.value)}>
        <option value="backup">Make a verified backup</option><option value="stop">Close without a new backup</option>
      </select></label>
      <p>Study folder</p><code>{view.source}</code>
      {mode==='backup'&&<><p>A new private backup will be created in</p><code>{view.backupRoot}</code><p>The copy includes your notes, history, owner access key and saved browser sessions. It is not encrypted. Model keys stay in your system credential store.</p></>}
      <p>Other browsers will disconnect. Active work must finish or be cancelled first. You can reopen the same study from the maintenance screen.</p>
      <div className="maintenance-actions"><button className="primary" disabled={busy||!online||!view.canStart} onClick={start}>{mode==='backup'?'Back up and close study':'Close study without a new backup'} <ArrowUpRight size={16}/></button>
        <button disabled={busy} onClick={()=>setReview(false)}>Keep working</button></div>
    </div>}
    {error&&<p className="error" role="alert">{error}</p>}
  </section>;
}

export function MaintenancePage({initial,onReopened}:{initial?:MaintenanceView;onReopened:()=>void}){
  const [view,setView]=useState<MaintenanceView|null>(initial??null),[error,setError]=useState(''),[action,setAction]=useState(''),[disconnected,setDisconnected]=useState(false);
  useEffect(()=>{
    let stale=false,timer:ReturnType<typeof setTimeout>;
    async function refresh(){
      let unavailable=false;
      try{
        const session=await api<{csrf:string}>('/session');if(stale)return;setCsrf(session.csrf);
        const result=await api<MaintenanceView>('/maintenance');if(stale)return;
        if(result.phase==='ready'){onReopened();return;}
        setView(result);setDisconnected(false);setError('');
      }catch(error){unavailable=true;if(!stale){setDisconnected(true);setError((error as Error).message);}}
      if(!stale&&!(unavailable&&action==='close'))timer=setTimeout(refresh,1000);
    }
    void refresh();return()=>{stale=true;clearTimeout(timer);};
  },[onReopened,action]);
  async function finish(mode:string){
    if(!view)return;setAction(mode);setError('');
    try{await api('/maintenance/finish',{version:view.version,mode});}
    catch(error){setAction('');setError((error as Error).message);}
  }
  const working=!view||['closing','copying'].includes(view.phase);
  return <main className="maintenance-page"><div className="wordmark"><span className="mark">T</span> THADDEUS</div>
    <Raven state={working?'running':'idle'}/><p className="eyebrow">A SAFE STOPPING POINT</p><h1>Study maintenance</h1>
    <p role="status" aria-live="polite">{action==='reopen'?'Reopening your study…':action==='close'?(disconnected?'Connection closed. You can close this tab.':'Closing Thaddeus…'):view?.message??'Connecting to the maintenance screen…'}</p>
    {view?.receipt&&<section className="scope-card" aria-label="Verified backup"><h2><Check size={20}/> Backup verified</h2>
      <code>{view.receipt.directory}</code><p>{view.receipt.files.toLocaleString()} files · {(view.receipt.bytes/1048576).toFixed(2)} MiB · database version {view.receipt.databaseSchemaVersion}</p>
      <details><summary>Verification receipt</summary><p>Manifest SHA-256</p><code>{view.receipt.manifestSha256}</code></details>
    </section>}
    {view&&!working&&!action&&<><div className="maintenance-actions"><button className="primary" onClick={()=>finish('reopen')}><RotateCcw size={16}/> Reopen study</button>
      <button onClick={()=>finish('close')}><Power size={16}/> {view.phase==='failed'?'Close without a verified backup':'Finish and close Thaddeus'}</button></div>
      <p>Your original study remains at <code>{view.source}</code>.</p>
      <p>Keep your previous application package and the verified backup before upgrading. To open this study later, use Start Thaddeus in its application folder.</p>
    </>}
    {action==='close'&&disconnected&&<p>To return later, open Start Thaddeus. Your saved work remains in its study folder.</p>}
    {disconnected&&action!=='close'&&<p className="muted">The host may be changing over. This page will retry. If it does not return, reopen Start Thaddeus and inspect your backup folder before assuming the copy completed.</p>}
    {error&&!disconnected&&<p className="error" role="alert">{error}</p>}
  </main>;
}
