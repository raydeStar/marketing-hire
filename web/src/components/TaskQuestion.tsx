import {useState} from 'react';
import {api} from '../api';

type Question = {id:string;text:string;choices:string[];answer?:string};
export function TaskQuestion({runId,question,online,ready=true,continues=false,onChanged}:{runId:string;question:Question;online:boolean;ready?:boolean;continues?:boolean;onChanged:()=>Promise<unknown>}) {
  const [answer,setAnswer]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  async function submit(){
    setBusy(true);setError('');
    try {await api('/runs/'+runId+'/answer',{questionId:question.id,answer});setAnswer('');await onChanged();}
    catch(e){setError((e as Error).message);}finally{setBusy(false);}
  }
  return <section className="scope-card" aria-label="Task question"><h2>A detail before I continue.</h2><p>{question.text}</p>
    {question.answer?<p><strong>Your answer:</strong> {question.answer}</p>:<>
      {question.choices.length>0&&<div className="suggestions">{question.choices.map(choice=><button key={choice} disabled={busy||!online||!ready} onClick={()=>setAnswer(choice)}>{choice}</button>)}</div>}
      <label>Your answer<textarea value={answer} maxLength={4000} onChange={e=>setAnswer(e.target.value)} disabled={busy||!online||!ready}/></label>
      <button className="primary" disabled={!answer.trim()||busy||!online||!ready} onClick={submit}>{busy?'Saving answer…':continues?'Send answer & continue':'Save answer'}</button>
      {!ready&&<p role="status">The worker must stop safely before it can accept your answer.</p>}<p className="muted">The question and answer stay with this task through a restart.</p>
    </>}{error&&<p role="alert" className="error">{error}</p>}
  </section>;
}
