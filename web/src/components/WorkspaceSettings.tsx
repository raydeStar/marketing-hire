import {useState} from 'react';
import {api} from '../api';
import type {Run} from '../types';

type Review={runId:string;workerId:string;backend:string;status:string;files:number;bytes:number;digest:string;canRemove:boolean;summary:string};
export function WorkspaceSettings({runs,online,onChanged}:{runs:Run[];online:boolean;onChanged:()=>Promise<unknown>}){
  const [review,setReview]=useState<Review|null>(null),[confirmation,setConfirmation]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const retained=runs.filter(run=>run.research?.workerRetained);
  async function inspect(id:string){
    setBusy(true);setError('');setReview(null);setConfirmation('');
    try{setReview(await api<Review>('/runs/'+id+'/workspace/inspect',{}));}
    catch(error){setError((error as Error).message);}finally{setBusy(false);}
  }
  async function remove(){
    if(!review)return;setBusy(true);setError('');
    try{
      await api('/runs/'+review.runId+'/workspace/remove',{digest:review.digest,confirmation});
      setReview(await api<Review>('/runs/'+review.runId+'/workspace/inspect',{}));setConfirmation('');await onChanged();
    }catch(error){setError((error as Error).message);setReview(null);setConfirmation('');}
    finally{setBusy(false);}
  }
  return <section aria-label="Stored research workspaces"><h2>Stored research workspaces</h2>
    <p>Each workspace contains its private disk, execution transcript and worker logs. Removing it leaves imported notes and task receipts in your study.</p>
    {!retained.length&&<p>No private research workspaces are retained.</p>}
    {retained.map(run=><div className="stored-workspace" key={run.id}><p>{run.goal.objective}</p>
      <button disabled={busy||!online||run.research?.phase!=='finished'} onClick={()=>inspect(run.id)}>Inspect stored workspace</button>
      {run.research?.phase!=='finished'&&<p className="muted">Finish or cancel this task before removing its workspace.</p>}
    </div>)}
    {review&&<section className="scope-card" aria-label="Workspace removal review"><h3>{runs.find(run=>run.id===review.runId)?.goal.objective}</h3><p role="status">{review.summary}</p>
      {review.canRemove&&<><p>{review.files.toLocaleString()} files · {(review.bytes/1048576).toFixed(2)} MiB of file contents. This cannot be undone.</p>
        <label>Confirm workspace removal<input placeholder="Type REMOVE WORKSPACE" value={confirmation} onChange={event=>setConfirmation(event.target.value)}/></label>
        <button disabled={busy||!online||confirmation!=='REMOVE WORKSPACE'} onClick={remove}>Remove reviewed workspace</button></>}
    </section>}
    {error&&<p role="alert" className="error">{error}</p>}
  </section>;
}
