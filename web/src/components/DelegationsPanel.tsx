import {Bell,CalendarClock,Mail,PauseCircle,ShieldCheck,X} from 'lucide-react';
import type {DelegationJob,DelegationOccurrence} from '../types';

const terminal=new Set(['cancelled','completed','succeeded','failed','missed']);
const labels:Record<string,string>={scheduled:'Scheduled',working:'Working',succeeded:'Delivered',failed:'Failed',unknown:'Needs review',missed:'Missed',cancelled:'Cancelled','needs-approval':'Needs approval',completed:'Completed'};
const icon=(kind:string)=>kind==='email'?Mail:kind==='brief'?CalendarClock:Bell;

function when(job:DelegationJob){
 if(job.schedule.kind==='weekdays')return `Weekdays at ${job.schedule.localTime} · ${job.schedule.timeZone}`;
 return job.nextRunUtc?`${new Date(job.nextRunUtc).toLocaleString()} · ${job.schedule.timeZone}`:'No future run';
}

export function DelegationsPanel({jobs,occurrences,online,busy,onCancel,onRead}:{jobs:DelegationJob[];occurrences:DelegationOccurrence[];online:boolean;busy:boolean;onCancel:(job:DelegationJob)=>void;onRead:(occurrence:DelegationOccurrence)=>void}){
 const sorted=[...jobs].sort((a,b)=>(a.nextRunUtc||'9999').localeCompare(b.nextRunUtc||'9999'));
 return <section className="delegations-panel" aria-label="Delegated work">
  <div className="delegations-heading"><div><h3>Delegated work</h3><p><span className={'host-dot '+(online?'online':'')}/>{online?'Host available':'Host unavailable'}</p></div><ShieldCheck size={18}/></div>
  {!online&&<p className="delegation-warning">Local work cannot fire while this computer is asleep, shut down, or the Thaddeus host is closed.</p>}
  {!sorted.length&&<p className="muted">No scheduled work. Ask in Chat when you would like Thaddeus to follow through later.</p>}
  {sorted.map(job=>{
   const Icon=icon(job.kind);const latest=[...occurrences].filter(item=>item.jobId===job.id).sort((a,b)=>b.sequence-a.sequence)[0];
   return <article className={'delegation-card '+job.state} key={job.id}>
    <div className="delegation-card-main"><span className="delegation-icon"><Icon size={16}/></span><div><strong>{job.title}</strong><time dateTime={job.nextRunUtc||undefined}>{when(job)}</time><small>{labels[job.state]||job.state}{job.lastSummary?` · ${job.lastSummary}`:''}</small>{latest&&<small>Latest: {labels[latest.state]||latest.state} · {latest.summary}</small>}</div></div>
    {!terminal.has(job.state)&&<button type="button" className="delegation-cancel" disabled={!online||busy||job.cancellationRequested} onClick={()=>onCancel(job)}><X size={14}/>{job.cancellationRequested?'Cancelling':'Cancel'}</button>}
    {latest&&latest.state!=='working'&&!latest.readAt&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onRead(latest)}>Mark result read</button>}
    {job.state==='unknown'&&<p className="delegation-warning"><PauseCircle size={14}/>The outcome is uncertain. It will not be replayed automatically.</p>}
   </article>;
  })}
 </section>;
}
