import {useEffect,useState} from 'react';
import {Clock3,FastForward,Pause,Play,Square} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {stageHelp,stageLabel,type Shift,type ShiftView} from './shifts';
import {Dialog} from './shared';
import {WorkHoursDialog,WorkHoursLine,useWorkSchedule} from './WorkHours';
import {ShiftFeed,useCurrentActivity} from './ShiftFeed';

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

/** What it's doing this moment, said first: "Now: Writing “Your first week of posts”", or that it's between check-ins. */
function NowLine({shiftId,running,between,next}:{shiftId:string;running:boolean;between:boolean;next:string|null}){
  const activity=useCurrentActivity(shiftId,running);
  if(!running)return null;
  return <p className="fe-shift-now" role="status" aria-live="polite">{activity&&!between?<><i className="fe-dot busy" aria-hidden="true"/><strong>Now:</strong> {activity}</>
    :<><i className="fe-dot live" aria-hidden="true"/><span>Between check-ins{between&&next?`; the next one is at ${clock(next)}`:''}. Everything so far is below.</span></>}</p>;
}

/** The cockpit's shift control: status, the stage strip, budget, and pause / stop / run now for the owner. */
export function ShiftPanel({view,owner,onChanged,onOpenReport,onOpenLog}:{view:ShiftView|null;owner:boolean;onChanged:(shift?:Shift)=>void;onOpenReport:(wikiId:string)=>void;onOpenLog:()=>void}){
  const [starting,setStarting]=useState(false),[busy,setBusy]=useState<string|null>(null),[error,setError]=useState(''),[hours,setHours]=useState(false);
  const schedule=useWorkSchedule();
  useEffect(()=>{void schedule.load();},[view?.current?.id]);
  if(!view)return null;
  const shift=view.current,last=view.recent.find(item=>item.status==='completed'||item.status==='stopped');
  const between=shift?.status==='running'&&!!shift.nextCycleAt&&Date.parse(shift.nextCycleAt)>Date.now();
  async function act(action:string){
    if(!shift||busy)return;setBusy(action);setError('');
    try{onChanged(await api<Shift>(`/shifts/${shift.id}/${action}`,{}));}catch(cause){setError((cause as Error).message);}finally{setBusy(null);}
  }
  return <section className="fe-cockpit-shift" aria-label="Shift">
    <div className="fe-cockpit-shift-head"><div><strong>{shift?.requests?'Working on what you asked':shift?shift.status==='paused'?'Shift paused':'On shift':'Off shift'}</strong>
      <small title={shift?`${length(shift)} shift · ${shift.turnsUsed} of ${shift.turnBudget} work steps${shift.tokenBudget?` · ${shift.tokensUsed.toLocaleString()} of ${shift.tokenBudget.toLocaleString()} tokens`:''}`:undefined}>{shift?.requests?'It stops when your list is done. Start a shift for it to find work of its own.':shift?`Working until ${clock(shift.endsAt)}`:last?`Last shift ended ${readableTime(last.endedAt||last.startedAt)}`:'Not working right now'}</small></div>
      {owner&&!shift&&<button type="button" className="primary" onClick={()=>setStarting(true)}><Play size={14}/> Start shift</button>}
      {owner&&shift?.requests&&<button type="button" className="fe-icon-button" aria-label="Stop working on requests" title="Stop" disabled={!!busy} onClick={()=>void act('stop')}><Square size={14}/></button>}
      {owner&&shift&&!shift.requests&&<div className="fe-cockpit-shift-actions">
        <button type="button" className="fe-icon-button" aria-label="Check in now" title="Look for new work now" disabled={!!busy||shift.status!=='running'} onClick={()=>void act('cycle')}><FastForward size={15}/></button>
        {shift.status==='running'?<button type="button" className="fe-icon-button" aria-label="Pause shift" title="Pause" disabled={!!busy} onClick={()=>void act('pause')}><Pause size={15}/></button>
          :<button type="button" className="fe-icon-button" aria-label="Resume shift" title="Resume" disabled={!!busy} onClick={()=>void act('resume')}><Play size={15}/></button>}
        <button type="button" className="fe-icon-button" aria-label="Stop shift" title="Stop and write the report" disabled={!!busy} onClick={()=>{if(window.confirm('Stop the shift now? It writes its report: what it did, and what’s next for you.'))void act('stop');}}><Square size={14}/></button>
      </div>}</div>
    {shift&&<NowLine shiftId={shift.id} running={shift.status==='running'} between={between} next={shift.nextCycleAt}/>}
    {shift&&<ShiftFeed shiftId={shift.id} running={shift.status==='running'}/>}
    {shift&&<p className="fe-cockpit-shift-next"><Clock3 size={13}/>{busy==='cycle'?'Checking in…':shift.status==='running'?(between?`Next check-in ${clock(shift.nextCycleAt)}`:'Working now'):'Paused. Nothing runs until you resume.'}{shift.runtime==='scripted'&&<em>practice</em>}</p>}
    {(shift||last)&&<button type="button" className="fe-link" onClick={onOpenLog}>View the shift log</button>}
    {!shift&&last?.reportWikiId&&<button type="button" className="fe-link" onClick={()=>onOpenReport(last.reportWikiId!)}>Read the last shift report</button>}
    <WorkHoursLine owner={owner} view={schedule.view} onEdit={()=>setHours(true)}/>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {hours&&<WorkHoursDialog view={schedule.view} live={view.live} onClose={()=>setHours(false)} onSaved={schedule.setView}/>}
    {starting&&<StartShift view={view} onClose={()=>setStarting(false)} onStarted={started=>{setStarting(false);onChanged(started);}}/>}
  </section>;
}

