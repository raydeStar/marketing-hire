import {useCallback, useEffect, useRef, useState} from 'react';
import {ArrowRight, CheckCircle2, CircleAlert, LoaderCircle, MessageCircle, Plus, RefreshCw, Send, WifiOff} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import '../marketing.css';

export type TaskStatus='ready'|'working'|'needs_you'|'done';
export type TaskPriority='high'|'normal'|'low';
export type ActionState='agent_ready'|'user_waiting'|'blocked'|'none';
export type ConnectionStatus='connected'|'disconnected'|'auth_required'|'busy'|'failed';
export type RequestStatus='pending'|'succeeded'|'failed'|'unknown';

export type MarketingTask={
  id:string;title:string;status:TaskStatus;priority:TaskPriority;
  next_action:string;action_state:ActionState;blocker?:string|null;
  conversation_key:string;version:number;updated_at:number;
};
export type MarketingMessage={id:string;sessionKey:string;taskId?:string|null;role:'user'|'assistant';content:string;createdAt:number|string};
export type MarketingRequest={requestId:string;sessionKey:string;status:RequestStatus;error?:string|null};
export type MarketingProfile={id:string;display_name:string;product_summary:string;audience:string;voice:string;goals:string;guardrails:string;channels:string;version:number;updated_at:number};
export type MarketingDraft={id:number;channel:string;destination:string;content:string;rationale:string;rules_url:string;status:'pending'|'approved'|'rejected'|'posted'|'withdrawn';revision:number;digest:string;decided_by?:string|null;decided_at?:number|null};
export type OwnerDecision={requestId:string;draftId:number;decision:'approved'|'rejected';revision:number;digest:string;status:'pending_sync'|'confirmed';createdAt:string};
export type MarketingEvidence={id:string;task_id:string;url:string;title:string;note:string;query:string;source:string;created_at:number};
export type MarketingState={
  employee:{name:string;model:string;sessionKey:string};
  connection:{status:ConnectionStatus;detail?:string|null};
  taskStoreAvailable:boolean;canConfigure:boolean;
  profile:MarketingProfile;drafts:MarketingDraft[];evidence:MarketingEvidence[];ownerDecisions:OwnerDecision[];
  tasks:MarketingTask[];messages:MarketingMessage[];requests:MarketingRequest[];
};

export const statusLabel:Record<TaskStatus,string>={ready:'Ready',working:'Working',needs_you:'Needs you',done:'Done'};
export const priorityLabel:Record<TaskPriority,string>={high:'High',normal:'Normal',low:'Low'};
export const actionLabel:Record<ActionState,string>={agent_ready:'Employee can act',user_waiting:'Waiting on you',blocked:'Blocked',none:'No next action'};
export const statusOrder:TaskStatus[]=['ready','working','needs_you','done'];
export const priorityOrder:Record<TaskPriority,number>={high:0,normal:1,low:2};

export function requestId(){return crypto.randomUUID();}
export function publicLink(value:string){try{const parsed=new URL(value);return parsed.protocol==='https:'?parsed.toString():null;}catch{return null;}}
export function readableTime(value:number|string){
  const date=new Date(typeof value==='number'&&value<100000000000?value*1000:value);
  return Number.isNaN(date.getTime())?'Recorded':date.toLocaleString(undefined,{month:'short',day:'numeric',hour:'numeric',minute:'2-digit'});
}

