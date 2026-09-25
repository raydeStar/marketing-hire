import {useState} from 'react';
import {CircleAlert,Plus} from 'lucide-react';
import {api} from '../api';
import {MarketingEvidencePanel,actionLabel,priorityLabel,readableTime,statusLabel,statusOrder,type MarketingState,type MarketingTask,type TaskPriority,type TaskStatus} from '../components/MarketingPanels';
import {WorkBoard} from '../components/WorkBoard';
import {Conversation} from './ChatView';
import {Dialog,PageHead,useAttempt} from './shared';

export function TaskDialog({task,state,canWrite,canChat,pastMeeting=false,onClose,onRefresh}:{task:MarketingTask;state:MarketingState;canWrite:boolean;canChat:boolean;pastMeeting?:boolean;onClose:()=>void;onRefresh:()=>Promise<void>}){
  const [tab,setTab]=useState<'details'|'sources'|'conversation'>('details'),[working,setWorking]=useState(false),[error,setError]=useState('');
  const attempt=useAttempt();
  const name=state.employee.name||'Marketing';
  async function update(changes:Partial<Pick<MarketingTask,'status'|'priority'>>){
    if(!canWrite||working)return;setWorking(true);setError('');
    const fields={...changes,version:task.version};
    try{await api('/marketing/tasks/'+task.id,{...fields,requestId:attempt.id(task.id+JSON.stringify(fields))},'PUT');attempt.done();await onRefresh();}
    catch(cause){setError((cause as Error).message);}finally{setWorking(false);}
  }
  return <Dialog title={task.title} wide={tab==='conversation'} onClose={onClose}>
    <div className="fe-task-dialog">
      <p className="fe-task-byline">{name} · updated {readableTime(task.updated_at)}</p>
      {pastMeeting&&<p className="fe-notice">This task came from a past meeting. It’s kept as a record and can’t be changed.</p>}
      <nav className="fe-segmented" aria-label="Task detail views">{(['details','sources','conversation'] as const).map(item=><button type="button" key={item} aria-pressed={tab===item} onClick={()=>setTab(item)}>{item==='details'?'Details':item==='sources'?'Sources':'Conversation'}</button>)}</nav>
      {tab==='details'&&<div className="fe-form">
        <div className="fe-form-row">
          <label>Status<select value={task.status} disabled={!canWrite||working} onChange={event=>void update({status:event.target.value as TaskStatus})}>{statusOrder.map(status=><option key={status} value={status}>{statusLabel[status]}</option>)}</select></label>
          <label>Priority<select value={task.priority} disabled={!canWrite||working} onChange={event=>void update({priority:event.target.value as TaskPriority})}>{(['high','normal','low'] as const).map(priority=><option key={priority} value={priority}>{priorityLabel[priority]}</option>)}</select></label>
        </div>
        <div><h4>Next step</h4><p className="fe-task-next">{task.next_action||'No next step recorded yet.'}</p><small>{task.status==='paused'?'Paused. Change the status when this should resume.':actionLabel[task.action_state]}</small></div>
        {task.blocker&&<div className="fe-notice attn"><CircleAlert size={17}/><span><strong>Waiting on</strong>{task.blocker}</span></div>}
        <div><button type="button" className="fe-ghost" onClick={()=>setTab('conversation')}>Discuss with {name} →</button></div>
      </div>}
      {tab==='sources'&&<MarketingEvidencePanel task={task} evidence={state.evidence||[]} canAdd={canWrite} onRefresh={onRefresh} onError={setError}/>}
      {tab==='conversation'&&<div className="fe-task-chat"><Conversation key={task.conversation_key} state={state} task={task} canWrite={canChat} onRefresh={onRefresh} compact/></div>}
      {error&&<p className="fe-alert" role="alert">{error}</p>}
    </div>
  </Dialog>;
}

export function TasksView({state,pastMeetingTasks=[],canWrite,onOpenTask,onRefresh}:{state:MarketingState;pastMeetingTasks?:MarketingTask[];canWrite:boolean;onOpenTask:(id:string)=>void;onRefresh:()=>Promise<void>}){
  const [creating,setCreating]=useState(false),[title,setTitle]=useState(''),[next,setNext]=useState(''),[priority,setPriority]=useState<TaskPriority>('normal');
  const [working,setWorking]=useState(false),[error,setError]=useState('');
  const attempt=useAttempt();
  const name=state.employee.name||'Marketing';
  const tasks=[...state.tasks].sort((a,b)=>({high:0,normal:1,low:2}[a.priority]-{high:0,normal:1,low:2}[b.priority])||b.updated_at-a.updated_at);
  const moveAttempt=useAttempt();
  async function move(task:MarketingTask,status:TaskStatus){
    if(!canWrite)return;setError('');
    const fields={status,version:task.version};
    try{await api('/marketing/tasks/'+task.id,{...fields,requestId:moveAttempt.id(task.id+JSON.stringify(fields))},'PUT');moveAttempt.done();await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}
  }
  async function create(event:React.FormEvent){
    event.preventDefault();if(!title.trim()||working)return;setWorking(true);setError('');
    const fields={title:title.trim(),status:'ready',priority,next_action:next.trim(),action_state:next.trim()?'agent_ready':'none'};
    try{
      const created=await api<MarketingTask>('/marketing/tasks',{...fields,requestId:attempt.id(JSON.stringify(fields))});
      attempt.done();setCreating(false);setTitle('');setNext('');await onRefresh();onOpenTask(created.id);
    }catch(cause){setError((cause as Error).message);}finally{setWorking(false);}
  }
  return <div className="fe-page"><div className="fe-page-inner">
    <PageHead title="Tasks" subtitle={`Everything ${name} is working on, and what’s waiting for you.`}><button type="button" className="primary" disabled={!canWrite} onClick={()=>setCreating(true)}><Plus size={16}/> New task</button></PageHead>
    <WorkBoard tasks={tasks} pastMeetingTasks={pastMeetingTasks} employeeName={name} onOpen={onOpenTask} onCreate={()=>setCreating(true)} canCreate={canWrite} onMove={canWrite?(task,status)=>void move(task,status):undefined}/>
    {error&&!creating&&<p className="fe-alert" role="alert">{error}</p>}
    {canWrite&&<p className="fe-muted fe-board-hint">Drag a card between lanes to change its status.</p>}
    {creating&&<Dialog title="New task" onClose={()=>setCreating(false)}><form className="fe-form" onSubmit={event=>void create(event)}>
      <label>What needs doing?<input autoFocus required maxLength={160} value={title} onChange={event=>setTitle(event.target.value)} placeholder="e.g. Find three communities our buyers read"/></label>
      <label>First step for {name}<textarea rows={3} maxLength={2000} value={next} onChange={event=>setNext(event.target.value)} placeholder="Optional. Be specific about the outcome you want."/></label>
      <label>Priority<select value={priority} onChange={event=>setPriority(event.target.value as TaskPriority)}><option value="high">High</option><option value="normal">Normal</option><option value="low">Low</option></select></label>
      {error&&<p className="fe-alert" role="alert">{error}</p>}
      <footer><button type="button" className="fe-ghost" onClick={()=>setCreating(false)}>Cancel</button><button className="primary" disabled={working||!title.trim()}>{working?'Saving…':'Create task'}</button></footer>
    </form></Dialog>}
  </div></div>;
}
