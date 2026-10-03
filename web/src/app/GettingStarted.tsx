import {useEffect,useState,type ReactNode} from 'react';
import {Check,ChevronRight,X} from 'lucide-react';
import {api,noted,scoped} from '../api';
import {isFirstWin,type MarketingState} from '../components/MarketingPanels';
import type {State} from '../types';
import {briefComplete} from './BriefEditor';
import {FirstSteps} from './FirstSteps';
import {FirstWin} from './Experience';
import {openSiteConnect} from './PublishingView';

const dismissKey='fe-getting-started-dismissed';

type StartProps={state:MarketingState;goalsSet?:boolean;onBrief:()=>void;onGoals?:()=>void;onMeeting:()=>void;onPage:()=>void;onInvite:()=>void;onRefresh?:()=>Promise<void>;onOpen?:(key:string)=>void};

/** The first-day steps, each checked from real workspace data, not from clicks, and each one click to do. */
function useStartSteps({state,goalsSet,onBrief,onGoals,onMeeting,onPage,onInvite,onOpen}:StartProps){
  const [dismissed,setDismissed]=useState(()=>{try{return noted(dismissKey)==='yes';}catch{return false;}});
  const [pages,setPages]=useState<number|null>(null),[teammates,setTeammates]=useState<number|null>(null),[working,setWorking]=useState<boolean|null>(null),[site,setSite]=useState<boolean|null>(null),[ownSite,setOwnSite]=useState<boolean|null>(null);
  useEffect(()=>{
    if(dismissed)return;
    void api<State>('/state').then(legacy=>setPages((legacy.artifacts||[]).filter(app=>!app.archived).length)).catch(()=>setPages(0));
    void api<{devices:{owner:boolean}[]}>('/devices').then(result=>setTeammates(result.devices.filter(device=>!device.owner).length)).catch(()=>setTeammates(0));
    void Promise.all([api<{schedule:{enabled:boolean}|null}>('/shifts/schedule'),api<{settings:{enabled:boolean}}>('/weekly')]).then(([hours,weekly])=>setWorking(!!hours.schedule?.enabled&&weekly.settings.enabled)).catch(()=>setWorking(false));
    void api<{connections:{kind:string;status:string}[]}>('/publishing').then(data=>setSite(data.connections.some(item=>(item.kind==='hirezero'||item.kind==='wordpress')&&item.status==='ready'))).catch(()=>setSite(false));
    // An owner with no website has no site to connect (a business on Google, Facebook or a directory page).
    void api<{revision:{content:{ownSite?:string|null}}}>('/objectives').then(view=>setOwnSite(!!view.revision.content.ownSite)).catch(()=>setOwnSite(true));
  },[dismissed]);
  const name=state.employee.name||'Marketing';
  const met=state.messages.some(message=>message.role==='user'&&message.content.startsWith('Morning meeting'));
  const firstShift=state.tasks.some(task=>isFirstWin(state,task));
  const steps=[
    {done:briefComplete(state.profile),label:`Teach ${name} about your business`,hint:'Three short answers, or paste your website',run:onBrief},
    {done:firstShift,label:'Start your first shift',hint:'It fixes your most important page and makes what you pick',run:()=>onOpen?.('view:chat')},
    ...(onGoals?[{done:!!goalsSet,label:`Tell ${name} your main goal`,hint:'It puts that goal first when it plans',run:onGoals}]:[]),
    ...(ownSite!==false?[{done:!!site,label:'Connect your site',hint:'Approved fixes are saved there as drafts for you to publish',run:openSiteConnect}]:[]),
    {done:!!working,label:`Put ${name} to work`,hint:'Weekday hours: it works and reports back by itself',run:()=>void api('/employee/put-to-work',{timeZone:Intl.DateTimeFormat().resolvedOptions().timeZone||'UTC'},'POST').then(()=>setWorking(true)).catch(()=>{})},
    {done:met,label:'Have a morning check-in',hint:`${name} suggests what to do today`,run:onMeeting},
    {done:(teammates??0)>0,label:'Invite a teammate',hint:'They can review work and leave comments',run:onInvite},
    {done:(pages??0)>0,label:'Make a web page for an offer',hint:'Start from a ready-made template',run:onPage}
  ];
  const ready=pages!==null&&teammates!==null&&working!==null&&site!==null&&ownSite!==null;
  const dismiss=()=>{setDismissed(true);try{localStorage.setItem(scoped(dismissKey),'yes');}catch{}};
  return {steps,remaining:steps.filter(step=>!step.done).length,ready,dismissed,dismiss};
}

/** In the cockpit, under the work: the first-day checklist, one click per step, gone when it's all done (or hidden). */
export function StartChecklist(props:StartProps){
  const {steps,remaining,ready,dismissed,dismiss}=useStartSteps(props);
  if(dismissed||!remaining||!ready)return null;
  const next=steps.find(step=>!step.done)!;
  return <section className="fe-cockpit-start" aria-label="Getting started">
    <div className="fe-cockpit-start-head"><h3>Getting started <span className="fe-count">{steps.length-remaining}/{steps.length}</span></h3>
      <button type="button" className="fe-icon-button" aria-label="Hide getting started" title="Hide" onClick={dismiss}><X size={14}/></button></div>
    <div className="fe-start-bar" aria-hidden="true"><i style={{width:`${(steps.length-remaining)/steps.length*100}%`}}/></div>
    <button type="button" className="fe-cockpit-start-next" onClick={next.run}><span><small>Next</small><strong>{next.label}</strong><small>{next.hint}</small></span><ChevronRight size={15}/></button>
    <details><summary>All steps</summary><div className="fe-row-list">{steps.map(step=><button type="button" key={step.label} className={'fe-row compact'+(step.done?' done':'')} disabled={step.done} onClick={step.run}>
      <span className={'fe-start-check'+(step.done?' done':'')}>{step.done&&<Check size={13}/>}</span>
      <span className="fe-row-body"><strong>{step.label}</strong></span>{!step.done&&<ChevronRight size={15}/>}</button>)}</div></details>
  </section>;
}

/** In chat, for a new owner: the first shift, then things to hand it. The checklist lives in the cockpit. */
export function GettingStarted({state,fallback,onRefresh,onOpen}:StartProps&{fallback?:ReactNode}){
  const firstWin=onRefresh&&onOpen?<FirstWin state={state} owner onRefresh={onRefresh} onOpen={onOpen}/>:null;
  // Until the first shift has run, it is the one thing on the page. For a day after it ends, its results stay the one thing too.
  const firstWinTask=state.tasks.find(task=>isFirstWin(state,task)&&task.status==='done');
  const firstWinPending=briefComplete(state.profile)&&!!state.profile.audience.trim()&&(!firstWinTask||Date.now()/1000-firstWinTask.updated_at<86_400);
  if(firstWinPending&&firstWin)return <>{firstWin}{fallback}</>;
  return <>{firstWin}{onRefresh&&briefComplete(state.profile)?<FirstSteps state={state} owner onRefresh={onRefresh}/>:fallback}</>;
}
