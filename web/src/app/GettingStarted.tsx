import {useEffect,useState,type ReactNode} from 'react';
import {Check,ChevronRight,X} from 'lucide-react';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import type {State} from '../types';
import {briefComplete} from './BriefEditor';
import {FirstSteps} from './FirstSteps';
import {FirstWin} from './Experience';

const dismissKey='fe-getting-started-dismissed';

/** First-day guide: each step is checked from real workspace data, not from clicks. */
export function GettingStarted({state,fallback,goalsSet,onBrief,onGoals,onMeeting,onPage,onInvite,onRefresh,onOpen}:{state:MarketingState;fallback?:ReactNode;goalsSet?:boolean;onBrief:()=>void;onGoals?:()=>void;onMeeting:()=>void;onPage:()=>void;onInvite:()=>void;onRefresh?:()=>Promise<void>;onOpen?:(key:string)=>void}){
  const [dismissed,setDismissed]=useState(()=>{try{return localStorage.getItem(dismissKey)==='yes';}catch{return false;}});
  const [pages,setPages]=useState<number|null>(null),[teammates,setTeammates]=useState<number|null>(null),[working,setWorking]=useState<boolean|null>(null);
  useEffect(()=>{
    if(dismissed)return;
    void api<State>('/state').then(legacy=>setPages((legacy.artifacts||[]).filter(app=>!app.archived).length)).catch(()=>setPages(0));
    void api<{devices:{owner:boolean}[]}>('/devices').then(result=>setTeammates(result.devices.filter(device=>!device.owner).length)).catch(()=>setTeammates(0));
    void Promise.all([api<{schedule:{enabled:boolean}|null}>('/shifts/schedule'),api<{settings:{enabled:boolean}}>('/weekly')]).then(([hours,weekly])=>setWorking(!!hours.schedule?.enabled&&weekly.settings.enabled)).catch(()=>setWorking(false));
  },[dismissed]);
  const name=state.employee.name||'Marketing';
  const met=state.messages.some(message=>message.role==='user'&&message.content.startsWith('Morning meeting'));
  const steps=[
    {done:briefComplete(state.profile),label:`Teach ${name} about your business`,hint:'Onboarding from your links, a chat, or a form',run:onBrief},
    ...(onGoals?[{done:!!goalsSet,label:`Tell ${name} your main goal`,hint:'It puts that goal first when it plans',run:onGoals}]:[]),
    {done:met,label:'Have a morning check-in',hint:`${name} suggests what to do today`,run:onMeeting},
    {done:!!working,label:`Put ${name} to work`,hint:'Set working hours, and it works and reports back by itself',run:()=>void api('/employee/put-to-work',{timeZone:Intl.DateTimeFormat().resolvedOptions().timeZone||'UTC'},'POST').then(()=>setWorking(true)).catch(()=>setWorking(false))},
    {done:(pages??0)>0,label:'Make a web page for an offer',hint:'Start from a ready-made template',run:onPage},
    {done:(teammates??0)>0,label:'Invite a teammate',hint:'They can review work and leave comments',run:onInvite}
  ];
  const remaining=steps.filter(step=>!step.done).length;
  const firstWin=onRefresh&&onOpen?<FirstWin state={state} owner onRefresh={onRefresh} onOpen={onOpen}/>:null;
  // Until the first shift has run, it is the one thing on the page: the checklist and the list of starters wait until after.
  // For a day after it ends, its results (the next steps) stay the one thing too.
  const firstWinTask=state.tasks.find(task=>task.title==='Prepare my first useful win'&&task.status==='done');
  const firstWinPending=briefComplete(state.profile)&&!!state.profile.audience.trim()&&(!firstWinTask||Date.now()/1000-firstWinTask.updated_at<86_400);
  if(dismissed||!remaining||firstWinPending&&firstWin)return <>{firstWin}{fallback}</>;
  if(pages===null||teammates===null||working===null)return null;
  return <>{firstWin}<section className="fe-card fe-start" aria-label="Getting started">
    <div className="fe-card-head"><div><h3>Getting started</h3><small>{steps.length-remaining} of {steps.length} done</small></div>
      <button type="button" className="fe-icon-button" aria-label="Hide getting started" onClick={()=>{setDismissed(true);try{localStorage.setItem(dismissKey,'yes');}catch{}}}><X size={16}/></button></div>
    <div className="fe-start-bar" aria-hidden="true"><i style={{width:`${(steps.length-remaining)/steps.length*100}%`}}/></div>
    <div className="fe-row-list">{steps.map(step=><button type="button" key={step.label} className={'fe-row compact'+(step.done?' done':'')} disabled={step.done} onClick={step.run}>
      <span className={'fe-start-check'+(step.done?' done':'')}>{step.done&&<Check size={13}/>}</span>
      <span className="fe-row-body"><strong>{step.label}</strong><small>{step.hint}</small></span>{!step.done&&<ChevronRight size={15}/>}</button>)}</div>
    {onRefresh&&briefComplete(state.profile)&&<FirstSteps state={state} owner onRefresh={onRefresh}/>}
  </section></>;
}
