import {useState} from 'react';
import {RotateCcw,ShieldCheck} from 'lucide-react';
import {api} from '../api';
import type {Run} from '../types';

export function CheckpointRecovery({run,owner,online,onChanged}:{run:Run;owner:boolean;online:boolean;onChanged:()=>Promise<unknown>}){
  const [busy,setBusy]=useState(''),[error,setError]=useState('');
  const review=run.research?.recovery;
  const current=!!review&&review.version===run.version&&Date.parse(review.expires)>Date.now();
  async function act(action:'inspect'|'restore'){
    setBusy(action);setError('');
    try{
      await api('/runs/'+run.id+'/recovery/'+action,action==='inspect'?{version:run.version}:{digest:review?.digest});
      await onChanged();
    }catch(e){setError((e as Error).message);}finally{setBusy('');}
  }
  return <section className="approval-card checkpoint-recovery" aria-label="Saved checkpoint recovery" aria-busy={!!busy}>
    <div className="card-heading"><RotateCcw aria-hidden="true"/><div><h2>Pick up from a saved checkpoint</h2><p>Inspect the saved workspace before restoring its question or artifact review.</p></div></div>
    <p>This does not start the worker or use model tokens. Any answer, continuation or file approval remains your next decision.</p>
    {review&&<div className="recovery-result" role="status"><p>{review.summary}</p>{review.workerStopped&&<p className="check"><ShieldCheck size={17} aria-hidden="true"/>Saved worker confirmed stopped</p>}{!current&&<p>The task or review changed. Inspect again before restoring.</p>}</div>}
    {owner?<div className="approval-actions"><button disabled={!online||!!busy} onClick={()=>act('inspect')}>{busy==='inspect'?'Inspecting saved worker…':review?'Inspect again':'Inspect saved worker'}</button>{review?.canRestore&&<button className="primary" disabled={!online||!!busy||!current} onClick={()=>act('restore')}>{busy==='restore'?'Restoring checkpoint…':'Restore saved checkpoint'}</button>}</div>:<p>The host owner can inspect and restore this checkpoint.</p>}
    {error&&<p className="error" role="alert">{error}</p>}
  </section>;
}
