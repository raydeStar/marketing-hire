import {TaskActivity} from './components/TaskActivity';
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
import {ArtifactApps,ArtifactPage,localDay} from './components/ArtifactApps';
import {MessageComposer} from './components/MessageComposer';
import {ActivityDialog} from './components/ActivityDialog';
import {Modal} from './components/Modal';
import {TodoBoard} from './components/TodoBoard';
import {Ideas} from './components/Ideas';
import {useFileUploads} from './use-file-uploads';
import {UploadFeedback} from './components/UploadFeedback';
import type {UploadFile} from './types';
import './experience.css';
import {TaskDetail} from './components/TaskDetail';
import {DelegationsPanel} from './components/DelegationsPanel';
import {BudgetFields,defaultLimits} from './components/BudgetFields';
import {readComposerDraft,saveComposerDraft,type ComposerDraft} from './composer-draft';


import {ResearchScope} from './components/ResearchScope';
import {ModelUsageButton,TokenUsage} from './components/TokenUsage';
import {TaskGuidance} from './components/TaskGuidance';
import {ConnectionSetupCard} from './components/ConnectionSetupCard';

import {MemoryNotebook} from './components/MemoryNotebook';


import {MaintenancePage,type MaintenanceView} from './components/Maintenance';
import type {MemorySelection} from './types';

const appIdFromLocation=()=>/^\/apps\/([a-f0-9]{32})\/?$/.exec(location.pathname)?.[1]||null;
type NoteTarget={kind:'new'}|{kind:'open';path:string};

