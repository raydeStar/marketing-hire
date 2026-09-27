import {createContext,useCallback,useContext,useEffect,useState} from 'react';
import {ArrowRight,Check,ChevronRight,FileText,Lightbulb,Pause,RotateCcw,Sparkles,Target} from 'lucide-react';
import {api} from '../api';
import {FirstShiftPanel} from './FirstShiftPanel';
import {readableTime,type MarketingState} from '../components/MarketingPanels';
import type {Library} from './library';
import type {Shift,ShiftView} from './shifts';
import {ShiftFeed} from './ShiftFeed';
import {useAttempt} from './shared';
import type {FeedbackEntry} from './Feedback';
import {useCampaigns} from './campaigns';
import './experience.css';

export type Recommendation={id:string;version:number;title:string;whyNow:string;recommendation:string;nextStep:string;hypothesis:string;measurement:string;uncertainty:string;
  outputs:string[];sources:{url:string;title:string;coverage:string}[];campaignId:string|null;shiftId:string;simulated:boolean;status:'ready'|'parked';decisionReason:string|null;createdAt:string;updatedAt:string};
type Revision={taskId:string;key:string;title:string;feedback:string;by:string;at:string;result:string|null;doneAt:string|null;original:string|null};
type ExperienceData={ledger:{recommendations:Recommendation[]};feedback:FeedbackEntry[];revisions:Revision[];notebook:{wikiId:string|null;worked:string[];didNotWork:string[];openQuestions:string[]};
  outcomes:{ratedUseful:number;ratedNotUseful:number;reportedMinutesSaved:number|null;timeReports:number;revisionsCompleted:number}};
type Experience={data:ExperienceData|null;error:string;reload:()=>Promise<void>;decide:(item:Recommendation,status:'ready'|'parked',reason?:string)=>Promise<void>};
const Context=createContext<Experience|null>(null);
export const ExperienceProvider=Context.Provider;
export function useExperience(){return useContext(Context);}

export function useExperienceData(enabled:boolean):Experience{
  const [data,setData]=useState<ExperienceData|null>(null),[error,setError]=useState('');
  const reload=useCallback(async()=>{
    if(!enabled){setData(null);setError('');return;}
    try{setData(await api<ExperienceData>('/experience'));setError('');}catch(cause){setError((cause as Error).message);}
  },[enabled]);
  useEffect(()=>{void reload();const timer=setInterval(()=>{if(document.visibilityState==='visible')void reload();},10000);return()=>clearInterval(timer);},[reload]);
  async function decide(item:Recommendation,status:'ready'|'parked',reason=''){
    await api('/experience/'+item.id+'/decision',{expectedVersion:item.version,status,reason});await reload();
  }
  return {data,error,reload,decide};
}