export function MarketingDiscussion({state,task,canWrite,onRefresh}:{state:MarketingState;task?:MarketingTask;canWrite:boolean;onRefresh:()=>Promise<void>}){
  const [draft,setDraft]=useState('');
  const [sending,setSending]=useState(false);
  const [notice,setNotice]=useState('');
  const [attempt,setAttempt]=useState<string|null>(null);
  const [reviewedUnknown,setReviewedUnknown]=useState<string|null>(null);
  const lastAttempt=useRef<{id:string;content:string;sessionKey:string}|null>(null);
  const scroller=useRef<HTMLDivElement>(null);
  const sessionKey=task?.conversation_key||state.employee.sessionKey;
  const messages=state.messages.filter(message=>message.sessionKey===sessionKey&&(task?message.taskId===task.id:!message.taskId));
  const latest=[...state.requests].reverse().find(item=>item.sessionKey===sessionKey);
  const unresolved=latest&&(latest.status==='pending'||(latest.status==='unknown'&&reviewedUnknown!==latest.requestId))?latest:undefined;
  const attempted=attempt?state.requests.find(item=>item.requestId===attempt):undefined;
  useEffect(()=>{try{setDraft(localStorage.getItem('employee-draft:'+sessionKey)||'');}catch{setDraft('');}setNotice('');setAttempt(null);setReviewedUnknown(null);lastAttempt.current=null;},[sessionKey]);
  useEffect(()=>{if(scroller.current)scroller.current.scrollTop=scroller.current.scrollHeight;},[messages.length,sessionKey]);

  async function send(){
    const content=draft.trim();
    if(!content||sending||!canWrite||unresolved)return;
    const previous=lastAttempt.current;
    const id=previous?.content===content&&previous.sessionKey===sessionKey&&attempted?.status!=='failed'&&attempted?.status!=='unknown'?previous.id:requestId();
    lastAttempt.current={id,content,sessionKey};setAttempt(id);setSending(true);setNotice('Sending to OpenClaw…');
    try{
      await api('/marketing/chat',{requestId:id,content,...(task?{taskId:task.id}:{})});
      setDraft('');try{localStorage.removeItem('employee-draft:'+sessionKey);}catch{}lastAttempt.current=null;setNotice('Reply confirmed. Refreshing the conversation…');
    }catch(error){
      // A lost response may hide a completed turn. Reconcile the saved request before another send.
      setNotice(`The turn could not be confirmed: ${(error as Error).message} Refresh the record before sending again.`);
    }finally{
      try{await onRefresh();}catch{/* The workspace keeps its explicit stale-state warning. */}
      setSending(false);
    }
  }

  return <section className="marketing-discussion" aria-label={task?`Discussion for ${task.title}`:'Marketing employee conversation'}>
    <div className="marketing-discussion-heading"><div><p className="eyebrow">{task?'TASK DISCUSSION':'DIRECT CONVERSATION'}</p><h2>{task?task.title:'Talk with your marketing employee'}</h2></div><span className="marketing-session">{task?'Task context':'Main context'}</span></div>
    <div className="marketing-messages" ref={scroller} aria-live="polite">
      {messages.length?messages.map(message=>{const record=message.role==='user'?state.requests.find(item=>item.requestId===message.id.replace(/:user$/,'')):undefined;return <article className={'marketing-message '+message.role} key={message.id}>
        <div className="marketing-message-meta"><strong>{message.role==='user'?'You':state.employee.name||'OpenClaw employee'}</strong><time>{readableTime(message.createdAt)}</time>{record&&record.status!=='succeeded'&&<span className={'marketing-message-status '+record.status}>{record.status}</span>}</div>
        <div className="marketing-message-content"><Markdown>{message.content}</Markdown></div>
      </article>;}):<div className="marketing-empty-discussion"><MessageCircle size={24}/><p>{task?'No discussion on this task yet. Ask for an update or give a specific direction.':'No marketing conversation has been recorded yet. Ask your employee what to tackle first.'}</p></div>}
    </div>
    {attempted?.status==='failed'&&<p className="marketing-inline-alert" role="alert">That turn failed. {attempted.error||'The employee did not provide a confirmed reply.'}</p>}
    {unresolved&&<div className="marketing-inline-alert" role="status"><span>A prior turn is {unresolved.status}. {unresolved.status==='unknown'?'Review the recorded conversation before starting a new message.':'Wait for its confirmed outcome before sending again.'}</span>{unresolved.status==='unknown'&&<button type="button" onClick={()=>{setReviewedUnknown(unresolved.requestId);lastAttempt.current=null;setNotice('The earlier outcome remains unknown. A new message will be a separate turn.');}}>I reviewed it</button>}</div>}
    {notice&&<p className="marketing-send-notice" role="status">{notice}</p>}
    <form className="marketing-composer" onSubmit={event=>{event.preventDefault();void send();}}>
      <label className="marketing-sr-only" htmlFor={task?'marketing-task-message':'marketing-main-message'}>Message to marketing employee</label>
      <textarea id={task?'marketing-task-message':'marketing-main-message'} value={draft} onChange={event=>{setDraft(event.target.value);try{localStorage.setItem('employee-draft:'+sessionKey,event.target.value);}catch{}}} placeholder={task?'Discuss this task with your employee…':'Ask your marketing employee…'} rows={3} disabled={!canWrite||sending||!!unresolved}/>
      <div><small>Your messages and replies are saved.</small><button className="primary" type="submit" disabled={!draft.trim()||!canWrite||sending||!!unresolved}>{sending?<LoaderCircle size={16} className="marketing-spin"/>:<Send size={16}/>} Send</button></div>
    </form>
  </section>;
}

