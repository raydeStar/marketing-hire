import {useCallback,useEffect,useMemo,useState} from 'react';
import {CheckCircle2,Download,FileText,Link2,Search,ShieldCheck,X} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import {WorkActivity} from '../components/WorkActivity';
import {ArtifactBody} from '../components/MarketingRunwayPanel';
import {publicLink,readableTime,type MarketingMessage,type MarketingState,type RunwayArtifact} from '../components/MarketingPanels';
import {download,Empty,PageHead,plain} from './shared';

type Kind='reply'|'source'|'draft'|'decision'|'task'|'deliverable';
type Record={id:string;kind:Kind;title:string;body:string;date:number|string;taskId?:string;url?:string;meta:string;artifact?:RunwayArtifact};
const kindLabel:{[key in Kind]:string}={reply:'Reply',source:'Source',draft:'Draft',decision:'Decision',task:'Completed task',deliverable:'Deliverable'};
const icons={reply:FileText,source:Link2,draft:FileText,decision:ShieldCheck,task:CheckCircle2,deliverable:FileText};
const stamp=(value:number|string)=>typeof value==='number'?(value<1e12?value*1000:value):Date.parse(value)||0;

function records(state:MarketingState,history:MarketingMessage[]):Record[]{
  const tasks=new Map(state.tasks.map(task=>[task.id,task]));
  const replies=new Map([...history,...state.messages].filter(message=>message.role==='assistant').map(message=>[message.id,message]));
  return [
    ...[...replies.values()].map(message=>({id:'reply:'+message.id,kind:'reply' as const,title:(message.taskId?tasks.get(message.taskId)?.title:undefined)||plain(message.content).slice(0,85)||'Reply',body:message.content,date:message.createdAt,taskId:message.taskId||undefined,meta:message.taskId?'Task conversation':'Chat'})),
    ...(state.evidence||[]).map(source=>({id:'source:'+source.id,kind:'source' as const,title:source.title,body:source.note,date:source.created_at,taskId:source.task_id,url:publicLink(source.url)||undefined,meta:source.source})),
    ...(state.drafts||[]).map(draft=>({id:'draft:'+draft.id,kind:'draft' as const,title:`${draft.channel} draft`,body:draft.content+'\n\n**Why:** '+draft.rationale,date:draft.decided_at||0,url:publicLink(draft.destination)||undefined,meta:draft.status})),
    ...(state.ownerDecisions||[]).map(decision=>({id:'decision:'+decision.requestId,kind:'decision' as const,title:`Draft ${decision.decision}`,body:`You **${decision.decision}** draft #${decision.draftId} (revision ${decision.revision}).`,date:decision.createdAt,meta:decision.status==='confirmed'?'Confirmed':'Syncing'})),
    ...(state.runway?.artifacts||[]).map(artifact=>({id:'deliverable:'+artifact.id,kind:'deliverable' as const,title:({audience_note:'Audience & problem note',post_angles:'Draft post angles',review_packet:'Review packet',revision_angles:'Revised post angles'} as {[key:string]:string})[artifact.kind]||artifact.kind.replaceAll('_',' '),body:'```json\n'+artifact.content+'\n```',date:artifact.created_at,meta:'Assignment',artifact})),
    ...state.tasks.filter(task=>task.status==='done').map(task=>({id:'task:'+task.id,kind:'task' as const,title:task.title,body:task.next_action,date:task.updated_at,taskId:task.id,meta:'Completed'}))
  ].sort((a,b)=>stamp(b.date)-stamp(a.date)||a.id.localeCompare(b.id));
}

