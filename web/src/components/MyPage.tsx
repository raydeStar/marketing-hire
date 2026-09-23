import {useEffect,useRef,useState} from 'react';
import {Check,ArrowUpRight,ListTodo,Pin,Clock3} from 'lucide-react';
import {api} from '../api';
import type {LibraryItem,State} from '../types';
import {localDay} from './ArtifactApps';
import {names,StateIcon} from './Raven';
import '../my-page.css';

export function todayTasks(items:LibraryItem[],today:string){
  return items.filter(item=>{
    if(item.kind!=='todo'||item.status!=='open')return false;
    const plan=item.tracking;
    if(plan&&plan.cadence!=='none'&&plan.lastCheckIn===today)return false;
    return !!(item.due&&item.due<=today)||!!(plan?.nextCheckIn&&plan.nextCheckIn<=today)||
      ((!plan||plan.section==='daily')&&(!item.due||item.due<=today)&&(!plan?.nextCheckIn||plan.nextCheckIn<=today));
  }).sort((a,b)=>(a.due||a.tracking?.nextCheckIn||'9999').localeCompare(b.due||b.tracking?.nextCheckIn||'9999')||a.created.localeCompare(b.created));
}

export function MyPage({data,online,onChanged,onTodo,onRun,onUpcoming,onApps,onOpenApp,onUnpin}:{data:State|null;online:boolean;onChanged:()=>Promise<unknown>;onTodo:(id?:string)=>void;onRun:(id:string)=>void;onUpcoming:()=>void;onApps:()=>void;onOpenApp:(id:string)=>void;onUnpin:()=>void}){
  const [today,setToday]=useState(()=>localDay()),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const saving=useRef(false);
  const [undo,setUndo]=useState<{before:LibraryItem;version:string}|null>(null);
  useEffect(()=>{
    const update=()=>setToday(localDay());
    // Midnight changes the view, never the owner's saved intentions.
    const interval=window.setInterval(update,1000);
    window.addEventListener('focus',update);document.addEventListener('visibilitychange',update);
    return()=>{clearInterval(interval);window.removeEventListener('focus',update);document.removeEventListener('visibilitychange',update);};
  },[]);
  const items=todayTasks(data?.library||[],today);
  const active=(data?.runs||[]).filter(run=>['queued','running','awaitingApproval','awaitingInput','needsAttention'].includes(run.state));
  const results=(data?.runs||[]).filter(run=>!active.includes(run)&&localDay(new Date(run.updated))===today&&
    (run.state==='failed'||run.state==='cancelled'||run.artifactResult||run.outputPath||run.background||run.approval)).slice(0,5);
  const unread=(data?.delegationOccurrences||[]).filter(item=>!!item.completedAt&&!item.readAt).slice(0,5);
  const stale=!!undo&&data?.library.find(item=>item.id===undo.before.id)?.version!==undo.version;
  async function complete(item:LibraryItem){
    if(saving.current)return;saving.current=true;setBusy(true);setError('');
    const recurring=item.tracking&&item.tracking.cadence!=='none';
    const next=new Date(today+'T12:00:00');next.setDate(next.getDate()+(item.tracking?.cadence==='weekly'?7:1));
    try{
      const saved=await api<LibraryItem>('/library/'+item.id,recurring?{...item,tracking:{...item.tracking,lastCheckIn:today,nextCheckIn:localDay(next)}}:{...item,status:'done'},'PUT');
      setUndo({before:item,version:saved.version});await onChanged();
    }catch(reason){setError((reason as Error).message);}finally{setBusy(false);saving.current=false;}
  }
  async function undoChange(){
    if(!undo||stale||saving.current)return;saving.current=true;setBusy(true);setError('');
    try{await api('/library/'+undo.before.id,{...undo.before,version:undo.version},'PUT');setUndo(null);await onChanged();}
    catch(reason){setError((reason as Error).message);}finally{setBusy(false);saving.current=false;}
  }
  const pinned=data?.myPage?.mode==='artifact'?data.artifacts?.find(app=>app.id===data.myPage?.artifactId&&!app.archived):null;
  return <div className="my-page" role="region" aria-label="My page">
    <div className="today-heading"><h2>Today</h2><p>{new Date(today+'T12:00:00').toLocaleDateString(undefined,{weekday:'long',month:'long',day:'numeric'})}</p></div>
    {!online&&<p role="status">Reconnecting. Saved items remain here; changes are paused.</p>}
    {error&&<p className="error" role="alert">{error}</p>}
    {undo&&<div className="today-undo" role="status"><span>{stale?'This item changed again. Undo is unavailable.':`Saved: ${undo.before.title}`}</span><button disabled={stale||busy||!online} onClick={()=>void undoChange()}>Undo</button></div>}
    <section aria-label="Today's tasks"><div className="today-section-heading"><h3><ListTodo size={16}/>Your tasks <small>{items.length}</small></h3><button onClick={()=>onTodo()}>All tasks</button></div>
      {!items.length&&<p className="quiet-empty">A little breathing room. Add a daily task or a due date in To-do.</p>}
      {items.map(item=><article className="today-task" key={item.id}><button className="item-check" disabled={!online||busy} aria-label={(item.tracking&&item.tracking.cadence!=='none'?'Check in: ':'Mark complete: ')+item.title} onClick={()=>void complete(item)}><Check size={16}/></button><button className="today-task-detail" onClick={()=>onTodo(item.id)}><strong>{item.title}</strong>{item.tracking?.nextStep&&<span>{item.tracking.nextStep}</span>}{item.due&&<small className={item.due<today?'overdue':''}>{item.due<today?'Overdue · ': 'Due · '}{item.due}</small>}{item.tracking?.cadence!=='none'&&item.tracking?.cadence&&<small>{item.tracking.cadence} check-in</small>}</button></article>)}
    </section>
    <section aria-label="Thaddeus work"><div className="today-section-heading"><h3><Clock3 size={16}/>Thaddeus's work</h3></div>
      {!active.length&&<p className="quiet-empty">No task is running. Your list waits for your instructions.</p>}
      {active.map(run=><button className="today-work" key={run.id} onClick={()=>onRun(run.id)}><StateIcon state={run.state}/><span><strong>{run.goal.objective}</strong><small>{names[run.state]} · {run.summary}</small></span><ArrowUpRight size={14}/></button>)}
      {unread.map(item=><button className="today-work" key={item.id} onClick={onUpcoming}><Clock3 size={16}/><span><strong>{data?.delegations?.find(job=>job.id===item.jobId)?.title||'Scheduled result'}</strong><small>{item.summary||item.state}</small></span><ArrowUpRight size={14}/></button>)}
    </section>
    {!!results.length&&<section aria-label="Today's results"><h3>Recent results</h3>{results.map(run=><button className="today-work" key={run.id} onClick={()=>onRun(run.id)}><StateIcon state={run.state}/><span><strong>{run.goal.objective}</strong><small>{names[run.state]} · {run.summary}</small></span><ArrowUpRight size={14}/></button>)}</section>}
    <footer>{data?.myPage?.mode==='artifact'?<><p>{pinned?'Pinned beside your checklist':'Your pinned app is unavailable. Restore it from Trash, or choose another.'}</p>{pinned&&<button className="today-pinned-link" onClick={()=>onOpenApp(pinned.id)}><Pin size={14}/>{pinned.title}<ArrowUpRight size={14}/></button>}<div className="today-pin-actions"><button onClick={onApps}>Change pinned app</button><button disabled={!online} onClick={onUnpin}>Remove pin</button></div></>:<><button onClick={onApps}><Pin size={14}/>Choose an app instead</button><p>Pin an app beside your checklist, or ask Thaddeus to make one.</p></>}</footer>
  </div>;
}
