import {useEffect,useState,type ReactNode} from 'react';
import {api} from '../api';
import {Check,ChevronDown,ChevronRight,ChevronUp,Coffee,GripVertical,PanelRightClose,Send} from 'lucide-react';
import {Raven} from '../components/Raven';
import {priorityLabel,readableTime,type MarketingState} from '../components/MarketingPanels';
import type {InboxItem} from './InboxView';
import type {EmployeeStatus} from './shared';
import {EmployeeContinuity} from './Experience';
import {TodayDesk} from './Today';
import {useCurrentActivity} from './ShiftFeed';
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
export function Cockpit({state,status,owner,canChat,shifts,northStar,start,onOpenItem,onOpenTask,onMeeting,onBoard,onClose,onOpen,onChat,shiftView}:{
  state:MarketingState;status:EmployeeStatus;owner:boolean;canChat:boolean;
  shifts?:ReactNode;northStar?:ReactNode;start?:ReactNode;onOpenItem:(item:InboxItem)=>void;onOpenTask:(id:string)=>void;onMeeting:()=>void;onBoard:()=>void;onClose:()=>void;
  onOpen?:(key:string)=>void;onChat?:(text:string)=>void;shiftView?:ShiftView|null;
}){
  const name=state.employee.name||'Marketing';
  // Approved but not out yet: decided, so not a decision, but someone still publishes or posts it.
  const ready=owner?state.drafts.filter(draft=>draft.status==='approved'):[];
  const moving=state.tasks.filter(task=>task.status==='working').sort((a,b)=>b.updated_at-a.updated_at);
  const rank:Record<string,number>={urgent:0,high:1,normal:2,low:3};
  // Up next: what's queued, in the order it's likely taken (priority first, then oldest), so a "Fix for me" is seen landing.
  const upNext=state.tasks.filter(task=>task.status==='ready').sort((a,b)=>(rank[a.priority]??2)-(rank[b.priority]??2)||a.updated_at-b.updated_at);
  const queued=upNext.length;
  // Just done: finished in the last few hours, newest first; one that finished moments ago checks itself off.
  const fresh=(task:{updated_at:number})=>Date.now()/1000-task.updated_at<30;
  const justDone=state.tasks.filter(task=>(task.status==='done'||task.status==='needs_you')&&Date.now()/1000-task.updated_at<3*3600).sort((a,b)=>b.updated_at-a.updated_at).slice(0,3);
  const met=lastMeeting(state);
  // A shift works through the assigned tasks without marking them "in progress": say what it's on, from its live feed.
  const onShift=shiftView?.current&&['running','finishing'].includes(shiftView.current.status)?shiftView.current:null;
  const activity=useCurrentActivity(onShift?.id,onShift?.status==='running');
  const s=(count:number)=>count===1?'':'s';
  const working=!!activity||moving.length>0;
  // Up next in the owner's order (kept by the host, which works from the top); what isn't arranged follows as it came.
  const [order,setOrder]=useState<string[]>(shiftView?.queueOrder||[]);
  useEffect(()=>{setOrder(shiftView?.queueOrder||[]);},[(shiftView?.queueOrder||[]).join()]);
  const [dragging,setDragging]=useState<string|null>(null);
  const ordered=[...upNext].sort((a,b)=>{const x=order.indexOf(a.id),y=order.indexOf(b.id);return (x<0?1e9:x)-(y<0?1e9:y);});
  async function move(id:string,to:number){
    const ids=ordered.map(task=>task.id).filter(item=>item!==id);ids.splice(Math.max(0,Math.min(to,ids.length)),0,id);
    setOrder(ids);
    try{await api('/queue/order',{ids},'PUT');}catch{setOrder(shiftView?.queueOrder||[]);}
  }
  return <aside className="fe-cockpit" aria-label="Cockpit">
    <header className="fe-cockpit-head"><h2>Cockpit</h2><button type="button" className="fe-icon-button" aria-label="Hide cockpit" title="Hide cockpit" onClick={onClose}><PanelRightClose size={17}/></button></header>
    <div className="fe-cockpit-body">
      <section className="fe-cockpit-employee" aria-label={name}>
        <Raven state={status.tone==='busy'?'working':status.tone==='warn'?'attention':status.tone==='off'?'asleep':ready.length?'letter':'idle'}/>
        <div><strong>{name}</strong><span className={'fe-status-chip '+status.tone}><i className={'fe-dot '+status.tone}/>{status.label}</span></div>
      </section>
      {/* What it's doing, first: now, next, just done, and what it learned, in one place (it was three sections). */}
      {/* What it's doing, first: now (lit up while it works), then what's next in the order you arrange, then what's just done. */}
      <section className={'fe-cockpit-section fe-working-on'+(working?' active':'')} aria-label="Working on">
        <h3>Working on</h3>
        {working?<div className="fe-now-card" role="status">
            <span className="fe-now-pulse" aria-hidden="true"/>
            <div className="fe-now-text"><small>{onShift?.requests?'On what you asked':onShift?'On shift':'Now'}</small>
              {activity?<strong>{activity}</strong>:<button type="button" className="fe-now-title" onClick={()=>onOpenTask(moving[0].id)}>{moving[0].title}</button>}
              {moving[0]&&activity&&<button type="button" className="fe-link" onClick={()=>onOpenTask(moving[0].id)}>{moving[0].title}</button>}</div>
            <i className="fe-now-bar" aria-hidden="true"/>
          </div>
          :<p className="fe-cockpit-clear">{onShift?`${name} is on shift, between check-ins${onShift.nextCycleAt&&Date.parse(onShift.nextCycleAt)>Date.now()?`; the next is at ${new Date(onShift.nextCycleAt).toLocaleTimeString([],{hour:'numeric',minute:'2-digit'})}`:''}.`:queued?`${name} starts on what's next in a moment.`:`Nothing right now. Ask ${name} for something in chat, or start a shift and it finds work of its own.`}</p>}
        {moving.length>1&&<div className="fe-cockpit-list">{moving.slice(1,5).map(task=><button type="button" className="fe-cockpit-item" key={task.id} onClick={()=>onOpenTask(task.id)}>
          <i className={'fe-priority '+task.priority} aria-label={priorityLabel[task.priority]+' priority'}/><span><strong>{task.title}</strong><small>{task.next_action||'Working'}</small></span><ChevronRight size={15}/></button>)}</div>}
        {queued>0&&<div className="fe-up-next" aria-label="Up next"><h3>Up next <span className="fe-count">{queued}</span></h3>
          <ol>{ordered.slice(0,8).map((task,index)=><li key={task.id} className={(fresh(task)?'fresh ':'')+(dragging===task.id?'dragging':'')} draggable={owner}
              onDragStart={event=>{setDragging(task.id);event.dataTransfer.effectAllowed='move';}} onDragEnd={()=>setDragging(null)}
              onDragOver={event=>{if(dragging&&dragging!==task.id){event.preventDefault();}}} onDrop={event=>{event.preventDefault();if(dragging)void move(dragging,index);setDragging(null);}}>
            {owner&&<span className="fe-drag-grip" aria-hidden="true"><GripVertical size={13}/></span>}
            <button type="button" className="fe-up-next-title" onClick={()=>onOpenTask(task.id)}><span>{task.title}</span>{index===0&&<small>{onShift?.requests||!onShift?'Next':'At the next check-in'}</small>}</button>
            {owner&&ordered.length>1&&<span className="fe-up-next-move">
              <button type="button" className="fe-icon-button" aria-label={`Move ${task.title} up`} title="Move up" disabled={index===0} onClick={()=>void move(task.id,index-1)}><ChevronUp size={13}/></button>
              <button type="button" className="fe-icon-button" aria-label={`Move ${task.title} down`} title="Move down" disabled={index===ordered.length-1} onClick={()=>void move(task.id,index+1)}><ChevronDown size={13}/></button></span>}
          </li>)}</ol>
          {queued>8&&<small className="fe-muted">and {queued-8} more</small>}
          {owner&&queued>1&&<small className="fe-muted fe-up-next-hint">Drag to reorder; it works from the top.</small>}</div>}
        {justDone.length>0&&<div className="fe-up-next done" aria-label="Just done"><h3>Just done</h3>
          <ol>{justDone.map(task=><li key={task.id+':'+task.status} className={fresh(task)?'fresh':undefined}><button type="button" className="fe-up-next-title" onClick={()=>onOpenTask(task.id)}>
            <Check size={13} className="fe-done-check" aria-hidden="true"/><span>{task.title}</span><small>{task.status==='needs_you'?'Waiting for you':readableTime(task.updated_at)}</small></button></li>)}</ol></div>}
        {onOpen&&<EmployeeContinuity view={shiftView??null} onOpen={onOpen} compact/>}
        <button type="button" className="fe-link" onClick={onBoard}>Open the board</button>
      </section>
      {shifts}
      {/* What it prepared for you, and what is ready to go out. */}
      <TodayDesk state={state} owner={owner} onOpen={onOpen||((target)=>onOpenItem({id:target,target,kind:'document',title:target,detail:''}))} onOpenItem={onOpenItem} onChat={onChat}
        next={moving.length?`${name} is working on ${moving[0].title}.`:onShift&&queued?`${name} is working through ${queued} assignment${s(queued)} on this shift.`:onShift?`${name} is on shift, looking for what to do next.`:queued?`${queued} assignment${s(queued)} wait for the next shift you start.`:`${name} is ready for the next assignment.`}/>
      {ready.length>0&&<section className="fe-cockpit-section" aria-label="Ready to post">
        <h3>Ready to post<span className="fe-count">{ready.length}</span></h3>
        <div className="fe-cockpit-list">{ready.slice(0,5).map(draft=><button type="button" className="fe-cockpit-item" key={draft.id} onClick={()=>onOpenItem({id:'draft:'+draft.id,kind:'draft',title:draft.channel,detail:''})}>
          <Send size={15}/><span><strong>{draft.channel} draft #{draft.id}</strong><small>Approved. Publish or schedule it, or post it yourself.</small></span><ChevronRight size={15}/></button>)}</div>
      </section>}
      <section className="fe-cockpit-meeting">
        <div><strong>Morning check-in</strong><small>{met?(today(met)?'Done today · ':'Last one ')+readableTime(met):`${name} suggests what to do today`}</small></div>
        <button type="button" disabled={!canChat} onClick={onMeeting}><Coffee size={15}/> {met&&today(met)?'Run again':'Start'}</button>
      </section>
      {/* Getting started, under the work: the next step in one click, and the goal it plans around. */}
      {start}
      {northStar}
    </div>
  </aside>;
}
