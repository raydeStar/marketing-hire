import {useCallback, useEffect, useRef, useState} from 'react';
import {ArrowRight, CheckCircle2, CircleAlert, LoaderCircle, MessageCircle, Plus, RefreshCw, Send, WifiOff} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import '../marketing.css';

type TaskStatus='ready'|'working'|'needs_you'|'done';
type TaskPriority='high'|'normal'|'low';
type ActionState='agent_ready'|'user_waiting'|'blocked'|'none';
type ConnectionStatus='connected'|'disconnected'|'auth_required'|'busy'|'failed';
type RequestStatus='pending'|'succeeded'|'failed'|'unknown';

type MarketingTask={
  id:string;title:string;status:TaskStatus;priority:TaskPriority;
  next_action:string;action_state:ActionState;blocker?:string|null;
  conversation_key:string;version:number;updated_at:number;
};
type MarketingMessage={id:string;sessionKey:string;taskId?:string|null;role:'user'|'assistant';content:string;createdAt:number|string};
type MarketingRequest={requestId:string;sessionKey:string;status:RequestStatus;error?:string|null};
type MarketingState={
  employee:{name:string;model:string;sessionKey:string};
  connection:{status:ConnectionStatus;detail?:string|null};
  taskStoreAvailable:boolean;
  tasks:MarketingTask[];messages:MarketingMessage[];requests:MarketingRequest[];
};

const statusLabel:Record<TaskStatus,string>={ready:'Ready',working:'Working',needs_you:'Needs you',done:'Done'};
const priorityLabel:Record<TaskPriority,string>={high:'High',normal:'Normal',low:'Low'};
const actionLabel:Record<ActionState,string>={agent_ready:'Employee can act',user_waiting:'Waiting on you',blocked:'Blocked',none:'No next action'};
const statusOrder:TaskStatus[]=['ready','working','needs_you','done'];
const priorityOrder:Record<TaskPriority,number>={high:0,normal:1,low:2};

function requestId(){return crypto.randomUUID();}
function readableTime(value:number|string){
  const date=new Date(typeof value==='number'&&value<100000000000?value*1000:value);
  return Number.isNaN(date.getTime())?'Recorded':date.toLocaleString(undefined,{month:'short',day:'numeric',hour:'numeric',minute:'2-digit'});
}

