import {useCallback,useEffect,useState} from 'react';
import {CalendarRange,ExternalLink,FileText,Settings2} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {usePublishing} from './PublishingView';
import {Dialog} from './shared';

export type WeeklyDoc={kind:'plan'|'update'|'month'|'brief';week:string;wikiId:string;title:string;at:string;emailUrl:string|null;summary?:string|null};
type WeeklySettings={enabled:boolean;timeZone:string;planDay:number;planTime:string;updateDay:number;updateTime:string;emailDraft:boolean};
export type WeeklyView={settings:WeeklySettings;latest:WeeklyDoc[]};

const days=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];
const zone=(()=>{try{return Intl.DateTimeFormat().resolvedOptions().timeZone;}catch{return 'UTC';}})();
export function useWeekly(){
  const [view,setView]=useState<WeeklyView|null>(null);
  const load=useCallback(async()=>{try{setView(await api<WeeklyView>('/weekly'));}catch{setView(null);}},[]);
  useEffect(()=>{void load();},[load]);
  return {view,load,setView};
}

function RhythmSettings({view,hasEmail,onClose,onSaved}:{view:WeeklyView;hasEmail:boolean;onClose:()=>void;onSaved:(next:WeeklyView)=>void}){
  const current=view.settings;
  // Opened from “Turn on”, it starts switched on.
  const [enabled,setEnabled]=useState(true);
  const [planDay,setPlanDay]=useState(current.planDay),[planTime,setPlanTime]=useState(current.planTime),[updateDay,setUpdateDay]=useState(current.updateDay),[updateTime,setUpdateTime]=useState(current.updateTime);
  const [email,setEmail]=useState(current.emailDraft&&hasEmail),[busy,setBusy]=useState(false),[error,setError]=useState('');
  async function save(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{onSaved(await api<WeeklyView>('/weekly',{enabled,timeZone:zone,planDay,planTime,updateDay,updateTime,emailDraft:email},'PUT'));onClose();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const day=(value:number,set:(next:number)=>void,label:string)=><label>{label}<select value={value} disabled={!enabled} onChange={event=>set(Number(event.target.value))}>{days.map((name,index)=><option key={name} value={index}>{name}</option>)}</select></label>;
  return <Dialog title="Weekly rhythm" onClose={onClose}><form className="fe-form" onSubmit={event=>void save(event)}>
    <p className="fe-muted">A plan to start the week and an update to end it, written from the workspace’s records: numbers, what went out and how it did, work done, what waits on you. They cost no model turns and go to Library → Reports → Weekly.</p>
    <label className="fe-check"><input type="checkbox" checked={enabled} onChange={event=>setEnabled(event.target.checked)}/>Write them automatically</label>
    <div className="fe-form-row">{day(planDay,setPlanDay,'Plan on')}<label>at<input type="time" value={planTime} disabled={!enabled} onChange={event=>setPlanTime(event.target.value)}/></label></div>
    <div className="fe-form-row">{day(updateDay,setUpdateDay,'Update on')}<label>at<input type="time" value={updateTime} disabled={!enabled} onChange={event=>setUpdateTime(event.target.value)}/></label></div>
    <label className="fe-check"><input type="checkbox" checked={email} disabled={!hasEmail} onChange={event=>setEmail(event.target.checked)}/>Also save the update as a Gmail draft to forward{!hasEmail&&' (connect Email in Settings → Publishing channels)'}</label>
    <small className="fe-muted">Times are in {zone}. The workspace has to be running; if it wasn’t, the plan is written the next time it runs that week.</small>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy}>{busy?'Saving…':'Save'}</button></footer>
  </form></Dialog>;
}

/** Work → This week: the latest weekly plan and update, and a way to write either now. */
export function WeeklySection({owner,onOpen}:{owner:boolean;onOpen:(key:string)=>void}){
  const {view,load,setView}=useWeekly();
  const publishing=usePublishing();
  const [open,setOpen]=useState(false),[busy,setBusy]=useState(''),[error,setError]=useState('');
  if(!view)return null;
  const hasEmail=!!publishing.data?.connections.some(item=>item.kind==='email'&&item.status==='ready');
  const latest=(kind:'plan'|'update'|'month'|'brief')=>view.latest.find(item=>item.kind===kind);
  async function write(kind:'plan'|'update'|'month'|'brief'){
    if(busy)return;setBusy(kind);setError('');
    try{const doc=await api<WeeklyDoc>(`/weekly/${kind}`,{});await load();onOpen('wiki:'+doc.wikiId);}catch(cause){setError((cause as Error).message);}finally{setBusy('');}
  }
  const s=view.settings;
  return <section className="fe-section" aria-label="This week">
    <div className="fe-section-head"><div><h3>This week</h3><small>{s.enabled?`Plan ${days[s.planDay]}s at ${s.planTime}, update ${days[s.updateDay]}s at ${s.updateTime}${s.emailDraft?', also as a Gmail draft':''}.`:'The weekly plan and update aren’t automatic yet.'}</small></div>
      {owner&&<button type="button" onClick={()=>setOpen(true)}><Settings2 size={15}/> {s.enabled?'Rhythm':'Turn on'}</button>}</div>
    <div className="fe-weekly">{(['brief','plan','update','month'] as const).map(kind=>{const doc=latest(kind);
      return <div key={kind} className="fe-data-row">
        <span className="fe-row-icon">{kind==='plan'?<CalendarRange size={15}/>:<FileText size={15}/>}</span>
        <span className="fe-list-main"><strong>{doc?doc.title:kind==='brief'?'No morning brief yet':kind==='plan'?'No weekly plan yet':kind==='update'?'No weekly update yet':'No monthly report yet'}</strong><small>{doc?(doc.summary?doc.summary+' · ':'')+`Written ${readableTime(new Date(doc.at).getTime()/1000)}`:kind==='brief'?'Each weekday morning from the connected data: KPIs, what worked, what didn’t, and push or pivot':kind==='month'?'Last month against the month before: numbers, posts, experiments, work, spend':kind==='plan'?'Focus, the queue, what waits on you, what goes out this week':'Numbers, what went out and how it did, work done, decisions, learnings'}</small></span>
        {doc?.emailUrl&&<a className="fe-icon-button" href={doc.emailUrl} target="_blank" rel="noopener noreferrer" aria-label="Open the Gmail draft" title="Open the Gmail draft"><ExternalLink size={14}/></a>}
        {doc&&<button type="button" className="fe-ghost" onClick={()=>onOpen('wiki:'+doc.wikiId)}>Open</button>}
        {owner&&<button type="button" className="fe-ghost" disabled={!!busy} onClick={()=>void write(kind)}>{busy===kind?'Writing…':doc?'Write again':'Write now'}</button>}
      </div>;})}</div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {open&&<RhythmSettings view={view} hasEmail={hasEmail} onClose={()=>setOpen(false)} onSaved={setView}/>}
  </section>;
}
