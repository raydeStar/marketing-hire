import {useState} from 'react';
import {Check,Clock3,FastForward,Pause,Play,Square,X} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {stageHelp,stageLabel,type Shift,type ShiftView} from './shifts';
import {Dialog} from './shared';

// The real length, from start to end: short test shifts are not rounded up to an hour.
const length=(shift:Shift)=>{const minutes=Math.round((Date.parse(shift.endsAt)-Date.parse(shift.startedAt))/60000);return minutes%60===0?`${minutes/60}h`:minutes<60?`${minutes} min`:`${Math.floor(minutes/60)}h ${minutes%60}m`;};
const clock=(value:string|null)=>value?new Date(value).toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'}):'—';

/** The eight stages of the operating loop, with how each went in the latest cycle. */
export function StageStrip({shift,stages}:{shift:Shift|null;stages:string[]}){
  const last=shift?.cycles[shift.cycles.length-1];
  return <ol className="fe-stages" aria-label="Operating loop">{stages.map(stage=>{const record=last?.stages.find(item=>item.stage===stage);
    return <li key={stage} className={record?.status||'idle'} title={`${stageLabel[stage]}: ${record?record.summary:stageHelp[stage]}`}>
      <span aria-hidden="true">{record?.status==='done'?<Check size={11}/>:record?.status==='failed'?<X size={11}/>:null}</span>{stageLabel[stage]}</li>;})}</ol>;
}

function StartShift({view,onClose,onStarted}:{view:ShiftView;onClose:()=>void;onStarted:(shift:Shift)=>void}){
  const [hours,setHours]=useState(8),[cycle,setCycle]=useState(60),[budget,setBudget]=useState(''),[tokens,setTokens]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const suggested=Math.max(6,Math.round(hours*60/cycle*2));
  // Live turns average about 3,000 tokens with research and review; a live shift always gets a token limit.
  const suggestedTokens=Math.min(2000000,Math.max(8000,Math.round((Number(budget)||suggested)*3)*1000));
  async function start(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{onStarted(await api<Shift>('/shifts',{requestId:crypto.randomUUID(),hours,cycleMinutes:cycle,turnBudget:Number(budget)||suggested,tokenBudget:Number(tokens)||(view.live?suggestedTokens:null)}));}
    catch(cause){setError((cause as Error).message);setBusy(false);}
  }
  return <Dialog title="Start a shift" onClose={onClose}><form className="fe-form" onSubmit={event=>void start(event)}>
    <p className="fe-muted">The employee works the loop on its own: sense, prioritize, create, align, launch, measure, decide, learn. It never posts, sends or spends; public-facing work comes to you as drafts.</p>
    <fieldset className="fe-choice-row"><legend>Length</legend>{[1,8,16,24].map(value=><label key={value} className={hours===value?'active':''}><input type="radio" name="shift-hours" checked={hours===value} onChange={()=>setHours(value)}/>{value===1?'1 hour':`${value} hours`}</label>)}</fieldset>
    <label>Check in every<select value={cycle} onChange={event=>setCycle(Number(event.target.value))}><option value={30}>30 minutes</option><option value={60}>hour</option><option value={120}>2 hours</option><option value={240}>4 hours</option></select></label>
    <label>Model-turn budget<input inputMode="numeric" value={budget} onChange={event=>setBudget(event.target.value.replace(/\D/g,''))} placeholder={`${suggested} (suggested)`}/></label>
    <label>Token limit<input inputMode="numeric" value={tokens} onChange={event=>setTokens(event.target.value.replace(/\D/g,''))} placeholder={view.live?`${suggestedTokens.toLocaleString()} (suggested)`:'Not needed for the stand-in'}/></label>
    <small>Cycles with nothing to act on spend no model turns. The shift ends early when either budget is used; each turn is checked before it is sent, and room is kept for the shift report.</small>
    {!view.live&&<p className="fe-notice">Runtime: <strong>scripted stand-in</strong>. The loop, records and effects are real; the writing is placeholder text and costs nothing. Live shifts use the local OpenClaw employee once enabled.</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy}>{busy?'Starting…':`Start ${hours}-hour shift`}</button></footer>
  </form></Dialog>;
}

