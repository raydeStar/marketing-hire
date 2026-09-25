import {Activity,ArrowRight,CalendarDays,CheckCircle2,ChevronRight,Coffee,FlaskConical,Inbox,LineChart,NotebookPen,Radar,Sparkles} from 'lucide-react';
import Markdown from 'react-markdown';
import {readableTime,type MarketingState} from '../components/MarketingPanels';
import {inboxItems} from './InboxView';
import {initials,type EmployeeStatus} from './shared';
import {briefComplete} from './BriefEditor';
import {GettingStarted} from './GettingStarted';

export const meetingPrompt=`Morning meeting. Work through your heartbeat checklist and give me a short brief:
1. What changed since yesterday (signals, replies, results)?
2. Anything that needs my decision today?
3. Which tasks are blocked, and on whom?
4. Today's top 3 priorities, each with your recommended next step.
5. Drafts waiting for my approval.
6. One learning worth writing down.
Lead with decisions, keep it under 200 words.`;

// The weekly rhythm from the owner's research on elite marketing managers.
const rhythm=[
  {day:'Mon',focus:'Business review',detail:'KPI changes and this week’s priorities',prompt:'Monday business review: what moved last week, what does it mean, and what should our top three priorities be this week?'},
  {day:'Tue',focus:'Insight & creative',detail:'Customer truth and new ideas',prompt:'Customer insight day: summarize what our audience has been saying lately and propose two creative ideas built on it.'},
  {day:'Wed',focus:'Experiments',detail:'Tests, channels and funnel',prompt:'Experiment review: propose one experiment for this week with a hypothesis, primary metric and a decision rule set in advance.'},
  {day:'Thu',focus:'Production',detail:'Assets, partners, launches',prompt:'Production check: what assets do we need for upcoming work, and what is at risk of slipping?'},
  {day:'Fri',focus:'Learn & plan',detail:'Retro, decision log, next week',prompt:'Friday retro: what did we learn this week, which decisions should go in the decision log, and what is next week’s agenda?'}
];

const plays=[
  {icon:LineChart,label:'Weekly scorecard',prompt:'Put together a weekly growth scorecard: one primary KPI, a few leading indicators, notable changes, and the decisions I need to make.'},
  {icon:FlaskConical,label:'Plan an experiment',prompt:'Help me design an experiment card: hypothesis, audience, primary metric, guardrails, duration and a predetermined decision rule.'},
  {icon:NotebookPen,label:'Write a campaign brief',prompt:'Draft a one-page campaign brief: objective, audience and problem, key insight, proposition, desired behavior, channels, KPI, guardrails and non-goals.'},
  {icon:Radar,label:'Scan the market',prompt:'Scan for anything our audience or competitors did recently that we should react to. Cite sources and flag what is a guess.'}
];

function greeting(){const hour=new Date().getHours();return hour<12?'Good morning':hour<18?'Good afternoon':'Good evening';}

/** The latest morning-meeting reply from today, if one exists. */
function todaysBrief(state:MarketingState){
  const start=new Date();start.setHours(0,0,0,0);
  const time=(value:number|string)=>typeof value==='number'?(value<1e12?value*1000:value):Date.parse(value);
  const main=state.messages.filter(message=>message.sessionKey===state.employee.sessionKey&&!message.taskId);
  const index=main.map(message=>message.role==='user'&&message.content.startsWith('Morning meeting')&&time(message.createdAt)>=start.getTime()).lastIndexOf(true);
  if(index<0)return null;
  return main.slice(index+1).find(message=>message.role==='assistant')||{pending:true} as const;
}

