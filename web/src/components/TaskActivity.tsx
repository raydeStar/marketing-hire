import {useEffect,useRef,useState} from 'react';
import {CheckCircle2,CircleAlert,LoaderCircle,X} from 'lucide-react';
import type {Run} from '../types';
import './task-activity.css';

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
  const active=runs.filter(run=>['queued','running'].includes(run.state));
  const recent=runs.filter(run=>run.background&&!['queued','running'].includes(run.state)).sort((a,b)=>Date.parse(b.updated)-Date.parse(a.updated)).slice(0,6);
  const items=[...active,...recent];
  const latest=recent[0];
  if(!items.length)return null;
  return <div className="task-activity" ref={root}>
    <button ref={toggle} type="button" className="task-activity-toggle" aria-label={active.length?'Tasks: '+active.length+' active':'Tasks: recent results'} title={active.length?active.length+' active '+(active.length===1?'task':'tasks'):'Recent task results'} aria-expanded={open} onClick={()=>setOpen(!open)}>
      {active.length?<LoaderCircle className={online?'task-spinner':''} size={16}/>:latest?.state==='succeeded'?<CheckCircle2 size={16}/>:<CircleAlert size={16}/>} {!!active.length&&<span>{active.length}</span>}
    </button>
    <span className="composer-hint" role="status">{latest?`${latest.state==='succeeded'?'Task completed':'Task stopped'}: ${latest.goal.objective}. ${latest.summary}`:''}</span>
    {open&&<section className="task-activity-panel" aria-label="Task activity" onKeyDown={event=>{if(event.key==='Escape'){event.stopPropagation();close();}}}>
      <div className="task-activity-title"><strong>{active.length?`${active.length} active ${active.length===1?'task':'tasks'}`:'Recent tasks'}</strong><button type="button" aria-label="Close task activity" onClick={close}><X size={16}/></button></div>
      <p>{active.length?'You can keep chatting while these tasks finish.':'Nothing running. Your recent results are below.'}</p>
      {items.map(run=><article key={run.id}>
        <strong>{run.goal.objective}</strong><small>{['queued','running'].includes(run.state)?(online?run.background?'Working in background':'Working':'Connection lost · last known status: working'):run.state==='succeeded'?'Completed':run.state==='cancelled'?'Cancelled':'Needs attention'}</small>
        <p>{run.summary}</p><div>
          {['queued','running'].includes(run.state)&&<button disabled={!online} onClick={()=>onCancel(run.id)}>Cancel task</button>}
          {run.state==='succeeded'&&run.artifactResult&&!run.artifactResult.deleted&&<button onClick={()=>{setOpen(false);onArtifact(run.artifactResult!.id);}}>Open app</button>}
          <button onClick={()=>{setOpen(false);onDetails(run.id);}}>Details</button>
        </div>
      </article>)}
    </section>}
  </div>;
}
