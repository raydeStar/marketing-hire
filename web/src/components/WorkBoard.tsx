import {useState} from 'react';
import {ArrowRight,Check,CheckCircle2,ChevronRight,Circle,Clock3,Pause,Plus,Search,ShieldCheck} from 'lucide-react';
import {priorityLabel,readableTime,type MarketingTask} from './MarketingPanels';

// Earlier holds were stored as blocked tasks. Keep their intent until the ledger is migrated.
export function isPaused(task:MarketingTask){return task.status==='paused'||(task.status==='needs_you'&&task.action_state==='blocked'&&/\bon hold\b/i.test(task.title));}
export function needsDecision(task:MarketingTask){return task.status==='needs_you'&&!isPaused(task);}
function plain(value:string){return value.replace(/[#*`>]/g,'').replace(/\s+/g,' ').trim();}
const lanes=[
  {id:'needs_you',title:'Needs decision',description:'Your input moves this forward',icon:ShieldCheck,match:needsDecision},
  {id:'working',title:'In progress',description:'Currently with your team',icon:Clock3,match:(t:MarketingTask)=>t.status==='working'},
  {id:'ready',title:'Assigned',description:'The next agreed commitments',icon:Circle,match:(t:MarketingTask)=>t.status==='ready'}
];

export function WorkBoard({tasks,employeeName,onOpen,onCreate,canCreate}:{tasks:MarketingTask[];employeeName:string;onOpen:(id:string)=>void;onCreate:()=>void;canCreate:boolean}){
  const [query,setQuery]=useState('');
  const visible=tasks.filter(t=>`${t.title} ${t.next_action} ${t.blocker||''}`.toLowerCase().includes(query.toLowerCase()));
  const waiting=tasks.filter(needsDecision).length,working=tasks.filter(t=>t.status==='working').length,ready=tasks.filter(t=>t.status==='ready').length;
  const paused=visible.filter(isPaused),done=visible.filter(t=>t.status==='done');
  function card(task:MarketingTask){return <button className="work-card" key={task.id} onClick={()=>onOpen(task.id)}>
    <div className="work-card-top"><span className="work-card-department">Marketing</span>{task.priority==='high'&&<span className="work-priority">High priority</span>}</div>
    <h3>{task.title}</h3><div className="work-card-next"><span>{needsDecision(task)?'DECISION NEEDED':'NEXT ACTION'}</span><p>{plain(needsDecision(task)?task.blocker||task.next_action:task.next_action)||'Define the next action with the owner.'}</p></div>
    {task.blocker&&!needsDecision(task)&&<p className="work-card-blocker">Blocked: {plain(task.blocker)}</p>}
    <footer><span className="work-owner"><i>{employeeName.slice(0,1)}</i>{employeeName}</span><ArrowRight size={15}/></footer>
  </button>;}
  function archiveRow(task:MarketingTask){return <button className="work-archive-row" key={task.id} onClick={()=>onOpen(task.id)}>{isPaused(task)?<Pause size={16}/>:<CheckCircle2 size={16}/>}<span><strong>{task.title}</strong><small>{employeeName} · {isPaused(task)?'Held until you resume it':readableTime(task.updated_at)}</small></span><ChevronRight size={16}/></button>;}
  return <section className="work-board" aria-label="Team tasks">
    <div className="work-board-heading"><div><h2>Team board</h2><p>Clear owners. Agreed next actions.</p></div><button className="primary" disabled={!canCreate} onClick={onCreate}><Plus size={16}/>New task</button></div>
    <div className="work-board-toolbar"><div className="work-board-summary"><span><ShieldCheck size={15}/><b>{waiting}</b> needs decision</span><span><Clock3 size={15}/><b>{working}</b> in progress</span><span><Circle size={15}/><b>{ready}</b> assigned</span></div><label className="work-board-search"><Search size={16}/><input value={query} onChange={e=>setQuery(e.target.value)} aria-label="Search tasks" placeholder="Find a task…"/></label></div>
    <div className="work-board-lanes">{lanes.map(lane=>{const items=visible.filter(lane.match),Icon=lane.icon;return <section className={'work-lane '+lane.id} key={lane.id} aria-label={lane.title}><header><div><Icon size={16}/><h3>{lane.title}</h3><span>{items.length}</span></div><p>{lane.description}</p></header><div className="work-lane-items">{items.map(card)}{!items.length&&<div className="work-lane-empty"><Check size={17}/><p>{query?'No matching tasks':lane.id==='needs_you'?'Nothing waiting on you':lane.id==='working'?'No work in progress':'No pending assignments'}</p></div>}</div></section>;})}</div>
    <div className="work-archives"><details open={query?true:undefined}><summary><Pause size={16}/><strong>Paused work</strong><span>{paused.length}</span><small>Kept out of your decision queue</small><ChevronRight size={16}/></summary>{paused.length?paused.map(archiveRow):<p>No paused tasks.</p>}</details><details open={query?true:undefined}><summary><CheckCircle2 size={16}/><strong>Completed</strong><span>{done.length}</span><small>Results and their supporting records</small><ChevronRight size={16}/></summary>{done.length?done.map(archiveRow):<p>No completed tasks yet.</p>}</details></div>
  </section>;
}
