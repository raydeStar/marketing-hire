import {useEffect,useState,type ReactNode} from 'react';
import {Check,ChevronRight,X} from 'lucide-react';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import type {State} from '../types';
import {briefComplete} from './BriefEditor';

const dismissKey='fe-getting-started-dismissed';

/** First-day guide: each step is checked from real workspace data, not from clicks. */
export function GettingStarted({state,fallback,onBrief,onMeeting,onPage,onInvite}:{state:MarketingState;fallback?:ReactNode;onBrief:()=>void;onMeeting:()=>void;onPage:()=>void;onInvite:()=>void}){
  const [dismissed,setDismissed]=useState(()=>{try{return localStorage.getItem(dismissKey)==='yes';}catch{return false;}});
  const [pages,setPages]=useState<number|null>(null),[teammates,setTeammates]=useState<number|null>(null);
  useEffect(()=>{
    if(dismissed)return;
    void api<State>('/state').then(legacy=>setPages((legacy.artifacts||[]).filter(app=>!app.archived).length)).catch(()=>setPages(0));
    void api<{devices:{owner:boolean}[]}>('/devices').then(result=>setTeammates(result.devices.filter(device=>!device.owner).length)).catch(()=>setTeammates(0));
  },[dismissed]);
  const name=state.employee.name||'Marketing';
  const met=state.messages.some(message=>message.role==='user'&&message.content.startsWith('Morning meeting'));
  const steps=[
    {done:briefComplete(state.profile),label:`Teach ${name} about your business`,hint:'Onboarding from your links, a chat, or a form',run:onBrief},
    {done:met,label:'Run your first morning meeting',hint:`${name} proposes today’s priorities`,run:onMeeting},
    {done:(pages??0)>0,label:'Make a campaign page',hint:'Start from a landing page template',run:onPage},
    {done:(teammates??0)>0,label:'Invite a teammate',hint:'Share a campaign for review',run:onInvite}
  ];
  const remaining=steps.filter(step=>!step.done).length;
  if(dismissed||!remaining)return <>{fallback}</>;
  if(pages===null||teammates===null)return null;
  return <section className="fe-card fe-start" aria-label="Getting started">
    <div className="fe-card-head"><div><h3>Getting started</h3><small>{steps.length-remaining} of {steps.length} done</small></div>
      <button type="button" className="fe-icon-button" aria-label="Hide getting started" onClick={()=>{setDismissed(true);try{localStorage.setItem(dismissKey,'yes');}catch{}}}><X size={16}/></button></div>
    <div className="fe-start-bar" aria-hidden="true"><i style={{width:`${(steps.length-remaining)/steps.length*100}%`}}/></div>
    <div className="fe-row-list">{steps.map(step=><button type="button" key={step.label} className={'fe-row compact'+(step.done?' done':'')} disabled={step.done} onClick={step.run}>
      <span className={'fe-start-check'+(step.done?' done':'')}>{step.done&&<Check size={13}/>}</span>
      <span className="fe-row-body"><strong>{step.label}</strong><small>{step.hint}</small></span>{!step.done&&<ChevronRight size={15}/>}</button>)}</div>
  </section>;
}
