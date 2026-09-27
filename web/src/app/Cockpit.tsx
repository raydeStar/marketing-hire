import type {ReactNode} from 'react';
import {ChevronRight,Coffee,PanelRightClose,Send} from 'lucide-react';
import {Raven} from '../components/Raven';
import {priorityLabel,readableTime,type MarketingState} from '../components/MarketingPanels';
import type {InboxItem} from './InboxView';
import type {EmployeeStatus} from './shared';
import {EmployeeContinuity} from './Experience';
import {TodayDesk} from './Today';
import type {ShiftView} from './shifts';

/** When the morning meeting last ran, from the conversation itself. */
export function lastMeeting(state:MarketingState){
  const main=state.messages.filter(message=>message.sessionKey===state.employee.sessionKey&&!message.taskId&&message.role==='user'&&message.content.startsWith('Morning meeting'));
  return main.length?main[main.length-1].createdAt:null;
}
function today(value:number|string|null){
  if(value===null)return false;const date=new Date(typeof value==='number'&&value<1e12?value*1000:value);const now=new Date();
  return date.getFullYear()===now.getFullYear()&&date.getMonth()===now.getMonth()&&date.getDate()===now.getDate();
}

/** The pinned right panel: only what needs a decision and what is moving right now. */
export function Cockpit({state,status,owner,canChat,shifts,northStar,onOpenItem,onOpenTask,onMeeting,onBoard,onClose,onOpen,onChat,shiftView}:{
  state:MarketingState;status:EmployeeStatus;owner:boolean;canChat:boolean;
  shifts?:ReactNode;northStar?:ReactNode;onOpenItem:(item:InboxItem)=>void;onOpenTask:(id:string)=>void;onMeeting:()=>void;onBoard:()=>void;onClose:()=>void;
  onOpen?:(key:string)=>void;onChat?:(text:string)=>void;shiftView?:ShiftView|null;
}){
  const name=state.employee.name||'Marketing';
  // Approved but not out yet: decided, so not a decision, but someone still publishes or posts it.
  const ready=owner?state.drafts.filter(draft=>draft.status==='approved'):[];
  const moving=state.tasks.filter(task=>task.status==='working').sort((a,b)=>b.updated_at-a.updated_at);
  const queued=state.tasks.filter(task=>task.status==='ready').length;
  const met=lastMeeting(state);
  return <aside className="fe-cockpit" aria-label="Cockpit">
    <header className="fe-cockpit-head"><h2>Cockpit</h2><button type="button" className="fe-icon-button" aria-label="Hide cockpit" title="Hide cockpit" onClick={onClose}><PanelRightClose size={17}/></button></header>
    <div className="fe-cockpit-body">
      <section className="fe-cockpit-employee" aria-label={name}>
        <Raven state={status.tone==='busy'?'working':status.tone==='warn'?'attention':status.tone==='off'?'asleep':ready.length?'letter':'idle'}/>
        <div><strong>{name}</strong><span className={'fe-status-chip '+status.tone}><i className={'fe-dot '+status.tone}/>{status.label}</span></div>
      </section>
      <TodayDesk state={state} owner={owner} onOpen={onOpen||((target)=>onOpenItem({id:target,target,kind:'document',title:target,detail:''}))} onOpenItem={onOpenItem} onChat={onChat}
        next={moving.length?`${name} is working on ${moving[0].title}.`:queued?`${queued} assignments wait for the next authorized shift.`:`${name} is ready for the next assignment.`}/>
      {northStar}
      {onOpen&&<EmployeeContinuity view={shiftView??null} onOpen={onOpen}/>}
      {shifts}
      <section className="fe-cockpit-meeting">
        <div><strong>Morning meeting</strong><small>{met?(today(met)?'Held today · ':'Last held ')+readableTime(met):'Not held yet'}</small></div>
        <button type="button" disabled={!canChat} onClick={onMeeting}><Coffee size={15}/> {met&&today(met)?'Run again':'Start'}</button>
      </section>
      {ready.length>0&&<section className="fe-cockpit-section" aria-label="Ready to post">
        <h3>Ready to post<span className="fe-count">{ready.length}</span></h3>
        <div className="fe-cockpit-list">{ready.slice(0,5).map(draft=><button type="button" className="fe-cockpit-item" key={draft.id} onClick={()=>onOpenItem({id:'draft:'+draft.id,kind:'draft',title:draft.channel,detail:''})}>
          <Send size={15}/><span><strong>{draft.channel} draft #{draft.id}</strong><small>Approved. Publish or schedule it, or post it yourself.</small></span><ChevronRight size={15}/></button>)}</div>
      </section>}
      <section className="fe-cockpit-section" aria-label="In progress">
        <h3>In progress<span className="fe-count">{moving.length}</span></h3>
        {moving.length?<div className="fe-cockpit-list">{moving.slice(0,5).map(task=><button type="button" className="fe-cockpit-item" key={task.id} onClick={()=>onOpenTask(task.id)}>
          <i className={'fe-priority '+task.priority} aria-label={priorityLabel[task.priority]+' priority'}/><span><strong>{task.title}</strong><small>{task.next_action||'Working'}</small></span><ChevronRight size={15}/></button>)}</div>
          :<p className="fe-cockpit-clear">{name} isn’t working on a task right now.</p>}
        <button type="button" className="fe-link" onClick={onBoard}>{queued?`${queued} assigned and waiting · `:''}Open the board</button>
      </section>
    </div>
  </aside>;
}