/** A piece's review, as the host records it ("Title: Marketing rubric B → B over 2 passes (Strategy B, …), revised: fix; fix.
 * Checked against the assignment: … The assignment: 5 of 6 done ✗ (still to do: …)."), said as one line with the detail folded. */
export function ReviewLine({text}:{text:string}){
  const head=/^(.+?): Marketing rubric ([A-F](?: → [A-F] over \d+ passes)?)(?: \(([^)]*)\))?, ([\s\S]*)$/.exec(text);
  if(!head)return <p>{text}</p>;
  const [,title,grade,categories,rest]=head;
  const markers=[' Checked against the assignment: ',' The assignment: ',' Your notes: '];
  const cut=(from:string)=>{const at=markers.map(marker=>rest.indexOf(marker,rest.indexOf(from)+from.length)).filter(index=>index>=0);return at.length?Math.min(...at):rest.length;};
  const part=(marker:string)=>{const at=rest.indexOf(marker);return at<0?'':rest.slice(at+marker.length,cut(marker)).trim().replace(/\.$/,'');};
  const first=markers.map(marker=>rest.indexOf(marker)).filter(index=>index>=0);
  const opening=rest.slice(0,first.length?Math.min(...first):rest.length).trim().replace(/\.$/,'');
  const colon=opening.indexOf(': ');
  const outcome=colon<0?opening:opening.slice(0,colon);
  const fixes=colon<0?[]:opening.slice(colon+2).split('; ').filter(Boolean);
  const final=grade.split(' → ').at(-1)!.slice(0,1),passes=/over (\d+) passes/.exec(grade)?.[1];
  const asked=(part(' The assignment: ')||part(' Your notes: ')).replace(/\.?\s*\d+ top score\(s\) lowered[^]*$/,'');
  const checked=part(' Checked against the assignment: ');
  return <div className="fe-log-review">
    <p><strong>{title}</strong> <span className={'fe-grade g-'+final.toLowerCase()}>{final}</span> <small>{passes?`after ${passes} review passes`:'after one review'}, {outcome}</small></p>
    {asked&&<small className="fe-muted">Assignment: {asked}</small>}
    {(fixes.length>0||checked||categories)&&<details><summary>What the review asked for{fixes.length?` (${fixes.length})`:''}</summary>
      {fixes.length>0&&<ul>{fixes.map((fix,index)=><li key={index}>{fix.replace(/\.$/,'')}</li>)}</ul>}
      {checked&&<p><strong>Checked against the assignment:</strong> {checked}</p>}
      {categories&&<p><strong>Grades:</strong> {categories}</p>}</details>}
  </div>;
}

/** A stage's summary, a line at a time: what it read, each piece's review, where it saved it. */
// Logs written before one note a line: split where a note begins, and where a piece's review begins after a sentence.
const noteStart=/(?<=[.✓✗)])\s+(?=(?:Read |Checked \d+ pages|Wrote |Drafted |The complete|Could not|Research for|Rejected |Budget reached|Self-review|Filed with|Attached the plan|Prepared the campaign|Redrafted|Busy;|The answer))/;
function notes(text:string){
  return text.split('\n').flatMap(line=>line.split(noteStart)).flatMap(chunk=>{
    const at=chunk.indexOf(': Marketing rubric '),before=at>0?chunk.lastIndexOf('. ',at):-1;
    return before>0?[chunk.slice(0,before+1),chunk.slice(before+2)]:[chunk];
  }).map(line=>line.trim()).filter(Boolean);
}
function StageSummary({text}:{text:string}){
  const lines=notes(text);
  return <div className="fe-stage-summary">{lines.map((line,index)=>/: Marketing rubric [A-F]/.test(line)?<ReviewLine key={index} text={line}/>:<p key={index}>{line}</p>)}</div>;
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
        <td><StageSummary text={stage.summary}/>{stage.outputs.length>0&&<ul className="fe-cycle-outputs">{stage.outputs.map(output=>{const key=output.split(' ')[0];const openable=/^(wiki|draft|task|pagecopy|exp|media):/.test(key);const label=openable&&output.includes(' ')?output.slice(output.indexOf(' ')+1):output;return <li key={output}>{openable?<button type="button" className="fe-link" onClick={()=>onOpen(key)}>{label}</button>:label}</li>;})}</ul>}</td></tr>)}</tbody></table>
    </details>)}</div>}
  </section>;
}