export function FirstWin({state,owner,onOpen,onRefresh,level=3}:{state:MarketingState;owner:boolean;onOpen:(key:string)=>void;onRefresh:()=>Promise<void>;level?:2|3}){
  const Heading=level===2?'h2':'h3';
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[queued,setQueued]=useState<string|null>(null);
  const [shifts,setShifts]=useState<ShiftView|null>(null),[started,setStarted]=useState<Shift|null>(null);
  const attempt=useAttempt();
  const experience=useExperience();
  const firstWins=state.tasks.filter(item=>item.title==='Prepare my first useful win');
  const task=firstWins.find(item=>item.status!=='done');
  // Once the first win is done, its shift's results stay on the card for a day, instead of the card offering to start again.
  const finished=!queued&&!task?firstWins.find(item=>item.status==='done'):undefined;
  const taskId=queued||task?.id||finished?.id;
  const eligible=owner&&!!state.profile.product_summary.trim()&&!!state.profile.audience.trim();
  useEffect(()=>{
    if(!eligible||!taskId)return;
    let stop=false;
    const load=()=>api<ShiftView>('/shifts').then(next=>{if(!stop){setShifts(next);setStarted(current=>current?next.recent.find(item=>item.id===current.id)||current:null);}}).catch(()=>{});
    void load();const timer=setInterval(()=>{if(document.visibilityState==='visible')void load();},5000);
    return()=>{stop=true;clearInterval(timer);};
  },[eligible,taskId]);
  const shift=started||shifts?.current||(finished?shifts?.recent.find(item=>item.endedAt&&Date.now()-new Date(item.endedAt).getTime()<86_400_000):undefined);
  if(!eligible||finished&&!shift||experience?.data?.ledger.recommendations.length&&!shift)return null;
  async function prepare(){
    if(busy)return;setBusy(true);setError('');
    try{const result=await api<{taskId:string;queued:boolean}>('/experience/first-win',{});setQueued(result.taskId);await onRefresh();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function start(){
    if(busy||!taskId)return;setBusy(true);setError('');
    try{
      const current=await api<ShiftView>('/shifts');setShifts(current);
      if(current.current){setStarted(current.current);return;}
      const next=await api<Shift>('/shifts',{requestId:attempt.id('first-win:'+taskId),hours:1,durationMinutes:30,cycleMinutes:30,turnBudget:30});
      setStarted(next);attempt.done();await onRefresh();
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <section className="fe-first-win" aria-label="Your first useful win"><span className="fe-experience-eyebrow"><Sparkles size={14}/> Start with something useful</span>
    <Heading>Your first shift: a week of posts, your site's biggest fix, and one competitor.</Heading><p>{state.employee.name||'Marketing'} prepares five posts for you to approve, the one change that matters most on your site with the copy written, and a snapshot of a competitor—all from your brief.</p>
    {!taskId?<><button type="button" className="primary" disabled={busy||!state.taskStoreAvailable} onClick={()=>void prepare()}>{busy?'Saving assignment…':'Prepare my first win'}<ArrowRight size={15}/></button><small>Saved as an assignment for the next authorized shift. You control the shift and its budget.</small></>
      :<><p className="fe-first-win-receipt" role="status"><Check size={14}/> Assignment saved.{!shift?' Ready when you are.':''}</p>
        {!shift&&<><button type="button" className="primary" disabled={busy} onClick={()=>void start()}>{busy?'Starting shift…':'Start a 30-minute shift now'}</button><small>Authorizes 30 minutes of work, a 30-minute cycle and up to 30 model turns. Other ready assignments may also be worked on.</small></>}
        {shift&&<><p>{shift.runtime==='scripted'?'Simulated shift · ':''}{shift.status==='running'?'Working on the saved assignments.':shift.status==='paused'?'Shift paused.':'Shift '+shift.status+'.'} Ends {readableTime(shift.endsAt)}.</p><FirstShiftPanel shiftId={shift.id} running={shift.status==='running'||shift.status==='finishing'} onOpen={onOpen}/><button type="button" className="fe-link" onClick={()=>onOpen('section:shifts')}>Open shift controls and report →</button></>}
        <button type="button" className="fe-link" onClick={()=>onOpen('task:'+taskId)}>Open the assignment →</button></>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}

/** One recommendation first. Opening it never records approval or starts work. */
export function PreparedSpotlight({onOpen}:{onOpen:(key:string)=>void}){
  const experience=useExperience();
  const item=experience?.data?.ledger.recommendations.filter(item=>item.status==='ready').at(-1);
  if(experience?.error)return <p className="fe-alert" role="status">Recommendations couldn’t refresh. {experience.error}</p>;
  if(!item)return null;
  return <section className="fe-prepared-spotlight" aria-label="Prepared recommendation">
    <span className="fe-experience-eyebrow"><Lightbulb size={14}/>{item.simulated?'Simulated · prepared for review':'Prepared for you'}</span>
    <h3>{item.title}</h3><p>{item.whyNow}</p><p className="fe-prepared-choice">{item.recommendation}</p>
    <button type="button" onClick={()=>onOpen('recommendation:'+item.id)}>Review the package <ChevronRight size={15}/></button>
    <small>{item.outputs.length} saved item{item.outputs.length===1?'':'s'} · {readableTime(item.createdAt)}</small>
  </section>;
}

function titleOf(key:string,state:MarketingState,library:Library){
  const draft=state.drafts.find(item=>'draft:'+item.id===key);
  if(draft)return draft.channel+' · '+draft.content.split('\n').find(line=>line.trim())?.slice(0,65);
  return library.items.find(item=>item.key===key)?.title||key;
}

export function PreparedWorkList({onOpen}:{onOpen:(key:string)=>void}){
  const experience=useExperience();
  const items=experience?.data?.ledger.recommendations.slice().reverse()||[];
  if(!items.length)return null;
  return <section className="fe-section" aria-label="Prepared work"><div className="fe-section-head"><div><h2>Prepared work</h2><small>Recommendations with saved work behind them</small></div></div>
    {items.filter(item=>item.status==='ready').slice(0,6).map(item=><button type="button" className="fe-list-row" key={item.id} onClick={()=>onOpen('recommendation:'+item.id)}><Lightbulb size={16}/><span className="fe-list-main"><strong>{item.title}</strong><small>{item.simulated?'Simulated · ':''}{item.outputs.length} saved items · {item.whyNow}</small></span><ChevronRight size={16}/></button>)}
    {items.some(item=>item.status==='parked')&&<details><summary>Parked recommendations</summary>{items.filter(item=>item.status==='parked').map(item=><button type="button" className="fe-list-row" key={item.id} onClick={()=>onOpen('recommendation:'+item.id)}><Pause size={15}/><span className="fe-list-main"><strong>{item.title}</strong><small>{item.decisionReason||'Parked for later review'}</small></span><ChevronRight size={16}/></button>)}</details>}
  </section>;
}
function excerptOf(key:string,state:MarketingState,library:Library){
  return state.drafts.find(item=>'draft:'+item.id===key)?.content||library.items.find(item=>item.key===key)?.summary||'';
}

export function PackageItems({keys,state,library,onOpen}:{keys:string[];state:MarketingState;library:Library;onOpen:(key:string)=>void}){
  return <div className="fe-package-grid">{keys.map(key=>{
    const draft=state.drafts.find(item=>'draft:'+item.id===key),item=library.items.find(item=>item.key===key);
    const measured=key.startsWith('exp:');
    const available=measured||key.startsWith('pagecopy:')||!!draft||!!item&&!item.archived;
    const preview=excerptOf(key,state,library).replace(/^#+\s*/,'').slice(0,220);
    return <button type="button" className="fe-package-item" key={key} disabled={!available} onClick={()=>onOpen(measured?'section:scorecard':key)}>
      <span className="fe-experience-eyebrow"><FileText size={13}/>{draft?draft.status==='approved'?'Approved · not posted':draft.status==='posted'?'Posted':draft.status==='pending'?'Needs your approval':draft.status:item?.label||'Prepared item'}</span>
      <strong>{titleOf(key,state,library)}</strong>{preview&&<p>{preview}{excerptOf(key,state,library).length>220?'…':''}</p>}
      <small>{available?'Open and inspect →':'Item unavailable; saved reference retained'}</small>
    </button>;
  })}</div>;
}

export function RecommendationReview({id,state,library,owner,onOpen,onChat}:{id:string;state:MarketingState;library:Library;owner:boolean;onOpen:(key:string)=>void;onChat:(text:string,send?:boolean)=>void}){
  const experience=useExperience(),book=useCampaigns();
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[parking,setParking]=useState(false),[reason,setReason]=useState('');
  const item=experience?.data?.ledger.recommendations.find(item=>item.id===id);
  if(!item)return <p className="fe-muted">{experience?.error||(!experience?.data?'Loading the recommendation…':'This recommendation is no longer available.')}</p>;
  const campaign=book?.ledger?.campaigns.find(campaign=>campaign.id===item.campaignId);
  const keys=[...new Set([...item.outputs,...(campaign?book?.keysOf(campaign.id)||[]:[])])].filter(key=>!key.startsWith('task:'));
  async function decide(status:'ready'|'parked'){
    if(!experience||!item||busy)return;setBusy(true);setError('');
    try{await experience.decide(item,status,reason);setParking(false);}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <article className="fe-doc fe-recommendation-review">
    <span className="fe-experience-eyebrow">{item.simulated?'Simulated work · ':' '}{item.status==='parked'?'Parked':'Prepared for review'}</span>
    <h2>{item.title}</h2><p className="fe-lead">{item.whyNow}</p>
    <small className="fe-muted">Recommendation recorded {readableTime(item.createdAt)}. Package previews show the current saved work.</small>
    <section aria-label="My recommendation"><h3>My recommendation</h3><p>{item.recommendation}</p></section>
    {campaign&&<button type="button" className="fe-link" onClick={()=>onOpen('campaign:'+campaign.id)}>Part of {campaign.name} →</button>}
    <section aria-label="Review the package"><h3>The work is ready to inspect</h3><PackageItems keys={keys} state={state} library={library} onOpen={onOpen}/></section>
    <section className="fe-prepared-next" aria-label="Next decision"><h3>The next decision</h3><p>{item.nextStep}</p>
      <small>Opening or parking this recommendation doesn’t approve its drafts. Each draft keeps its own review and publishing controls.</small>
      {owner&&<div className="fe-actions"><button type="button" onClick={()=>onChat(`About the prepared recommendation “${item.title}” (recommendation:${item.id}), change direction: `)}>Change direction</button>
        {item.status==='parked'?<button type="button" disabled={busy} onClick={()=>void decide('ready')}><RotateCcw size={14}/> Reopen</button>:<button type="button" className="fe-ghost" disabled={busy} onClick={()=>setParking(value=>!value)}><Pause size={14}/> Park it</button>}</div>}
      {parking&&<div className="fe-form"><label>Reason to park <span className="fe-muted">(optional)</span><textarea value={reason} maxLength={600} rows={2} onChange={event=>setReason(event.target.value)}/></label><button type="button" disabled={busy} onClick={()=>void decide('parked')}>Save decision</button></div>}
      {item.decisionReason&&<p>Decision: {item.decisionReason}</p>}{error&&<p role="alert" className="fe-alert">{error}</p>}
    </section>
    <section aria-label="Hypothesis and evidence"><h3>What we hope to change</h3><p>{item.hypothesis||'A hypothesis has not been set for this work.'}</p><p><strong>How to judge it:</strong> {item.measurement||'Measurement not set. Choose a metric and review condition before treating this as an experiment.'}</p><p className="fe-muted">{item.uncertainty}</p></section>
    <details><summary>Sources and coverage ({item.sources.length})</summary>{item.sources.length?<ul>{item.sources.map(source=><li key={source.url}><a href={source.url} target="_blank" rel="noopener noreferrer">{source.title||source.url}</a><small className="fe-source-coverage">{source.coverage}</small></li>)}</ul>:<p>No external sources were attached. Inspect the work’s brief references before relying on its claims.</p>}</details>
  </article>;
}

/** Concrete revision receipts, not claims that the employee has learned a general skill. */
export function LearningTrail({state,library,onOpen,itemKey}:{state:MarketingState;library?:Library;onOpen:(key:string)=>void;itemKey?:string}){
  const experience=useExperience();
  const revisions=experience?.data?.revisions.filter(item=>!itemKey||item.key===itemKey||item.result===itemKey).slice().reverse().slice(0,itemKey?3:4)||[];
  if(!revisions.length)return null;
  return <section className="fe-learning-trail" aria-label="Feedback in action"><h3>Feedback in action</h3>{revisions.map(item=>{
    const after=library&&item.result?(state.drafts.find(draft=>'draft:'+draft.id===item.result)?.content||library.wiki.find(page=>'wiki:'+page.id===item.result)?.body):undefined;
    return <article key={item.taskId}><span className="fe-experience-eyebrow">{item.doneAt?<><Check size={13}/> Revision saved</>:<>Queued for the next shift</>}</span><strong>{item.title}</strong>
      <blockquote>{item.feedback}</blockquote><small>{item.by} · {readableTime(item.at)}</small>
      <button type="button" className="fe-link" onClick={()=>onOpen(item.result||'task:'+item.taskId)}>{item.doneAt?'Inspect the saved revision':'Open the revision task'} →</button>
      {item.original&&after&&<details><summary>Compare your feedback with the work</summary><div className="fe-revision-compare"><div><h4>When you asked for changes</h4><pre>{item.original}</pre></div><div><h4>Current saved version</h4><pre>{after}</pre></div></div><small>Compare the text to judge whether the feedback was answered.</small></details>}
    </article>;
  })}</section>;
}

type Continuity={finished:string[];changedMind:string[];needsYou:number;needsYouTop:string[];next:string;bets:{id:string;title:string;hypothesis:string;measurement:string;status:string;result:string;uncertainty:string}[];since:string|null};

export function EmployeeContinuity({view,onOpen}:{view:ShiftView|null;onOpen:(key:string)=>void}){
  const [continuity,setContinuity]=useState<Continuity|null>(null),[stale,setStale]=useState(false);
  const experience=useExperience();
  const shift=view?.current||view?.recent[0];
  useEffect(()=>{
    let stop=false;
    const load=()=>api<Continuity>('/continuity').then(next=>{if(!stop){setContinuity(next);setStale(false);}}).catch(()=>{if(!stop)setStale(true);});
    void load();const timer=setInterval(()=>{if(document.visibilityState==='visible')void load();},15000);
    return()=>{stop=true;clearInterval(timer);};
  },[shift?.id,shift?.status,shift?.cycles.length]);
  if(continuity)return <section className="fe-continuity" aria-label="Where we stand"><span className="fe-experience-eyebrow"><Target size={14}/> Where we stand</span>
    {continuity.since&&<small>Since {readableTime(continuity.since)}{view?.live===false?' · Simulated work':''}</small>}
    <p><strong>{continuity.needsYou?`${continuity.needsYou} decision${continuity.needsYou===1?'':'s'} waiting for you.`:'Nothing needs you right now.'}</strong></p>
    {!!continuity.needsYouTop.length&&<details><summary>What needs you ({continuity.needsYouTop.length})</summary><ul>{continuity.needsYouTop.map((title,index)=><li key={index}>{title}</li>)}</ul></details>}
    <p className="fe-continuity-next"><strong>Next:</strong> {continuity.next}</p>
    {!!continuity.finished.length&&<details><summary>Finished ({continuity.finished.length})</summary><ul>{continuity.finished.map((title,index)=><li key={index}>{title}</li>)}</ul></details>}
    {!!continuity.changedMind.length&&<details><summary>What changed my mind ({continuity.changedMind.length})</summary><ul>{continuity.changedMind.map((text,index)=><li key={index}>{text}</li>)}</ul></details>}
    {!!continuity.bets.length&&<details><summary>Bets and results ({continuity.bets.length})</summary><div className="fe-continuity-bets">{continuity.bets.map(bet=><article key={bet.id}><h4>{bet.title}</h4><span className="fe-pill">{bet.status==='ready'?'Prepared':bet.status==='parked'?'Parked':bet.status}</span><dl><div><dt>Hypothesis</dt><dd>{bet.hypothesis||'Not recorded'}</dd></div><div><dt>How to judge it</dt><dd>{bet.measurement||'Not recorded'}</dd></div><div><dt>Result so far</dt><dd>{bet.result}</dd></div>{bet.uncertainty&&<div><dt>Uncertainty</dt><dd>{bet.uncertainty}</dd></div>}</dl><button type="button" className="fe-link" onClick={()=>onOpen('recommendation:'+bet.id)}>Inspect the prepared work →</button></article>)}</div></details>}
    {shift?.reportWikiId&&<button type="button" className="fe-link" onClick={()=>onOpen('wiki:'+shift.reportWikiId)}>Read the shift report →</button>}
    {stale&&<small role="status">The summary couldn’t refresh. Showing the last saved response.</small>}
  </section>;
  if(!shift)return null;
  const priorities=shift.cycles.at(-1)?.stages.find(stage=>stage.stage==='prioritize');
  const prepared=experience?.data?.ledger.recommendations.filter(item=>item.shiftId===shift.id&&item.status==='ready')||[];
  const active=view?.current;
  return <section className="fe-continuity" aria-label="Where we stand"><span className="fe-experience-eyebrow"><Target size={14}/> Where we stand</span>
    <p><strong>{shift.created.length} saved item{shift.created.length===1?'':'s'}</strong> from {view?.live?'the employee':'a simulated shift'}. {prepared.length?`${prepared.length} recommendation${prepared.length===1?'':'s'} ready to inspect.`:'Open the report to see the work and its limits.'}</p>
    {priorities?.summary&&<details><summary>Why this work</summary><p>{priorities.summary}</p></details>}
    <small>{active?active.status==='paused'?'The shift is paused. Resume it when you are ready.':active.status==='finishing'?'Finishing the shift report.':active.nextCycleAt?'Next cycle '+readableTime(active.nextCycleAt):'The current cycle is in progress.':'Off shift. Assigned work waits for the next authorized shift.'}</small>
    {shift.reportWikiId&&<button type="button" className="fe-link" onClick={()=>onOpen('wiki:'+shift.reportWikiId)}>Read the shift report →</button>}
    {experience?.data?.notebook.wikiId&&<button type="button" className="fe-link" onClick={()=>onOpen('wiki:'+experience.data!.notebook.wikiId)}>Open the marketing notebook →</button>}
  </section>;
}

export function OutcomeSnapshot({onOpen}:{onOpen:(key:string)=>void}){
  const experience=useExperience(),outcomes=experience?.data?.outcomes;
  if(!outcomes)return null;
  return <section className="fe-section" aria-label="Useful work and outcomes"><div className="fe-section-head"><div><h2>Is the work helping?</h2><small>Recorded feedback, rather than the number of drafts made</small></div></div>
    <dl className="fe-facts"><div><dt>Rated useful</dt><dd>{outcomes.ratedUseful}</dd></div><div><dt>Rated not useful</dt><dd>{outcomes.ratedNotUseful}</dd></div><div><dt>Revisions completed</dt><dd>{outcomes.revisionsCompleted}</dd></div><div><dt>Reported time saved</dt><dd>{outcomes.reportedMinutesSaved===null?'Not reported':outcomes.reportedMinutesSaved+' min'}</dd></div></dl>
    <p className="fe-outcome-note">Time saved is your estimate from {outcomes.timeReports} item{outcomes.timeReports===1?'':'s'}, not an automatic measurement. These verdicts don’t establish campaign lift. The scorecard holds measured business results.</p>
    <button type="button" className="fe-link" onClick={()=>onOpen('section:scorecard')}>Review the scorecard →</button>
  </section>;
}