export function HistoryView({state,onOpenTask}:{state:MarketingState;onOpenTask:(id:string)=>void}){
  const [tab,setTab]=useState<'records'|'activity'>('records'),[query,setQuery]=useState(''),[kind,setKind]=useState<Kind|'all'>('all'),[openId,setOpenId]=useState<string|null>(null);
  const [history,setHistory]=useState<MarketingMessage[]>([]),[cursor,setCursor]=useState<string|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const name=state.employee.name||'Marketing';
  const more=useCallback(async(before?:string)=>{setBusy(true);try{const page=await api<{items:MarketingMessage[];nextCursor:string|null}>('/marketing/history'+(before?'?before='+encodeURIComponent(before):''));setHistory(current=>[...new Map([...current,...page.items].map(item=>[item.id,item])).values()]);setCursor(page.nextCursor);setError('');}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}},[]);
  useEffect(()=>{void more();},[more]);
  const all=useMemo(()=>records(state,history),[state,history]);
  const visible=all.filter(item=>(kind==='all'||item.kind===kind)&&`${item.title} ${item.body} ${item.meta}`.toLowerCase().includes(query.toLowerCase()));
  const open=visible.find(item=>item.id===openId);
  return <div className="fe-page"><div className="fe-page-inner">
    <PageHead title="History" subtitle={`Every reply, source, draft and decision, so nothing ${name} learned gets lost.`}/>
    <nav className="fe-segmented fe-history-tabs" aria-label="History views"><button type="button" aria-pressed={tab==='records'} onClick={()=>setTab('records')}>Records</button><button type="button" aria-pressed={tab==='activity'} onClick={()=>setTab('activity')}>Activity log</button></nav>
    {tab==='activity'?<WorkActivity events={state.activity||[]} tasks={state.tasks} onTask={onOpenTask}/>:<>
      <div className="fe-toolbar"><label className="fe-search"><Search size={16}/><input aria-label="Search records" value={query} onChange={event=>{setQuery(event.target.value);setOpenId(null);}} placeholder="Search by title, content or source"/>{query&&<button type="button" className="fe-icon-button" aria-label="Clear search" onClick={()=>setQuery('')}><X size={15}/></button>}</label>
        <select aria-label="Record type" value={kind} onChange={event=>{setKind(event.target.value as Kind|'all');setOpenId(null);}}><option value="all">Everything</option>{Object.entries(kindLabel).map(([value,label])=><option key={value} value={value}>{label}s</option>)}</select></div>
      <div className="fe-split">
        <aside className="fe-row-list" aria-label="Records">{visible.map(item=>{const Icon=icons[item.kind];return <button type="button" className="fe-row" key={item.id} aria-pressed={item.id===openId} onClick={()=>setOpenId(item.id)}><span className="fe-row-icon"><Icon size={17}/></span><span className="fe-row-body"><strong>{item.title}</strong><small>{kindLabel[item.kind]} · {item.date?readableTime(item.date):item.meta}</small></span></button>;})}
          {!visible.length&&<p className="fe-muted fe-files-empty">{query?'Nothing matches that search.':'Records appear as Marketing works.'}</p>}
          {cursor&&<button type="button" className="fe-ghost" disabled={busy} onClick={()=>void more(cursor)}>{busy?'Loading…':'Load earlier replies'}</button>}
          {error&&<p className="fe-alert" role="alert">{error}</p>}
        </aside>
        {open?<article className="fe-reader" aria-label="Record preview"><div className="fe-reader-head"><div><h2>{open.title}</h2><div className="fe-reader-meta"><span className="fe-pill">{kindLabel[open.kind]}</span><small>{open.date?readableTime(open.date):''} · {open.meta}</small></div></div>
          <button type="button" className="fe-icon-button" aria-label="Download record" onClick={()=>download(open.title.replace(/[^a-zA-Z0-9 _-]/g,'').slice(0,70)+'.md',`# ${open.title}\n\n${open.body}${open.url?'\n\nSource: '+open.url:''}\n`)}><Download size={17}/></button></div>
          {open.url&&<p><a href={open.url} target="_blank" rel="noopener noreferrer"><Link2 size={14}/> {new URL(open.url).hostname}</a></p>}
          {open.artifact?<ArtifactBody artifact={open.artifact}/>:<div className="fe-prose"><Markdown>{open.body}</Markdown></div>}
          {open.taskId&&state.tasks.some(task=>task.id===open.taskId)&&<button type="button" className="fe-ghost" onClick={()=>onOpenTask(open.taskId!)}>Open the related task →</button>}
        </article>:<div className="fe-reader"><Empty icon={<FileText size={30}/>} title="Pick a record">Read the full text and trace it back to its task.</Empty></div>}
      </div></>}
  </div></div>;
}
