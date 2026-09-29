import {useEffect,useState} from 'react';
import {ChevronRight,Megaphone,Plus} from 'lucide-react';
import {campaignTitle} from '../components/MarketingRunwayPanel';
import {readableTime,type MarketingState,type MarketingTask} from '../components/MarketingPanels';
import {WorkActivity} from '../components/WorkActivity';
import {WorkBoard} from '../components/WorkBoard';
import {inboxItems} from './InboxView';
import {NewTaskDialog,useTaskMove} from './TasksView';
import {ScorecardSection} from './ScorecardView';
import {ListeningSection} from './ListeningView';
import {SiteCheckSection} from './SiteCheckView';
import {PageChangesSection} from './PageCopy';
import {ContentCalendar} from './PublishingView';
import {WeeklySection} from './WeeklyView';
import {ShiftLog} from './ShiftPanel';
import type {ShiftView} from './shifts';
import {CampaignRows,CampaignStrip,NewCampaignRow,useCampaigns} from './campaigns';
import {FirstSteps} from './FirstSteps';
import {FirstWin,LearningTrail,OutcomeSnapshot,PreparedWorkList} from './Experience';

export const workTabs=['todo','campaigns','calendar','results','listening','history'] as const;
export type WorkTab=typeof workTabs[number];
const workTabKey='fe-work-tab',workTabEvent='fe-work-tab';
/** Where a link into Work ("section:scorecard") lands: its tab opens first. */
export const sectionTab:Record<string,WorkTab>={board:'todo',calendar:'calendar',weekly:'calendar',scorecard:'results',listening:'listening',shifts:'history'};
export function openWorkTab(tab:WorkTab){try{localStorage.setItem(workTabKey,tab);}catch{}window.dispatchEvent(new CustomEvent(workTabEvent,{detail:tab}));}

const projectStatus:Record<string,string>={needs_review:'Waiting for review',running:'In progress',queued:'Queued',waiting:'Waiting',completed:'Complete',failed:'Stopped',cancelled:'Cancelled',paused:'Paused'};
const humanize=(value:string)=>projectStatus[value]||value.replaceAll('_',' ').replace(/^./,letter=>letter.toUpperCase());