function MarketingDiscussion({state,task,canWrite,onRefresh}:{state:MarketingState;task?:MarketingTask;canWrite:boolean;onRefresh:()=>Promise<void>}){
  const [draft,setDraft]=useState('');
  const [sending,setSending]=useState(false);
  const [notice,setNotice]=useState('');
  const [attempt,setAttempt]=useState<string|null>(null);
  const [reviewedUnknown,setReviewedUnknown]=useState<string|null>(null);
  const lastAttempt=useRef<{id:string;content:string;sessionKey:string}|null>(null);
  const bottom=useRef<HTMLDivElement>(null);
  const sessionKey=task?.conversation_key||state.employee.sessionKey;
  const messages=state.messages.filter(message=>message.sessionKey===sessionKey&&(task?message.taskId===task.id:!message.taskId));
  const latest=[...state.requests].reverse().find(item=>item.sessionKey===sessionKey);
  const unresolved=latest&&(latest.status==='pending'||(latest.status==='unknown'&&reviewedUnknown!==latest.requestId))?latest:undefined;
  const attempted=attempt?state.requests.find(item=>item.requestId===attempt):undefined;
  useEffect(()=>{setDraft('');setNotice('');setAttempt(null);setReviewedUnknown(null);lastAttempt.current=null;},[sessionKey]);
  useEffect(()=>{bottom.current?.scrollIntoView({block:'nearest'});},[messages.length,sessionKey]);

  async function send(){
    const content=draft.trim();
    if(!content||sending||!canWrite||unresolved)return;
    const previous=lastAttempt.current;
    const id=previous?.content===content&&previous.sessionKey===sessionKey&&attempted?.status!=='failed'&&attempted?.status!=='unknown'?previous.id:requestId();
    lastAttempt.current={id,content,sessionKey};setAttempt(id);setSending(true);setNotice('Sending to OpenClaw…');
    try{
      await api('/marketing/chat',{requestId:id,content,...(task?{taskId:task.id}:{})});
      setDraft('');lastAttempt.current=null;setNotice('Reply confirmed. Refreshing the conversation…');
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
    <div className="marketing-messages" aria-live="polite">
      {messages.length?messages.map(message=>{const record=message.role==='user'?state.requests.find(item=>item.requestId===message.id.replace(/:user$/,'')):undefined;return <article className={'marketing-message '+message.role} key={message.id}>
        <div className="marketing-message-meta"><strong>{message.role==='user'?'You':state.employee.name||'OpenClaw employee'}</strong><time>{readableTime(message.createdAt)}</time>{record&&record.status!=='succeeded'&&<span className={'marketing-message-status '+record.status}>{record.status}</span>}</div>
        <div className="marketing-message-content"><Markdown>{message.content}</Markdown></div>
      </article>;}):<div className="marketing-empty-discussion"><MessageCircle size={24}/><p>{task?'No discussion on this task yet. Ask for an update or give a specific direction.':'No marketing conversation has been recorded yet. Ask your employee what to tackle first.'}</p></div>}
      <div ref={bottom}/>
    </div>
    {attempted?.status==='failed'&&<p className="marketing-inline-alert" role="alert">That turn failed. {attempted.error||'The employee did not provide a confirmed reply.'}</p>}
    {unresolved&&<div className="marketing-inline-alert" role="status"><span>A prior turn is {unresolved.status}. {unresolved.status==='unknown'?'Review the recorded conversation before starting a new message.':'Wait for its confirmed outcome before sending again.'}</span>{unresolved.status==='unknown'&&<button type="button" onClick={()=>{setReviewedUnknown(unresolved.requestId);lastAttempt.current=null;setNotice('The earlier outcome remains unknown. A new message will be a separate turn.');}}>I reviewed it</button>}</div>}
    {notice&&<p className="marketing-send-notice" role="status">{notice}</p>}
    <form className="marketing-composer" onSubmit={event=>{event.preventDefault();void send();}}>
      <label className="marketing-sr-only" htmlFor={task?'marketing-task-message':'marketing-main-message'}>Message to marketing employee</label>
      <textarea id={task?'marketing-task-message':'marketing-main-message'} value={draft} onChange={event=>setDraft(event.target.value)} placeholder={task?'Discuss this task with your employee…':'Ask your marketing employee…'} rows={3} disabled={!canWrite||sending||!!unresolved}/>
      <div><small>Sending invokes one real OpenClaw turn.</small><button className="primary" type="submit" disabled={!draft.trim()||!canWrite||sending||!!unresolved}>{sending?<LoaderCircle size={16} className="marketing-spin"/>:<Send size={16}/>} Send</button></div>
    </form>
  </section>;
}

export function MarketingWorkspace({hostOnline}:{hostOnline:boolean}){
  const [view,setView]=useState<'chat'|'work'>('chat');
  const [state,setState]=useState<MarketingState|null>(null);
  const [loading,setLoading]=useState(true);
  const [readError,setReadError]=useState('');
  const [actionError,setActionError]=useState('');
  const [selectedId,setSelectedId]=useState<string|null>(null);
  const [creating,setCreating]=useState(false);
  const [newTitle,setNewTitle]=useState('');
  const [newPriority,setNewPriority]=useState<TaskPriority>('normal');
  const [newAction,setNewAction]=useState('');
  const [working,setWorking]=useState(false);
  const sequence=useRef(0);
  const createAttempt=useRef<{signature:string;id:string}|null>(null);
  const updateAttempt=useRef<{signature:string;id:string}|null>(null);

  const refresh=useCallback(async()=>{
    const current=++sequence.current;
    try{
      const snapshot=await api<MarketingState>('/marketing/state');
      if(current!==sequence.current)return;
      if(!snapshot||!Array.isArray(snapshot.tasks)||!Array.isArray(snapshot.messages)||!Array.isArray(snapshot.requests))throw new Error('The marketing state response is incomplete.');
      setState(snapshot);setReadError('');
    }catch(error){if(current===sequence.current)setReadError((error as Error).message);throw error;}
    finally{if(current===sequence.current)setLoading(false);}
  },[]);

  useEffect(()=>{
    void refresh().catch(()=>{});
    const timer=window.setInterval(()=>{if(document.visibilityState==='visible')void refresh().catch(()=>{});},8000);
    const visible=()=>{if(document.visibilityState==='visible')void refresh().catch(()=>{});};
    document.addEventListener('visibilitychange',visible);
    return()=>{sequence.current++;window.clearInterval(timer);document.removeEventListener('visibilitychange',visible);};
  },[refresh]);

  const selected=state?.tasks.find(task=>task.id===selectedId);
  const connection=state?.connection.status||'disconnected';
  const canChatWrite=hostOnline&&!readError&&connection==='connected';
  const canTaskWrite=hostOnline&&!readError&&state?.taskStoreAvailable===true;
  const tasks=[...(state?.tasks||[])].sort((a,b)=>priorityOrder[a.priority]-priorityOrder[b.priority]||b.updated_at-a.updated_at);
  const nextSteps=tasks.filter(task=>task.status!=='done'&&task.next_action&&task.action_state!=='none').sort((a,b)=>(a.action_state==='user_waiting'?0:1)-(b.action_state==='user_waiting'?0:1));

  async function createTask(event:React.FormEvent){
    event.preventDefault();if(!newTitle.trim()||!canTaskWrite||working)return;
    setWorking(true);setActionError('');
    try{
      const fields={title:newTitle.trim(),status:'ready',priority:newPriority,next_action:newAction.trim(),action_state:newAction.trim()?'agent_ready':'none'};
      const signature=JSON.stringify(fields);
      const id=createAttempt.current?.signature===signature?createAttempt.current.id:requestId();
      createAttempt.current={signature,id};
      const created=await api<MarketingTask>('/marketing/tasks',{...fields,requestId:id});
      createAttempt.current=null;setNewTitle('');setNewAction('');setCreating(false);await refresh();setSelectedId(created.id);
    }catch(error){setActionError((error as Error).message);await refresh().catch(()=>{});}
    finally{setWorking(false);}
  }

  async function updateTask(task:MarketingTask,changes:Partial<Pick<MarketingTask,'status'|'priority'>>){
    if(!canTaskWrite||working)return;
    setWorking(true);setActionError('');
    try{
      const fields={...changes,version:task.version};
      const signature=task.id+':'+JSON.stringify(fields);
      const id=updateAttempt.current?.signature===signature?updateAttempt.current.id:requestId();
      updateAttempt.current={signature,id};
      await api(`/marketing/tasks/${encodeURIComponent(task.id)}`,{...fields,requestId:id},'PUT');
      updateAttempt.current=null;await refresh();
    }
    catch(error){setActionError((error as Error).message);await refresh().catch(()=>{});}
    finally{setWorking(false);}
  }

  return <section className="marketing-workspace" aria-label="Marketing employee workspace">
    <div className="marketing-hero">
      <div><p className="eyebrow">YOUR MARKETING EMPLOYEE</p><h1>{state?.employee.name||'Marketing'}</h1><p>OpenClaw main agent · tasks and conversation from the live business record{state?.employee.model?` · configured model: ${state.employee.model}`:''}</p></div>
      <div className="marketing-hero-actions"><span className={'marketing-connection '+connection} role="status">{connection==='connected'?<CheckCircle2 size={15}/>:connection==='busy'?<LoaderCircle size={15} className="marketing-spin"/>:<WifiOff size={15}/>} {connection==='connected'?'Gateway connected':connection==='auth_required'?'Authentication needed':connection==='busy'?'Employee busy':connection==='failed'?'Connection failed':'Disconnected'}</span><button type="button" onClick={()=>void refresh().catch(()=>{})} disabled={loading} aria-label="Refresh marketing state"><RefreshCw size={16}/></button></div>
    </div>
    {state?.connection.detail&&connection!=='connected'&&<p className="marketing-connection-detail" role="status">{state.connection.detail}</p>}
    {!hostOnline&&<p className="marketing-inline-alert" role="status">The Thaddeus host is disconnected. Marketing changes are paused.</p>}
    {readError&&<div className="marketing-inline-alert" role="alert"><CircleAlert size={16}/><span>Marketing state could not be refreshed: {readError}. {state?'The record below may be stale.':'No live record is available.'}</span></div>}
    {actionError&&<div className="marketing-inline-alert" role="alert"><CircleAlert size={16}/><span>{actionError}</span><button type="button" onClick={()=>setActionError('')} aria-label="Dismiss marketing error">×</button></div>}
    <nav className="marketing-view-switch" aria-label="Marketing views"><button type="button" aria-current={view==='chat'?'page':undefined} onClick={()=>setView('chat')}><MessageCircle size={17}/> Chat</button><button type="button" aria-current={view==='work'?'page':undefined} onClick={()=>setView('work')}><CheckCircle2 size={17}/> Work <span>{tasks.filter(task=>task.status!=='done').length}</span></button></nav>
    {loading&&!state?<div className="marketing-loading" role="status"><LoaderCircle size={22} className="marketing-spin"/> Reading marketing state…</div>:!state?<div className="marketing-blank"><p>The employee workspace is unavailable. Refresh after the host connection is restored.</p></div>:view==='chat'?<div className="marketing-chat-layout"><MarketingDiscussion state={state} canWrite={canChatWrite} onRefresh={refresh}/><aside className="marketing-next-steps" aria-label="Next steps"><div className="marketing-panel-heading"><p className="eyebrow">FROM THE TASK BOARD</p><h2>At a glance</h2></div><div className="marketing-board-summary" aria-label="Task board summary"><div><strong>{tasks.filter(task=>task.status==='ready').length}</strong><span>Ready</span></div><div><strong>{tasks.filter(task=>task.status==='working').length}</strong><span>Working</span></div><div><strong>{tasks.filter(task=>task.status==='needs_you').length}</strong><span>Needs you</span></div><div><strong>{tasks.filter(task=>task.priority==='high'&&task.status!=='done').length}</strong><span>High priority</span></div></div><h3>Next steps</h3>{nextSteps.length?nextSteps.slice(0,8).map(task=><button type="button" key={task.id} onClick={()=>{setSelectedId(task.id);setView('work');}}><span className={'marketing-action-dot '+task.action_state}/><span><strong>{task.title}</strong><small>{actionLabel[task.action_state]} · {task.next_action}</small></span><ArrowRight size={15}/></button>):<p className="marketing-empty-note">No next action is recorded yet. Add one to a task when the work becomes clear.</p>}<small>Queue order reflects task priority and action state. No work starts from opening this list.</small></aside></div>:<div className="marketing-work-layout">
      <div className="marketing-board-area"><div className="marketing-board-heading"><div><p className="eyebrow">SHARED TASK RECORD</p><h2>Work board</h2></div><button className="primary" type="button" onClick={()=>setCreating(value=>!value)} disabled={!canTaskWrite||working}><Plus size={16}/> New task</button></div>
        {creating&&<form className="marketing-create" onSubmit={event=>void createTask(event)}><label>Task title<input value={newTitle} onChange={event=>setNewTitle(event.target.value)} required maxLength={160}/></label><label>Priority<select value={newPriority} onChange={event=>setNewPriority(event.target.value as TaskPriority)}><option value="high">High</option><option value="normal">Normal</option><option value="low">Low</option></select></label><label className="marketing-create-action">Next action<input value={newAction} onChange={event=>setNewAction(event.target.value)} placeholder="What should happen next?"/></label><div><button type="button" onClick={()=>setCreating(false)}>Cancel</button><button className="primary" disabled={!newTitle.trim()||!canTaskWrite||working}>{working?'Saving…':'Create task'}</button></div></form>}
        <div className="marketing-board">{statusOrder.map(status=><section className="marketing-column" key={status} aria-label={`${statusLabel[status]} tasks`}><h3>{statusLabel[status]} <span>{tasks.filter(task=>task.status===status).length}</span></h3><div>{tasks.filter(task=>task.status===status).map(task=><button type="button" className={'marketing-task-card'+(selectedId===task.id?' selected':'')} key={task.id} onClick={()=>setSelectedId(task.id)} aria-current={selectedId===task.id?'true':undefined}><span className={'marketing-priority '+task.priority}>{priorityLabel[task.priority]}</span><strong>{task.title}</strong><small>{task.next_action||'No next action recorded'}</small>{task.blocker&&<span className="marketing-task-blocker">Blocked: {task.blocker}</span>}</button>)}{!tasks.some(task=>task.status===status)&&<p>No tasks</p>}</div></section>)}</div>
      </div>
      <aside className="marketing-task-detail" aria-label="Task details">{selected?<><div className="marketing-task-detail-heading"><p className="eyebrow">TASK DETAIL</p><h2>{selected.title}</h2><small>Updated {readableTime(selected.updated_at)} · version {selected.version}</small></div><div className="marketing-task-controls"><label>Status<select value={selected.status} disabled={!canTaskWrite||working} onChange={event=>void updateTask(selected,{status:event.target.value as TaskStatus})}>{statusOrder.map(status=><option key={status} value={status}>{statusLabel[status]}</option>)}</select></label><label>Priority<select value={selected.priority} disabled={!canTaskWrite||working} onChange={event=>void updateTask(selected,{priority:event.target.value as TaskPriority})}>{(['high','normal','low'] as TaskPriority[]).map(priority=><option key={priority} value={priority}>{priorityLabel[priority]}</option>)}</select></label></div><div className="marketing-task-next"><span>Next action</span><p>{selected.next_action||'None recorded'}</p><small>{actionLabel[selected.action_state]}</small>{selected.blocker&&<p className="marketing-task-blocker">Blocked: {selected.blocker}</p>}</div><MarketingDiscussion state={state} task={selected} canWrite={canChatWrite} onRefresh={refresh}/></>:<div className="marketing-task-prompt"><CheckCircle2 size={25}/><h2>Select a task</h2><p>Open its record, change its priority or status, and discuss that task with your employee.</p></div>}</aside>
    </div>}
  </section>;
}