function App({onMaintenance}:{onMaintenance:(view:MaintenanceView)=>void}) {
  const [session,setSession]=useState<{id:string;owner:boolean}|null>(null),[loaded,setLoaded]=useState(false),[key,setKey]=useState(''),[pair,setPair]=useState(false);
  const [data,setData]=useState<State|null>(null),[tab,setTab]=useState('Home'),[selected,setSelected]=useState<string|null>(null),[online,setOnline]=useState(false),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  const [attachments,setAttachments]=useState<UploadFile[]>([]);
  const [pendingDiscussion,setPendingDiscussion]=useState<string|null>(null);
  const [pendingChatEdit,setPendingChatEdit]=useState<{content:string;run?:Run}|null>(null);
  const [draftNotice,setDraftNotice]=useState('');
  const retryKeys=useRef<Record<string,string>>({});
  const [message,setMessage]=useState(''),[scope,setScope]=useState<string[]>([]),[fault,setFault]=useState(false),[showScope,setShowScope]=useState(false);
  const [page,setPage]=useState<Page|null>(null),[edit,setEdit]=useState(''),[revisions,setRevisions]=useState<Page[]>([]),[trace,setTrace]=useState<any[]>([]);
  const [pendingNote,setPendingNote]=useState<NoteTarget|null>(null),[noteAction,setNoteAction]=useState<'loading'|'saving'|null>(null),[noteNotice,setNoteNotice]=useState('');
  const noteOperation=useRef(false);
  const noteDirty=!!page&&(edit!==page.content||(page.version==='absent'&&page.path!=='notes/new-note.md'));
  useEffect(()=>{
    if(!noteDirty)return;
    // A half-written page should not vanish while its author reaches for another book.
    const warn=(event:BeforeUnloadEvent)=>{event.preventDefault();event.returnValue='';};
    window.addEventListener('beforeunload',warn);return()=>window.removeEventListener('beforeunload',warn);
  },[noteDirty]);
  const [limits,setLimits]=useState(defaultLimits);
  const [chatLimits,setChatLimits]=useState({...defaultLimits,modelCalls:2,toolCalls:2,repairs:0,seconds:600});
  const [mode,setMode]=useState('chat'),[hosts,setHosts]=useState('');
  const [publicSearch,setPublicSearch]=useState(false),[openResults,setOpenResults]=useState(true),[searchQueries,setSearchQueries]=useState(3);
  const [memoryScope,setMemoryScope]=useState<MemorySelection[]>([]);
  const [focusId,setFocusId]=useState<string|undefined>();
  const [artifactView,setArtifactView]=useState<'all'|'apps'|'notes'|'images'|'files'>('all');
  const [artifactPanelId,setArtifactPanelId]=useState<string|null>(appIdFromLocation);
  const [artifactChatVisible,setArtifactChatVisible]=useState(false);
  const artifactReturn=useRef<{tab:string;selected:string|null}>(history.state?.artifactReturn||{tab:'Home',selected:null});
  const artifactTrigger=useRef<HTMLElement|null>(null);
  const [artifactChatId,setArtifactChatId]=useState<string|null>(appIdFromLocation);
  const [latestChatRun,setLatestChatRun]=useState<string|null>(null);
  const [connectionSetup,setConnectionSetup]=useState<{target:'google'|'mcp';product?:string}|null>(null);
  const shownArtifactRun=useRef<string|null>(null);
  useEffect(()=>{if(data&&artifactChatId&&!data.artifacts?.some(app=>app.id===artifactChatId&&!app.archived))setArtifactChatId(null);},[data,artifactChatId]);
  useEffect(()=>{
    const completed=data?.runs.find(run=>run.id===latestChatRun&&run.state==='succeeded');
    if(!completed?.artifactResult||shownArtifactRun.current===completed.id)return;
    if(completed.background){shownArtifactRun.current=completed.id;return;}
    shownArtifactRun.current=completed.id;
    const result=completed.artifactResult;
    if(result.deleted){if(artifactPanelId===result.id)dismissArtifact();setArtifactChatId(current=>current===result.id?null:current);return;}
    if(result.id!==artifactChatId||!result.changed)openArtifact(result.id,true);
    setArtifactChatId(result.id);
  },[data,latestChatRun,artifactChatId]);
  const [sidebarExpanded,setSidebarExpanded]=useState(false);
  // Ink by night, paper by day. The inline script in index.html applied the first choice before paint.
  const [theme,setTheme]=useState<'light'|'dark'>(()=>document.documentElement.dataset.theme==='light'?'light':'dark');
  useEffect(()=>{document.documentElement.dataset.theme=theme;document.querySelector('meta[name=theme-color]')?.setAttribute('content',theme==='light'?'#f3f0e8':'#141415');},[theme]);
  function toggleTheme(){const next=theme==='light'?'dark':'light';setTheme(next);try{localStorage.setItem('thaddeus-theme',next);}catch{}}
  const [logOpen,setLogOpen]=useState(()=>new URLSearchParams(location.search).get('view')==='upcoming'||(!appIdFromLocation()&&window.innerWidth>1100));
  const [logView,setLogView]=useState<'activity'|'approvals'|'upcoming'|'info'>(()=>new URLSearchParams(location.search).get('view')==='upcoming'?'upcoming':'activity'),[usageExpanded,setUsageExpanded]=useState(true),[logFocusRequest,setLogFocusRequest]=useState(0);
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
  const [draftSession,setDraftSession]=useState<string|null>(null),[draftStorageError,setDraftStorageError]=useState('');
  const restoredSession=useRef<string|null>(null);
  useEffect(()=>{
    if(!session?.id||!data||restoredSession.current===session.id)return;
    restoredSession.current=session.id;
    try{
      const draft=readComposerDraft(session.id);
      if(draft){
        const files=draft.uploadIds.map(id=>data.uploads?.find(file=>file.id===id&&!file.archived)).filter((file):file is UploadFile=>!!file);
        const app=draft.artifactId&&data.artifacts?.some(app=>app.id===draft.artifactId&&!app.archived)?draft.artifactId:null;
        const missing=files.length!==draft.uploadIds.length||!!draft.artifactId&&!app;
        setMessage(draft.message);setAttachments(files);setArtifactChatId(app);setMode(draft.mode==='guidance'?'chat':draft.mode);setScope(draft.scope);setMemoryScope(draft.memoryScope);setHosts(draft.hosts);setPublicSearch(draft.publicSearch);setOpenResults(draft.openResults);setSearchQueries(draft.searchQueries);setChatLimits(draft.chatLimits);setResearchLimits(draft.researchLimits);
        if(draft.message||files.length)setDraftNotice(missing?'Draft restored. Some original files or the selected app are unavailable; review the remaining context before sending.':'Draft restored in this tab.');
      }else setScope(data.pages.filter(page=>page.path.startsWith('notes/')).map(page=>page.path));
    }catch{setDraftStorageError('Draft recovery is unavailable in this browser. Keep a copy of unfinished text before reloading.');}
    setDraftSession(session.id);
  },[session?.id,data]);
  const composerDraft:ComposerDraft={message,uploadIds:attachments.map(file=>file.id),artifactId:artifactChatId,mode,scope,memoryScope,hosts,publicSearch,openResults,searchQueries,chatLimits,researchLimits};
  const serializedDraft=JSON.stringify(composerDraft);
  useEffect(()=>{
    if(!session?.id||draftSession!==session.id)return;
    try{saveComposerDraft(session.id,JSON.parse(serializedDraft));}catch{setDraftStorageError('Draft recovery is unavailable in this browser. Keep a copy of unfinished text before reloading.');}
  },[session?.id,draftSession,serializedDraft]);
  useEffect(()=>{const small=window.matchMedia('(max-width:1100px)');const changed=()=>{if(small.matches)setLogOpen(false);};small.addEventListener('change',changed);return()=>small.removeEventListener('change',changed);},[]);
  async function refresh() { const state=await api<State>('/state'); setData(state); return state; }
  const chatUploads=useFileUploads({disabled:busy||!online,maxFiles:4-attachments.length,onUploaded:file=>setAttachments(list=>[...list,file]),onChanged:refresh});
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
    refresh().catch(e=>setError(e.message));
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
  const ravenState=!online?'disconnected':companionRun?.state||(message.trim()?'listening':'idle');
  const companionStatus=!online?'Disconnected':companionRun?names[companionRun.state]:'At your service.';
  async function loadNote(target:NoteTarget){
    if(noteOperation.current)return;
    noteOperation.current=true;setNoteAction('loading');
    try{
      const [next,history]=target.kind==='new'
        ? [{path:'notes/new-note.md',content:'',version:'absent',updated:''},[]] as [Page,Page[]]
        : await Promise.all([api<Page>('/knowledge?path='+encodeURIComponent(target.path)),api<Page[]>('/revisions?path='+encodeURIComponent(target.path))]);
      setPage(next);setEdit(next.content);setRevisions(history);setNoteNotice('');setPendingNote(null);
      setArtifactView('notes');setTab('Knowledge');setSelected(null);setLogOpen(false);setSidebarExpanded(false);
    }finally{noteOperation.current=false;setNoteAction(null);}
  }
  async function requestNote(target:NoteTarget){
    if(noteOperation.current)return;
    if(noteDirty){setPendingNote(target);return;}
    await loadNote(target);
  }
  async function openPage(path:string){await requestNote({kind:'open',path});}
  async function saveNote(){
    if(!page||!noteDirty||noteOperation.current)return;
    noteOperation.current=true;setNoteAction('saving');setNoteNotice('');
    try{
      const saved=await api<Page>('/knowledge',{path:page.path,content:edit,version:page.version},'PUT');
      setPage(saved);setEdit(saved.content);
      try{setRevisions(await api<Page[]>('/revisions?path='+encodeURIComponent(saved.path)));}
      catch{setNoteNotice('Saved. Revision history could not refresh; reopen this note to try again.');}
    }finally{noteOperation.current=false;setNoteAction(null);}
  }
  async function seed(){await api('/demo/seed',{});await refresh();setScope(['notes/deadlines.md','notes/constraints.md','notes/conflict.md']);setShowScope(true);}
  async function start(){const r=await api<Run>('/runs',{objective:message||'Turn my scattered notes into a useful weekly plan.',readScope:scope,demoFailure:fault,budget:limits});showRun(r.id);setMessage('');setShowScope(false);}
  async function sendMessage(override?:string){
    if(chatUploads.busy)return;
    const sentMessage=override??message;
    const sentAttachments=attachments.map(file=>file.id);
    const payload=mode==='research'?{content:sentMessage,mode,readScope:scope,memories:memoryScope,budget:researchLimits,web:hosts.trim()||publicSearch?{hosts:hosts.split(',').map(host=>host.trim().toLowerCase()).filter(Boolean),maxFetches:4,...(publicSearch?{search:{provider:'brave',credentialId:data?.search?.credentialId,maxQueries:searchQueries,openResults}}:{})}:null}:{content:sentMessage,uploadIds:attachments.map(f=>f.id),budget:chatLimits,artifactId:artifactChatId,localDate:localDay()};
    const created=await api<Run>('/chat',payload);setMessage(current=>current===sentMessage?'':current);setAttachments(current=>current.filter(file=>!sentAttachments.includes(file.id)));setDraftNotice('');setFocusId(undefined);
    if(mode==='research'){showRun(created.id);}else{setLatestChatRun(created.id);if(created.connectionSetup)setConnectionSetup({target:created.connectionSetup,product:created.connectionSetupProduct});}
  }
  async function retryReply(run:Run){
    const operationId=retryKeys.current[run.id]??=crypto.randomUUID().replaceAll('-','');
    const created=await api<Run>('/chat/'+run.id+'/retry',{operationId});
    delete retryKeys.current[run.id];setLatestChatRun(created.id);setFocusId(undefined);
  }
  function restoreChatDraft(draft:{content:string;run?:Run}){
    const files=(draft.run?.uploadIds||[]).map(id=>data?.uploads?.find(file=>file.id===id&&!file.archived));
    const appId=draft.run?.artifactContext?.selected?.id;
    if(files.some(file=>!file)){setError('An original attachment is no longer available. Copy the message and attach the files you want to use.');setPendingChatEdit(null);return;}
    if(appId&&!data?.artifacts?.some(app=>app.id===appId&&!app.archived)){setError('The original app is in Trash or no longer available. Restore it before editing this message.');setPendingChatEdit(null);return;}
    setMessage(draft.content);setAttachments(files as UploadFile[]);setArtifactChatId(appId||null);setMode('chat');setPendingChatEdit(null);setDraftNotice('Editing a copy. Sending keeps the earlier messages.');nav('Home');
    requestAnimationFrame(()=>document.querySelector<HTMLTextAreaElement>('[aria-label="Message or goal"]')?.focus());
  }
  function editChatMessage(original:{content:string},run?:Run){
    const draft={content:original.content,run};
    if(message.trim()||attachments.length){setPendingChatEdit(draft);return;}
    restoreChatDraft(draft);
  }
  async function decision(allow:boolean){if(!run?.approval)return;await api('/runs/'+run.id+'/approve',{approvalId:run.approval.id,digest:run.approval.digest,allow});}
  function nav(name:string){dismissArtifact();setFocusId(undefined);setTab(name);setSelected(null);if(window.innerWidth<=1100)setLogOpen(false);if(window.innerWidth<=700)setSidebarExpanded(false);}
  function showRun(id:string){setSelected(id);}
  function openArtifact(id:string,withChat=tab==='Home'){
    if(!artifactPanelId){artifactReturn.current={tab,selected};artifactTrigger.current=document.activeElement as HTMLElement;}
    if(withChat)artifactReturn.current={tab:'Home',selected:null};
    if(appIdFromLocation()!==id)history[artifactPanelId?'replaceState':'pushState']({artifactPage:true,artifactReturn:artifactReturn.current},'', '/apps/'+id);
    else history.replaceState({...history.state,artifactReturn:artifactReturn.current},'');
    setArtifactPanelId(id);setArtifactChatId(id);setArtifactChatVisible(withChat&&window.innerWidth>1000);
    setSidebarExpanded(false);setLogOpen(false);setSelected(null);if(withChat)setTab('Home');
    requestAnimationFrame(()=>document.querySelector<HTMLButtonElement>('.artifact-page-header button')?.focus({preventScroll:true}));
  }
  function dismissArtifact(){
    if(!artifactPanelId)return;
    history.replaceState(null,'','/');setArtifactPanelId(null);setArtifactChatVisible(false);
  }
  function closeArtifact(){
    dismissArtifact();setTab(artifactReturn.current.tab);setSelected(artifactReturn.current.selected);
    requestAnimationFrame(()=>{(artifactReturn.current.tab==='Home'?document.querySelector<HTMLElement>('[aria-label="Message or goal"]'):artifactTrigger.current?.isConnected?artifactTrigger.current:document.querySelector<HTMLElement>('.artifact-heading button, .rail-toggle'))?.focus({preventScroll:true});});
  }
  function toggleArtifactChat(){
    setArtifactChatVisible(visible=>!visible);setSidebarExpanded(false);setLogOpen(false);
    if(!artifactChatVisible){
      // Once chat is underneath, closing the app should not take a detour to its shelf.
      artifactReturn.current={tab:'Home',selected:null};history.replaceState({...history.state,artifactReturn:artifactReturn.current},'');
      setArtifactChatId(artifactPanelId);setTab('Home');setSelected(null);setMode('chat');
    }
    requestAnimationFrame(()=>document.querySelector<HTMLElement>(!artifactChatVisible?'[aria-label="Message or goal"]':'.artifact-page-header button')?.focus({preventScroll:true}));
  }
  useEffect(()=>{
    const navigate=()=>{const id=appIdFromLocation();if(id&&history.state?.artifactReturn)artifactReturn.current=history.state.artifactReturn;setArtifactPanelId(id);if(id)setArtifactChatId(id);setArtifactChatVisible(false);setLogOpen(false);if(!id){setTab(artifactReturn.current.tab);setSelected(artifactReturn.current.selected);}};
    window.addEventListener('popstate',navigate);return()=>window.removeEventListener('popstate',navigate);
  },[]);
  const artifactTitle=data?.artifacts?.find(app=>app.id===artifactPanelId)?.title;
  useEffect(()=>{document.title=artifactPanelId?(artifactTitle||'App')+' · Thaddeus':'Thaddeus · Your private study';},[artifactPanelId,artifactTitle]);
  function buildApp(prompt?:string){setArtifactChatId(null);nav('Home');setMode('chat');if(prompt)setMessage(prompt);else if(!message.trim())setMessage('Build me an app for ');requestAnimationFrame(()=>document.querySelector<HTMLTextAreaElement>('[aria-label="Message or goal"]')?.focus());}
  function openTokenInfo(trigger:HTMLButtonElement){if(artifactPanelId){dismissArtifact();setTab('Home');}logTriggerRef.current=trigger;setLogView('info');setUsageExpanded(true);setLogOpen(true);setLogFocusRequest(value=>value+1);}
  function openActivityLog(trigger:HTMLButtonElement){if(artifactPanelId){dismissArtifact();setTab('Home');}logTriggerRef.current=trigger;setLogView('activity');setLogOpen(true);setLogFocusRequest(value=>value+1);}
  function closeSidebar(){setSidebarExpanded(false);document.querySelector<HTMLButtonElement>('.rail-toggle')?.focus();}
  function closeLog(){
    setLogOpen(false);
    // Let the raven return to his perch before handing keyboard focus back.
    requestAnimationFrame(()=>(logTriggerRef.current?.isConnected?logTriggerRef.current:document.querySelector<HTMLButtonElement>('.model-usage'))?.focus({preventScroll:true}));
  }
  function chooseMessageMode(next:string){setMode(next);requestAnimationFrame(()=>document.querySelector<HTMLTextAreaElement>('[aria-label="Message or goal"]')?.focus());}
  function openDiscussion(text:string,append=false){
    nav('Home');setMessage(current=>append?[current,text].filter(Boolean).join('\n\n'):text);setMode('chat');
    if(!append)setArtifactChatId(null);
    setPendingDiscussion(null);
    requestAnimationFrame(()=>document.querySelector<HTMLTextAreaElement>('[aria-label="Message or goal"]')?.focus());
  }
  function discuss(text:string){
    // A new clipping may join an unfinished letter; the butler never throws the letter away.
    if(message.trim()||attachments.length){setPendingDiscussion(text);return;}
    openDiscussion(text);
  }
  function openConnectionSetup(target:'google'|'mcp',product?:string){
    nav('Home');setMode('chat');setConnectionSetup({target,product});
    requestAnimationFrame(()=>document.querySelector<HTMLElement>('[aria-label="Secure connection setup"]')?.focus());
  }
  useEffect(()=>{const escape=(event:KeyboardEvent)=>{if(event.key==='Escape'){if(selected)return;else if(sidebarExpanded)closeSidebar();else if(logOpen)closeLog();else if(artifactPanelId&&!(event.target instanceof Element&&event.target.closest('input,textarea,select')))closeArtifact();}};window.addEventListener('keydown',escape);return()=>window.removeEventListener('keydown',escape);},[logOpen,sidebarExpanded,artifactPanelId,selected]);
  function ledger(items:Run[]){let previous='';return items.map(r=>{const date=new Date(r.created);const today=new Date();const yesterday=new Date();yesterday.setDate(today.getDate()-1);const group=date.toDateString()===today.toDateString()?'Today':date.toDateString()===yesterday.toDateString()?'Yesterday':date.toLocaleDateString(undefined,{month:'long',day:'numeric'});const heading=group!==previous;previous=group;return <React.Fragment key={r.id}>{heading&&<h3 className="time-group">{group}</h3>}<button data-run-id={r.id} aria-current={r.id===selected?'true':undefined} className="ledger-row" onClick={()=>{showRun(r.id);}}><span className={'state-icon '+r.state}><StateIcon state={r.state}/></span><span className="ledger-copy"><strong>{r.goal.objective.length>80?r.goal.objective.slice(0,77)+'…':r.goal.objective}</strong><small>{r.summary}</small></span><span className="ledger-time"><span className={'badge '+r.state}>{names[r.state]}</span><time>{date.toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'})}</time></span><ChevronRight size={16}/></button></React.Fragment>;});}
  const taskActivity=<TaskActivity runs={data?.runs||[]} online={online} onCancel={id=>void act(()=>api('/runs/'+id+'/cancel',{}))} onDetails={showRun} onArtifact={id=>openArtifact(id,false)}/>;
  if(!loaded)return <main className="unlock"><Raven/><h1>Opening the study…</h1></main>;
  if(session&&draftSession!==session.id)return <main className="unlock"><Raven/><h1>Opening the study…</h1>{error&&<><p role="alert">{error}</p><button onClick={()=>void act(refresh)}>Try reconnecting</button></>}</main>;
  if(!session)return <main className="unlock"><div className="wordmark"><span className="mark">T</span>THADDEUS</div><Raven state="listening"/><p className="eyebrow">YOUR PRIVATE STUDY</p><h1>A little order.<br/><em>Entirely yours.</em></h1><p>Unlock this browser with the host access key.<br/>Your notes remain on the computer running Thaddeus.</p><form onSubmit={e=>{e.preventDefault();setError('');(pair?api('/pair/claim',{code:key,name:'Phone browser'}):api('/auth/login',{key})).then(s=>{if(pair){setError('Waiting for confirmation on the host. Then select Finish pairing.');}else{setCsrf(s.csrf);setSession(s);setKey('');}}).catch(e=>setError(e.message));}}><label>{pair?'One-time pairing code':'Host access key'}<input type="password" autoComplete="off" value={key} onChange={e=>setKey(e.target.value)} required/></label><button className="primary">{pair?'Request pairing':'Unlock study'} <ArrowUpRight size={17}/></button></form><button className="text-button" onClick={()=>setPair(!pair)}>{pair?'Use host access key':'Connect a phone instead'}</button>{pair&&<button onClick={()=>api('/pair/exchange',{}).then(s=>{if(s){setCsrf(s.csrf);setSession(s);}else setError('Host confirmation is still pending.');}).catch(e=>setError(e.message))}>Finish pairing</button>}<small>Host key: <code>.data/host-key.txt</code><br/>Phone access requires your host’s trusted HTTPS address.</small>{error&&<p role="alert" className="error">{error}</p>}</main>;
  return <div className={'app study-shell '+(logOpen?'log-open ':'')+(sidebarExpanded?'sidebar-expanded ':'')+(artifactPanelId?'artifact-open ':'')+(artifactPanelId&&artifactChatVisible?'artifact-with-chat':'')}>
  <StudyNavigation current={selected?null:tab} openTodos={data?.library?.filter(item=>item.kind==='todo'&&item.status==='open').length||0} open={sidebarExpanded} onNavigate={nav} theme={theme} onToggleTheme={toggleTheme}/>
  {sidebarExpanded&&<button type="button" className="rail-scrim" aria-label="Close sidebar" onClick={closeSidebar}/>}
  <div className="workspace" hidden={!!artifactPanelId&&!artifactChatVisible}><header>
    <div className="header-location">
      <button type="button" className="rail-toggle" aria-label={sidebarExpanded?'Collapse sidebar':'Expand sidebar'} title={sidebarExpanded?'Hide sidebar':'Show sidebar'} aria-expanded={sidebarExpanded} aria-controls="study-sidebar" onClick={()=>setSidebarExpanded(value=>!value)}><PanelLeft size={19} strokeWidth={1.6} aria-hidden="true"/></button>
      <div className="breadcrumb"><span>Thaddeus</span><ChevronRight size={13}/><strong>{selected?'Run details':({Home:'Chat',Knowledge:'Artifacts',Todo:'To-do'} as Record<string,string>)[tab]||tab}</strong></div>
    </div>
    <div className="header-companion"><button type="button" className="raven-log-toggle" aria-label="Thaddeus: open activity log" title="Open activity log" aria-expanded={logOpen} aria-controls="activity-log" onClick={event=>openActivityLog(event.currentTarget)}><Raven state={ravenState}/></button></div>
    <div className="header-actions">
      {taskActivity}
      <ModelUsageButton model={data?.provider.kind==='scripted'?'SCRIPTED DEMO':data?.provider.model||'Model'} runs={data?.runs||[]} online={online} expanded={logOpen&&logView==='info'} onOpen={openTokenInfo}/>
      {pending.length>0&&<button className="approval-pill" onClick={()=>showRun(pending[0].id)}><ShieldCheck size={15}/>{pending.length} approval</button>}
    </div>
  </header>
  {!online&&<div role="status" className="disconnect"><WifiOff size={17}/> Connection lost. Writes and approvals are disabled until the host reconnects.</div>}
  {error&&<div className="error alert" role="alert">{error}<button aria-label="Dismiss error" onClick={()=>setError('')}><X size={16}/></button></div>}
  <main key={tab} className="main" aria-label="Workspace">
{tab==='Home'?<section className="home conversation-workspace"><div className="conversation-title"><p className="eyebrow">A LITTLE ORDER. ROOM FOR WONDER.</p><h1>Conversation</h1></div>
  {!data?.chats.length&&<div className="conversation-empty"><Feather size={26}/><h2>What shall we make of today?</h2><p>Bring a question, an idea, or a little unfinished business.</p></div>}
  <Conversation uploads={data?.uploads||[]} onArtifact={openArtifact} apps={data?.artifacts||[]} focusId={focusId} messages={data?.chats||[]} runs={data?.runs||[]} online={online} busy={busy||chatUploads.busy} onCancel={id=>act(()=>api('/runs/'+id+'/cancel',{}))} onRetry={run=>act(()=>retryReply(run))} onConnectionSetup={openConnectionSetup} onEdit={editChatMessage} onDetails={showRun}/>
  {active&&active.goal.kind!=='conversation'&&<button className="active-work" onClick={()=>showRun(active.id)}><StateIcon state={active.state}/><span><strong>{names[active.state]}</strong><small>{active.goal.objective}</small></span><ArrowUpRight size={16}/></button>}
  <div className="conversation-compose">
  {connectionSetup&&<ConnectionSetupCard target={connectionSetup.target} initialProduct={connectionSetup.product} online={online} onClose={()=>setConnectionSetup(null)} onConnected={async()=>{await refresh();setConnectionSetup(null);setDraftNotice('Connection saved. Thaddeus can now propose its available tools in chat.');}}/>}
  {data?.provider.kind==='scripted'&&<div className="collection-notice" role="region" aria-label="Demo model setup"><span>Demo mode uses scripted replies. {session.owner?'Connect a model for real conversations.':'Ask the host owner to connect a model for real conversations.'}</span>{session.owner&&<button type="button" onClick={()=>{nav('Settings');requestAnimationFrame(()=>document.getElementById('model-connection-heading')?.focus());}}>Connect a model</button>}</div>}
  {draftStorageError&&<p className="draft-restored" role="status">{draftStorageError}</p>}
  {mode==='research'&&<ResearchScope availability={data?.research} pages={data?.pages||[]} scope={scope} onScope={setScope} memories={data?.memories||[]} memoryScope={memoryScope} onMemories={setMemoryScope} hosts={hosts} onHosts={setHosts} searchConnection={data?.search} search={publicSearch} onSearch={setPublicSearch} openResults={openResults} onOpenResults={setOpenResults} searchQueries={searchQueries} onSearchQueries={setSearchQueries} limits={researchLimits} onLimits={setResearchLimits}/>}
  {mode==='guidance'&&<div className="composer-guidance"><button className="text-button" onClick={()=>chooseMessageMode('chat')}>Back to message</button>{guidanceRun?<TaskGuidance key={guidanceRun.id} run={guidanceRun} online={online} onChanged={refresh}/>:<p role="status">There is no active research task to guide.</p>}</div>}
  {mode!=='guidance'&&<>
  {draftNotice&&<p className="draft-restored" role="status">{draftNotice}<button aria-label="Dismiss draft notice" onClick={()=>setDraftNotice('')}><X size={13}/></button></p>}
  {mode==='chat'&&artifactChatId&&<div className="artifact-chat-scope"><button type="button" onClick={()=>openArtifact(artifactChatId)} title="Open selected app">{data?.artifacts?.find(app=>app.id===artifactChatId)?.title||'Selected app'}</button><span>can be updated in chat</span><button type="button" aria-label="Stop using this app in chat" onClick={()=>setArtifactChatId(null)}><X size={13}/></button></div>}
  <UploadFeedback state={chatUploads.state} onDismiss={chatUploads.clear}/>
  <MessageComposer attachments={attachments} onRemoveAttachment={id=>setAttachments(list=>list.filter(f=>f.id!==id))} uploadDisabled={busy||!online||chatUploads.busy} onUpload={chatUploads.upload} onBuild={()=>buildApp()} value={message} onChange={setMessage} mode={mode} onMode={chooseMessageMode} canGuide={!!guidanceRun} canSend={!(!message.trim()||busy||chatUploads.busy||!online||(mode==='research'?(!data?.research?.enabled||data?.provider.kind!=='compatible'||(!scope.length&&!hosts.trim()&&!memoryScope.length&&!publicSearch)||(publicSearch&&!data?.search?.configured)||memoryScope.some(selection=>!data?.memories?.some(({entry,sourceStatus})=>entry.id===selection.id&&entry.version===selection.version&&sourceStatus==='current'))||data?.runs.some(r=>r.research&&r.research.phase!=='finished')):data?.runs.some(r=>r.goal.kind==='conversation'&&!r.background&&['queued','running'].includes(r.state))))} onSend={()=>act(sendMessage)}/>
  {mode==='research'&&data?.provider.kind!=='compatible'&&<p role="status">Configure a compatible model in Settings before starting research.</p>}
  </>}
  {showScope&&<section className="scope-card" aria-label="Plan scope"><h2>A small, explicit workspace</h2><p>Allow reading only these notes. The next write will need a separate approval.</p>{data?.pages.filter(p=>p.path.startsWith('notes/')).map(p=><label className="checkbox" key={p.path}><input type="checkbox" checked={scope.includes(p.path)} onChange={e=>setScope(e.target.checked?[...scope,p.path]:scope.filter(s=>s!==p.path))}/>{p.path}</label>)}{!data?.pages.length&&<button onClick={()=>act(seed)}>Load fictional notes</button>}<label className="checkbox"><input type="checkbox" checked={fault} onChange={e=>setFault(e.target.checked)}/> Demo only: exercise one bounded draft repair</label><BudgetFields value={limits} onChange={setLimits}/><button className="primary" disabled={!scope.length||busy||!online} onClick={()=>act(start)}>Read selected notes & create a plan <ArrowUpRight size={16}/></button></section>}
  </div></section>:
  tab==='Feed'?<Feed search={data?.search} key={focusId||'feed'} focusId={focusId} feeds={data?.feeds} items={data?.library||[]} online={online} onChanged={refresh} onDiscuss={discuss}/>:
  tab==='Todo'?<TodoBoard key={focusId||'todo'} focusId={focusId} items={data?.library||[]} online={online} onChanged={refresh} onDiscuss={discuss}/>:tab==='Ideas'&&data?<Ideas data={data} focusId={focusId} online={online} onChanged={refresh} onDiscuss={discuss} onDetails={showRun} onStart={async prompt=>{if(message.trim()||attachments.length){discuss(prompt);return;}const created=await api<Run>('/chat',{content:prompt,budget:chatLimits,localDate:localDay()});nav('Home');setMode('chat');setArtifactChatId(null);setLatestChatRun(created.id);}}/>:
  tab==='Search'&&data?<StudySearch data={data} onArtifact={id=>openArtifact(id,false)} onFile={id=>{nav('Knowledge');setArtifactView(data.uploads?.find(file=>file.id===id)?.mediaType.startsWith('image/')?'images':'files');setFocusId(id);}} onPage={path=>act(()=>openPage(path))} onRun={showRun} onCollection={(kind,id)=>{nav(kind==='todo'?'Todo':kind==='idea'?'Ideas':'Feed');setFocusId(id);}} onChat={id=>{nav('Home');setFocusId(id);}}/>:
  tab==='Knowledge'?<ArtifactApps key={focusId||'artifacts'} focusId={focusId} files={data?.uploads||[]} pages={data?.pages||[]} onPage={path=>act(()=>openPage(path))} onAttach={file=>{if(chatUploads.busy){setError("Wait for the current uploads to finish before attaching another file.");return;}if(attachments.length>=4){setError("Attach up to four files.");return;}setAttachments(list=>list.some(f=>f.id===file.id)?list:[...list,file]);nav("Home");setMode("chat");}} view={artifactView} onView={setArtifactView} apps={data?.artifacts||[]} onSelect={id=>openArtifact(id,false)} onBuild={buildApp} onChanged={refresh} online={online}><section className="knowledge"><h2>{page?(page.path.split('/').pop()||'Untitled note').replace('.md','').replaceAll('-',' '):'Notes & memory'}</h2><MemoryNotebook memories={data?.memories||[]} pages={data?.pages||[]} online={online} onChanged={refresh} onOpen={path=>act(()=>openPage(path))}/><div className="knowledge-grid"><div className="page-list"><button disabled={busy||!!noteAction} onClick={()=>act(()=>requestNote({kind:'new'}))}><Plus size={16}/> New note</button>{data?.pages.map(p=><button disabled={busy||!!noteAction} className={page?.path===p.path?'active':''} key={p.path} onClick={()=>act(()=>openPage(p.path))}><FileText size={16}/>{p.path}</button>)}</div>{page?<div className="editor"><label>Page path<input disabled={page.version!=='absent'||!!noteAction} value={page.path} onChange={e=>setPage({...page,path:e.target.value})}/></label><label>Markdown<textarea aria-label="Markdown editor" disabled={!!noteAction} value={edit} onChange={e=>{setEdit(e.target.value);setNoteNotice('');}}/></label><div className="note-save-actions"><button className="primary" disabled={busy||!online||!!noteAction||!noteDirty} onClick={()=>act(saveNote)}>Save my edits <Check size={16}/></button><span className="note-save-status" role="status">{noteAction==='saving'?'Saving\u2026':noteAction==='loading'?'Opening\u2026':noteDirty?'Unsaved changes':noteNotice||(page.version==='absent'?'Not saved yet':'Saved')}</span></div><details><summary>Reading view</summary><div className="draft"><Markdown components={{a:({href,children})=>href && /^(notes|plans)\/[a-z0-9-]+\.md$/.test(href)?<button className="text-button" onClick={()=>act(()=>openPage(href))}>{children}</button>:<a href={href}>{children}</a>}}>{edit}</Markdown></div></details><details><summary>Revision history · {revisions.length}</summary>{revisions.map((p,i)=><details key={i}><summary>{new Date(p.updated).toLocaleString()} · {p.version.slice(0,12)}</summary><pre>{p.content}</pre></details>)}</details></div>:<div className="empty"><BookOpen/><p>Choose a page or start a note.<br/>Your words are stored as ordinary Markdown.</p></div>}</div></section></ArtifactApps>:
  <StudySettings data={data} owner={session.owner} online={online} onChanged={refresh} onConnectionSetup={openConnectionSetup} onMaintenance={onMaintenance} onDataDeleted={()=>setPage(null)} unsavedNote={noteDirty?(page?.path||'Untitled note'):null} onReturnToNote={()=>{setArtifactView('notes');nav('Knowledge');requestAnimationFrame(()=>document.querySelector<HTMLTextAreaElement>('[aria-label="Markdown editor"]')?.focus());}}/>}

  </main></div>
  {pendingNote&&<Modal title="Discard unsaved note changes?" className="note-discard-dialog" onClose={()=>{if(!noteAction)setPendingNote(null);}}><p>Your changes to <strong>{page?.path}</strong> have not been saved. Keep editing to save them, or discard them and {pendingNote.kind==='new'?'start a new note.':'open '+pendingNote.path+'.'}</p><footer><button disabled={!!noteAction} onClick={()=>setPendingNote(null)}>Keep editing</button><button disabled={!!noteAction||!online} onClick={()=>act(()=>loadNote(pendingNote))}>{noteAction?'Opening\u2026':'Discard changes'}</button></footer></Modal>}
  {pendingChatEdit&&<Modal title="Replace your draft?" onClose={()=>setPendingChatEdit(null)}><p>Your current draft{attachments.length?' and attachments':''} will be replaced with a copy of the earlier message. Nothing is sent until you send it.</p><footer><button onClick={()=>setPendingChatEdit(null)}>Keep current draft</button><button className="primary" onClick={()=>restoreChatDraft(pendingChatEdit)}>Replace draft</button></footer></Modal>}
  {pendingDiscussion&&<Modal title="Add this to your draft?" className="discussion-dialog" onClose={()=>setPendingDiscussion(null)}><div className="discussion-preview"><p>Your unfinished message{attachments.length?' and attached files':''} will stay. Add this item below it, then send when you are ready.</p><blockquote>{pendingDiscussion}</blockquote></div><footer><button onClick={()=>setPendingDiscussion(null)}>Keep current draft</button><button className="primary" onClick={()=>openDiscussion(pendingDiscussion,true)}>Add to draft</button></footer></Modal>}
  {run&&<ActivityDialog key={run.id} run={run} trace={trace} onClose={()=>setSelected(null)}><TaskDetail run={run} jobs={data?.delegations||[]} owner={session.owner} trace={trace} online={online} busy={busy} onBack={()=>setSelected(null)} onPage={path=>act(()=>openPage(path))} onDecision={allow=>act(()=>decision(allow))} onCancel={()=>act(()=>api('/runs/'+run.id+'/cancel',{}))} onResume={()=>act(()=>api('/runs/'+run.id+'/resume',{}))} onReconcile={refresh}/></ActivityDialog>}
  {artifactPanelId&&<ArtifactPage key={artifactPanelId} id={artifactPanelId} summary={data?.artifacts?.find(app=>app.id===artifactPanelId)} online={online} chatVisible={artifactChatVisible} onToggleChat={toggleArtifactChat} activity={taskActivity} onClose={closeArtifact} onChanged={refresh}/>}
  {logOpen&&!artifactPanelId&&<aside className="activity-log" id="activity-log" aria-label="Activity log"><div className="log-heading"><h2>Activity log</h2><button aria-label="Close activity log" onClick={closeLog}><X size={17}/></button></div><div className="companion"><Raven state={ravenState} onClick={companionRun?()=>showRun(companionRun.id):undefined}/><h2>Thaddeus</h2><p>{companionStatus}</p></div>
    <nav className="log-views" aria-label="Log views"><button type="button" aria-pressed={logView==='activity'} onClick={()=>setLogView('activity')}>Activity</button><button type="button" aria-pressed={logView==='approvals'} onClick={()=>setLogView('approvals')}>Approvals</button><button type="button" aria-pressed={logView==='upcoming'} onClick={()=>setLogView('upcoming')}>Upcoming</button><button type="button" aria-pressed={logView==='info'} onClick={()=>setLogView('info')}>Info</button></nav>
    {logView==='info'?<div className="log-info" ref={logInfoRef} role="region" aria-label="Model and token information"><p className="log-model"><small>SELECTED MODEL</small><strong>{data?.provider.kind==='scripted'?'Scripted demo':data?.provider.model}</strong></p><TokenUsage runs={data?.runs||[]} onRun={showRun} expanded={usageExpanded} onExpandedChange={setUsageExpanded}/><section className="reply-limits" aria-label="Reply limits"><h3>Website reading</h3><p>Paste a public HTTPS link in Chat to read and discuss it. Page text only; no search credits used. Signed-in pages and interactive websites need a browser connection.</p><h3>Reply limits</h3><p>{chatLimits.maxTotalTokens.toLocaleString()} token allowance · up to {chatLimits.maxOutputTokens.toLocaleString()} output tokens per call · {chatLimits.modelCalls} model call{chatLimits.modelCalls===1?'':'s'}.</p><BudgetFields value={chatLimits} onChange={setChatLimits}/></section></div>:logView==='upcoming'?<div className="log-upcoming"><DelegationsPanel jobs={data?.delegations||[]} occurrences={data?.delegationOccurrences||[]} online={online} busy={busy} onCancel={job=>void act(()=>api('/delegations/'+job.id+'/cancel',{version:job.version}))} onPause={job=>void act(()=>api('/delegations/'+job.id+'/pause',{version:job.version}))} onResume={job=>void act(()=>api('/delegations/'+job.id+'/resume',{version:job.version}))} onEditInstruction={(job,instruction)=>void act(()=>api('/delegations/'+job.id+'/inbox-instruction',{version:job.version,instruction}))} onRead={occurrence=>void act(()=>api('/delegation-occurrences/'+occurrence.id+'/read',{version:occurrence.version}))} onReview={(job,occurrence)=>discuss(occurrence?.notificationError?`Review “${job.title}”. The reminder was saved in Thaddeus, but its Windows notification failed: ${occurrence.notificationError} Explain the safe notification-setting checks and do not schedule another reminder unless I ask.`:job.state==='unknown'?`Review “${job.title}”. Its provider outcome is unknown. Explain the retained evidence and my safe next options without retrying it automatically.`:`Review “${job.title}”, which is ${job.state}. Explain what happened and help me create a newly reviewed schedule only if I ask to proceed.`)}/><h3>Planning check-ins</h3>{data?.library.filter(i=>i.kind==='todo'&&i.status==='open'&&(i.tracking?.nextCheckIn||i.due)).sort((a,b)=>(a.tracking?.nextCheckIn||a.due||'').localeCompare(b.tracking?.nextCheckIn||b.due||'')).map(i=><button key={i.id} onClick={()=>{nav('Todo');setFocusId(i.id);}}><strong>{i.title}</strong><small>{i.tracking?.nextCheckIn||i.due}</small><span>{i.tracking?.nextStep}</span></button>)}<p className="muted">Planning check-ins are dates on your list. Only items under Delegated work schedule background delivery.</p></div>:<><div className="log-caption"><span>{logView==='approvals'?'PERMISSIONS':'RECORDED WORK'}</span><small>{data?.runs.length||0} runs</small></div><div className="log-entries">{(logView==='approvals'?data?.runs.some(r=>r.approval):data?.runs.length)?ledger(logView==='approvals'?data!.runs.filter(r=>r.approval):data!.runs):<p className="log-empty">Nothing in the ledger yet. I shall resist inventing an achievement.</p>}</div></>}
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