export function TodayView({state,status,ownerName,canWrite,onMeeting,onPrompt,onInbox,onOpenBrief,onOnboard,onHistory,onNewPage,onInvite}:{
  state:MarketingState;status:EmployeeStatus;ownerName:string;canWrite:boolean;
  onMeeting:()=>void;onPrompt:(text:string)=>void;onInbox:()=>void;onOpenBrief:()=>void;onOnboard:()=>void;onHistory:()=>void;onNewPage:()=>void;onInvite:()=>void;
}){
  const name=state.employee.name||'Marketing';
  const items=inboxItems(state);
  const today=new Date();
  const weekday=today.getDay();
  const brief=todaysBrief(state);
  const dayAgo=Date.now()/1000-86400;
  const recent=(state.activity||[]).filter(event=>event.ts>=dayAgo);
  const working=state.tasks.filter(task=>task.status==='working').length;
  const ready=state.tasks.filter(task=>task.status==='ready').length;
  const first=ownerName&&!/device|browser/i.test(ownerName)?' '+ownerName.split(/\s+/)[0]:'';
  return <div className="fe-page"><div className="fe-page-inner fe-today">
    <header className="fe-today-head"><p className="eyebrow">{today.toLocaleDateString(undefined,{weekday:'long',month:'long',day:'numeric'})}</p><h1>{greeting()}{first}.</h1>
      <p>{items.length?`${name} has ${items.length} thing${items.length===1?'':'s'} for you today.`:`${name} is on it. Nothing needs you right now.`}</p></header>


    <GettingStarted state={state} fallback={!briefComplete(state.profile)&&<button type="button" className="fe-setup fe-onboard-cta" onClick={onOnboard}><Sparkles size={22}/><div><strong>Get {name} up to speed</strong><p>Share your website and socials, or just talk it through. {name} drafts your brand brief and you edit it.</p></div><ArrowRight size={18}/></button>} onBrief={onOnboard} onMeeting={onMeeting} onPage={onNewPage} onInvite={onInvite}/>

    <section className="fe-meeting fe-card" aria-label="Morning meeting">
      <div className="fe-meeting-head"><span className="fe-avatar large" aria-hidden="true">{initials(name)}</span><div><h2>{brief&&!('pending' in brief)?'Today’s brief':'Morning meeting'}</h2><small><i className={'fe-dot '+status.tone}/>{name} · {status.label}</small></div>
        <button type="button" className={brief?'':'primary'} disabled={!canWrite} onClick={onMeeting}><Coffee size={16}/> {brief?'Run it again':'Start the meeting'}</button></div>
      {brief&&!('pending' in brief)?<div className="fe-prose fe-meeting-brief"><Markdown>{brief.content}</Markdown><small>Delivered {readableTime(brief.createdAt)}</small></div>
        :brief?<p className="fe-muted">{name} is preparing today’s brief…</p>
        :<p className="fe-meeting-copy">{name} checks what changed overnight, picks today’s top three priorities, and brings you only the decisions that need you. Takes about a minute.</p>}
    </section>

    <div className="fe-today-grid">
      <button type="button" className="fe-card fe-stat" onClick={onInbox}><Inbox size={20}/><strong>{items.length}</strong><span>Waiting for you</span>{items[0]&&<small>{items[0].title}</small>}<ChevronRight size={16}/></button>
      <div className="fe-card fe-stat"><Sparkles size={20}/><strong>{working+ready}</strong><span>Tasks in motion</span><small>{working} in progress · {ready} up next</small></div>
      <div className="fe-card fe-stat"><Radar size={20}/><strong>{recent.length}</strong><span>Updates in the last day</span><small>{recent[0]?recent[0].title.replace(/^Owner session [a-f0-9]+ /,'You '):'Quiet so far'}</small></div>
    </div>

    {(state.activity||[]).length>0&&<section className="fe-inbox-group" aria-label="What Marketing did"><h3><Activity size={14}/> What {name} did</h3>
      <div className="fe-card fe-feed">{(state.activity||[]).slice(0,5).map(event=><div className="fe-feed-row" key={event.id}><CheckCircle2 size={16}/><span>{event.title.replace(/^Owner session [a-f0-9]+ /,'You ')}</span><time>{readableTime(event.ts)}</time></div>)}
        <button type="button" className="fe-ghost fe-feed-more" onClick={onHistory}>See the full activity log <ChevronRight size={14}/></button></div>
    </section>}

    <section className="fe-inbox-group" aria-label="This week"><h3><CalendarDays size={14}/> This week’s rhythm</h3>
      <div className="fe-rhythm">{rhythm.map((item,index)=>{const active=index+1===weekday;return <button type="button" key={item.day} className={'fe-rhythm-day'+(active?' today':'')} disabled={!canWrite} onClick={()=>onPrompt(item.prompt)} aria-current={active?'date':undefined}>
        <span>{item.day}</span><strong>{item.focus}</strong><small>{item.detail}</small></button>;})}</div>
    </section>

    <section className="fe-inbox-group" aria-label="Plays"><h3>Quick plays</h3>
      <div className="fe-suggestions">{plays.map(({icon:Icon,label,prompt})=><button type="button" key={label} className="fe-suggestion" disabled={!canWrite} onClick={()=>onPrompt(prompt)}><Icon size={18}/><span>{label}<small>{prompt.slice(0,78).replace(/\s\S*$/,'')}…</small></span></button>)}</div>
    </section>

    {briefComplete(state.profile)&&<p className="fe-muted fe-today-foot">{name} works from your <button type="button" className="fe-link" onClick={onOpenBrief}>business brief</button>. Once agent setup is connected, this meeting runs on its own each morning.</p>}
  </div></div>;
}
