import {useEffect,useState} from 'react';
import {Clock3,FastForward,Pause,Play,Square} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {stageHelp,stageLabel,type Shift,type ShiftView} from './shifts';
import {Dialog} from './shared';
import {WorkHoursDialog,WorkHoursLine,useWorkSchedule} from './WorkHours';
import {ShiftFeed} from './ShiftFeed';

// The real length, from start to end: short test shifts are not rounded up to an hour.
const length=(shift:Shift)=>{const minutes=Math.round((Date.parse(shift.endsAt)-Date.parse(shift.startedAt))/60000);return minutes%60===0?`${minutes/60}h`:minutes<60?`${minutes} min`:`${Math.floor(minutes/60)}h ${minutes%60}m`;};
const clock=(value:string|null)=>value?new Date(value).toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'}):'—';

function StartShift({view,onClose,onStarted}:{view:ShiftView;onClose:()=>void;onStarted:(shift:Shift)=>void}){
  const [hours,setHours]=useState(8),[cycle,setCycle]=useState(60),[budget,setBudget]=useState(''),[tokens,setTokens]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const suggested=Math.max(6,Math.round(hours*60/cycle*2));
  // A live shift always gets a token limit; the default is the owner's daily cap.
  const suggestedTokens=10_000_000;
  async function start(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{onStarted(await api<Shift>('/shifts',{requestId:crypto.randomUUID(),hours,cycleMinutes:cycle,turnBudget:Number(budget)||suggested,tokenBudget:Number(tokens)||(view.live?suggestedTokens:null)}));}
    catch(cause){setError((cause as Error).message);setBusy(false);}
  }
  return <Dialog title="Start a shift" onClose={onClose}><form className="fe-form" onSubmit={event=>void start(event)}>
    <p className="fe-muted">On shift, it works through your assignments by itself and brings you drafts to approve. Nothing is posted, sent or spent without you, and you can stop it any time.</p>
    <fieldset className="fe-choice-row"><legend>How long</legend>{[1,4,8,16,24].map(value=><label key={value} className={hours===value?'active':''}><input type="radio" name="shift-hours" checked={hours===value} onChange={()=>setHours(value)}/>{value===1?'1 hour':`${value} hours`}</label>)}</fieldset>
    <label>Look for new work every<select value={cycle} onChange={event=>setCycle(Number(event.target.value))}><option value={30}>30 minutes</option><option value={60}>hour</option><option value={120}>2 hours</option><option value={240}>4 hours</option></select></label>
    {/* The limits are there for anyone who wants them; the suggested ones suit a first shift. */}
    <details className="fe-shift-limits"><summary>Limits (optional)</summary>
      <label>Most work steps<input inputMode="numeric" value={budget} onChange={event=>setBudget(event.target.value.replace(/\D/g,''))} placeholder={`${suggested} (suggested)`}/></label>
      <label>Most model usage, in tokens<input inputMode="numeric" value={tokens} onChange={event=>setTokens(event.target.value.replace(/\D/g,''))} placeholder={view.live?`${suggestedTokens.toLocaleString()} (suggested)`:'Not needed in practice mode'}/>{view.live&&<small>It sets aside room before each step, more for longer work, so it stops a little before this limit.</small>}</label>
      <small>A check-in with nothing to do costs nothing. It stops early when a limit is reached, and always keeps room to write its report.</small>
    </details>
    {!view.live&&<p className="fe-notice"><strong>Practice mode.</strong> Every step is real, but the writing is placeholder text and costs nothing.</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy}>{busy?'Starting…':`Start ${hours}-hour shift`}</button></footer>
  </form></Dialog>;
}

