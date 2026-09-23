import {useState} from 'react';
import {ArrowRight,CheckCircle2,Search} from 'lucide-react';
import {readableTime,type MarketingState} from './MarketingPanels';

export function WorkActivity({events,tasks,onTask}:{events:NonNullable<MarketingState['activity']>;tasks:MarketingState['tasks'];onTask:(id:string)=>void}){
  const [query,setQuery]=useState('');
  const visible=events.filter(event=>`${event.title} ${event.kind}`.toLowerCase().includes(query.toLowerCase()));
  return <section className="work-activity" aria-label="Activity log">
    <div className="company-section-title"><div><h2>Activity log</h2><p>The latest 100 recorded actions from Marketing. Newest first.</p></div></div>
    <label className="work-board-search"><Search size={16}/><input aria-label="Search activity" placeholder="Find an action…" value={query} onChange={event=>setQuery(event.target.value)}/></label>
    <div className="work-activity-list">{visible.map(event=>{
      const id=event.data.task_id??event.data.id;
      const task=tasks.find(task=>task.id===id);
      const title=event.kind==='evidence'&&task?`Source attached to ${task.title}`:event.title.replace(/^Owner session [a-f0-9]+ /,'Owner ');
      return <article key={event.id}><span className="work-activity-icon"><CheckCircle2 size={17}/></span><div><header><strong>{title}</strong><time>{readableTime(event.ts)}</time></header><small>{event.kind.replaceAll('_',' ')} · Recorded action</small>{task&&<button className="company-text-link" onClick={()=>onTask(task.id)}>Open task <ArrowRight size={14}/></button>}<details><summary>View receipt</summary><pre>{JSON.stringify(event.data,null,2)}</pre></details></div></article>;
    })}{!visible.length&&<div className="company-empty"><p>{query?'No matching actions.':'No activity recorded for this team yet.'}</p></div>}</div>
  </section>;
}
