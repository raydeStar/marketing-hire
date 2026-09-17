import {useState} from 'react';
import Markdown from 'react-markdown';
import {Bell,BellOff,CalendarClock,Mail,MessageCircle,Pause,PauseCircle,Play,ShieldCheck,X} from 'lucide-react';
import type {DelegationJob,DelegationOccurrence} from '../types';
import {Modal} from './Modal';

const terminal=new Set(['cancelled','completed','succeeded','failed','unknown','missed']);
const reviewable=new Set(['failed','unknown','missed','needs-approval']);
const labels:Record<string,string>={scheduled:'Scheduled',paused:'Paused',working:'Working',succeeded:'Completed',failed:'Failed',unknown:'Needs review',missed:'Missed',cancelled:'Cancelled','needs-approval':'Needs approval',completed:'Completed'};
const icon=(kind:string)=>kind==='email'||kind==='inbox-watch'?Mail:kind==='brief'?CalendarClock:Bell;

function when(job:DelegationJob){
 if(job.state==='paused')return job.schedule.kind==='interval'?`Paused · every ${job.schedule.intervalMinutes} minutes`:`Paused · ${job.schedule.localTime} ${job.schedule.timeZone}`;
 if(job.schedule.kind==='weekdays')return `Weekdays at ${job.schedule.localTime} · ${job.schedule.timeZone}`;
 if(job.schedule.kind==='interval')return `Every ${job.schedule.intervalMinutes} minutes · ${job.schedule.timeZone}`;
 return job.nextRunUtc?`${new Date(job.nextRunUtc).toLocaleString()} · ${job.schedule.timeZone}`:'No future run';
}

type Props={jobs:DelegationJob[];occurrences:DelegationOccurrence[];online:boolean;busy:boolean;onCancel:(job:DelegationJob)=>void;onPause:(job:DelegationJob)=>void;onResume:(job:DelegationJob)=>void;onEditInstruction:(job:DelegationJob,instruction:string)=>void;onRead:(occurrence:DelegationOccurrence)=>void;onReview:(job:DelegationJob,occurrence?:DelegationOccurrence)=>void};

