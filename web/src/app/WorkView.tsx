import {useState} from 'react';
import {ChevronRight,Megaphone,Plus} from 'lucide-react';
import {campaignTitle} from '../components/MarketingRunwayPanel';
import {readableTime,type MarketingState,type MarketingTask} from '../components/MarketingPanels';
import {WorkActivity} from '../components/WorkActivity';
import {WorkBoard} from '../components/WorkBoard';
import {inboxItems} from './InboxView';
import {NewTaskDialog,useTaskMove} from './TasksView';
import {ScorecardSection} from './ScorecardView';
import {ListeningSection} from './ListeningView';
import {ContentCalendar} from './PublishingView';
import {ShiftLog} from './ShiftPanel';
import type {ShiftView} from './shifts';

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
    {label:'Needs decision',value:inboxItems(state).length,tone:'attn'},
    {label:'In progress',value:state.tasks.filter(task=>task.status==='working').length,tone:''},
    {label:'Assigned',value:state.tasks.filter(task=>task.status==='ready').length,tone:''},
    {label:'Done this week',value:state.tasks.filter(task=>task.status==='done'&&task.updated_at>=week).length,tone:'ok'}
  ];
  const runway=state.runway;
  return <div className="fe-work">
    <dl className="fe-stats">{stats.map(stat=><div key={stat.label} className={stat.value&&stat.tone?stat.tone:''}><dt>{stat.label}</dt><dd>{stat.value}</dd></div>)}</dl>
    <ScorecardSection canEdit={canWrite} owner={owner}/>
    <ContentCalendar state={state} owner={owner} onOpen={onOpen}/>
    <ListeningSection owner={owner} onOpen={onOpen}/>
    <section className="fe-section" aria-label="Campaigns">
      <div className="fe-section-head"><div><h3>Campaigns</h3><small>Assignments {name} runs for you, each with its own review and record</small></div></div>
      {runway?<button type="button" className="fe-list-row" onClick={()=>onOpen('campaign:current')}>
        <span className="fe-row-icon"><Megaphone size={16}/></span>
        <span className="fe-list-main"><strong>{campaignTitle(runway.project.goal)}</strong><small>Started {readableTime(runway.project.created_at)} · {runway.artifacts.length} deliverable{runway.artifacts.length===1?'':'s'} · {runway.reviews.length} review{runway.reviews.length===1?'':'s'}</small></span>
        <span className={'fe-status-chip '+(runway.project.status==='needs_review'?'warn':runway.project.status==='completed'?'live':'')}>{humanize(runway.project.status)}</span><ChevronRight size={16}/></button>
        :<button type="button" className="fe-list-row" onClick={()=>onOpen('campaign:current')}><span className="fe-row-icon"><Megaphone size={16}/></span><span className="fe-list-main"><strong>No campaign yet</strong><small>Open Campaigns to scope the first assignment and set its limits</small></span><ChevronRight size={16}/></button>}
    </section>
    <section className="fe-section" aria-label="Board">
      <div className="fe-section-head"><div><h3>Board</h3><small>{canWrite?'Drag a card between lanes to change its status':'Read only'}</small></div>{canWrite&&<button type="button" onClick={()=>setCreating(true)}><Plus size={15}/> New task</button>}</div>
      <WorkBoard tasks={tasks} pastMeetingTasks={pastMeetingTasks} employeeName={name} onOpen={id=>onOpen('task:'+id)} onCreate={()=>setCreating(true)} canCreate={canWrite} onMove={canWrite?(task,status)=>void move(task,status):undefined}/>
      {error&&<p className="fe-alert" role="alert">{error}</p>}
    </section>
    <ShiftLog view={shifts} onOpen={onOpen}/>
    {(state.activity||[]).length>0&&<section className="fe-section" aria-label="Recent activity">
      <div className="fe-section-head"><div><h3>Recent activity</h3><small>Recorded by the host as it happens</small></div></div>
      <WorkActivity events={(state.activity||[]).slice(-12)} tasks={state.tasks} onTask={id=>onOpen('task:'+id)}/>
    </section>}
    {creating&&<NewTaskDialog state={state} onClose={()=>setCreating(false)} onCreated={id=>{setCreating(false);onOpen('task:'+id);}} onRefresh={onRefresh}/>}
  </div>;
}
