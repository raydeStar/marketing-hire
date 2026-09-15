import React, { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import Markdown from 'react-markdown';
import { Home, ListTodo, Clock3, BookOpen, Settings, ArrowUpRight, ArrowUp, Plus, Check, ShieldCheck, ChevronRight, X, Feather, CircleAlert, WifiOff, FileText, Ban, LoaderCircle, PanelLeft } from 'lucide-react';
import './style.css';
import './workspace.css';
import './raven.css';
import {Collections} from './components/Collections';
import {Feed} from './components/Feed';
import {StudySettings} from './components/StudySettings';
import {StudySearch} from './components/StudySearch';
import {StudyNavigation} from './components/StudyNavigation';
import {api,setCsrf,readReplay,restoreSession} from './api';
import type {Page,Run,State} from './types';
import {names,StateIcon,Raven} from './components/Raven';
import {Conversation} from './components/Conversation';
import {MessageComposer} from './components/MessageComposer';
import {TaskDetail} from './components/TaskDetail';
import {BudgetFields,defaultLimits} from './components/BudgetFields';


import {ResearchScope} from './components/ResearchScope';
import {ModelUsageButton,TokenUsage} from './components/TokenUsage';
import {TaskGuidance} from './components/TaskGuidance';

import {MemoryNotebook} from './components/MemoryNotebook';


import {MaintenancePage,type MaintenanceView} from './components/Maintenance';
import type {MemorySelection} from './types';

function App({onMaintenance}:{onMaintenance:(view:MaintenanceView)=>void}) {
  const [session,setSession]=useState<{owner:boolean}|null>(null),[loaded,setLoaded]=useState(false),[key,setKey]=useState(''),[pair,setPair]=useState(false);
  const [data,setData]=useState<State|null>(null),[tab,setTab]=useState('Home'),[selected,setSelected]=useState<string|null>(null),[online,setOnline]=useState(false),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  const [message,setMessage]=useState(''),[scope,setScope]=useState<string[]>([]),[fault,setFault]=useState(false),[showScope,setShowScope]=useState(false);
  const [page,setPage]=useState<Page|null>(null),[edit,setEdit]=useState(''),[revisions,setRevisions]=useState<Page[]>([]),[trace,setTrace]=useState<any[]>([]);
  const [limits,setLimits]=useState(defaultLimits);
  const [chatLimits,setChatLimits]=useState({...defaultLimits,modelCalls:1,toolCalls:0,repairs:0});
  const [mode,setMode]=useState('chat'),[hosts,setHosts]=useState('');
  const [publicSearch,setPublicSearch]=useState(false),[openResults,setOpenResults]=useState(true),[searchQueries,setSearchQueries]=useState(3);
  const [memoryScope,setMemoryScope]=useState<MemorySelection[]>([]);
  const [focusId,setFocusId]=useState<string|undefined>();
  const [sidebarExpanded,setSidebarExpanded]=useState(false);
  const [logOpen,setLogOpen]=useState(()=>window.innerWidth>1100);
  const [logView,setLogView]=useState<'activity'|'info'>('activity'),[usageExpanded,setUsageExpanded]=useState(true),[logFocusRequest,setLogFocusRequest]=useState(0);
  const logInfoRef=useRef<HTMLDivElement>(null),logTriggerRef=useRef<HTMLButtonElement|null>(null);
  useEffect(()=>{
    if(!logOpen)return;
    if(logView==='info'){
      logInfoRef.current?.querySelector('summary')?.focus({preventScroll:true});
      if(logInfoRef.current)logInfoRef.current.scrollTop=0;
    }else if(logTriggerRef.current?.classList.contains('raven-log-toggle')){
      document.querySelector<HTMLButtonElement>('[aria-label="Close activity log"]')?.focus({preventScroll:true});
    }
  },[logOpen,logView,logFocusRequest]);
  const [researchLimits,setResearchLimits]=useState({...defaultLimits,modelCalls:6,toolCalls:16,seconds:600,maxTotalTokens:96000});
  useEffect(()=>{const small=window.matchMedia('(max-width:1100px)');const changed=()=>{if(small.matches)setLogOpen(false);};small.addEventListener('change',changed);return()=>small.removeEventListener('change',changed);},[]);
  async function refresh() { const state=await api<State>('/state'); setData(state); return state; }
  async function act(work:()=>Promise<unknown>) { setBusy(true);setError('');try {await work();await refresh();}catch(e){setError((e as Error).message);}finally{setBusy(false);} }
  useEffect(()=>{
    let stale=false;
    const restore=()=>restoreSession().then(s=>{if(!stale&&s){setCsrf(s.csrf);setSession(s);setError('');}}).catch(e=>{if(!stale)setError(e.message);}).finally(()=>{if(!stale)setLoaded(true);});
    const launch=()=>{if(new URLSearchParams(location.hash.slice(1)).has('launch'))void restore();};
    void restore();window.addEventListener('hashchange',launch);
    return()=>{stale=true;window.removeEventListener('hashchange',launch);};
  },[]);
  useEffect(()=>{
    if(!session)return;
    refresh().then(d=>{setScope(d.pages.filter(p=>p.path.startsWith('notes/')).map(p=>p.path));}).catch(e=>setError(e.message));
    const events=new EventSource('/api/events');let timer:ReturnType<typeof setTimeout>|undefined;
    const offline=()=>setOnline(false);const reconnect=()=>api('/session').then(()=>{setOnline(true);return refresh();}).catch(()=>setOnline(false));
    window.addEventListener('offline',offline);window.addEventListener('online',reconnect);
    events.onopen=()=>{setOnline(true);refresh().catch(()=>setOnline(false));};events.onerror=()=>setOnline(false);events.onmessage=()=>{clearTimeout(timer);timer=setTimeout(()=>refresh().catch(()=>setOnline(false)),80);};
    return()=>{events.close();clearTimeout(timer);window.removeEventListener('offline',offline);window.removeEventListener('online',reconnect);};
  },[session]);
  useEffect(()=>{let stale=false;setTrace([]);if(selected)readReplay(selected,()=>stale).then(events=>{if(!stale)setTrace(events);}).catch(e=>{if(!stale)setError(e.message);});return()=>{stale=true;};},[selected,data?.runs.find(r=>r.id===selected)?.updated]);
  const run=data?.runs.find(r=>r.id===selected);
  const pending=data?.runs.filter(r=>r.state==='awaitingApproval')||[];
  const active=data?.runs.find(r=>['running','queued','awaitingApproval','awaitingInput','needsAttention'].includes(r.state));
  const guidanceRun=data?.runs.find(item=>item.research&&item.research.phase!=='finished');
  const companionRun=run||active;
  const ravenState=!online?'disconnected':companionRun?.state||'idle';
  const companionStatus=!online?'Disconnected':companionRun?names[companionRun.state]:'At your service.';
  async function openPage(path:string){const p=await api<Page>('/knowledge?path='+encodeURIComponent(path));setPage(p);setEdit(p.content);setRevisions(await api('/revisions?path='+encodeURIComponent(path)));setTab('Knowledge');setSelected(null);}
  async function seed(){await api('/demo/seed',{});await refresh();setScope(['notes/deadlines.md','notes/constraints.md','notes/conflict.md']);setShowScope(true);}
  async function start(){const r=await api<Run>('/runs',{objective:message||'Turn my scattered notes into a useful weekly plan.',readScope:scope,demoFailure:fault,budget:limits});showRun(r.id);setMessage('');setShowScope(false);}
  async function sendMessage(){
    const sentMessage=message;
    const payload=mode==='research'?{content:message,mode,readScope:scope,memories:memoryScope,budget:researchLimits,web:hosts.trim()||publicSearch?{hosts:hosts.split(',').map(host=>host.trim().toLowerCase()).filter(Boolean),maxFetches:4,...(publicSearch?{search:{provider:'brave',credentialId:data?.search?.credentialId,maxQueries:searchQueries,openResults}}:{})}:null}:{content:message,budget:chatLimits};
    const created=await api<Run>('/chat',payload);setMessage(current=>current===sentMessage?'':current);setFocusId(undefined);
    if(mode==='research'){showRun(created.id);}
  }
  async function decision(allow:boolean){if(!run?.approval)return;await api('/runs/'+run.id+'/approve',{approvalId:run.approval.id,digest:run.approval.digest,allow});}
  function nav(name:string){setFocusId(undefined);setTab(name);setSelected(null);if(window.innerWidth<=1100)setLogOpen(false);}
  function showRun(id:string){setSelected(id);setTab('Activity');if(window.innerWidth<=1100)setLogOpen(false);}
  function openTokenInfo(trigger:HTMLButtonElement){logTriggerRef.current=trigger;setLogView('info');setUsageExpanded(true);setLogOpen(true);setLogFocusRequest(value=>value+1);}
  function openActivityLog(trigger:HTMLButtonElement){logTriggerRef.current=trigger;setLogView('activity');setLogOpen(true);setLogFocusRequest(value=>value+1);}
  function closeSidebar(){setSidebarExpanded(false);document.querySelector<HTMLButtonElement>('.rail-toggle')?.focus();}
  function closeLog(){
    setLogOpen(false);
    // Let the raven return to his perch before handing keyboard focus back.
    requestAnimationFrame(()=>(logTriggerRef.current?.isConnected?logTriggerRef.current:document.querySelector<HTMLButtonElement>('.model-usage'))?.focus({preventScroll:true}));
  }
  function chooseMessageMode(next:string){setMode(next);requestAnimationFrame(()=>document.querySelector<HTMLTextAreaElement>('[aria-label="Message or goal"]')?.focus());}
  function discuss(text:string){nav('Home');setMessage(text);setMode('chat');requestAnimationFrame(()=>document.querySelector<HTMLTextAreaElement>('[aria-label="Message or goal"]')?.focus());}
  useEffect(()=>{const escape=(event:KeyboardEvent)=>{if(event.key==='Escape'){if(sidebarExpanded)closeSidebar();else if(logOpen)closeLog();}};window.addEventListener('keydown',escape);return()=>window.removeEventListener('keydown',escape);},[logOpen,sidebarExpanded]);
  function ledger(items:Run[]){let previous='';return items.map(r=>{const date=new Date(r.created);const today=new Date();const yesterday=new Date();yesterday.setDate(today.getDate()-1);const group=date.toDateString()===today.toDateString()?'Today':date.toDateString()===yesterday.toDateString()?'Yesterday':date.toLocaleDateString(undefined,{month:'long',day:'numeric'});const heading=group!==previous;previous=group;return <React.Fragment key={r.id}>{heading&&<h3 className="time-group">{group}</h3>}<button data-run-id={r.id} className="ledger-row" onClick={()=>{showRun(r.id);}}><span className={'state-icon '+r.state}><StateIcon state={r.state}/></span><span className="ledger-copy"><strong>{r.goal.objective.length>80?r.goal.objective.slice(0,77)+'…':r.goal.objective}</strong><small>{r.summary}</small></span><span className="ledger-time"><span className={'badge '+r.state}>{names[r.state]}</span><time>{date.toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'})}</time></span><ChevronRight size={16}/></button></React.Fragment>;});}
  if(!loaded)return <main className="unlock"><Raven/><h1>Opening the study…</h1></main>;
  if(!session)return <main className="unlock"><div className="wordmark"><span className="mark">T</span>THADDEUS</div><Raven state="listening"/><p className="eyebrow">YOUR PRIVATE STUDY</p><h1>A little order.<br/><em>Entirely yours.</em></h1><p>Unlock this browser with the host access key.<br/>Your notes remain on the computer running Thaddeus.</p><form onSubmit={e=>{e.preventDefault();setError('');(pair?api('/pair/claim',{code:key,name:'Phone browser'}):api('/auth/login',{key})).then(s=>{if(pair){setError('Waiting for confirmation on the host. Then select Finish pairing.');}else{setCsrf(s.csrf);setSession(s);setKey('');}}).catch(e=>setError(e.message));}}><label>{pair?'One-time pairing code':'Host access key'}<input type="password" autoComplete="off" value={key} onChange={e=>setKey(e.target.value)} required/></label><button className="primary">{pair?'Request pairing':'Unlock study'} <ArrowUpRight size={17}/></button></form><button className="text-button" onClick={()=>setPair(!pair)}>{pair?'Use host access key':'Connect a phone instead'}</button>{pair&&<button onClick={()=>api('/pair/exchange',{}).then(s=>{if(s){setCsrf(s.csrf);setSession(s);}else setError('Host confirmation is still pending.');}).catch(e=>setError(e.message))}>Finish pairing</button>}<small>Host key: <code>.data/host-key.txt</code><br/>Phone access requires your host’s trusted HTTPS address.</small>{error&&<p role="alert" className="error">{error}</p>}</main>;
  return <div className={'app study-shell '+(logOpen?'log-open ':'')+(sidebarExpanded?'sidebar-expanded':'')}>
  <StudyNavigation current={selected?null:tab} openTodos={data?.library?.filter(item=>item.kind==='todo'&&item.status==='open').length||0} open={sidebarExpanded} onNavigate={nav}/>
  <div className="workspace"><header>
    <div className="header-location">
      <button type="button" className="rail-toggle" aria-label={sidebarExpanded?'Collapse sidebar':'Expand sidebar'} title={sidebarExpanded?'Hide sidebar':'Show sidebar'} aria-expanded={sidebarExpanded} aria-controls="study-sidebar" onClick={()=>setSidebarExpanded(value=>!value)}><PanelLeft size={19} strokeWidth={1.6} aria-hidden="true"/></button>
      <div className="breadcrumb"><span>Thaddeus</span><ChevronRight size={13}/><strong>{selected?'Run details':({Home:'Chat',Knowledge:'Artifacts',Todo:'To-do'} as Record<string,string>)[tab]||tab}</strong></div>
    </div>
    <div className="header-companion"><button type="button" className="raven-log-toggle" aria-label="Thaddeus: open activity log" title="Open activity log" aria-expanded={logOpen} aria-controls="activity-log" onClick={event=>openActivityLog(event.currentTarget)}><Raven state={ravenState}/></button></div>
    <div className="header-actions">
      <ModelUsageButton model={data?.provider.kind==='scripted'?'SCRIPTED DEMO':data?.provider.model||'Model'} runs={data?.runs||[]} online={online} expanded={logOpen&&logView==='info'} onOpen={openTokenInfo}/>
      {pending.length>0&&<button className="approval-pill" onClick={()=>showRun(pending[0].id)}><ShieldCheck size={15}/>{pending.length} approval</button>}
    </div>
  </header>
  {!online&&<div role="status" className="disconnect"><WifiOff size={17}/> Connection lost. Writes and approvals are disabled until the host reconnects.</div>}
  {error&&<div className="error alert" role="alert">{error}<button aria-label="Dismiss error" onClick={()=>setError('')}><X size={16}/></button></div>}
  <main className={run?'main task-layout':'main'} aria-label="Workspace">
{run?<TaskDetail run={run} owner={session.owner} trace={trace} online={online} busy={busy} onBack={()=>nav('Home')} onPage={path=>act(()=>openPage(path))} onDecision={allow=>act(()=>decision(allow))} onCancel={()=>act(()=>api('/runs/'+run.id+'/cancel',{}))} onResume={()=>act(()=>api('/runs/'+run.id+'/resume',{}))} onReconcile={refresh}/>:
  tab==='Home'?<section className="home conversation-workspace"><div className="conversation-title"><p className="eyebrow">A LITTLE ORDER. ROOM FOR WONDER.</p><h1>Conversation</h1></div>
  {!data?.chats.length&&<div className="conversation-empty"><Feather size={26}/><h2>What shall we make of today?</h2><p>Bring a question, an idea, or a little unfinished business.</p></div>}
  <Conversation focusId={focusId} messages={data?.chats||[]} runs={data?.runs||[]} online={online} busy={busy} onCancel={id=>act(()=>api('/runs/'+id+'/cancel',{}))} onGoal={text=>{setMessage(text);setShowScope(true);}}/>
  {active&&active.goal.kind!=='conversation'&&<button className="active-work" onClick={()=>showRun(active.id)}><StateIcon state={active.state}/><span><strong>{names[active.state]}</strong><small>{active.goal.objective}</small></span><ArrowUpRight size={16}/></button>}
  <div className="conversation-compose">
  {mode==='research'&&<ResearchScope availability={data?.research} pages={data?.pages||[]} scope={scope} onScope={setScope} memories={data?.memories||[]} memoryScope={memoryScope} onMemories={setMemoryScope} hosts={hosts} onHosts={setHosts} searchConnection={data?.search} search={publicSearch} onSearch={setPublicSearch} openResults={openResults} onOpenResults={setOpenResults} searchQueries={searchQueries} onSearchQueries={setSearchQueries} limits={researchLimits} onLimits={setResearchLimits}/>}
  {mode==='guidance'&&<div className="composer-guidance"><button className="text-button" onClick={()=>chooseMessageMode('chat')}>Back to message</button>{guidanceRun?<TaskGuidance key={guidanceRun.id} run={guidanceRun} online={online} onChanged={refresh}/>:<p role="status">There is no active research task to guide.</p>}</div>}
  {mode!=='guidance'&&<>
  <MessageComposer value={message} onChange={setMessage} mode={mode} onMode={chooseMessageMode} canGuide={!!guidanceRun} canSend={!(!message.trim()||busy||!online||(mode==='research'?(!data?.research?.enabled||data?.provider.kind!=='compatible'||(!scope.length&&!hosts.trim()&&!memoryScope.length&&!publicSearch)||(publicSearch&&!data?.search?.configured)||memoryScope.some(selection=>!data?.memories?.some(({entry,sourceStatus})=>entry.id===selection.id&&entry.version===selection.version&&sourceStatus==='current'))||data?.runs.some(r=>r.research&&r.research.phase!=='finished')):data?.runs.some(r=>r.goal.kind==='conversation'&&['queued','running'].includes(r.state))))} onSend={()=>act(sendMessage)}/>
  {mode==='research'&&data?.provider.kind!=='compatible'&&<p role="status">Configure a compatible model in Settings before starting research.</p>}
  </>}
  {showScope&&<section className="scope-card" aria-label="Plan scope"><h2>A small, explicit workspace</h2><p>Allow reading only these notes. The next write will need a separate approval.</p>{data?.pages.filter(p=>p.path.startsWith('notes/')).map(p=><label className="checkbox" key={p.path}><input type="checkbox" checked={scope.includes(p.path)} onChange={e=>setScope(e.target.checked?[...scope,p.path]:scope.filter(s=>s!==p.path))}/>{p.path}</label>)}{!data?.pages.length&&<button onClick={()=>act(seed)}>Load fictional notes</button>}<label className="checkbox"><input type="checkbox" checked={fault} onChange={e=>setFault(e.target.checked)}/> Demo only: exercise one bounded draft repair</label><BudgetFields value={limits} onChange={setLimits}/><button className="primary" disabled={!scope.length||busy||!online} onClick={()=>act(start)}>Read selected notes & create a plan <ArrowUpRight size={16}/></button></section>}
  </div></section>:
  tab==='Feed'?<Feed key={focusId||'feed'} focusId={focusId} feeds={data?.feeds} items={data?.library||[]} online={online} onChanged={refresh} onDiscuss={discuss}/>:
  tab==='Todo'||tab==='Ideas'?<Collections key={tab+(focusId||'')} focusId={focusId} kind={tab==='Todo'?'todo':'idea'} items={data?.library||[]} online={online} onChanged={refresh} onDiscuss={discuss}/>:
  tab==='Search'&&data?<StudySearch data={data} onPage={path=>act(()=>openPage(path))} onRun={showRun} onCollection={(kind,id)=>{nav(kind==='todo'?'Todo':kind==='idea'?'Ideas':'Feed');setFocusId(id);}} onChat={id=>{nav('Home');setFocusId(id);}}/>:
  tab==='Knowledge'?<section className="knowledge"><p className="eyebrow">SAVED & SOURCE-LINKED</p><h1>{page?page.path.split('/')[1].replace('.md','').replaceAll('-',' '):'Artifacts'}</h1><MemoryNotebook memories={data?.memories||[]} pages={data?.pages||[]} online={online} onChanged={refresh} onOpen={path=>act(()=>openPage(path))}/><div className="knowledge-grid"><div className="page-list"><button onClick={()=>{setPage({path:'notes/new-note.md',content:'',version:'absent',updated:''});setEdit('');setRevisions([]);}}><Plus size={16}/> New note</button>{data?.pages.map(p=><button className={page?.path===p.path?'active':''} key={p.path} onClick={()=>act(()=>openPage(p.path))}><FileText size={16}/>{p.path}</button>)}</div>{page?<div className="editor"><label>Page path<input disabled={page.version!=='absent'} value={page.path} onChange={e=>setPage({...page,path:e.target.value})}/></label><label>Markdown<textarea aria-label="Markdown editor" value={edit} onChange={e=>setEdit(e.target.value)}/></label><button className="primary" disabled={busy||!online} onClick={()=>act(async()=>{const p=await api<Page>('/knowledge',{path:page.path,content:edit,version:page.version},'PUT');setPage(p);setRevisions(await api('/revisions?path='+encodeURIComponent(p.path)));})}>Save my edits <Check size={16}/></button><details><summary>Reading view</summary><div className="draft"><Markdown components={{a:({href,children})=>href && /^(notes|plans)\/[a-z0-9-]+\.md$/.test(href)?<button className="text-button" onClick={()=>act(()=>openPage(href))}>{children}</button>:<a href={href}>{children}</a>}}>{edit}</Markdown></div></details><details><summary>Revision history · {revisions.length}</summary>{revisions.map((p,i)=><details key={i}><summary>{new Date(p.updated).toLocaleString()} · {p.version.slice(0,12)}</summary><pre>{p.content}</pre></details>)}</details></div>:<div className="empty"><BookOpen/><p>Choose a page or start a note.<br/>Your words are stored as ordinary Markdown.</p></div>}</div></section>:
  <StudySettings data={data} owner={session.owner} online={online} onChanged={refresh} onMaintenance={onMaintenance} onDataDeleted={()=>setPage(null)}/>}

  </main></div>
  {logOpen&&<aside className="activity-log" id="activity-log" aria-label="Activity log"><div className="log-heading"><h2>Activity log</h2><button aria-label="Close activity log" onClick={closeLog}><X size={17}/></button></div><div className="companion"><Raven state={ravenState} onClick={companionRun?()=>showRun(companionRun.id):undefined}/><h2>Thaddeus</h2><p>{companionStatus}</p></div>
    <nav className="log-views" aria-label="Log views"><button type="button" aria-pressed={logView==='activity'} onClick={()=>setLogView('activity')}>Activity</button><button type="button" aria-pressed={logView==='info'} onClick={()=>setLogView('info')}>Info</button></nav>
    {logView==='info'?<div className="log-info" ref={logInfoRef} role="region" aria-label="Model and token information"><p className="log-model"><small>SELECTED MODEL</small><strong>{data?.provider.kind==='scripted'?'Scripted demo':data?.provider.model}</strong></p><TokenUsage runs={data?.runs||[]} onRun={showRun} expanded={usageExpanded} onExpandedChange={setUsageExpanded}/><section className="reply-limits" aria-label="Reply limits"><h3>Reply limits</h3><p>{chatLimits.maxTotalTokens.toLocaleString()} token allowance · up to {chatLimits.maxOutputTokens.toLocaleString()} output tokens per call · {chatLimits.modelCalls} model call{chatLimits.modelCalls===1?'':'s'}.</p><BudgetFields value={chatLimits} onChange={setChatLimits}/></section></div>:<><div className="log-caption"><span>RECORDED WORK</span><small>{data?.runs.length||0} runs</small></div><div className="log-entries">{data?.runs.length?ledger(data.runs):<p className="log-empty">Nothing in the ledger yet. I shall resist inventing an achievement.</p>}</div></>}
  </aside>}
  </div>;
}
function Root(){
  const [maintenance,setMaintenance]=useState(location.pathname==='/maintenance'),[initial,setInitial]=useState<MaintenanceView>();
  const reopened=useCallback(()=>{history.replaceState(null,'','/');setMaintenance(false);setInitial(undefined);},[]);
  function started(view:MaintenanceView){history.replaceState(null,'','/maintenance');setInitial(view);setMaintenance(true);}
  return maintenance?<MaintenancePage initial={initial} onReopened={reopened}/>:<App onMaintenance={started}/>;
}
createRoot(document.getElementById('root')!).render(<Root/>);
if('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(()=>{});
