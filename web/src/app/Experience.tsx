import {createContext,useCallback,useContext,useEffect,useState} from 'react';
import {ArrowRight,Check,ChevronRight,FileText,Lightbulb,Pause,RotateCcw,Sparkles,Target} from 'lucide-react';
import {api} from '../api';
import {readableTime,type MarketingState} from '../components/MarketingPanels';
import type {Library} from './library';
import type {ShiftView} from './shifts';
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

export function FirstWin({state,owner,onOpen,onRefresh}:{state:MarketingState;owner:boolean;onOpen:(key:string)=>void;onRefresh:()=>Promise<void>}){
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const experience=useExperience();
  if(!owner||!state.profile.product_summary.trim()||!state.profile.audience.trim()||experience?.data?.ledger.recommendations.length)return null;
  async function prepare(){
    if(busy)return;setBusy(true);setError('');
    try{const result=await api<{taskId:string;queued:boolean}>('/experience/first-win',{});await onRefresh();onOpen('task:'+result.taskId);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <section className="fe-first-win" aria-label="Your first useful win"><span className="fe-experience-eyebrow"><Sparkles size={14}/> Start with something useful</span>
    <h3>One concrete improvement to your offer.</h3><p>{state.employee.name||'Marketing'} will choose a sharper opening, a customer-objection answer, or a campaign angle—and prepare the actual copy from your brief.</p>
    <button type="button" className="primary" disabled={busy||!state.taskStoreAvailable} onClick={()=>void prepare()}>{busy?'Saving assignment…':'Prepare my first win'}<ArrowRight size={15}/></button>
    <small>Saved as an assignment for the next authorized shift. You control the shift and its budget.</small>
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
  return <section className="fe-section" aria-label="Prepared work"><div className="fe-section-head"><div><h3>Prepared work</h3><small>Recommendations with saved work behind them</small></div></div>
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

export function EmployeeContinuity({view,onOpen}:{view:ShiftView|null;onOpen:(key:string)=>void}){
  const experience=useExperience();
  const shift=view?.current||view?.recent[0];
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
  return <section className="fe-section" aria-label="Useful work and outcomes"><div className="fe-section-head"><div><h3>Is the work helping?</h3><small>Recorded feedback, rather than the number of drafts made</small></div></div>
    <dl className="fe-facts"><div><dt>Rated useful</dt><dd>{outcomes.ratedUseful}</dd></div><div><dt>Rated not useful</dt><dd>{outcomes.ratedNotUseful}</dd></div><div><dt>Revisions completed</dt><dd>{outcomes.revisionsCompleted}</dd></div><div><dt>Reported time saved</dt><dd>{outcomes.reportedMinutesSaved===null?'Not reported':outcomes.reportedMinutesSaved+' min'}</dd></div></dl>
    <p className="fe-outcome-note">Time saved is your estimate from {outcomes.timeReports} item{outcomes.timeReports===1?'':'s'}, not an automatic measurement. These verdicts don’t establish campaign lift. The scorecard holds measured business results.</p>
    <button type="button" className="fe-link" onClick={()=>onOpen('section:scorecard')}>Review the scorecard →</button>
  </section>;
}