export function DelegationsPanel({jobs,occurrences,online,busy,onCancel,onPause,onResume,onEditInstruction,onRead,onReview}:Props){
 const [selectedId,setSelectedId]=useState<string|null>(null);
 const selected=occurrences.find(item=>item.id===selectedId);
 const selectedJob=jobs.find(job=>job.id===selected?.jobId);
 const reminder=selectedJob?.kind==='reminder'&&typeof selectedJob.action.payload.message==='string'?selectedJob.action.payload.message:null;
 const [editing,setEditing]=useState<string|null>(null);const [instruction,setInstruction]=useState('');
 const sorted=[...jobs].sort((a,b)=>(a.nextRunUtc||'9999').localeCompare(b.nextRunUtc||'9999'));
 const evidence=selected?.providerEvidence;
 const brief=typeof evidence?.brief==='string'?evidence.brief:typeof evidence?.attention==='string'?evidence.attention:null;
 const sources=Array.isArray(evidence?.sources)?evidence.sources as Record<string,unknown>[]:[];
 return <><section className="delegations-panel" aria-label="Delegated work">
  <div className="delegations-heading"><div><h3>Delegated work</h3><p><span className={'host-dot '+(online?'online':'')}/>{online?'Host available':'Host unavailable'}</p></div><ShieldCheck size={18}/></div>
  {!online&&<p className="delegation-warning">Local work cannot fire while this computer is asleep, shut down, or the Thaddeus host is closed.</p>}
  {!sorted.length&&<p className="muted">No scheduled work. Ask in Chat when you would like Thaddeus to follow through later.</p>}
  {sorted.map(job=>{
   const Icon=icon(job.kind);const history=[...occurrences].filter(item=>item.jobId===job.id).sort((a,b)=>b.sequence-a.sequence);const latest=history[0];
   const lastSuccess=history.find(item=>item.state==='succeeded')?.completedAt;
   const watchInstruction=job.kind==='inbox-watch'&&typeof job.action.payload.importanceInstruction==='string'?job.action.payload.importanceInstruction:'';
   const notificationFailed=!!latest&&['failed','unsupported'].includes(latest.notificationStatus);
   const jobLabel=notificationFailed?'Saved · Notification failed':labels[job.state]||job.state;
   return <article className={'delegation-card '+job.state} key={job.id}>
    <button type="button" className="delegation-card-main" onClick={()=>latest&&setSelectedId(latest.id)} disabled={!latest} aria-label={latest?`Inspect latest result for ${job.title}`:undefined}><span className="delegation-icon"><Icon size={16}/></span><div><strong>{job.title}</strong><time dateTime={job.nextRunUtc||latest?.dueUtc}>{job.schedule.kind==='once'&&latest&&!job.nextRunUtc?new Date(latest.dueUtc).toLocaleString():when(job)}</time><small className="delegation-state">{jobLabel}{latest&&!latest.readAt&&latest.state!=='working'?' · Unread':''}</small>{(latest?.summary||job.lastSummary)&&<small>{latest?.summary||job.lastSummary}</small>}</div></button>
    <div className="delegation-actions">
     {(job.kind==='brief'||job.kind==='inbox-watch')&&job.state==='scheduled'&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onPause(job)}><Pause size={14}/>Pause</button>}
     {(job.kind==='brief'||job.kind==='inbox-watch')&&job.state==='paused'&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onResume(job)}><Play size={14}/>Resume</button>}
     {job.kind==='inbox-watch'&&['scheduled','paused'].includes(job.state)&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>{setEditing(job.id);setInstruction(watchInstruction);}}>Edit importance</button>}
     {!terminal.has(job.state)&&<button type="button" className="delegation-cancel" disabled={!online||busy||job.cancellationRequested} onClick={()=>onCancel(job)}><X size={14}/>{job.cancellationRequested?'Cancelling':'Cancel'}</button>}
     {(reviewable.has(job.state)||notificationFailed)&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onReview(job,latest)}><MessageCircle size={14}/>Review in Chat</button>}
     {latest&&latest.state!=='working'&&!latest.readAt&&<button type="button" className="delegation-cancel" disabled={!online||busy} onClick={()=>onRead(latest)}>Mark result read</button>}
    </div>
    {job.kind==='inbox-watch'&&<p className="muted">{lastSuccess?`Last successful check ${new Date(lastSuccess).toLocaleString()}`:'No successful check yet'} · Mail remains read-only.</p>}
    {editing===job.id&&<form className="delegation-instruction" onSubmit={event=>{event.preventDefault();onEditInstruction(job,instruction);setEditing(null);}}><label>What deserves attention<textarea value={instruction} maxLength={500} onChange={event=>setInstruction(event.target.value)} /></label><div><button type="button" onClick={()=>setEditing(null)}>Close</button><button type="submit" disabled={busy||!online||!instruction.trim()}>Save instruction</button></div></form>}
    {job.state==='unknown'&&<p className="delegation-warning"><PauseCircle size={14}/>The outcome is uncertain. It will not be replayed automatically.</p>}
    {notificationFailed&&<p className="delegation-warning"><BellOff size={14}/><span>{latest.notificationError||'Windows could not display this notification.'} The unread result is still saved here.</span></p>}
   </article>;
  })}
 </section>{selected&&<Modal title={selectedJob?.kind==='reminder'?'Reminder':'Saved result'} onClose={()=>setSelectedId(null)} className="delegation-result-modal"><div className="delegation-result">
  <div className="delegation-result-heading"><span className="delegation-icon">{selectedJob?.kind==='reminder'?<Bell size={20}/>:<CalendarClock size={20}/>}</span><div><span className="delegation-result-state">{labels[selected.state]||selected.state}{!selected.readAt&&selected.state!=='working'?' · Unread':''}</span><h3>{selectedJob?.title||'Delegated work'}</h3><time dateTime={selected.dueUtc}>{new Date(selected.dueUtc).toLocaleString(undefined,{dateStyle:'medium',timeStyle:'short'})}</time></div></div>
  {reminder&&<p className="delegation-reminder-message">{reminder}</p>}
  {brief&&<section className="delegation-brief-output"><Markdown>{brief}</Markdown></section>}
  <p className="delegation-delivery-summary">{selected.summary}</p>
  {selected.notificationError&&<div className="delegation-warning"><BellOff size={16}/><div><strong>Notification issue</strong><p>{selected.notificationError}</p></div></div>}
  {selected.state!=='working'&&!selected.readAt&&<button type="button" className="delegation-result-read" disabled={!online||busy} onClick={()=>onRead(selected)}><ShieldCheck size={15}/>Mark result read</button>}
  {!!sources.length&&<section><h3>Sources</h3>{sources.map((source,index)=><p key={index}><strong>{String(source.kind||'Source')}</strong> · {String(source.connector||'Unknown')} · {String(source.status||'unknown')}{source.error?` · ${String(source.error)}`:''}</p>)}</section>}
  <details className="delegation-technical-receipt"><summary>Technical receipt</summary><dl><dt>Scheduled</dt><dd>{new Date(selected.dueUtc).toLocaleString()}</dd><dt>Completed</dt><dd>{selected.completedAt?new Date(selected.completedAt).toLocaleString():'Not completed'}</dd><dt>Dispatch</dt><dd>{selected.dispatchState}</dd><dt>Notification</dt><dd>{selected.notificationStatus}</dd>{selected.providerId&&<><dt>Provider receipt</dt><dd>{selected.providerId}</dd></>}</dl><pre>{JSON.stringify(selected,null,2)}</pre></details>
 </div></Modal>}</>;
}
