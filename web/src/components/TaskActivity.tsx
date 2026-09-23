import {useEffect,useRef,useState} from 'react';
import {CheckCircle2,CircleAlert,LoaderCircle,X} from 'lucide-react';
import type {Run} from '../types';
import './task-activity.css';

const working=(run:Run)=>['queued','running'].includes(run.state);
const waiting=(run:Run)=>['awaitingApproval','awaitingInput','paused','needsAttention'].includes(run.state);
const labels:Record<string,string>={queued:'Queued',running:'Working',awaitingApproval:'Waiting for approval',awaitingInput:'Waiting for your answer',paused:'Paused',needsAttention:'Needs attention',succeeded:'Completed',failed:'Failed',cancelled:'Cancelled',denied:'Declined'};

export function TaskActivity({runs,online,onCancel,onDetails,onArtifact}:{runs:Run[];online:boolean;onCancel:(id:string)=>void;onDetails:(id:string)=>void;onArtifact:(id:string)=>void}){
  const [open,setOpen]=useState(false);
  const root=useRef<HTMLDivElement>(null),toggle=useRef<HTMLButtonElement>(null);
  function close(){setOpen(false);toggle.current?.focus({preventScroll:true});}
  useEffect(()=>{
    if(!open)return;
    const outside=(event:PointerEvent)=>{if(event.target instanceof Node&&!root.current?.contains(event.target))setOpen(false);};
    document.addEventListener('pointerdown',outside);
    root.current?.querySelector<HTMLButtonElement>('[aria-label="Close task activity"]')?.focus({preventScroll:true});
    return()=>document.removeEventListener('pointerdown',outside);
  },[open]);
  const active=runs.filter(working),pending=runs.filter(waiting);
  const recent=runs.filter(run=>run.background&&!working(run)&&!waiting(run)).sort((a,b)=>Date.parse(b.updated)-Date.parse(a.updated)).slice(0,6);
  const items=[...pending,...active,...recent];
  const latest=recent[0];
  const title=[active.length?`${active.length} active`:null,pending.length?`${pending.length} waiting`:null].filter(Boolean).join(' · ')||'Recent results';
  const notice=pending[0]||latest;
  if(!items.length)return null;
  return <div className="task-activity" ref={root}>
    <button ref={toggle} type="button" className="task-activity-toggle" aria-label={'Tasks: '+title.toLowerCase()} title={title} aria-expanded={open} onClick={()=>setOpen(!open)}>
      {pending.length?<CircleAlert size={16}/>:active.length?<LoaderCircle className={online?'task-spinner':''} size={16}/>:latest?.state==='succeeded'?<CheckCircle2 size={16}/>:<CircleAlert size={16}/>} {!!(active.length+pending.length)&&<span>{active.length+pending.length}</span>}
    </button>
    <span className="composer-hint" role="status">{notice?`${notice.state==='succeeded'?'Task completed':labels[notice.state]||'Needs attention'}: ${notice.goal.objective}. ${notice.summary}`:''}</span>
    {open&&<section className="task-activity-panel" aria-label="Task activity" onKeyDown={event=>{if(event.key==='Escape'){event.stopPropagation();close();}}}>
      <div className="task-activity-title"><strong>{title}</strong><button type="button" aria-label="Close task activity" onClick={close}><X size={16}/></button></div>
      <p>{pending.length?'Waiting tasks need your review or input. Open one to continue.':active.length?'You can keep chatting while these tasks finish.':'Nothing running. Your recent results are below.'}</p>
      {items.map(run=><article key={run.id}>
        <strong>{run.goal.objective}</strong><small>{!online&&(working(run)||waiting(run))?'Connection lost · last known status: ':''}{run.state==='running'&&run.background?'Working in background':labels[run.state]||'Needs attention'}</small>
        <p>{run.summary}</p><div>
          {['queued','running'].includes(run.state)&&<button disabled={!online} onClick={()=>onCancel(run.id)}>Cancel task</button>}
          {run.state==='succeeded'&&run.artifactResult&&!run.artifactResult.deleted&&<button onClick={()=>{setOpen(false);onArtifact(run.artifactResult!.id);}}>Open app</button>}
          <button onClick={()=>{setOpen(false);onDetails(run.id);}}>{run.state==='awaitingApproval'?'Review':run.state==='awaitingInput'?'Answer question':run.state==='paused'?'Open paused task':'Details'}</button>
        </div>
      </article>)}
    </section>}
  </div>;
}
