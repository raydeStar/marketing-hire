import {useCallback,useEffect,useState} from 'react';
import {BriefcaseBusiness,CalendarClock} from 'lucide-react';
import {api} from '../api';
import {Dialog} from './shared';

export type WorkSchedule={enabled:boolean;days:number[];start:string;end:string;timeZone:string;cycleMinutes:number;turnBudget:number;tokenBudget:number|null;monthlyTokens?:number|null;lastStartedFor:string|null;updatedBy:string;updatedAt:string};
export type ScheduleView={schedule:WorkSchedule|null;nextStart:string|null;monthUsed?:number;monthSpent?:boolean};

const dayNames=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
const browserZone=(()=>{try{return Intl.DateTimeFormat().resolvedOptions().timeZone;}catch{return 'UTC';}})();
function span(days:number[]){
  const sorted=[...days].sort();
  if(sorted.join()==='1,2,3,4,5')return 'Weekdays';
  if(sorted.length===7)return 'Every day';
  return sorted.map(day=>dayNames[day]).join(', ');
}
const compact=(value:number)=>value>=1e6?`${+(value/1e6).toFixed(1)}M`:value>=1e3?`${Math.round(value/1e3)}k`:String(value);
const when=(value:string)=>new Date(value).toLocaleString(undefined,{weekday:'short',hour:'numeric',minute:'2-digit'});

export function useWorkSchedule(){
  const [view,setView]=useState<ScheduleView|null>(null);
  const load=useCallback(async()=>{try{setView(await api<ScheduleView>('/shifts/schedule'));}catch{setView(null);}},[]);
  useEffect(()=>{void load();},[load]);
  return {view,load,setView};
}

/** One line under the shift controls: the working hours, and when the next shift starts. */
export function WorkHoursLine({owner,onEdit,view}:{owner:boolean;onEdit:()=>void;view:ScheduleView|null}){
  const schedule=view?.schedule;
  return <p className="fe-work-hours"><CalendarClock size={13}/>
    {schedule?.enabled?<span>{span(schedule.days)} {schedule.start}–{schedule.end}{view?.monthSpent?' · this month’s token limit is spent':view?.nextStart?` · next ${when(view.nextStart)}`:''}{schedule.monthlyTokens?` · ${compact(view?.monthUsed??0)} of ${compact(schedule.monthlyTokens)} tokens this month`:''}</span>:<span>No working hours set</span>}
    {owner&&<button type="button" className="fe-link" onClick={onEdit}>{schedule?.enabled?'Change':'Set working hours'}</button>}</p>;
}

