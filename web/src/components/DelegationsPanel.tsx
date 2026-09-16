import {useState} from 'react';
import Markdown from 'react-markdown';
import {Bell,BellOff,CalendarClock,Mail,MessageCircle,Pause,PauseCircle,Play,ShieldCheck,X} from 'lucide-react';
import type {DelegationJob,DelegationOccurrence} from '../types';
import {Modal} from './Modal';

const terminal=new Set(['cancelled','completed','succeeded','failed','unknown','missed']);
const reviewable=new Set(['failed','unknown','missed','needs-approval']);
const labels:Record<string,string>={scheduled:'Scheduled',paused:'Paused',working:'Working',succeeded:'Delivered',failed:'Failed',unknown:'Needs review',missed:'Missed',cancelled:'Cancelled','needs-approval':'Needs approval',completed:'Completed'};
const icon=(kind:string)=>kind==='email'?Mail:kind==='brief'?CalendarClock:Bell;

function when(job:DelegationJob){
 if(job.state==='paused')return `Paused · ${job.schedule.localTime} ${job.schedule.timeZone}`;
 if(job.schedule.kind==='weekdays')return `Weekdays at ${job.schedule.localTime} · ${job.schedule.timeZone}`;
 return job.nextRunUtc?`${new Date(job.nextRunUtc).toLocaleString()} · ${job.schedule.timeZone}`:'No future run';
}

type Props={jobs:DelegationJob[];occurrences:DelegationOccurrence[];online:boolean;busy:boolean;onCancel:(job:DelegationJob)=>void;onPause:(job:DelegationJob)=>void;onResume:(job:DelegationJob)=>void;onRead:(occurrence:DelegationOccurrence)=>void;onReview:(job:DelegationJob,occurrence?:DelegationOccurrence)=>void};

export function DelegationsPanel({jobs,occurrences,online,busy,onCancel,onPause,onResume,onRead,onReview}:Props){
 const [selected,setSelected]=useState<DelegationOccurrence|null>(null);
 const sorted=[...jobs].sort((a,b)=>(a.nextRunUtc||'9999').localeCompare(b.nextRunUtc||'9999'));
 const evidence=selected?.providerEvidence;
 const brief=typeof evidence?.brief==='string'?evidence.brief:null;
 const sources=Array.isArray(evidence?.sources)?evidence.sources as Record<string,unknown>[]:[];
 return <><section className="delegations-panel" aria-label="Delegated work">
  <div className="delegations-heading"><div><h3>Delegated work</h3><p><span className={'host-dot '+(online?'online':'')}/>{online?'Host available':'Host unavailable'}</p></div><ShieldCheck size={18}/></div>
  {!online&&<p className="delegation-warning">Local work cannot fire while this computer is asleep, shut down, or the Thaddeus host is closed.</p>}
  {!sorted.length&&<p className="muted">No scheduled work. Ask in Chat when you would like Thaddeus to follow through later.</p>}
  {sorted.map(job=>{
   const Icon=icon(job.kind);const latest=[...occurrences].filter(item=>item.jobId===job.id).sort((a,b)=>b.sequence-a.sequence)[0];
   const notificationFailed=!!latest&&['failed','unsupported'].includes(latest.notificationStatus);
   const jobLabel=notificationFailed?'Saved · Notification failed':labels[job.state]||job.state;
   return <article className={'delegation-card '+job.state} key={job.id}>
    <button type="button" className="delegation-card-main" onClick={()=>latest&&setSelected(latest)} disabled={!latest} aria-label={latest?`Inspect latest result for ${job.title}`:undefined}><span className="delegation-icon"><Icon size={16}/></span><div><strong>{job.title}</strong><time dateTime={job.nextRunUtc||undefined}>{when(job)}</time><small>{jobLabel}{job.lastSummary?` · ${job.lastSummary}`:''}</small>{latest&&<small>Latest: {notificationFailed?'Saved in Thaddeus · Notification failed':labels[latest.state]||latest.state} · {latest.summary}</small>}</div></button>
    <div className="delegation-actions">
     {job.kind==='brief'&&job.state==='scheduled'&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onPause(job)}><Pause size={14}/>Pause</button>}
     {job.kind==='brief'&&job.state==='paused'&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onResume(job)}><Play size={14}/>Resume</button>}
     {!terminal.has(job.state)&&<button type="button" className="delegation-cancel" disabled={!online||busy||job.cancellationRequested} onClick={()=>onCancel(job)}><X size={14}/>{job.cancellationRequested?'Cancelling':'Cancel'}</button>}
     {(reviewable.has(job.state)||notificationFailed)&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onReview(job,latest)}><MessageCircle size={14}/>Review in Chat</button>}
     {latest&&latest.state!=='working'&&!latest.readAt&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onRead(latest)}>Mark result read</button>}
    </div>
    {job.state==='unknown'&&<p className="delegation-warning"><PauseCircle size={14}/>The outcome is uncertain. It will not be replayed automatically.</p>}
    {notificationFailed&&<p className="delegation-warning"><BellOff size={14}/><span>{latest.notificationError||'Windows could not display this notification.'} The unread reminder is still saved here.</span></p>}
   </article>;
  })}
 </section>{selected&&<Modal title={`Delegated result · ${labels[selected.state]||selected.state}`} onClose={()=>setSelected(null)} className="delegation-result-modal"><div className="delegation-result">
  <p>{selected.summary}</p><dl><dt>Scheduled</dt><dd>{new Date(selected.dueUtc).toLocaleString()}</dd><dt>Completed</dt><dd>{selected.completedAt?new Date(selected.completedAt).toLocaleString():'Not completed'}</dd><dt>Dispatch</dt><dd>{selected.dispatchState}</dd><dt>Notification</dt><dd>{selected.notificationStatus}</dd>{selected.notificationError&&<><dt>Notification issue</dt><dd>{selected.notificationError}</dd></>}{selected.providerId&&<><dt>Provider receipt</dt><dd>{selected.providerId}</dd></>}</dl>
  {brief&&<section className="delegation-brief-output"><Markdown>{brief}</Markdown></section>}
  {!!sources.length&&<section><h3>Sources</h3>{sources.map((source,index)=><p key={index}><strong>{String(source.kind||'Source')}</strong> · {String(source.connector||'Unknown')} · {String(source.status||'unknown')}{source.error?` · ${String(source.error)}`:''}</p>)}</section>}
  <details><summary>Technical receipt</summary><pre>{JSON.stringify(selected,null,2)}</pre></details>
 </div></Modal>}</>;
}
