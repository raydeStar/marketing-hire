import {useState} from 'react';
import {CircleAlert} from 'lucide-react';
import {api} from '../api';
import {MarketingEvidencePanel,actionLabel,priorityLabel,readableTime,statusLabel,statusOrder,type MarketingState,type MarketingTask,type TaskPriority,type TaskStatus} from '../components/MarketingPanels';
import {Conversation} from './ChatView';
import {Dialog,useAttempt} from './shared';

/** A task opened in the work window: its state, next step, sources and its own conversation. */
export function TaskDetail({task,state,canWrite,canChat,pastMeeting=false,onRefresh}:{task:MarketingTask;state:MarketingState;canWrite:boolean;canChat:boolean;pastMeeting?:boolean;onRefresh:()=>Promise<void>}){
  const [tab,setTab]=useState<'details'|'sources'|'conversation'>('details'),[working,setWorking]=useState(false),[error,setError]=useState('');
  const attempt=useAttempt();
  const name=state.employee.name||'Marketing';
  const editable=canWrite&&!pastMeeting;
  async function update(changes:Partial<Pick<MarketingTask,'status'|'priority'>>){
    if(!editable||working)return;setWorking(true);setError('');
    const fields={...changes,version:task.version};
    try{await api('/marketing/tasks/'+task.id,{...fields,requestId:attempt.id(task.id+JSON.stringify(fields))},'PUT');attempt.done();await onRefresh();}
    catch(cause){setError((cause as Error).message);}finally{setWorking(false);}
  }
  const sources=(state.evidence||[]).filter(item=>item.task_id===task.id).length;
  return <div className="fe-task-detail">
    <dl className="fe-facts">
      <div><dt>Status</dt><dd><select aria-label="Status" value={task.status} disabled={!editable||working} onChange={event=>void update({status:event.target.value as TaskStatus})}>{statusOrder.map(status=><option key={status} value={status}>{statusLabel[status]}</option>)}</select></dd></div>
      <div><dt>Priority</dt><dd><select aria-label="Priority" value={task.priority} disabled={!editable||working} onChange={event=>void update({priority:event.target.value as TaskPriority})}>{(['high','normal','low'] as const).map(priority=><option key={priority} value={priority}>{priorityLabel[priority]}</option>)}</select></dd></div>
      <div><dt>Owner</dt><dd>{name}</dd></div>
      <div><dt>Updated</dt><dd>{readableTime(task.updated_at)}</dd></div>
    </dl>
    {pastMeeting&&<p className="fe-notice">This task came from a past meeting. It’s kept as a record and can’t be changed.</p>}
    <nav className="fe-tabs" aria-label="Task detail views">{(['details','sources','conversation'] as const).map(item=><button type="button" key={item} aria-pressed={tab===item} onClick={()=>setTab(item)}>{item==='details'?'Details':item==='sources'?`Sources${sources?` (${sources})`:''}`:'Conversation'}</button>)}</nav>
    {tab==='details'&&<div className="fe-stack">
      <section><h4>Next step</h4><p className="fe-task-next">{task.next_action||'No next step recorded yet.'}</p><small>{task.status==='paused'?'Paused. Change the status when this should resume.':actionLabel[task.action_state]}</small></section>
      {task.blocker&&<div className="fe-notice attn"><CircleAlert size={17}/><span><strong>Waiting on</strong>{task.blocker}</span></div>}
      <div><button type="button" onClick={()=>setTab('conversation')}>Discuss with {name}</button></div>
    </div>}
    {tab==='sources'&&<MarketingEvidencePanel task={task} evidence={state.evidence||[]} canAdd={editable} onRefresh={onRefresh} onError={setError}/>}
    {tab==='conversation'&&<div className="fe-task-chat"><Conversation key={task.conversation_key} state={state} task={task} canWrite={canChat&&!pastMeeting} onRefresh={onRefresh} compact/></div>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </div>;
}

/** Create a task for the employee. Returns the new task's id through onCreated. */
export function NewTaskDialog({state,onClose,onCreated,onRefresh}:{state:MarketingState;onClose:()=>void;onCreated:(id:string)=>void;onRefresh:()=>Promise<void>}){
  const [title,setTitle]=useState(''),[next,setNext]=useState(''),[priority,setPriority]=useState<TaskPriority>('normal');
  const [working,setWorking]=useState(false),[error,setError]=useState('');
  const attempt=useAttempt();
  const name=state.employee.name||'Marketing';
  async function create(event:React.FormEvent){
    event.preventDefault();if(!title.trim()||working)return;setWorking(true);setError('');
    const fields={title:title.trim(),status:'ready',priority,next_action:next.trim(),action_state:next.trim()?'agent_ready':'none'};
    try{const created=await api<MarketingTask>('/marketing/tasks',{...fields,requestId:attempt.id(JSON.stringify(fields))});attempt.done();await onRefresh();onCreated(created.id);}
    catch(cause){setError((cause as Error).message);}finally{setWorking(false);}
  }
  return <Dialog title="New task" onClose={onClose}><form className="fe-form" onSubmit={event=>void create(event)}>
    <label>What needs doing?<input autoFocus required maxLength={160} value={title} onChange={event=>setTitle(event.target.value)} placeholder="e.g. Find three communities our buyers read"/></label>
    <label>First step for {name}<textarea rows={3} maxLength={2000} value={next} onChange={event=>setNext(event.target.value)} placeholder="Optional. Be specific about the outcome you want."/></label>
    <label>Priority<select value={priority} onChange={event=>setPriority(event.target.value as TaskPriority)}><option value="high">High</option><option value="normal">Normal</option><option value="low">Low</option></select></label>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={working||!title.trim()}>{working?'Saving…':'Create task'}</button></footer>
  </form></Dialog>;
}

/** Moving a card between board lanes changes the task's status. */
export function useTaskMove(canWrite:boolean,onRefresh:()=>Promise<void>,onError:(message:string)=>void){
  const attempt=useAttempt();
  return async(task:MarketingTask,status:TaskStatus)=>{
    if(!canWrite)return;
    const fields={status,version:task.version};
    try{await api('/marketing/tasks/'+task.id,{...fields,requestId:attempt.id(task.id+JSON.stringify(fields))},'PUT');attempt.done();await onRefresh();}
    catch(cause){onError((cause as Error).message);await onRefresh().catch(()=>{});}
  };
}