/** Working hours: on these days the employee starts its own shift, in the owner's time zone. */
export function WorkHoursDialog({view,live,onClose,onSaved}:{view:ScheduleView|null;live:boolean;onClose:()=>void;onSaved:(next:ScheduleView)=>void}){
  const current=view?.schedule;
  const [enabled,setEnabled]=useState(current?.enabled??true),[days,setDays]=useState<number[]>(current?.days??[1,2,3,4,5]);
  const [start,setStart]=useState(current?.start??'08:00'),[end,setEnd]=useState(current?.end??'17:00'),[cycle,setCycle]=useState(current?.cycleMinutes??60);
  const [turns,setTurns]=useState(String(current?.turnBudget??200)),[tokens,setTokens]=useState(current?.tokenBudget?String(current.tokenBudget):live?'3000000':'');
  const [monthly,setMonthly]=useState(current?.monthlyTokens?String(current.monthlyTokens):live?'60000000':'');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const zone=current?.timeZone&&current.timeZone!==browserZone?current.timeZone:browserZone;
  async function save(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{onSaved(await api<ScheduleView>('/shifts/schedule',{enabled,days,start,end,timeZone:browserZone,cycleMinutes:cycle,turnBudget:Number(turns)||200,tokenBudget:Number(tokens)||null,monthlyTokens:Number(monthly)||null},'PUT'));onClose();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <Dialog title="Working hours" onClose={onClose}><form className="fe-form" onSubmit={event=>void save(event)}>
    <p className="fe-muted">On these days the employee starts its own shift at the start time and wraps up with a report at the end. If you stop a shift, it waits until the next working day.</p>
    <label className="fe-check"><input type="checkbox" checked={enabled} onChange={event=>setEnabled(event.target.checked)}/>Work on a schedule</label>
    <fieldset className="fe-day-picker" disabled={!enabled}><legend>Days</legend>{dayNames.map((day,index)=><label key={day} className={days.includes(index)?'active':''}>
      <input type="checkbox" checked={days.includes(index)} onChange={event=>setDays(event.target.checked?[...days,index]:days.filter(item=>item!==index))}/>{day}</label>)}</fieldset>
    <div className="fe-form-row">
      <label>Starts<input type="time" value={start} disabled={!enabled} onChange={event=>setStart(event.target.value)}/></label>
      <label>Ends<input type="time" value={end} disabled={!enabled} onChange={event=>setEnd(event.target.value)}/></label>
      <label>Check in every<select value={cycle} disabled={!enabled} onChange={event=>setCycle(Number(event.target.value))}><option value={30}>30 minutes</option><option value={60}>hour</option><option value={120}>2 hours</option></select></label>
    </div>
    <div className="fe-form-row">
      <label>Model turns per day<input inputMode="numeric" value={turns} disabled={!enabled} onChange={event=>setTurns(event.target.value.replace(/\D/g,''))}/></label>
      <label>Token limit per day<input inputMode="numeric" value={tokens} disabled={!enabled} placeholder={live?'':'Not needed for the stand-in'} onChange={event=>setTokens(event.target.value.replace(/\D/g,''))}/></label>
      <label>Token limit per month<input inputMode="numeric" value={monthly} disabled={!enabled} placeholder={live?'':'Not needed for the stand-in'} onChange={event=>setMonthly(event.target.value.replace(/\D/g,''))}/></label>
    </div>
    {view?.schedule?.monthlyTokens?<small className="fe-muted">{(view.monthUsed??0).toLocaleString()} tokens used by shifts so far this month.</small>:null}
    <small className="fe-muted">Times are in {zone}. The workspace has to be running; a day it misses is skipped, not made up later. Cycles with nothing to do spend nothing.</small>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy}>{busy?'Saving…':'Save working hours'}</button></footer>
  </form></Dialog>;
}

/** One click to put the employee to work: working hours on (weekdays 9–5 unless already set), token limits on the live model,
 * and the weekly plan, Friday update and morning brief. Shows what's on once it is. */
export function PutToWork({onDone,compactView=false}:{onDone?:()=>void;compactView?:boolean}){
  const [view,setView]=useState<ScheduleView|null>(null),[weekly,setWeekly]=useState<boolean|null>(null),[live,setLive]=useState(true);
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const load=useCallback(async()=>{
    try{const [hours,rhythm,shifts]=await Promise.all([api<ScheduleView>('/shifts/schedule'),api<{settings:{enabled:boolean}}>('/weekly'),api<{live:boolean}>('/shifts')]);setView(hours);setWeekly(rhythm.settings.enabled);setLive(shifts.live);}
    catch{setView(null);setWeekly(null);}
  },[]);
  useEffect(()=>{void load();},[load]);
  async function go(){
    if(busy)return;setBusy(true);setError('');
    try{const result=await api<{schedule:ScheduleView}>('/employee/put-to-work',{timeZone:browserZone},'POST');setView(result.schedule);setWeekly(true);onDone?.();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(weekly===null)return null;
  const schedule=view?.schedule;
  const on=!!schedule?.enabled&&weekly;
  return <div className={'fe-put-to-work'+(on?' on':'')+(compactView?' compact':'')} aria-label="Put it to work">
    <BriefcaseBusiness size={18}/>
    <span className="fe-list-main">{on
      ?<><strong>At work {span(schedule!.days).toLowerCase()}, {schedule!.start}–{schedule!.end}</strong><small>{view?.nextStart?`Next shift ${when(view.nextStart)}. `:''}The morning brief, Monday plan and Friday update are on. Nothing is published without your approval.</small></>
      :<><strong>Put it to work</strong><small>Weekday shifts from 9 to 5 with an hourly check-in, a daily and monthly token limit, and the morning brief, Monday plan and Friday update. Everything it makes waits for your approval.</small></>}</span>
    {!on&&<button type="button" className="primary" disabled={busy} onClick={()=>void go()}>{busy?'Starting…':'Put it to work'}</button>}
    {!live&&<p className="fe-notice">Shifts run on the scripted stand-in: the loop and records are real, the words are placeholders. Start the workspace with live shifts before you rely on the work.</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </div>;
}