export function MarketingBrief({profile,canEdit,onRefresh,onError}:{profile:MarketingProfile;canEdit:boolean;onRefresh:()=>Promise<void>;onError:(message:string)=>void}){
  const [editing,setEditing]=useState(false);
  const [fields,setFields]=useState(profile);
  const [saving,setSaving]=useState(false);
  const attempt=useRef<{signature:string;id:string}|null>(null);
  const editable=(['display_name','product_summary','audience','goals','voice','channels','guardrails'] as const);
  async function save(event:React.FormEvent){
    event.preventDefault();if(saving||!canEdit)return;
    const changes=Object.fromEntries(editable.map(key=>[key,fields[key].trim()]));
    const payload={...changes,version:fields.version};
    const signature=JSON.stringify(payload);
    const id=attempt.current?.signature===signature?attempt.current.id:requestId();
    attempt.current={signature,id};setSaving(true);onError('');
    try{
      await api('/marketing/profile',{...payload,requestId:id},'PUT');
      attempt.current=null;setEditing(false);await onRefresh();
    }catch(error){onError((error as Error).message);await onRefresh().catch(()=>{});}
    finally{setSaving(false);}
  }
  function change(key:keyof MarketingProfile,value:string){setFields(current=>({...current,[key]:value}));}
  return <section className="marketing-brief" aria-label="Marketing brief">
    <div className="marketing-section-heading"><div><p className="eyebrow">OWNER-CONFIGURED CONTEXT</p><h3>Marketing brief</h3></div>{!editing&&canEdit&&<button type="button" onClick={()=>{setFields(profile);setEditing(true);}}>Edit brief</button>}</div>
    {editing?<form className="marketing-brief-form" onSubmit={event=>void save(event)}>
      <label>Agent name<input value={fields.display_name} maxLength={80} required onChange={event=>change('display_name',event.target.value)}/></label>
      <label>What we are marketing<textarea value={fields.product_summary} maxLength={1200} onChange={event=>change('product_summary',event.target.value)}/></label>
      <label>Audience<textarea value={fields.audience} maxLength={800} onChange={event=>change('audience',event.target.value)} placeholder="Who should this product help?"/></label>
      <label>Goals<textarea value={fields.goals} maxLength={800} onChange={event=>change('goals',event.target.value)} placeholder="What outcomes matter now?"/></label>
      <label>Voice<input value={fields.voice} maxLength={400} onChange={event=>change('voice',event.target.value)} placeholder="How should the agent sound?"/></label>
      <label>Research channels<input value={fields.channels} maxLength={400} onChange={event=>change('channels',event.target.value)} placeholder="Public communities and sources"/></label>
      <label>Guardrails<textarea value={fields.guardrails} maxLength={1000} onChange={event=>change('guardrails',event.target.value)}/></label>
      <div className="marketing-brief-actions"><button type="button" onClick={()=>setEditing(false)} disabled={saving}>Cancel</button><button className="primary" disabled={saving||!fields.display_name.trim()}>{saving?'Saving…':'Save brief'}</button></div>
    </form>:<div className="marketing-brief-summary"><strong>{profile.display_name}</strong><p>{profile.product_summary||'Add a product summary before asking for targeted research.'}</p><small>Audience: {profile.audience||'not set'} · Goals: {profile.goals||'not set'} · version {profile.version}</small><small>Applied to new agent turns. Earlier conversations remain as recorded.</small></div>}
  </section>;
}

