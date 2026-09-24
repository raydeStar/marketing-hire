import {useCallback, useEffect, useMemo, useRef, useState, type ReactNode} from 'react';
import {ArrowLeft, ArrowRight, BookOpen, Building2, CheckCircle2, ChevronRight, CircleAlert, Download, FileText, FolderOpen, LayoutDashboard, Link2, LoaderCircle, MessageCircle, Plus, RefreshCw, Search, Settings2, ShieldCheck, Users, X} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import {MarketingBrief, MarketingDiscussion, MarketingDrafts, MarketingEvidencePanel, actionLabel, priorityLabel, priorityOrder, publicLink, readableTime, requestId, statusLabel, statusOrder,
  type MarketingState, type MarketingMessage, type MarketingTask, type TaskStatus, type TaskPriority} from './MarketingPanels';
import '../company.css';
import {WorkActivity} from './WorkActivity';
import {WorkBoard, needsDecision, isPaused} from './WorkBoard';
import {CompanyWikiPanel} from './CompanyWikiPanel';
import {MarketingRunwayPanel} from './MarketingRunwayPanel';

type Department={id:string;name:string;purpose:string};
type Employee={id:string;name:string;role:string;departmentId:string|null;kind:'employee'|'manager';runtimeKey:string|null};
type Directory={version:number;departments:Department[];agents:Employee[];updatedAt:string};
type Scope={kind:'company'|'department'|'agent';id:string};
type View='activity'|'overview'|'records'|'tasks'|'chat'|'brief'|'team'|'approvals'|'wiki';
type RecordKind='reply'|'source'|'draft'|'decision'|'task'|'deliverable';
type CompanyRecord={id:string;kind:RecordKind;title:string;body:string;date:number|string;taskId?:string;url?:string;meta:string};
const kindLabel:Record<RecordKind,string>={reply:'Agent reply',source:'Source',draft:'Draft',decision:'Decision',task:'Completed task',deliverable:'Project deliverable'};
const recordIcon={reply:FileText,source:Link2,draft:FileText,decision:ShieldCheck,task:CheckCircle2,deliverable:FileText};
const rootScope:Scope={kind:'company',id:'company'};
const routeKey='company-workspace-route-v1';
function initialRoute():{scope:Scope;view:View}{try{const value=JSON.parse(localStorage.getItem(routeKey)||'null');if(value&&['company','department','agent'].includes(value.scope?.kind)&&['overview','records','tasks','chat','brief','team','approvals','wiki'].includes(value.view))return value;}catch{}return {scope:rootScope,view:'overview'};}
function timestamp(value:number|string){return typeof value==='number'?(value<1e12?value*1000:value):Date.parse(value)||0;}
function summary(value:string){return value.replace(/[#*`>]/g,'').replace(/\s+/g,' ').trim();}

function Dialog({title,onClose,children}:{title:string;onClose:()=>void;children:ReactNode}){
  const ref=useRef<HTMLDialogElement>(null);
  useEffect(()=>{const dialog=ref.current!;dialog.showModal();return()=>dialog.close();},[]);
  return <dialog ref={ref} className="company-dialog" aria-label={title} onCancel={event=>{event.preventDefault();onClose();}} onClick={event=>{if(event.target===event.currentTarget)onClose();}}>
    <header><h2>{title}</h2><button type="button" onClick={onClose} aria-label="Close dialog"><X size={20}/></button></header>{children}
  </dialog>;
}

function TeamEditor({directory,initial,onSave,onClose}:{directory:Directory;initial:'department'|'agent'|'manager';onSave:(next:Directory)=>void;onClose:()=>void}){
  const [kind,setKind]=useState(initial),[name,setName]=useState(''),[purpose,setPurpose]=useState('');
  const [departmentId,setDepartmentId]=useState(initial==='manager'?'':directory.departments[0]?.id||'');
  const [saving,setSaving]=useState(false),[error,setError]=useState('');
  const identity=useRef(crypto.randomUUID()),attempt=useRef<{signature:string;id:string}|null>(null);
  async function save(event:React.FormEvent){
    event.preventDefault();if(saving||!name.trim())return;setSaving(true);setError('');
    const departments=kind==='department'?[...directory.departments,{id:identity.current,name:name.trim(),purpose:purpose.trim()}]:directory.departments;
    const agents=kind==='department'?directory.agents:[...directory.agents,{id:identity.current,name:name.trim(),role:purpose.trim(),departmentId:departmentId||null,kind:kind==='manager'?'manager' as const:'employee' as const,runtimeKey:null}];
    const fields={version:directory.version,departments,agents},signature=JSON.stringify(fields);
    const id=attempt.current?.signature===signature?attempt.current.id:requestId();attempt.current={signature,id};
    try{onSave(await api<Directory>('/organization',{...fields,requestId:id},'PUT'));onClose();}catch(cause){setError((cause as Error).message);}finally{setSaving(false);}
  }
  return <Dialog title="Add to your team" onClose={onClose}><form className="company-form" onSubmit={event=>void save(event)}>
    <label>Type<select value={kind} onChange={event=>setKind(event.target.value as typeof kind)}><option value="department">Department</option><option value="agent">Agent</option></select></label>
    <label>Name<input autoFocus required maxLength={80} value={name} onChange={event=>setName(event.target.value)} placeholder={kind==='department'?'e.g. Operations':kind==='manager'?'e.g. CEO manager':'e.g. Content researcher'}/></label>
    {kind!=='department'&&<label>Department<select value={departmentId} onChange={event=>setDepartmentId(event.target.value)}><option value="">Company-wide</option>{directory.departments.map(department=><option value={department.id} key={department.id}>{department.name}</option>)}</select></label>}
    <label>{kind==='department'?'Purpose':'Responsibility'}<textarea value={purpose} onChange={event=>setPurpose(event.target.value)} maxLength={500} placeholder="What should this part of the team own?"/></label>
    {kind!=='department'&&<p className="company-form-note">This adds a place in your team. It will show “Setup needed” until a separate agent runtime is connected.</p>}
    {error&&<p role="alert" className="company-error">{error}</p>}
    <footer><button type="button" onClick={onClose}>Cancel</button><button className="primary" disabled={saving||!name.trim()}>{saving?'Saving…':kind==='department'?'Add department':'Add agent'}</button></footer>
  </form></Dialog>;
}

function makeRecords(state:MarketingState,history:MarketingMessage[]):CompanyRecord[]{
  const tasks=new Map(state.tasks.map(task=>[task.id,task]));
  const replies=new Map([...history,...state.messages].filter(message=>message.role==='assistant').map(message=>[message.id,message]));
  return [
    ...[...replies.values()].map(message=>({id:'reply:'+message.id,kind:'reply' as const,title:(message.taskId?tasks.get(message.taskId)?.title:undefined)||summary(message.content).slice(0,85)||'Saved agent reply',body:message.content,date:message.createdAt,taskId:message.taskId||undefined,meta:message.taskId?'Task conversation':'Direct conversation'})),
    ...(state.evidence||[]).map(source=>({id:'source:'+source.id,kind:'source' as const,title:source.title,body:source.note,date:source.created_at,taskId:source.task_id,url:publicLink(source.url)||undefined,meta:source.source})),
    ...(state.drafts||[]).map(draft=>({id:'draft:'+draft.id,kind:'draft' as const,title:`Draft #${draft.id} · ${draft.channel}`,body:draft.content+'\n\n**Rationale**\n\n'+draft.rationale+'\n\n**Community rules**\n\n'+draft.rules_url,date:draft.decided_at||0,url:publicLink(draft.destination)||undefined,meta:`${draft.status} · revision ${draft.revision}`})),
    ...(state.ownerDecisions||[]).map(decision=>({id:'decision:'+decision.requestId,kind:'decision' as const,title:`Draft #${decision.draftId} · ${decision.decision}`,body:`Owner decision: **${decision.decision}**\n\nRevision: ${decision.revision}\n\nReceipt status: ${decision.status}\n\nContent digest: \`${decision.digest}\``,date:decision.createdAt,meta:decision.status==='confirmed'?'Confirmed owner decision':'Reconciliation needed'})),
    ...(state.runway?.artifacts||[]).map(artifact=>({id:'deliverable:'+artifact.id,kind:'deliverable' as const,title:artifact.kind.replaceAll('_',' '),body:'```json\n'+artifact.content+'\n```',date:artifact.created_at,taskId:state.runway?.steps.find(step=>step.id===artifact.step_id)?.task_id,meta:'Standing marketing assignment'})),
    ...state.tasks.filter(task=>task.status==='done').map(task=>({id:'task:'+task.id,kind:'task' as const,title:task.title,body:task.next_action,date:task.updated_at,taskId:task.id,meta:'Completed work'}))
  ].sort((a,b)=>timestamp(b.date)-timestamp(a.date)||a.id.localeCompare(b.id));
}

function RecordPreview({record,agentName,onClose,onTask}:{record:CompanyRecord;agentName:string;onClose:()=>void;onTask:(id:string)=>void}){
  function download(){const file=new Blob([`# ${record.title}\n\n${kindLabel[record.kind]} · ${agentName}\n\n${record.body}${record.url?'\n\nSource: '+record.url:''}\n`],{type:'text/markdown;charset=utf-8'});const url=URL.createObjectURL(file);const link=document.createElement('a');link.href=url;link.download=record.title.replace(/[^a-zA-Z0-9 _-]/g,'').slice(0,70)+'.md';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
  return <article className="company-record-preview" aria-label="Record preview"><div className="company-preview-tools"><span>{kindLabel[record.kind]}</span><div><button type="button" onClick={download} aria-label="Download record"><Download size={17}/></button><button type="button" onClick={onClose} aria-label="Close record"><X size={18}/></button></div></div>
    <h2>{record.title}</h2><div className="company-record-provenance">{agentName} · {record.date?readableTime(record.date):'Saved draft'} · {record.meta}</div>
    {record.url&&<a className="company-source-link" href={record.url} target="_blank" rel="noopener noreferrer"><Link2 size={15}/>{new URL(record.url).hostname}<ArrowRight size={14}/></a>}
    <div className="company-record-body"><Markdown>{record.body}</Markdown></div>
    {record.taskId&&<button type="button" className="company-text-link" onClick={()=>onTask(record.taskId!)}>Open related task <ArrowRight size={16}/></button>}
  </article>;
}

export function CompanyWorkspace({hostOnline,focus,meetingApprovals,meetingApprovalCount=0}:{hostOnline:boolean;focus?:{view:View;scope?:Scope;taskId?:string;key:number};meetingApprovals?:ReactNode;meetingApprovalCount?:number}){
  const [route,setRoute]=useState<{scope:Scope;view:View}>(()=>({scope:rootScope,view:'tasks'})),{scope,view}=route;
  useEffect(()=>{if(focus)setRoute({scope:focus.scope||rootScope,view:focus.view});},[focus]);
  const [directory,setDirectory]=useState<Directory|null>(null),[state,setState]=useState<MarketingState|null>(null);
  const [canConfigure,setCanConfigure]=useState(false),[loading,setLoading]=useState(true),[readError,setReadError]=useState(''),[directoryError,setDirectoryError]=useState(''),[actionError,setActionError]=useState('');
  const [editor,setEditor]=useState<'department'|'agent'|'manager'|null>(null),[query,setQuery]=useState(''),[recordKind,setRecordKind]=useState<RecordKind|'all'>('all'),[recordId,setRecordId]=useState<string|null>(null);
  const [history,setHistory]=useState<MarketingMessage[]>([]),[historyCursor,setHistoryCursor]=useState<string|null>(null),[historyLoaded,setHistoryLoaded]=useState(false),[historyBusy,setHistoryBusy]=useState(false),[historyError,setHistoryError]=useState('');
  const [selectedId,setSelectedId]=useState<string|null>(null),[detailTab,setDetailTab]=useState<'summary'|'sources'|'conversation'>('summary'),[taskFilter,setTaskFilter]=useState<TaskStatus|'all'>('all');
  useEffect(()=>{if(focus?.taskId){setSelectedId(focus.taskId);setDetailTab('summary');}},[focus]);
  const [creating,setCreating]=useState(false),[newTitle,setNewTitle]=useState(''),[newAction,setNewAction]=useState(''),[newPriority,setNewPriority]=useState<TaskPriority>('normal'),[working,setWorking]=useState(false);
  const sequence=useRef(0),createAttempt=useRef<{signature:string;id:string}|null>(null),updateAttempt=useRef<{signature:string;id:string}|null>(null);
  const refresh=useCallback(async()=>{
    const current=++sequence.current;
    const [org,marketing]=await Promise.allSettled([api<{directory:Directory;canConfigure:boolean}>('/organization'),api<MarketingState>('/marketing/state')]);
    if(current!==sequence.current)return;
    if(org.status==='fulfilled'){setDirectory(org.value.directory);setCanConfigure(org.value.canConfigure);setDirectoryError('');}else setDirectoryError(String(org.reason?.message||org.reason));
    if(marketing.status==='fulfilled'){setState(marketing.value);setReadError('');}else setReadError(String(marketing.reason?.message||marketing.reason));
    setLoading(false);
  },[]);
  useEffect(()=>{void refresh();const timer=window.setInterval(()=>{if(document.visibilityState==='visible')void refresh();},8000);return()=>{sequence.current++;clearInterval(timer);};},[refresh]);
  useEffect(()=>{try{localStorage.setItem(routeKey,JSON.stringify(route));}catch{}},[route]);
  useEffect(()=>{if(directory&&((scope.kind==='agent'&&!directory.agents.some(agent=>agent.id===scope.id))||(scope.kind==='department'&&!directory.departments.some(department=>department.id===scope.id))))setRoute({scope:rootScope,view:'overview'});},[directory,scope]);
  const loadHistory=useCallback(async(before?:string)=>{setHistoryBusy(true);setHistoryError('');try{const page=await api<{items:MarketingMessage[];nextCursor:string|null}>('/marketing/history'+(before?'?before='+encodeURIComponent(before):''));setHistory(current=>[...new Map([...current,...page.items].map(item=>[item.id,item])).values()]);setHistoryCursor(page.nextCursor);setHistoryLoaded(true);}catch(error){setHistoryError((error as Error).message);}finally{setHistoryBusy(false);}},[]);
  useEffect(()=>{if(view==='records'&&!historyLoaded&&!historyBusy&&!historyError)void loadHistory();},[view,historyLoaded,historyBusy,historyError,loadHistory]);
  const liveAgent=directory?.agents.find(agent=>agent.runtimeKey==='marketing');
  const agent=scope.kind==='agent'?directory?.agents.find(agent=>agent.id===scope.id):undefined;
  const department=scope.kind==='department'?directory?.departments.find(item=>item.id===scope.id):agent?.departmentId?directory?.departments.find(item=>item.id===agent.departmentId):undefined;
  const visibleAgents=directory?.agents.filter(item=>scope.kind==='company'||(scope.kind==='department'?item.departmentId===scope.id:item.id===scope.id))||[];
  const hasMarketing=visibleAgents.some(item=>item.runtimeKey==='marketing');
  const name=(item:Employee)=>item.runtimeKey==='marketing'?state?.employee.name||item.name:item.name;
  const connection=state?.connection.status||'disconnected';
  const canChatWrite=hasMarketing&&hostOnline&&!readError&&connection==='connected'&&state?.canConfigure===true;
  const canTaskWrite=hasMarketing&&hostOnline&&!readError&&state?.taskStoreAvailable===true&&state?.canConfigure===true;
  const allTasks=useMemo(()=>[...(state?.tasks||[])].sort((a,b)=>priorityOrder[a.priority]-priorityOrder[b.priority]||b.updated_at-a.updated_at),[state]);
  const tasks=hasMarketing?allTasks:[];
  const selected=state?.tasks.find(task=>task.id===selectedId);
  const allRecords=useMemo(()=>state?makeRecords(state,history):[],[state,history]);
  const records=hasMarketing?allRecords:[];
  const filteredRecords=records.filter(record=>(recordKind==='all'||record.kind===recordKind)&&`${record.title} ${record.body} ${record.meta}`.toLowerCase().includes(query.toLowerCase()));
  const selectedRecord=filteredRecords.find(record=>record.id===recordId);
  const pendingDrafts=hasMarketing?state?.drafts?.filter(draft=>draft.status==='pending').length||0:0;
  const attention=tasks.filter(needsDecision);
  function navigate(next:Scope,nextView:View=next.kind==='agent'?'records':'overview'){setRoute({scope:next,view:nextView});setRecordId(null);setSelectedId(null);setQuery('');setRecordKind('all');}
  function openTask(id:string){setSelectedId(id);setDetailTab('summary');}
  function showRecords(){setRoute(current=>({...current,view:'records'}));}
  const employeeName=liveAgent?name(liveAgent):'Marketing agent';
  async function createTask(event:React.FormEvent){
    event.preventDefault();if(!newTitle.trim()||!canTaskWrite||working)return;setWorking(true);setActionError('');
    const fields={title:newTitle.trim(),status:'ready',priority:newPriority,next_action:newAction.trim(),action_state:newAction.trim()?'agent_ready':'none'},signature=JSON.stringify(fields);
    const id=createAttempt.current?.signature===signature?createAttempt.current.id:requestId();createAttempt.current={signature,id};
    try{const created=await api<MarketingTask>('/marketing/tasks',{...fields,requestId:id});createAttempt.current=null;setCreating(false);setNewTitle('');setNewAction('');await refresh();openTask(created.id);}catch(error){setActionError((error as Error).message);}finally{setWorking(false);}
  }
  async function updateTask(task:MarketingTask,changes:Partial<Pick<MarketingTask,'status'|'priority'>>){
    if(!canTaskWrite||working)return;setWorking(true);setActionError('');const fields={...changes,version:task.version},signature=task.id+JSON.stringify(fields);
    const id=updateAttempt.current?.signature===signature?updateAttempt.current.id:requestId();updateAttempt.current={signature,id};
    try{await api('/marketing/tasks/'+task.id,{...fields,requestId:id},'PUT');updateAttempt.current=null;await refresh();}catch(error){setActionError((error as Error).message);}finally{setWorking(false);}
  }
  const agentStatus=(item:Employee)=>item.runtimeKey!=='marketing'?'Setup needed':readError?'Unable to refresh':connection==='connected'?'Connected':connection==='busy'?'Working':connection==='auth_required'?'Authentication needed':'Offline';
  const tabs:{id:View;label:string;icon:typeof FileText;count?:number}[]=[

    {id:'tasks',label:'Board',icon:CheckCircle2,count:tasks.filter(task=>task.status!=='done'&&!isPaused(task)).length},{id:'records',label:'Records',icon:FolderOpen,count:records.length},{id:'wiki',label:'Wiki',icon:BookOpen},
    {id:'activity',label:'Activity',icon:LayoutDashboard},{id:'approvals',label:'Approvals',icon:ShieldCheck,count:pendingDrafts+(hasMarketing?meetingApprovalCount:0)},
    {id:'team' as View,label:'Team',icon:Users,count:visibleAgents.length}, ...(hasMarketing?[{id:'brief' as View,label:'Brief & ethos',icon:Settings2}]:[])
  ];
  function AgentCard({item}:{item:Employee}){return <button className="company-agent-card" type="button" onClick={()=>navigate({kind:'agent',id:item.id})}><span className={'company-avatar '+(item.kind==='manager'?'manager':'')}>{item.kind==='manager'?<LayoutDashboard size={21}/>:name(item).slice(0,2).toUpperCase()}</span><span><strong>{name(item)}</strong><small>{item.role||'Responsibility to be defined'}</small><span className={'company-status '+(item.runtimeKey==='marketing'&&connection==='connected'?'live':'')}>{agentStatus(item)}</span></span><ArrowRight size={16}/></button>;}
  return <section className="company-workspace" aria-label="Company workspace">
    <div className="company-main">
      <div className="company-topbar"><div><p className="eyebrow">COMPANY WORKSPACE</p><h1>Work</h1></div><label className="work-scope">Showing<select aria-label="Work scope" value={scope.kind+':'+scope.id} onChange={event=>{const [kind,id]=event.target.value.split(':');navigate({kind:kind as Scope['kind'],id},view==='brief'?'records':view);}}><option value="company:company">Everyone</option>{directory?.departments.map(d=><optgroup key={d.id} label={d.name}><option value={'department:'+d.id}>{d.name} department</option>{directory.agents.filter(a=>a.departmentId===d.id).map(a=><option value={'agent:'+a.id} key={a.id}>{name(a)}</option>)}</optgroup>)}{directory?.agents.filter(a=>!a.departmentId).map(a=><option key={a.id} value={'agent:'+a.id}>{name(a)}</option>)}</select></label><button className="company-refresh" aria-label="Refresh company records" onClick={()=>void refresh()}><RefreshCw size={16}/></button></div>      {(directoryError||readError||!hostOnline)&&<div className="company-error" role="alert"><CircleAlert size={17}/><span>{directoryError?'Team directory unavailable: '+directoryError:!hostOnline?'The host is offline. Reconnect to make changes.':'Marketing records could not refresh: '+readError} {state?'Saved records shown here may be out of date.':''}</span></div>}
      {actionError&&<div className="company-error" role="alert"><span>{actionError}</span><button type="button" onClick={()=>setActionError('')} aria-label="Dismiss error"><X size={16}/></button></div>}
      <nav className="company-tabs" aria-label="Workspace views">{tabs.filter(tab=>!['team','brief'].includes(tab.id)).map(({id,label,icon:Icon,count})=><button type="button" key={id} aria-current={view===id?'page':undefined} onClick={()=>{setRoute(current=>({...current,view:id}));setRecordId(null);}}><Icon size={16}/>{label}{!!count&&<span>{count}</span>}</button>)}<details className="work-settings"><summary><Settings2 size={16}/>Manage</summary><div>{tabs.filter(tab=>['team','brief'].includes(tab.id)).map(tab=><button key={tab.id} onClick={event=>{setRoute(current=>({...current,view:tab.id}));event.currentTarget.closest('details')?.removeAttribute('open');}}>{tab.label}</button>)}</div></details></nav>
      {loading&&!directory?<div className="company-empty"><LoaderCircle className="marketing-spin"/><h2>Opening your company…</h2></div>:!directory?<div className="company-empty"><h2>Your directory is unavailable</h2><button onClick={()=>void refresh()}>Try again</button></div>:<>
        {view==='overview'&&<div className="company-overview"><div className="company-summary-strip"><button onClick={()=>setRoute(current=>({...current,view:'tasks'}))}><strong>{attention.length}</strong><span>Needs your attention</span><ArrowRight size={16}/></button><button onClick={showRecords}><strong>{records.length}</strong><span>Saved records</span><ArrowRight size={16}/></button><button onClick={()=>setRoute(current=>({...current,view:'team'}))}><strong>{visibleAgents.filter(item=>item.runtimeKey).length}<em> / {visibleAgents.length}</em></strong><span>Agents connected</span><ArrowRight size={16}/></button></div>
          <div className="company-overview-columns"><section className="company-panel"><div className="company-section-title"><div><p className="eyebrow">DECISION INBOX</p><h2>Where you’re needed</h2></div><ShieldCheck size={20}/></div>{attention.length?attention.slice(0,3).map(task=><button type="button" className="company-inbox-row" key={task.id} onClick={()=>openTask(task.id)}><span className={'company-priority-dot '+task.priority}/><span><strong>{task.title}</strong><small>{summary(task.blocker||task.next_action)}</small><em>{employeeName} · {priorityLabel[task.priority]} priority</em></span><ArrowRight size={16}/></button>):<div className="company-calm"><CheckCircle2 size={24}/><p>No task is waiting for your decision.</p></div>}{pendingDrafts>0&&<button className="company-text-link" onClick={()=>setRoute(current=>({...current,view:'approvals'}))}>Review {pendingDrafts} drafts <ArrowRight size={16}/></button>}</section>
          <section className="company-panel"><div className="company-section-title"><div><p className="eyebrow">THE PAPER TRAIL</p><h2>Latest from your team</h2></div><button className="company-text-link" onClick={showRecords}>All records <ArrowRight size={15}/></button></div>{records.slice(0,5).map(record=>{const Icon=recordIcon[record.kind];return <button className="company-recent-row" key={record.id} onClick={()=>{showRecords();setRecordId(record.id);}}><span className={'company-record-icon '+record.kind}><Icon size={18}/></span><span><strong>{record.title}</strong><small>{kindLabel[record.kind]} · {record.date?readableTime(record.date):record.meta}</small></span><ChevronRight size={15}/></button>;})}{!records.length&&<div className="company-calm"><FolderOpen size={25}/><p>Records will appear here as your agents work.</p></div>}</section></div>
          <div className="company-section-title"><div><p className="eyebrow">YOUR PEOPLE</p><h2>{scope.kind==='company'?'Departments & agents':'Department team'}</h2></div>{canConfigure&&<button className="company-text-link" onClick={()=>setEditor('agent')}><Plus size={15}/> Add agent</button>}</div><div className="company-agent-grid">{visibleAgents.map(item=><AgentCard key={item.id} item={item}/>)}{!visibleAgents.length&&<div className="company-empty compact"><Users size={26}/><p>Add an agent to this department to give its work a home.</p></div>}</div>
        </div>}
        {view==='records'&&<section aria-label="Agent records"><div className="company-section-title"><div><h2>Records</h2><p>Replies, research, drafts, and decisions. One place to find them.</p></div></div><div className="company-record-toolbar"><label className="company-search"><Search size={18}/><input aria-label="Search records" value={query} onChange={event=>{setQuery(event.target.value);setRecordId(null);}} placeholder="Find a record by title, content, or source…"/>{query&&<button aria-label="Clear search" onClick={()=>setQuery('')}><X size={15}/></button>}</label><label className="company-type-filter"><span>Type</span><select aria-label="Record type" value={recordKind} onChange={event=>{setRecordKind(event.target.value as typeof recordKind);setRecordId(null);}}><option value="all">All records</option>{Object.entries(kindLabel).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label></div>
          <div className={'company-record-layout '+(selectedRecord?'has-record':'')}><div className="company-record-list"><div className="company-list-caption">{filteredRecords.length} records <span>{scope.kind==='company'?'Across your company':agent?name(agent):department?.name}</span></div>{filteredRecords.map(record=>{const Icon=recordIcon[record.kind];return <button type="button" className="company-record-row" key={record.id} aria-pressed={selectedRecord?.id===record.id} onClick={()=>setRecordId(record.id)}><span className={'company-record-icon '+record.kind}><Icon size={19}/></span><span><strong>{record.title}</strong><small>{summary(record.body).slice(0,135)}</small><em>{kindLabel[record.kind]} · {employeeName}</em></span><time>{record.date?readableTime(record.date):record.meta}</time><ChevronRight size={15}/></button>;})}{!filteredRecords.length&&<div className="company-empty"><Search size={28}/><h3>{query?'No matching records':'No records yet'}</h3><p>{query?'Try a different search or record type.':agent&&!agent.runtimeKey?'This agent needs a runtime connection before it can create records.':'Your agent’s saved work will appear here.'}</p></div>}
            {hasMarketing&&(historyCursor||historyError)&&<div className="company-history-more">{historyError&&<p role="alert">Earlier replies could not load: {historyError}</p>}<button disabled={historyBusy} onClick={()=>void loadHistory(historyCursor||undefined)}>{historyBusy?'Loading…':'Load earlier agent replies'}</button></div>}
            {hasMarketing&&state&&(state.tasks.length>=1000||state.evidence.length>=500||state.drafts.length>=100)&&<p className="company-form-note">Showing the latest 1,000 tasks, 500 source records, and 100 drafts from the ledger.</p>}
          </div>{selectedRecord?<RecordPreview record={selectedRecord} agentName={employeeName} onClose={()=>setRecordId(null)} onTask={openTask}/>:<div className="company-preview-placeholder"><FolderOpen size={30}/><h3>Open a record</h3><p>Select any item to read its full contents and trace it back to the task.</p></div>}</div>
        </section>}
        {view==='wiki'&&<CompanyWikiPanel directory={directory} scope={scope} canEdit={canConfigure&&hostOnline}/>}
        {view==='tasks'&&<>{hasMarketing&&<MarketingRunwayPanel runway={state?.runway} profile={state?.profile} canControl={canConfigure&&hostOnline} canContribute={hostOnline&&!readError} liveWorkEnabled={state?.runwayLiveEnabled===true} deferredRevisionEnabled={state?.deferredRevisionEnabled===true} nativeSharedEnabled={state?.sharedGatewayEnabled===true} onRefresh={refresh}/>}<WorkBoard tasks={tasks} employeeName={employeeName} onOpen={openTask} onCreate={()=>setCreating(true)} canCreate={!!canTaskWrite}/></>}
        {view==='activity'&&<WorkActivity events={hasMarketing?state?.activity||[]:[]} tasks={tasks} onTask={openTask}/>}
        {view==='approvals'&&hasMarketing&&meetingApprovals}
        {view==='approvals'&&(hasMarketing&&state?<MarketingDrafts drafts={state.drafts||[]} decisions={state.ownerDecisions||[]} canDecide={!!canTaskWrite&&canConfigure} onRefresh={refresh} onError={setActionError}/>:<div className="company-empty"><ShieldCheck size={28}/><h2>No approvals waiting</h2><p>Drafts for this team will appear here when they are ready for you.</p></div>)}
        {view==='team'&&<section><div className="company-section-title"><div><h2>{scope.kind==='company'?'Your team':'Department agents'}</h2><p>Choose an agent to open its records, tasks, and conversation.</p></div>{canConfigure&&<button className="primary" onClick={()=>setEditor('agent')}><Plus size={16}/> Add agent</button>}</div><div className="company-agent-grid">{visibleAgents.map(item=><AgentCard key={item.id} item={item}/>)}</div>{!visibleAgents.length&&<div className="company-empty"><Users size={28}/><h3>No agents in this department yet</h3></div>}{scope.kind==='company'&&<div className="company-department-list"><div className="company-section-title"><h2>Departments</h2>{canConfigure&&<button onClick={()=>setEditor('department')}><Plus size={16}/> Add department</button>}</div>{directory.departments.map(item=><button key={item.id} onClick={()=>navigate({kind:'department',id:item.id})}><Building2 size={20}/><span><strong>{item.name}</strong><small>{item.purpose}</small></span><span>{directory.agents.filter(employee=>employee.departmentId===item.id).length} agents</span><ChevronRight size={16}/></button>)}</div>}</section>}
        {(view==='chat'||view==='brief')&&(hasMarketing&&state?(view==='chat'?<MarketingDiscussion key={state.employee.sessionKey} state={state} canWrite={canChatWrite} onRefresh={refresh}/>:<MarketingBrief profile={state.profile} canEdit={!!canTaskWrite&&canConfigure} onRefresh={refresh} onError={setActionError}/>):<div className="company-empty"><Users size={32}/><h2>{agent?'This agent needs a connection':'Choose an agent'}</h2><p>{agent?'Its place in the directory is saved. A separate runtime must be configured before it can receive assignments or messages.':'Open an agent from the team directory to see its conversation and brief.'}</p>{agent&&<span className="company-status">Setup needed</span>}</div>)}
      </>}
    </div>
    {editor&&directory&&<TeamEditor directory={directory} initial={editor} onSave={setDirectory} onClose={()=>setEditor(null)}/>}
    {creating&&<Dialog title="New task" onClose={()=>setCreating(false)}><form className="company-form" onSubmit={event=>void createTask(event)}><label>Task title<input autoFocus required maxLength={160} value={newTitle} onChange={event=>setNewTitle(event.target.value)}/></label><label>Next action<textarea value={newAction} onChange={event=>setNewAction(event.target.value)} maxLength={2000}/></label><label>Priority<select value={newPriority} onChange={event=>setNewPriority(event.target.value as TaskPriority)}><option value="high">High</option><option value="normal">Normal</option><option value="low">Low</option></select></label>{actionError&&<p role="alert">{actionError}</p>}<footer><button type="button" onClick={()=>setCreating(false)}>Cancel</button><button className="primary" disabled={working||!newTitle.trim()}>{working?'Saving…':'Create task'}</button></footer></form></Dialog>}
    {selected&&state&&<Dialog title={selected.title} onClose={()=>setSelectedId(null)}><div className="company-task-dialog"><div className="company-task-byline">{employeeName} · Updated {readableTime(selected.updated_at)}</div><nav className="company-filter-pills" aria-label="Task detail views">{(['summary','sources','conversation'] as const).map(item=><button type="button" key={item} aria-pressed={detailTab===item} onClick={()=>setDetailTab(item)}>{item==='summary'?'Details':item==='sources'?'Sources':'Conversation'}</button>)}</nav>{detailTab==='summary'?<><div className="marketing-task-controls"><label>Status<select value={selected.status} disabled={!canTaskWrite||working} onChange={event=>void updateTask(selected,{status:event.target.value as TaskStatus})}>{statusOrder.map(status=><option key={status} value={status}>{statusLabel[status]}</option>)}</select></label><label>Priority<select value={selected.priority} disabled={!canTaskWrite||working} onChange={event=>void updateTask(selected,{priority:event.target.value as TaskPriority})}>{(['high','normal','low'] as const).map(priority=><option key={priority} value={priority}>{priorityLabel[priority]}</option>)}</select></label></div><h3>Next action</h3><p className="company-task-next">{selected.next_action||'No next action recorded.'}</p><small>{selected.status==='paused'?'Paused. Change the status when this work should resume.':actionLabel[selected.action_state]}</small>{selected.blocker&&<div className="company-task-blocker"><CircleAlert size={17}/>{selected.blocker}</div>}<button className="company-text-link" onClick={()=>setDetailTab('conversation')}>Discuss with {employeeName} <ArrowRight size={16}/></button></>:detailTab==='sources'?<MarketingEvidencePanel task={selected} evidence={state.evidence||[]} canAdd={!!canTaskWrite&&canConfigure} onRefresh={refresh} onError={setActionError}/>:<MarketingDiscussion key={selected.conversation_key} state={state} task={selected} canWrite={canChatWrite} onRefresh={refresh}/>}{actionError&&<p role="alert" className="company-error">{actionError}</p>}</div></Dialog>}
  </section>;
}
