import {useEffect,useState} from 'react';
import {api} from '../api';
import type {Run} from '../types';
export function Reconciliation({run,online,onChanged}:{run:Run;online:boolean;onChanged:()=>Promise<unknown>}){
  const [inspection,setInspection]=useState<any>(null),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  async function inspect(){try{setInspection(await api('/runs/'+run.id+'/reconciliation'));setError('');}catch(e){setError((e as Error).message);}}
  useEffect(()=>{void inspect();},[run.id,run.updated]);
  async function decide(mode:string){setBusy(true);try{await api('/runs/'+run.id+'/reconciliation',{mode,observedVersion:inspection.observedVersion});await onChanged();}catch(e){await inspect();setError((e as Error).message);}finally{setBusy(false);}}
  return <section className="approval-card"><h2>Reconcile interrupted work</h2><p>Inspect what is on disk before deciding. Nothing is retried automatically.</p>{inspection&&<><dl><dt>Page</dt><dd>{inspection.action.path}</dd><dt>Current SHA-256</dt><dd className="hash">{inspection.observedVersion}</dd><dt>Committed content</dt><dd>{inspection.operation?'Durably recorded':'No content transaction found'}</dd><dt>Exact match</dt><dd>{inspection.exactMatch?'Yes · verification requires no write':'No'}</dd></dl><details><summary>Inspect committed content and original action</summary><pre>{inspection.action.content}</pre></details><div className="approval-actions"><button disabled={!online||busy||!inspection.exactMatch} onClick={()=>decide('verify')}>Verify existing content</button><button disabled={!online||busy||!inspection.operation||inspection.exactMatch} onClick={()=>decide('complete')}>Complete exact committed write</button><button disabled={!online||busy} onClick={()=>decide('abandon')}>Close without further writes</button></div></>}{error&&<p className="error" role="alert">{error}</p>}</section>;
}
