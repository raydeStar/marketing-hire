import {useRef,useState} from 'react';
import {api} from '../api';
import {requestId,readableTime,type RunwaySnapshot} from './MarketingPanels';
import '../runway.css';

const pilotGoal='Prepare a small internal campaign packet for the owner’s personal brand selling configurable marketing agents. Use one provisional founder audience as an experiment assumption. Save a source-backed audience/problem note, three evidence-linked draft post angles, and a review packet identifying unsupported claims and the next owner decision.';
const labels:Record<string,string>={audience_note:'Audience and problem note',post_angles:'Three draft post angles',review_packet:'Owner review packet'};

export function MarketingRunwayPanel({runway,canControl,canContribute,onRefresh}:{runway?:RunwaySnapshot|null;canControl:boolean;canContribute:boolean;onRefresh:()=>Promise<void>}){
  const [goal,setGoal]=useState(pilotGoal),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const [note,setNote]=useState('');
  const attempt=useRef<string|null>(null);
  const noteAttempt=useRef<{signature:string;id:string}|null>(null);
  const project=runway?.project;
  const current=runway?.steps.find(step=>step.status==='running');
  const next=runway?.steps.find(step=>step.status==='ready');
  const nextCheck=project?.next_due?readableTime(project.next_due):project?.status==='needs_review'?'When you review the packet':project?.status==='paused'?'When owner resumes':project?.status==='unknown'?'After the original execution is reconciled':project?.status==='budget_exhausted'?'After a new owner assignment':'After this step settles or relevant input arrives';
  async function start(){
    if(!canControl||busy||!goal.trim())return;
    setBusy(true);setError('');const id=attempt.current||requestId();attempt.current=id;
    try{await api('/marketing/runway',{requestId:id,goal:goal.trim()});attempt.current=null;await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }
  async function change(action:'pause'|'resume'){
    if(!project||!canControl||busy)return;
    setBusy(true);setError('');
    try{await api('/marketing/runway/'+action,{id:project.id,version:project.version});await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }
  async function addNote(){
    if(!project||!canContribute||busy||!note.trim())return;
    setBusy(true);setError('');
    const signature=project.id+':'+note.trim();
    const id=noteAttempt.current?.signature===signature?noteAttempt.current.id:requestId();
    noteAttempt.current={signature,id};
    try{await api(`/marketing/runway/${project.id}/input`,{requestId:id,version:project.version,content:note.trim()});noteAttempt.current=null;setNote('');await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }
  return <section className="marketing-runway" aria-label="Standing marketing assignment">
    <header><div><p className="eyebrow">STANDING ASSIGNMENT</p><h2>Marketing project</h2></div>{project&&<span className={'runway-status '+project.status}>{project.status.replaceAll('_',' ')}</span>}</header>
    {!project?<><p>Give Marketing one bounded internal project. It will move through three saved deliverables and stop for your review.</p>
      {canControl&&<div className="runway-setup"><label>Goal<textarea value={goal} onChange={event=>setGoal(event.target.value)} maxLength={1200} rows={4}/></label>
        <ul><li>One source-backed audience and problem note</li><li>Three distinct post angles with evidence and claim limits</li><li>A review packet with unsupported claims and your next decision</li></ul>
        <p>Internal research and local drafts only. Six model turns, 15 minutes active time, 150,000 reserved or reported tokens, and at most two repairs per step. Two fixed public sources. No posts or outreach.</p>
        <button className="primary" disabled={busy||!goal.trim()} onClick={()=>void start()}>{busy?'Starting…':'Start this project'}</button></div>}
    </>:<><p className="runway-goal">{project.goal}</p>
      <div className="runway-metrics"><span><strong>{project.run_count}/{project.max_runs}</strong> turns admitted</span><span><strong>{Math.max(0,project.token_limit-project.token_used-project.token_reserved).toLocaleString()}</strong> tokens unreserved</span><span><strong>{runway?.artifacts.length||0}/3</strong> deliverables saved</span></div>
      <div className="runway-next"><span><b>Current action</b>{current?labels[current.kind]||current.kind:project.status==='needs_review'?'Owner review':'No active step'}</span><span><b>Next action</b>{next?labels[next.kind]||next.kind:project.status==='needs_review'?'Owner decision':'None admitted'}</span><span><b>Next check</b>{nextCheck}</span></div>
      {project.wait_reason&&<p className="runway-reason">{project.wait_reason}</p>}
      <ol className="runway-steps">{runway?.steps.map(step=><li key={step.id}><span className={'runway-dot '+step.status}/><div><strong>{labels[step.kind]||step.kind}</strong><small>{step.status.replaceAll('_',' ')} · {step.attempts} attempt{step.attempts===1?'':'s'}</small></div></li>)}</ol>
      {canControl&&<div className="runway-actions">{['ready','running','waiting'].includes(project.status)&&<button disabled={busy} onClick={()=>void change('pause')}>Pause new work</button>}{project.status==='paused'&&<button disabled={busy} onClick={()=>void change('resume')}>Resume project</button>}</div>}
      <div className="runway-inputs"><strong>Project conversation</strong>{runway?.inputs.map(item=><p key={item.id}><b>{item.actor_name}</b> · {readableTime(item.created_at)}<br/>{item.content}</p>)}{canContribute&&<form onSubmit={event=>{event.preventDefault();void addNote();}}><label>Constraint or revision note<textarea value={note} maxLength={1000} rows={2} onChange={event=>setNote(event.target.value)} placeholder="Add context for the next eligible step"/></label><button disabled={busy||!note.trim()}>Add to project</button><small>Saved with your signed-in session. This does not approve spending or reopen a finished assignment.</small></form>}</div>
      {runway?.artifacts.map(artifact=><details className="runway-artifact" key={artifact.id}><summary>{labels[artifact.kind]||artifact.kind} · saved {readableTime(artifact.created_at)}</summary><pre>{artifact.content}</pre><small>Artifact {artifact.id} · sources {JSON.parse(artifact.source_urls).join(' · ')}</small></details>)}
      {runway?.executions.length? <details className="runway-executions"><summary>Execution and usage receipts</summary><ul>{runway.executions.map(item=><li key={item.id}>{readableTime(item.started_at)} · {item.status} · {item.reported_tokens==null?`usage unavailable; ${item.reserved_tokens.toLocaleString()} reserved`:item.reported_tokens.toLocaleString()+' reported tokens'}{item.error?' · '+item.error:''}</li>)}</ul></details>:null}
    </>}
    {error&&<p className="company-error" role="alert">{error}</p>}
  </section>;
}