/** The work overview: counts that matter, the campaign in flight, the board and what just happened. */
export function WorkView({state,pastMeetingTasks,canWrite,owner,shifts,onOpen,onRefresh}:{state:MarketingState;pastMeetingTasks:MarketingTask[];canWrite:boolean;owner:boolean;shifts:ShiftView|null;onOpen:(key:string)=>void;onRefresh:()=>Promise<void>}){
  const [creating,setCreating]=useState(false),[error,setError]=useState('');
  const move=useTaskMove(canWrite,onRefresh,setError);
  const name=state.employee.name||'Marketing';
  const week=Date.now()/1000-7*86400;
  const tasks=[...state.tasks].sort((a,b)=>({high:0,normal:1,low:2}[a.priority]-{high:0,normal:1,low:2}[b.priority])||b.updated_at-a.updated_at);
  const stats=[
    {label:'Waiting on you',value:inboxItems(state).length,tone:'attn'},
    {label:'In progress',value:state.tasks.filter(task=>task.status==='working').length,tone:''},
    {label:'Assigned',value:state.tasks.filter(task=>task.status==='ready').length,tone:''},
    {label:'Done this week',value:state.tasks.filter(task=>task.status==='done'&&task.updated_at>=week).length,tone:'ok'}
  ];
  const runway=state.runway;
  const named=(useCampaigns()?.ledger?.campaigns.length??0)>0;
  const [tab,setTab]=useState<WorkTab>(()=>{try{const saved=localStorage.getItem(workTabKey);return (workTabs as readonly string[]).includes(saved||'')?saved as WorkTab:'todo';}catch{return 'todo';}});
  const choose=(next:WorkTab)=>{setTab(next);try{localStorage.setItem(workTabKey,next);}catch{}};
  useEffect(()=>{const open=(event:Event)=>{const next=(event as CustomEvent<WorkTab>).detail;if((workTabs as readonly string[]).includes(next))choose(next);};
    window.addEventListener(workTabEvent,open);return()=>window.removeEventListener(workTabEvent,open);},[]);
  // One page of everything became tabs: the work in hand first, then campaigns, the calendar, results, listening and history.
  const waitingOnYou=stats[0].value;
  const tabs:{id:WorkTab;label:string;count?:number}[]=[{id:'todo',label:'To do',count:waitingOnYou||undefined},{id:'campaigns',label:'Campaigns'},{id:'calendar',label:'Calendar'},{id:'results',label:'Results'},{id:'listening',label:'Listening'},{id:'history',label:'History'}];
  return <div className="fe-work">
    <div className="fe-work-tabs" role="tablist" aria-label="Work sections">{tabs.map(item=><button type="button" role="tab" key={item.id} id={'work-tab-'+item.id} aria-selected={tab===item.id} aria-controls="work-tab-panel" className={tab===item.id?'active':''} onClick={()=>choose(item.id)}>
      {item.label}{item.count?<span className="fe-count">{item.count}</span>:null}</button>)}</div>
    <div className="fe-work-panel" role="tabpanel" id="work-tab-panel" aria-labelledby={'work-tab-'+tab}>
    {tab==='todo'&&<>
      <FirstWin state={state} owner={owner} onRefresh={onRefresh} onOpen={onOpen} level={2}/>
      <PreparedWorkList onOpen={onOpen}/>
      <CampaignStrip state={state} onOpen={onOpen}/>
      <div className="fe-stats fe-stats-links">{stats.map(stat=><button type="button" key={stat.label} className={stat.value&&stat.tone?stat.tone:''}
        onClick={()=>document.querySelector('section[aria-label="Board"]')?.scrollIntoView({behavior:'smooth',block:'start'})} aria-label={`${stat.label}: ${stat.value}. Show the board`}>
        <span className="fe-stat-label">{stat.label}</span><span className="fe-stat-value">{stat.value}</span></button>)}</div>
      <section className="fe-section" aria-label="Board">
        <div className="fe-section-head"><div><h2>Board</h2><small>{canWrite?'Drag a card to another column to change where it stands':'Read only'}</small></div>{canWrite&&<button type="button" onClick={()=>setCreating(true)}><Plus size={15}/> New task</button>}</div>
        {canWrite&&!state.tasks.some(task=>task.status==='ready'||task.status==='working')&&<FirstSteps state={state} owner={owner} onRefresh={onRefresh}
          title={`Nothing assigned: hand ${name} one of these`} hint="Each becomes a task it starts on right away. Nothing goes out without your approval."/>}
        <WorkBoard tasks={tasks} pastMeetingTasks={pastMeetingTasks} employeeName={name} onOpen={id=>onOpen('task:'+id)} onCreate={()=>setCreating(true)} canCreate={canWrite} onMove={canWrite?(task,status)=>void move(task,status):undefined}/>
        {error&&<p className="fe-alert" role="alert">{error}</p>}
      </section>
    </>}
    {tab==='campaigns'&&<>
      <section className="fe-section" aria-label="Campaigns">
        <div className="fe-section-head"><div><h2>Campaigns</h2><small>{named?`Pushes ${name} is working on, each with everything made for it`:`A launch, an offer or a season: ${name} plans it and makes the pieces`}</small></div></div>
        <CampaignRows state={state} owner={owner} onOpen={onOpen}/>
        {runway?<button type="button" className="fe-list-row" onClick={()=>onOpen('campaign:current')}>
          <span className="fe-row-icon"><Megaphone size={16}/></span>
          <span className="fe-list-main"><strong>{campaignTitle(runway.project.goal)}</strong><small>Started {readableTime(runway.project.created_at)} · {runway.artifacts.length} deliverable{runway.artifacts.length===1?'':'s'} · {runway.reviews.length} review{runway.reviews.length===1?'':'s'}</small></span>
          <span className={'fe-status-chip '+(runway.project.status==='needs_review'?'warn':runway.project.status==='completed'?'live':'')}>{humanize(runway.project.status)}</span><ChevronRight size={16}/></button>
          // The older standing assignment only where it's switched on (a pilot, or a test fixture).
          :state.runwayLiveEnabled||state.fixtureCampaignEnabled?<button type="button" className="fe-list-row" onClick={()=>onOpen('campaign:current')}><span className="fe-row-icon"><Megaphone size={16}/></span><span className="fe-list-main"><strong>{named?'Standing assignment':'No campaign yet'}</strong><small>Open Campaigns to scope the first assignment and set its limits</small></span><ChevronRight size={16}/></button>
          :!named&&<p className="fe-muted">No campaign yet. Start one below for a launch, an offer or a season.</p>}
        <NewCampaignRow owner={owner} onOpen={onOpen}/>
      </section>
    </>}
    {tab==='calendar'&&<>
      <WeeklySection owner={owner} onOpen={onOpen}/>
      <ContentCalendar state={state} owner={owner} onOpen={onOpen}/>
    </>}
    {tab==='results'&&<>
      <OutcomeSnapshot onOpen={onOpen}/>
      <ScorecardSection canEdit={canWrite} owner={owner}/>
    </>}
    {tab==='listening'&&<>
      <ListeningSection owner={owner} onOpen={onOpen}/>
      <PageChangesSection onOpen={onOpen}/>
      <SiteCheckSection owner={owner} onOpen={onOpen}/>
    </>}
    {tab==='history'&&<>
      <LearningTrail state={state} onOpen={onOpen}/>
      <ShiftLog view={shifts} onOpen={onOpen}/>
      {(state.activity||[]).length>0&&<section className="fe-section" aria-label="Recent activity">
        <div className="fe-section-head"><div><h2>Recent activity</h2><small>Everything done in this workspace, as it happens</small></div></div>
        <WorkActivity events={(state.activity||[]).slice(-12)} tasks={state.tasks} onTask={id=>onOpen('task:'+id)}/>
      </section>}
    </>}
    </div>
    {creating&&<NewTaskDialog state={state} onClose={()=>setCreating(false)} onCreated={id=>{setCreating(false);onOpen('task:'+id);}} onRefresh={onRefresh}/>}
  </div>;
}
