import {useState} from 'react';
import {Plus,Check,Repeat2,CalendarDays,Target,Activity,Pencil,Archive,X,RotateCcw} from 'lucide-react';
import {api} from '../api';
import type {LibraryItem,TrackingPlan} from '../types';
import {localDay} from './ArtifactApps';

const sections=[{id:'tracked',label:'Tracked',description:'Habits and progress, with a next check-in.',icon:Activity},{id:'daily',label:'Daily to-do',description:'One day at a time.',icon:Check},{id:'weekly',label:'Weekly to-do',description:'Make room for what matters this week.',icon:CalendarDays},{id:'goals',label:'Overall goals',description:'The larger direction. Small steps still count.',icon:Target}] as const;
const plan=(item:LibraryItem):TrackingPlan=>item.tracking||{section:'daily',cadence:'none'};
const date=(value?:string|null)=>value?new Date(value+'T12:00:00').toLocaleDateString(undefined,{month:'short',day:'numeric'}):'';
function nextDate(cadence:string){const next=new Date();next.setDate(next.getDate()+(cadence==='weekly'?7:1));return localDay(next);}

export function TodoBoard({items,online,onChanged,onDiscuss,focusId}:{items:LibraryItem[];online:boolean;onChanged:()=>Promise<unknown>;onDiscuss:(text:string)=>void;focusId?:string}){
 const [status,setStatus]=useState(items.find(i=>i.id===focusId)?.status||'open'),[draft,setDraft]=useState<LibraryItem|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const mine=items.filter(i=>i.kind==='todo'),today=localDay();
 async function save(item:LibraryItem,close=false){setBusy(true);setError('');try{await api('/library/'+item.id,item,'PUT');await onChanged();if(close)setDraft(null);}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 function create(section:TrackingPlan['section']){setDraft({id:crypto.randomUUID().replaceAll('-',''),kind:'todo',title:'',content:'',status:'open',url:null,due:null,version:'absent',created:'',updated:'',tracking:{section,cadence:section==='tracked'?'weekly':'none'}});}
 function setPlan(change:Partial<TrackingPlan>){if(draft)setDraft({...draft,tracking:{...plan(draft),...change}});}
 return <section className="todo-board" aria-label="To-do">
  <div className="page-heading"><div><p className="eyebrow">A LITTLE ORDER</p><h1>To-do</h1></div><button onClick={()=>create('daily')} disabled={!online||busy}><Plus size={16}/>Add a to-do</button></div>
  <nav className="collection-filters" aria-label="Task status">{[['open','Active'],['done','Completed'],['archived','Archived']].map(([key,label])=><button key={key} aria-pressed={status===key} onClick={()=>setStatus(key)}>{label}<small>{mine.filter(i=>i.status===key).length}</small></button>)}</nav>
  {error&&<p role="alert" className="error">{error}</p>}
  {draft&&<form className="collection-editor" aria-label="Edit to-do" onKeyDown={e=>{if(e.key==='Escape'){e.stopPropagation();setDraft(null);}}} onSubmit={e=>{e.preventDefault();void save(draft,true);}}>
   <div className="page-heading"><h2>{draft.version==='absent'?'Add a to-do':'Edit to-do'}</h2><button type="button" aria-label="Close item editor" onClick={()=>setDraft(null)}><X size={16}/></button></div>
   <label>Title<input autoFocus required maxLength={160} value={draft.title} onChange={e=>setDraft({...draft,title:e.target.value})}/></label>
   <div className="collection-fields"><label>Section<select value={plan(draft).section} onChange={e=>setPlan({section:e.target.value as TrackingPlan['section']})}>{sections.map(s=><option value={s.id} key={s.id}>{s.label}</option>)}</select></label><label>Repeat<select value={plan(draft).cadence} onChange={e=>setPlan({cadence:e.target.value as TrackingPlan['cadence']})}><option value="none">Does not repeat</option><option value="daily">Daily</option><option value="weekly">Weekly</option></select></label></div>
   <label>Notes<textarea value={draft.content} maxLength={12000} onChange={e=>setDraft({...draft,content:e.target.value})}/></label>
   <label>Next step<input value={plan(draft).nextStep||''} maxLength={1000} onChange={e=>setPlan({nextStep:e.target.value})}/></label>
   <div className="collection-fields"><label>Due date<input type="date" value={draft.due||''} onChange={e=>setDraft({...draft,due:e.target.value||null})}/></label><label>Next check-in<input type="date" value={plan(draft).nextCheckIn||''} onChange={e=>setPlan({nextCheckIn:e.target.value||null})}/></label></div>
   {['tracked','goals'].includes(plan(draft).section)&&<div className="progress-fields"><label>Current<input type="number" step="any" min={-1000000} max={1000000} value={plan(draft).current??''} onChange={e=>setPlan({current:e.target.value===''?null:Number(e.target.value)})}/></label><label>Target<input type="number" step="any" min={-1000000} max={1000000} value={plan(draft).target??''} onChange={e=>setPlan({target:e.target.value===''?null:Number(e.target.value)})}/></label><label>Unit<input maxLength={40} placeholder="lb, workouts, applications…" value={plan(draft).unit||''} onChange={e=>setPlan({unit:e.target.value})}/></label></div>}
   <p className="muted">Check-in dates appear here and in Upcoming. They do not schedule notifications.</p><button className="primary" disabled={busy||!online||!draft.title.trim()}>Save item</button>
  </form>}
  {sections.map(({id,label,description,icon:Icon})=>{const list=mine.filter(i=>i.status===status&&plan(i).section===id);return <section className="todo-section" key={id} aria-label={label}>
   <div className="section-heading"><div><Icon size={19}/><h2>{label}</h2><span>{list.length}</span></div><button className="text-button" aria-label={'Add to '+label} disabled={!online||busy} onClick={()=>create(id)}><Plus size={17}/></button></div><p className="muted">{description}</p>
   {!list.length&&<p className="quiet-empty">{status==='open'?'Nothing here yet.':`No ${status==='done'?'completed':'archived'} items.`}</p>}
   {list.map(item=>{const p=plan(item),recurring=p.cadence!=='none';return <article className={'todo-row '+(item.id===focusId?'focused':'')} key={item.id}>
    <button className="item-check" aria-label={(item.status==='done'?'Mark unfinished: ':recurring?'Check in: ':'Mark complete: ')+item.title} aria-pressed={item.status==='done'||recurring&&p.lastCheckIn===today} disabled={!online||busy||item.status==='archived'||recurring&&p.lastCheckIn===today} onClick={()=>void save(recurring?{...item,tracking:{...p,lastCheckIn:today,nextCheckIn:nextDate(p.cadence)}}:{...item,status:item.status==='done'?'open':'done'})}>{(item.status==='done'||recurring&&p.lastCheckIn===today)?<Check size={17}/>:<span/>}</button>
    <div className="todo-content"><h3>{item.title}</h3>{item.content&&<p>{item.content}</p>}{p.nextStep&&<p className="next-step">Next: {p.nextStep}</p>}
     <div className="item-meta">{recurring&&<span><Repeat2 size={12}/>{p.cadence}{p.lastCheckIn===today?' · checked in today':''}</span>}{item.due&&<span className={item.due<today?'overdue':''}>Due {date(item.due)}</span>}{p.nextCheckIn&&<span className={p.nextCheckIn<=today?'due-now':''}>Check-in {date(p.nextCheckIn)}</span>}{(p.current!=null||p.target!=null)&&<span>{p.current??'—'}{p.target!=null?' → '+p.target:''} {p.unit}</span>}</div>
     <div className="item-actions"><button disabled={!online||busy} aria-label={'Edit '+item.title} onClick={()=>setDraft(item)}><Pencil size={13}/>Edit</button><button onClick={()=>onDiscuss(`Help me with this ${id==='goals'?'goal':'task'} and its next step:\n${item.title}\n${item.content}\n${p.nextStep||''}`)}>Plan next step</button>{recurring&&<button disabled={!online||busy} onClick={()=>void save({...item,status:item.status==='done'?'open':'done'})}>{item.status==='done'?'Resume tracking':'Finish tracking'}</button>}<button disabled={!online||busy} onClick={()=>void save({...item,status:item.status==='archived'?'open':'archived'})}>{item.status==='archived'?<><RotateCcw size={13}/>Restore</>:<><Archive size={13}/>Archive</>}</button></div>
    </div>
   </article>;})}
  </section>;})}
 </section>;
}