/** The cockpit's shift control: status, the stage strip, budget, and pause / stop / run now for the owner. */
export function ShiftPanel({view,owner,onChanged,onOpenReport,onOpenLog}:{view:ShiftView|null;owner:boolean;onChanged:(shift?:Shift)=>void;onOpenReport:(wikiId:string)=>void;onOpenLog:()=>void}){
  const [starting,setStarting]=useState(false),[busy,setBusy]=useState<string|null>(null),[error,setError]=useState('');
  if(!view)return null;
  const shift=view.current,last=view.recent.find(item=>item.status==='completed'||item.status==='stopped');
  async function act(action:string){
    if(!shift||busy)return;setBusy(action);setError('');
    try{onChanged(await api<Shift>(`/shifts/${shift.id}/${action}`,{}));}catch(cause){setError((cause as Error).message);}finally{setBusy(null);}
  }
  return <section className="fe-cockpit-shift" aria-label="Shift">
    <div className="fe-cockpit-shift-head"><div><strong>{shift?shift.status==='paused'?'Shift paused':'On shift':'Off shift'}</strong>
      <small>{shift?`${length(shift)} · ends ${clock(shift.endsAt)} · ${shift.turnsUsed}/${shift.turnBudget} turns${shift.tokenBudget?` · ${shift.tokensUsed.toLocaleString()}/${shift.tokenBudget.toLocaleString()} tokens`:''}`:last?`Last shift ended ${readableTime(last.endedAt||last.startedAt)}`:'No shift yet'}</small></div>
      {owner&&!shift&&<button type="button" className="primary" onClick={()=>setStarting(true)}><Play size={14}/> Start shift</button>}
      {owner&&shift&&<div className="fe-cockpit-shift-actions">
        <button type="button" className="fe-icon-button" aria-label="Run a cycle now" title="Run a cycle now" disabled={!!busy||shift.status!=='running'} onClick={()=>void act('cycle')}><FastForward size={15}/></button>
        {shift.status==='running'?<button type="button" className="fe-icon-button" aria-label="Pause shift" title="Pause" disabled={!!busy} onClick={()=>void act('pause')}><Pause size={15}/></button>
          :<button type="button" className="fe-icon-button" aria-label="Resume shift" title="Resume" disabled={!!busy} onClick={()=>void act('resume')}><Play size={15}/></button>}
        <button type="button" className="fe-icon-button" aria-label="Stop shift" title="Stop and write the report" disabled={!!busy} onClick={()=>{if(window.confirm('Stop the shift? The employee writes its shift report now.'))void act('stop');}}><Square size={14}/></button>
      </div>}</div>
    <StageStrip shift={shift||last||null} stages={view.stages}/>
    {shift&&<p className="fe-cockpit-shift-next"><Clock3 size={13}/>{busy==='cycle'?'Running a cycle…':shift.status==='running'?`Cycle ${shift.cycles.length} done · next ${clock(shift.nextCycleAt)}`:'Paused. Nothing runs until you resume.'}{shift.runtime==='scripted'&&<em>scripted</em>}</p>}
    {(shift||last)&&<button type="button" className="fe-link" onClick={onOpenLog}>View the shift log</button>}
    {!shift&&last?.reportWikiId&&<button type="button" className="fe-link" onClick={()=>onOpenReport(last.reportWikiId!)}>Read the last shift report</button>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {starting&&<StartShift view={view} onClose={()=>setStarting(false)} onStarted={started=>{setStarting(false);onChanged(started);}}/>}
  </section>;
}

/** Every cycle of the current or last shift, stage by stage, as recorded by the host. */
export function ShiftLog({view,onOpen}:{view:ShiftView|null;onOpen:(key:string)=>void}){
  const shift=view?.current||view?.recent[0];
  if(!shift)return null;
  return <section className="fe-section" aria-label="Shift log">
    <div className="fe-section-head"><div><h3>Shift log</h3><small>{shift.status==='running'||shift.status==='paused'?'Current shift':'Last shift'} · started {readableTime(shift.startedAt)} · {shift.cycles.length} cycle{shift.cycles.length===1?'':'s'} · {shift.turnsUsed} model turn{shift.turnsUsed===1?'':'s'}{shift.stopReason?` · ${shift.stopReason}`:''}</small></div>
      {shift.reportWikiId&&<button type="button" onClick={()=>onOpen('wiki:'+shift.reportWikiId)}>Shift report</button>}</div>
    {shift.cycles.length===0?<p className="fe-muted">The first cycle starts within a minute.</p>:<div className="fe-list">{[...shift.cycles].reverse().slice(0,12).map(cycle=><details key={cycle.number} className="fe-cycle" open={cycle.number===shift.cycles.length}>
      <summary><strong>Cycle {cycle.number}</strong><small>{clock(cycle.startedAt)}</small><span className="fe-cycle-dots" aria-hidden="true">{cycle.stages.map(stage=><i key={stage.stage} className={stage.status}/>)}</span></summary>
      <table className="fe-table"><tbody>{cycle.stages.map(stage=><tr key={stage.stage}><td className="fe-cycle-stage">{stageLabel[stage.stage]}</td><td><span className={'fe-stage-status '+stage.status}>{stage.status}</span></td>
        <td>{stage.summary}{stage.outputs.length>0&&<ul className="fe-cycle-outputs">{stage.outputs.map(output=>{const key=output.split(' ')[0];const openable=/^(wiki|draft|task):/.test(key);return <li key={output}>{openable?<button type="button" className="fe-link" onClick={()=>onOpen(key)}>{output}</button>:output}</li>;})}</ul>}</td></tr>)}</tbody></table>
    </details>)}</div>}
  </section>;
}
