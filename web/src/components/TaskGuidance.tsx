import {useRef,useState} from 'react';
import {api} from '../api';
import type {Run} from '../types';

export function TaskGuidance({run,online,onChanged}:{run:Run;online:boolean;onChanged:()=>Promise<unknown>}){
  const [message,setMessage]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const attempt=useRef<{operationId:string;message:string}|null>(null);
  const commands=run.executionCommands??[],guidance=commands.filter(command=>command.kind==='steer');
  const ready=run.research?.phase==='working'&&run.state==='running'&&!!run.execution?.runtimeRunId&&commands.every(command=>command.status==='acknowledged');
  const budgetAvailable=run.modelCalls<run.goal.limits.modelCalls&&(run.chargedTokens??0)<run.goal.limits.maxTotalTokens;
  async function send(){
    if(!message.trim()||!ready||busy||!online)return;
    const request=attempt.current?.message===message?attempt.current:{operationId:crypto.randomUUID().replaceAll('-',''),message};
    attempt.current=request;setBusy(true);setError('');
    try{await api('/runs/'+run.id+'/guidance',request);setMessage('');attempt.current=null;}
    catch(failure){setError((failure as Error).message);}
    finally{try{await onChanged();}catch{setError('Could not refresh delivery status. Reconnect and inspect the saved guidance before sending again.');}setBusy(false);}
  }
  return <section className="guidance-card" aria-label="Task guidance">
    <h2>Guide this research</h2><p className="guidance-objective">{run.goal.objective}</p>
    <p className="muted">Uses this task’s existing sources, permissions and token allowance. Guidance may be handled after the current step.</p>
    <p className="muted">{(run.chargedTokens??0).toLocaleString()} charged / {run.goal.limits.maxTotalTokens.toLocaleString()} token allowance · {run.modelCalls} / {run.goal.limits.modelCalls} model calls.</p>
    {guidance.length>0&&<ol className="guidance-history">{guidance.map(command=><li key={command.id}><p>{command.message}</p><small>{command.status==='acknowledged'?'Received by the worker · outcome still needs review':'Delivery unconfirmed · inspect task receipts before continuing'}</small></li>)}</ol>}
    {error&&<p role="alert">{error}</p>}
    {ready?<><label>Additional guidance<textarea aria-label="Additional guidance" maxLength={4000} value={message} disabled={busy} onChange={event=>setMessage(event.target.value)} placeholder="Adjust the audience, emphasis, or desired result…"/></label><button className="primary" disabled={!message.trim()||busy||!online||!budgetAvailable||commands.length>=64} onClick={()=>void send()}>{busy?'Sending guidance…':'Send guidance'}</button>{!budgetAvailable&&<p role="status">The task has no allowance for another model call.</p>}</>:<p role="status">{run.state==='awaitingInput'?'Answer the saved question to continue this task.':run.state==='awaitingApproval'?'Review the proposed artifact before continuing.':'Guidance is available while this research task is working.'}</p>}
  </section>;
}
