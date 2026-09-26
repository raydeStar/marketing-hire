import {useEffect,useState,type ReactNode} from 'react';
import {Check,ChevronRight,X} from 'lucide-react';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import type {State} from '../types';
import {briefComplete} from './BriefEditor';
import {FirstSteps} from './FirstSteps';

const dismissKey='fe-getting-started-dismissed';

/** First-day guide: each step is checked from real workspace data, not from clicks. */
export function GettingStarted({state,fallback,goalsSet,onBrief,onGoals,onMeeting,onPage,onInvite,onRefresh}:{state:MarketingState;fallback?:ReactNode;goalsSet?:boolean;onBrief:()=>void;onGoals?:()=>void;onMeeting:()=>void;onPage:()=>void;onInvite:()=>void;onRefresh?:()=>Promise<void>}){
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
    ...(onGoals?[{done:!!goalsSet,label:'Set your north star and objectives',hint:`${name} ranks its work against them`,run:onGoals}]:[]),
    {done:met,label:'Run your first morning meeting',hint:`${name} proposes today’s priorities`,run:onMeeting},
    {done:!!working,label:`Put ${name} to work`,hint:'Weekday shifts 9–5, token limits, the morning brief and weekly reports',run:()=>void api('/employee/put-to-work',{timeZone:Intl.DateTimeFormat().resolvedOptions().timeZone||'UTC'},'POST').then(()=>setWorking(true)).catch(()=>setWorking(false))},
    {done:(pages??0)>0,label:'Make a campaign page',hint:'Start from a landing page template',run:onPage},
    {done:(teammates??0)>0,label:'Invite a teammate',hint:'Share a campaign for review',run:onInvite}
  ];
  const remaining=steps.filter(step=>!step.done).length;
  if(dismissed||!remaining)return <>{fallback}</>;
  if(pages===null||teammates===null||working===null)return null;
  return <section className="fe-card fe-start" aria-label="Getting started">
    <div className="fe-card-head"><div><h3>Getting started</h3><small>{steps.length-remaining} of {steps.length} done</small></div>
      <button type="button" className="fe-icon-button" aria-label="Hide getting started" onClick={()=>{setDismissed(true);try{localStorage.setItem(dismissKey,'yes');}catch{}}}><X size={16}/></button></div>
    <div className="fe-start-bar" aria-hidden="true"><i style={{width:`${(steps.length-remaining)/steps.length*100}%`}}/></div>
    <div className="fe-row-list">{steps.map(step=><button type="button" key={step.label} className={'fe-row compact'+(step.done?' done':'')} disabled={step.done} onClick={step.run}>
      <span className={'fe-start-check'+(step.done?' done':'')}>{step.done&&<Check size={13}/>}</span>
      <span className="fe-row-body"><strong>{step.label}</strong><small>{step.hint}</small></span>{!step.done&&<ChevronRight size={15}/>}</button>)}</div>
    {onRefresh&&briefComplete(state.profile)&&<FirstSteps state={state} owner onRefresh={onRefresh}/>}
  </section>;
}
