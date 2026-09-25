import {useCallback,useEffect,useState} from 'react';
import {CalendarClock} from 'lucide-react';
import {api} from '../api';
import {Dialog} from './shared';

export type WorkSchedule={enabled:boolean;days:number[];start:string;end:string;timeZone:string;cycleMinutes:number;turnBudget:number;tokenBudget:number|null;lastStartedFor:string|null;updatedBy:string;updatedAt:string};
type ScheduleView={schedule:WorkSchedule|null;nextStart:string|null};

const dayNames=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
const browserZone=(()=>{try{return Intl.DateTimeFormat().resolvedOptions().timeZone;}catch{return 'UTC';}})();
function span(days:number[]){
  const sorted=[...days].sort();
  if(sorted.join()==='1,2,3,4,5')return 'Weekdays';
  if(sorted.length===7)return 'Every day';
  return sorted.map(day=>dayNames[day]).join(', ');
}
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
    {schedule?.enabled?<span>{span(schedule.days)} {schedule.start}–{schedule.end}{view?.nextStart?` · next ${when(view.nextStart)}`:''}</span>:<span>No working hours set</span>}
    {owner&&<button type="button" className="fe-link" onClick={onEdit}>{schedule?.enabled?'Change':'Set working hours'}</button>}</p>;
}

/** Working hours: on these days the employee starts its own shift, in the owner's time zone. */
export function WorkHoursDialog({view,live,onClose,onSaved}:{view:ScheduleView|null;live:boolean;onClose:()=>void;onSaved:(next:ScheduleView)=>void}){
  const current=view?.schedule;
  const [enabled,setEnabled]=useState(current?.enabled??true),[days,setDays]=useState<number[]>(current?.days??[1,2,3,4,5]);
  const [start,setStart]=useState(current?.start??'08:00'),[end,setEnd]=useState(current?.end??'17:00'),[cycle,setCycle]=useState(current?.cycleMinutes??60);
  const [turns,setTurns]=useState(String(current?.turnBudget??12)),[tokens,setTokens]=useState(current?.tokenBudget?String(current.tokenBudget):live?'36000':'');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const zone=current?.timeZone&&current.timeZone!==browserZone?current.timeZone:browserZone;
  async function save(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{onSaved(await api<ScheduleView>('/shifts/schedule',{enabled,days,start,end,timeZone:browserZone,cycleMinutes:cycle,turnBudget:Number(turns)||12,tokenBudget:Number(tokens)||null},'PUT'));onClose();}
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
    </div>
    <small className="fe-muted">Times are in {zone}. The workspace has to be running; a day it misses is skipped, not made up later. Cycles with nothing to do spend nothing.</small>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy}>{busy?'Saving…':'Save working hours'}</button></footer>
  </form></Dialog>;
}
