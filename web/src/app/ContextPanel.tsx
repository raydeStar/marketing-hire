import {useRef,useState,type PointerEvent} from 'react';
import {BookOpen,ChevronRight,LayoutTemplate,Megaphone,NotebookPen,ShieldCheck,X} from 'lucide-react';
import {Raven} from '../components/Raven';
import {MarketingTokenUsage} from '../components/MarketingTokenUsage';
import {plain} from './shared';
import {inboxItems,type InboxItem} from './InboxView';
import type {MarketingState} from '../components/MarketingPanels';
import type {EmployeeStatus} from './shared';

const widthKey='fe-context-panel-width',minimum=280;
const maximum=()=>Math.max(minimum,Math.min(560,window.innerWidth-720));
const clamp=(width:number)=>Math.round(Math.max(minimum,Math.min(maximum(),width)));
export function initialContextWidth(){try{const saved=Number(localStorage.getItem(widthKey));if(saved>=minimum)return clamp(saved);}catch{}return 340;}

/** The Muse-style side panel beside chat: what's going on, without leaving the conversation. */
export function ContextPanel({state,status,showUsage=true,width,onWidth,onClose,onItem,onTask,onGo}:{
  state:MarketingState;status:EmployeeStatus;showUsage?:boolean;width:number;onWidth:(width:number)=>void;onClose:()=>void;
  onItem:(item:InboxItem)=>void;onTask:(id:string)=>void;onGo:(view:'assets'|'wiki'|'campaigns'|'brief')=>void;
}){
  const drag=useRef<{x:number;width:number}|null>(null),[dragging,setDragging]=useState(false);
  const name=state.employee.name||'Marketing';
  const items=inboxItems(state).slice(0,4);
  const moving=state.tasks.filter(task=>task.status==='working'||task.status==='ready').slice(0,4);
  function change(next:number){const value=clamp(next);onWidth(value);try{localStorage.setItem(widthKey,String(value));}catch{}}
  function finish(event:PointerEvent<HTMLDivElement>){drag.current=null;setDragging(false);if(event.currentTarget.hasPointerCapture(event.pointerId))event.currentTarget.releasePointerCapture(event.pointerId);}
  return <aside id="context-panel" className={'fe-context'+(dragging?' dragging':'')} aria-label="Context panel" style={{width}}>
    <div className="fe-context-resize" role="separator" aria-label="Resize context panel" aria-controls="context-panel" aria-orientation="vertical"
      aria-valuemin={minimum} aria-valuemax={maximum()} aria-valuenow={width} tabIndex={0} title="Drag to resize · arrow keys adjust"
      onPointerDown={event=>{if(event.button!==0)return;event.preventDefault();drag.current={x:event.clientX,width};event.currentTarget.setPointerCapture(event.pointerId);setDragging(true);}}
      onPointerMove={event=>{if(drag.current)change(drag.current.width+drag.current.x-event.clientX);}}
      onPointerUp={finish} onPointerCancel={finish}
      onKeyDown={event=>{const next=event.key==='ArrowLeft'?width+20:event.key==='ArrowRight'?width-20:event.key==='Home'?minimum:event.key==='End'?maximum():null;if(next!==null){event.preventDefault();change(next);}}}><span/></div>
    <header className="fe-context-head"><strong>At a glance</strong><button type="button" className="fe-icon-button" aria-label="Close context panel" onClick={onClose}><X size={17}/></button></header>
    <div className="fe-context-body">
      <section className="fe-context-employee"><Raven state={status.tone==='busy'?'running':'idle'}/><strong>{name}</strong><small><i className={'fe-dot '+status.tone}/>{status.label}</small></section>
      <section className="fe-context-section" aria-label="Waiting for you"><h3>Waiting for you <span>{inboxItems(state).length}</span></h3>
        {items.length?<div className="fe-row-list">{items.map(item=><button type="button" className="fe-row compact" key={item.id} onClick={()=>onItem(item)}>
          <span className={'fe-row-icon '+(item.kind==='review'?'accent':'attn')}>{item.kind==='brief'?<NotebookPen size={15}/>:item.kind==='review'?<Megaphone size={15}/>:<ShieldCheck size={15}/>}</span>
          <span className="fe-row-body"><strong>{item.title}</strong><small>{item.detail}</small></span><ChevronRight size={15}/></button>)}</div>
          :<p className="fe-muted">Nothing needs you right now.</p>}
      </section>
      <section className="fe-context-section" aria-label="In motion"><h3>In motion <span>{moving.length}</span></h3>
        {moving.length?<div className="fe-row-list">{moving.map(task=><button type="button" className="fe-row compact" key={task.id} onClick={()=>onTask(task.id)}>
          <span className={'fe-priority '+task.priority}/><span className="fe-row-body"><strong>{task.title}</strong><small>{task.status==='working'?`${name} is working`:plain(task.next_action)||'Up next'}</small></span></button>)}</div>
          :<p className="fe-muted">No tasks in progress.</p>}
      </section>
      <section className="fe-context-section" aria-label="Shortcuts"><h3>Shortcuts</h3>
        <div className="fe-context-links">
          <button type="button" onClick={()=>onGo('campaigns')}><Megaphone size={15}/> Campaigns</button>
          <button type="button" onClick={()=>onGo('assets')}><LayoutTemplate size={15}/> Assets</button>
          <button type="button" onClick={()=>onGo('wiki')}><BookOpen size={15}/> Wiki</button>
          <button type="button" onClick={()=>onGo('brief')}><NotebookPen size={15}/> Brief</button>
        </div>
      </section>
      {showUsage&&<details className="fe-context-section fe-context-usage"><summary>Usage</summary><div className="fe-usage"><MarketingTokenUsage/></div></details>}
    </div>
  </aside>;
}