/** The cockpit's shift control: status, the stage strip, budget, and pause / stop / run now for the owner. */
export function ShiftPanel({view,owner,onChanged,onOpenReport,onOpenLog}:{view:ShiftView|null;owner:boolean;onChanged:(shift?:Shift)=>void;onOpenReport:(wikiId:string)=>void;onOpenLog:()=>void}){
  const [starting,setStarting]=useState(false),[busy,setBusy]=useState<string|null>(null),[error,setError]=useState(''),[hours,setHours]=useState(false);
  const schedule=useWorkSchedule();
  useEffect(()=>{void schedule.load();},[view?.current?.id]);
  if(!view)return null;
  const shift=view.current,last=view.recent.find(item=>item.status==='completed'||item.status==='stopped');
  async function act(action:string){
    if(!shift||busy)return;setBusy(action);setError('');
    try{onChanged(await api<Shift>(`/shifts/${shift.id}/${action}`,{}));}catch(cause){setError((cause as Error).message);}finally{setBusy(null);}
  }
  return <section className="fe-cockpit-shift" aria-label="Shift">
    <div className="fe-cockpit-shift-head"><div><strong>{shift?shift.status==='paused'?'Shift paused':'On shift':'Off shift'}</strong>
      <small title={shift?`${length(shift)} shift · ${shift.turnsUsed} of ${shift.turnBudget} work steps${shift.tokenBudget?` · ${shift.tokensUsed.toLocaleString()} of ${shift.tokenBudget.toLocaleString()} tokens`:''}`:undefined}>{shift?`Working until ${clock(shift.endsAt)}`:last?`Last shift ended ${readableTime(last.endedAt||last.startedAt)}`:'Not working right now'}</small></div>
      {owner&&!shift&&<button type="button" className="primary" onClick={()=>setStarting(true)}><Play size={14}/> Start shift</button>}
      {owner&&shift&&<div className="fe-cockpit-shift-actions">
        <button type="button" className="fe-icon-button" aria-label="Check in now" title="Look for new work now" disabled={!!busy||shift.status!=='running'} onClick={()=>void act('cycle')}><FastForward size={15}/></button>
        {shift.status==='running'?<button type="button" className="fe-icon-button" aria-label="Pause shift" title="Pause" disabled={!!busy} onClick={()=>void act('pause')}><Pause size={15}/></button>
          :<button type="button" className="fe-icon-button" aria-label="Resume shift" title="Resume" disabled={!!busy} onClick={()=>void act('resume')}><Play size={15}/></button>}
        <button type="button" className="fe-icon-button" aria-label="Stop shift" title="Stop and write the report" disabled={!!busy} onClick={()=>{if(window.confirm('Stop the shift now? It writes its report: what it did, and what’s next for you.'))void act('stop');}}><Square size={14}/></button>
      </div>}</div>
    {shift&&<ShiftFeed shiftId={shift.id} running={shift.status==='running'}/>}
    {shift&&<p className="fe-cockpit-shift-next"><Clock3 size={13}/>{busy==='cycle'?'Checking in…':shift.status==='running'?(shift.nextCycleAt?`Next check-in ${clock(shift.nextCycleAt)}`:'Working now'):'Paused. Nothing runs until you resume.'}{shift.runtime==='scripted'&&<em>practice</em>}</p>}
    {(shift||last)&&<button type="button" className="fe-link" onClick={onOpenLog}>View the shift log</button>}
    {!shift&&last?.reportWikiId&&<button type="button" className="fe-link" onClick={()=>onOpenReport(last.reportWikiId!)}>Read the last shift report</button>}
    <WorkHoursLine owner={owner} view={schedule.view} onEdit={()=>setHours(true)}/>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {hours&&<WorkHoursDialog view={schedule.view} live={view.live} onClose={()=>setHours(false)} onSaved={schedule.setView}/>}
    {starting&&<StartShift view={view} onClose={()=>setStarting(false)} onStarted={started=>{setStarting(false);onChanged(started);}}/>}
  </section>;
}

/** Every cycle of the current or last shift, stage by stage, as recorded by the host. */
export function ShiftLog({view,onOpen}:{view:ShiftView|null;onOpen:(key:string)=>void}){
  const shift=view?.current||view?.recent[0];
  if(!shift)return null;
  return <section className="fe-section" aria-label="Shift log">
    <div className="fe-section-head"><div><h2>Shift log</h2><small>{shift.status==='running'||shift.status==='paused'?'Current shift':'Last shift'} · started {readableTime(shift.startedAt)} · {shift.cycles.length} check-in{shift.cycles.length===1?'':'s'} · {shift.turnsUsed} work step{shift.turnsUsed===1?'':'s'}{shift.stopReason?` · ${shift.stopReason}`:''}</small></div>
      {shift.reportWikiId&&<button type="button" onClick={()=>onOpen('wiki:'+shift.reportWikiId)}>Shift report</button>}</div>
    {shift.cycles.length===0?<p className="fe-muted">The first check-in starts within a minute.</p>:<div className="fe-list">{[...shift.cycles].reverse().slice(0,12).map(cycle=><details key={cycle.number} className="fe-cycle" open={cycle.number===shift.cycles.length}>
      <summary><strong>Check-in {cycle.number}</strong><small>{clock(cycle.startedAt)}</small><span className="fe-cycle-dots" aria-hidden="true">{cycle.stages.map(stage=><i key={stage.stage} className={stage.status}/>)}</span></summary>
      <table className="fe-table"><tbody>{cycle.stages.map(stage=><tr key={stage.stage}><td className="fe-cycle-stage" title={stageHelp[stage.stage]}>{stageLabel[stage.stage]}</td><td><span className={'fe-stage-status '+stage.status}>{({done:'Done',skipped:'Nothing to do',waiting:'Waiting',failed:'Didn’t work'} as Record<string,string>)[stage.status]||stage.status}</span></td>
        <td>{stage.summary}{stage.outputs.length>0&&<ul className="fe-cycle-outputs">{stage.outputs.map(output=>{const key=output.split(' ')[0];const openable=/^(wiki|draft|task):/.test(key);return <li key={output}>{openable?<button type="button" className="fe-link" onClick={()=>onOpen(key)}>{output}</button>:output}</li>;})}</ul>}</td></tr>)}</tbody></table>
    </details>)}</div>}
  </section>;
}