export function MarketingDrafts({drafts,decisions,canDecide,onRefresh,onError}:{drafts:MarketingDraft[];decisions:OwnerDecision[];canDecide:boolean;onRefresh:()=>Promise<void>;onError:(message:string)=>void}){
  const [working,setWorking]=useState<number|null>(null);
  const attempt=useRef<{signature:string;id:string}|null>(null);
  const pending=drafts.filter(draft=>draft.status==='pending');
  const recent=drafts.filter(draft=>draft.status!=='pending').slice(0,3);
  const receipt=(draft:MarketingDraft)=>decisions.find(item=>item.draftId===draft.id&&item.revision===draft.revision&&item.digest===draft.digest);
  const verified=(draft:MarketingDraft)=>{
    const item=receipt(draft);
    return item?.status==='confirmed'&&(item.decision===draft.status||(item.decision==='approved'&&draft.status==='posted'));
  };
  async function decide(draft:MarketingDraft,decision:'approved'|'rejected',retryId?:string){
    if(!canDecide||working!==null)return;
    const signature=`${draft.id}:${draft.revision}:${draft.digest}:${decision}`;
    const id=retryId||(attempt.current?.signature===signature?attempt.current.id:requestId());
    attempt.current={signature,id};setWorking(draft.id);onError('');
    try{
      await api(`/marketing/drafts/${draft.id}/decision`,{requestId:id,decision,revision:draft.revision,digest:draft.digest});
      attempt.current=null;await onRefresh();
    }catch(error){onError((error as Error).message);await onRefresh().catch(()=>{});}
    finally{setWorking(null);}
  }
  return <section className="marketing-drafts" aria-label="Draft approvals">
    <div className="marketing-section-heading"><div><p className="eyebrow">HUMAN DECISIONS</p><h3>Draft approvals <span>{pending.length}</span></h3></div></div>
    <p className="marketing-section-note">Review the exact text and destination. Approval records a decision; it does not post or contact anyone.</p>
    {pending.length?pending.map(draft=><article className="marketing-draft" key={draft.id}>
      <div className="marketing-draft-meta"><strong>Draft #{draft.id} · revision {draft.revision}</strong><span>{draft.channel}</span></div>
      {publicLink(draft.destination)?<a href={publicLink(draft.destination)!} target="_blank" rel="noopener noreferrer">{draft.destination}</a>:<span>{draft.destination}</span>}
      <div className="marketing-draft-content">{draft.content}</div>
      <p><strong>Why:</strong> {draft.rationale}</p><p><strong>Rules:</strong> {draft.rules_url}</p>
      <small>Content digest {draft.digest.slice(0,12)}…</small>
      {receipt(draft)?.status==='pending_sync'?<div className="marketing-draft-actions"><span>Decision needs reconciliation.</span>{canDecide&&<button type="button" disabled={working!==null} onClick={()=>void decide(draft,receipt(draft)!.decision,receipt(draft)!.requestId)}>Retry recorded decision</button>}</div>:canDecide&&<div className="marketing-draft-actions"><button type="button" disabled={working!==null} onClick={()=>void decide(draft,'rejected')}>Reject</button><button className="primary" type="button" disabled={working!==null} onClick={()=>void decide(draft,'approved')}>{working===draft.id?'Recording…':'Approve exact draft'}</button></div>}
    </article>):<p className="marketing-empty-note">No draft is waiting for approval.</p>}
    {recent.length>0&&<details><summary>Recent decisions</summary>{recent.map(draft=><p key={draft.id}>Draft #{draft.id} · {draft.status} · revision {draft.revision} · {verified(draft)?'owner decision verified':receipt(draft)?.status==='pending_sync'?'decision reconciliation needed':'unverified ledger status'} {receipt(draft)?.status==='pending_sync'&&canDecide&&<button type="button" disabled={working!==null} onClick={()=>void decide(draft,receipt(draft)!.decision,receipt(draft)!.requestId)}>Retry recorded decision</button>}</p>)}</details>}
  </section>;
}

export function MarketingEvidencePanel({task,evidence,canAdd,onRefresh,onError}:{task:MarketingTask;evidence:MarketingEvidence[];canAdd:boolean;onRefresh:()=>Promise<void>;onError:(message:string)=>void}){
  const [adding,setAdding]=useState(false);
  const [saving,setSaving]=useState(false);
  const [url,setUrl]=useState('');
  const [title,setTitle]=useState('');
  const [note,setNote]=useState('');
  const [source,setSource]=useState('');
  const [query,setQuery]=useState('');
  const attempt=useRef<{signature:string;id:string}|null>(null);
  const items=evidence.filter(item=>item.task_id===task.id);
  async function save(event:React.FormEvent){
    event.preventDefault();if(saving||!canAdd)return;
    const fields={url:url.trim(),title:title.trim(),note:note.trim(),source:source.trim(),query:query.trim()};
    const signature=task.id+':'+JSON.stringify(fields);
    const id=attempt.current?.signature===signature?attempt.current.id:requestId();
    attempt.current={signature,id};setSaving(true);onError('');
    try{
      await api(`/marketing/tasks/${encodeURIComponent(task.id)}/evidence`,{...fields,requestId:id});
      attempt.current=null;setAdding(false);setUrl('');setTitle('');setNote('');setSource('');setQuery('');await onRefresh();
    }catch(error){onError((error as Error).message);await onRefresh().catch(()=>{});}
    finally{setSaving(false);}
  }
  return <section className="marketing-task-evidence" aria-label={`Source evidence for ${task.title}`}>
    <div className="marketing-evidence-heading"><h3>Source links</h3>{canAdd&&<button type="button" onClick={()=>setAdding(value=>!value)} disabled={saving}>{adding?'Cancel':'Add source'}</button>}</div>
    <p className="marketing-evidence-note">A link and note record what was found; they do not prove a market trend or validate the page automatically.</p>
    {adding&&<form className="marketing-evidence-form" onSubmit={event=>void save(event)}>
      <label>HTTPS source URL<input type="url" required pattern="https://.*" maxLength={500} value={url} onChange={event=>setUrl(event.target.value)} placeholder="https://…"/></label>
      <label>Source name<input required maxLength={60} value={source} onChange={event=>setSource(event.target.value)} placeholder="Hacker News, Reddit, article…"/></label>
      <label>Title<input required maxLength={200} value={title} onChange={event=>setTitle(event.target.value)}/></label>
      <label>What this source supports<textarea required maxLength={300} value={note} onChange={event=>setNote(event.target.value)} placeholder="One concrete observation; distinguish inference from what the source says."/></label>
      <label>Search query, if used<input maxLength={200} value={query} onChange={event=>setQuery(event.target.value)}/></label>
      <button className="primary" disabled={saving||!url.trim()||!title.trim()||!note.trim()||!source.trim()}>{saving?'Saving…':'Attach source'}</button>
    </form>}
    {items.map(item=><article key={item.id}><strong>{item.title}</strong>{publicLink(item.url)?<a href={publicLink(item.url)!} target="_blank" rel="noopener noreferrer">{item.url}</a>:<span>{item.url}</span>}<p>{item.note}</p><small>{item.source}{item.query?` · query: ${item.query}`:''} · {readableTime(item.created_at)}</small></article>)}
    {items.length===0&&<p className="marketing-empty-note">No source is attached to this task yet.</p>}
  </section>;
}
