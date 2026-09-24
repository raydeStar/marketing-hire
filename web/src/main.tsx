import {TaskActivity} from './components/TaskActivity';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import Markdown from 'react-markdown';
import { Home, ListTodo, Clock3, BookOpen, Settings, ArrowUpRight, ArrowUp, Plus, Check, ShieldCheck, ChevronRight, X, Feather, CircleAlert, WifiOff, FileText, Ban, LoaderCircle, PanelLeft, List, Fingerprint, Pin } from 'lucide-react';
import './style.css';
import './workspace.css';
import './raven.css';
import {Collections} from './components/Collections';
import {Feed} from './components/Feed';
import {StudySettings} from './components/StudySettings';
import {StudySearch} from './components/StudySearch';
import {StudyNavigation} from './components/StudyNavigation';
import {api,setCsrf,readReplay,restoreSession} from './api';
import type {Page,Run,State,MyPageSetting} from './types';
import {names,StateIcon,Raven} from './components/Raven';
import {Conversation} from './components/Conversation';
import {ArtifactApps,ArtifactPage,localDay} from './components/ArtifactApps';
import {MessageComposer} from './components/MessageComposer';
import {ActivityDialog} from './components/ActivityDialog';
import {Modal} from './components/Modal';
import {MyPage} from './components/MyPage';
import {TodoBoard} from './components/TodoBoard';
import {Ideas} from './components/Ideas';
import {useFileUploads} from './use-file-uploads';
import {UploadFeedback} from './components/UploadFeedback';
import type {UploadFile} from './types';
import './experience.css';
import {TaskDetail} from './components/TaskDetail';
import {DelegationsPanel} from './components/DelegationsPanel';
import {BudgetFields,defaultLimits} from './components/BudgetFields';
import {BrowserAllowance,defaultBrowserLimits} from './components/BrowserTaskCard';
import {readComposerDraft,saveComposerDraft,type ComposerDraft} from './composer-draft';


import {ResearchScope} from './components/ResearchScope';
import {ModelUsageButton,TokenUsage} from './components/TokenUsage';
import {TaskGuidance} from './components/TaskGuidance';
import {ConnectionSetupCard} from './components/ConnectionSetupCard';
import {MarketingWorkspace} from './components/MarketingWorkspace';
import {ProfilePanel} from './components/ProfilePanel';

import {MemoryNotebook} from './components/MemoryNotebook';


import {MaintenancePage,type MaintenanceView} from './components/Maintenance';
import type {MemorySelection} from './types';
import './business-theme.css';

const appIdFromLocation=()=>/^\/apps\/([a-f0-9]{32})\/?$/.exec(location.pathname)?.[1]||null;
type NoteTarget={kind:'new'}|{kind:'open';path:string};
type SideView='activity'|'approvals'|'upcoming'|'info'|'profile'|'my-page';

function App({onMaintenance}:{onMaintenance:(view:MaintenanceView)=>void}) {
  const [session,setSession]=useState<{id:string;owner:boolean;name?:string}|null>(null),[loaded,setLoaded]=useState(false),[key,setKey]=useState(''),[pair,setPair]=useState(false);
  const [data,setData]=useState<State|null>(null),[tab,setTab]=useState(()=>{try{return localStorage.getItem('company-workspace-active')==='yes'?'Marketing':'Home';}catch{return 'Home';}}),[selected,setSelected]=useState<string|null>(null),[online,setOnline]=useState(false),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  const [approvalSettingsRequest,setApprovalSettingsRequest]=useState(0);
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
  const [browserLimits,setBrowserLimits]=useState(defaultBrowserLimits);
  const [chatLimits,setChatLimits]=useState({...defaultLimits,modelCalls:2,toolCalls:2,repairs:0,seconds:600});
  const [mode,setMode]=useState('chat'),[hosts,setHosts]=useState('');
  const [publicSearch,setPublicSearch]=useState(false),[openResults,setOpenResults]=useState(true),[searchQueries,setSearchQueries]=useState(3);
  const [memoryScope,setMemoryScope]=useState<MemorySelection[]>([]);
  const [focusId,setFocusId]=useState<string|undefined>();
  const [artifactView,setArtifactView]=useState<'all'|'apps'|'notes'|'images'|'files'>('all');
  const [artifactPanelId,setArtifactPanelId]=useState<string|null>(appIdFromLocation);
  const [retainedArtifactId,setRetainedArtifactId]=useState<string|null>(appIdFromLocation);
  const [artifactDirty,setArtifactDirty]=useState(false);
  const [pendingArtifact,setPendingArtifact]=useState<{id:string;withChat:boolean}|null>(null);
  useEffect(()=>{
    if(!artifactDirty)return;
    const warn=(event:BeforeUnloadEvent)=>{event.preventDefault();event.returnValue='';};
    window.addEventListener('beforeunload',warn);return()=>window.removeEventListener('beforeunload',warn);
  },[artifactDirty]);
  const [artifactChatVisible,setArtifactChatVisible]=useState(false);
  const [artifactIsMyPage,setArtifactIsMyPage]=useState(false);
  const artifactReturn=useRef<{tab:string;selected:string|null}>(history.state?.artifactReturn||{tab:'Home',selected:null});
  const artifactTrigger=useRef<HTMLElement|null>(null);
  const [artifactChatId,setArtifactChatId]=useState<string|null>(appIdFromLocation);
  const [latestChatRun,setLatestChatRun]=useState<string|null>(null);
  const [connectionSetup,setConnectionSetup]=useState<{target:'google'|'mcp';product?:string;runId?:string}|null>(null);
  const shownArtifactRun=useRef<string|null>(null);
  useEffect(()=>{if(data&&artifactChatId&&!data.artifacts?.some(app=>app.id===artifactChatId&&!app.archived))setArtifactChatId(null);},[data,artifactChatId]);
  useEffect(()=>{
    const completed=data?.runs.find(run=>run.id===latestChatRun&&run.state==='succeeded');
    if(!completed?.artifactResult||shownArtifactRun.current===completed.id)return;
    if(completed.background){shownArtifactRun.current=completed.id;return;}
    shownArtifactRun.current=completed.id;
    const result=completed.artifactResult;
    if(result.deleted){if(artifactPanelId===result.id)dismissArtifact();setArtifactChatId(current=>current===result.id?null:current);return;}
    setArtifactChatId(result.id);
  },[data,latestChatRun,artifactChatId]);
  const [sidebarExpanded,setSidebarExpanded]=useState(false);
  // Read the preference in the module; the host's CSP keeps inline scripts off the guest list.
  const [theme,setTheme]=useState<'light'|'dark'>(()=>{
    try{const saved=localStorage.getItem('thaddeus-theme');if(saved==='light'||saved==='dark')return saved;}catch{}
    return window.matchMedia('(prefers-color-scheme: light)').matches?'light':'dark';
  });
  useEffect(()=>{document.documentElement.dataset.theme=theme;document.querySelector('meta[name=theme-color]')?.setAttribute('content',theme==='light'?'#ffffff':'#191919');},[theme]);
  function toggleTheme(){const next=theme==='light'?'dark':'light';setTheme(next);try{localStorage.setItem('thaddeus-theme',next);}catch{}}
  const [logOpen,setLogOpen]=useState(()=>new URLSearchParams(location.search).get('view')==='upcoming'||(!appIdFromLocation()&&window.innerWidth>1100));
  const [logView,setLogView]=useState<SideView>(()=>new URLSearchParams(location.search).get('view')==='upcoming'?'upcoming':'my-page'),[usageExpanded,setUsageExpanded]=useState(true),[logFocusRequest,setLogFocusRequest]=useState(0);
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
        setMessage(draft.message);setAttachments(files);setArtifactChatId(app);setMode(draft.mode==='guidance'?'chat':draft.mode);setScope(draft.scope);setMemoryScope(draft.memoryScope);setHosts(draft.hosts);setPublicSearch(draft.publicSearch);setOpenResults(draft.openResults);setSearchQueries(draft.searchQueries);setChatLimits(draft.chatLimits);setBrowserLimits(draft.browserLimits||defaultBrowserLimits);setResearchLimits(draft.researchLimits);
        if(draft.message||files.length)setDraftNotice(missing?'Draft restored. Some original files or the selected app are unavailable; review the remaining context before sending.':'Draft restored in this tab.');
      }else setScope(data.pages.filter(page=>page.path.startsWith('notes/')).map(page=>page.path));
    }catch{setDraftStorageError('Draft recovery is unavailable in this browser. Keep a copy of unfinished text before reloading.');}
    setDraftSession(session.id);
  },[session?.id,data]);
  const composerDraft:ComposerDraft={message,uploadIds:attachments.map(file=>file.id),artifactId:artifactChatId,mode,scope,memoryScope,hosts,publicSearch,openResults,searchQueries,chatLimits,browserLimits,researchLimits};
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
    if(!session.owner){refresh().then(()=>setOnline(true)).catch(e=>setError(e.message));return;}
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
    const payload=mode==='research'?{content:sentMessage,mode,readScope:scope,memories:memoryScope,budget:researchLimits,web:hosts.trim()||publicSearch?{hosts:hosts.split(',').map(host=>host.trim().toLowerCase()).filter(Boolean),maxFetches:4,...(publicSearch?{search:{provider:'brave',credentialId:data?.search?.credentialId,maxQueries:searchQueries,openResults}}:{})}:null}:{content:sentMessage,uploadIds:attachments.map(f=>f.id),budget:chatLimits,browserBudget:browserLimits,artifactId:artifactChatId,localDate:localDay()};
    const created=await api<Run>('/chat',payload);setMessage(current=>current===sentMessage?'':current);setAttachments(current=>current.filter(file=>!sentAttachments.includes(file.id)));setDraftNotice('');setFocusId(undefined);
    if(mode==='research'){showRun(created.id);}else{setLatestChatRun(created.id);if(created.settingsSection==='approvals')openApprovalSettings();else if(created.connectionSetup)openConnectionSetup(created.connectionSetup,created.connectionSetupProduct,created.id,false);}
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
  async function decision(target:Run,allow:boolean,remember?:'allow'|'deny'){if(!target.approval)return;await api('/runs/'+target.id+'/approve',{approvalId:target.approval.id,digest:target.approval.digest,allow,remember});}
  function nav(name:string){dismissArtifact();setFocusId(undefined);setTab(name);setSelected(null);if(window.innerWidth<=1100)setLogOpen(false);if(window.innerWidth<=700)setSidebarExpanded(false);}
  function openApprovalSettings(){nav('Settings');setApprovalSettingsRequest(value=>value+1);}
  function showRun(id:string){setSelected(id);}
  function openArtifact(id:string,withChat=tab==='Home',discard=false){
    if(retainedArtifactId!==id&&artifactDirty&&!discard){setPendingArtifact({id,withChat});return false;}
    if(retainedArtifactId!==id){setArtifactDirty(false);setRetainedArtifactId(id);}
    setPendingArtifact(null);
    setArtifactIsMyPage(!!(withChat&&data?.myPage?.artifactId===id));
    if(!artifactPanelId){artifactReturn.current={tab,selected};artifactTrigger.current=document.activeElement as HTMLElement;}
    if(withChat)artifactReturn.current={tab:'Home',selected:null};
    if(appIdFromLocation()!==id)history[artifactPanelId?'replaceState':'pushState']({artifactPage:true,artifactReturn:artifactReturn.current},'', '/apps/'+id);
    else history.replaceState({...history.state,artifactReturn:artifactReturn.current},'');
    setArtifactPanelId(id);setArtifactChatId(id);setArtifactChatVisible(withChat&&window.innerWidth>1000);
    setSidebarExpanded(false);setLogOpen(false);setSelected(null);if(withChat)setTab('Home');
    requestAnimationFrame(()=>document.querySelector<HTMLButtonElement>('.artifact-page:not([hidden]) .artifact-page-header button')?.focus({preventScroll:true}));
    return true;
  }
  function dismissArtifact(){
    if(!artifactPanelId)return;
    history.replaceState(null,'','/');setArtifactPanelId(null);setArtifactChatVisible(false);setArtifactIsMyPage(false);
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
    const navigate=()=>{
      const id=appIdFromLocation();
      if(id&&id!==retainedArtifactId&&artifactDirty){history.replaceState({artifactReturn:artifactReturn.current},'',artifactPanelId?'/apps/'+artifactPanelId:'/');setPendingArtifact({id,withChat:false});return;}
      if(id&&history.state?.artifactReturn)artifactReturn.current=history.state.artifactReturn;
      if(id&&id!==retainedArtifactId){setRetainedArtifactId(id);setArtifactDirty(false);}
      setArtifactPanelId(id);if(id)setArtifactChatId(id);setArtifactChatVisible(false);setLogOpen(false);
      if(!id){setTab(artifactReturn.current.tab);setSelected(artifactReturn.current.selected);}
    };
    window.addEventListener('popstate',navigate);return()=>window.removeEventListener('popstate',navigate);
  },[retainedArtifactId,artifactDirty,artifactPanelId]);
  const artifactTitle=data?.artifacts?.find(app=>app.id===artifactPanelId)?.title;
  useEffect(()=>{document.title=artifactPanelId?(artifactTitle||'App')+' · First employee':'First employee · Marketing';},[artifactPanelId,artifactTitle]);
  function buildApp(prompt?:string){setArtifactChatId(null);nav('Home');setMode('chat');if(prompt)setMessage(prompt);else if(!message.trim())setMessage('Build me an app for ');requestAnimationFrame(()=>document.querySelector<HTMLTextAreaElement>('[aria-label="Message or goal"]')?.focus());}
  function openTokenInfo(trigger:HTMLButtonElement){if(artifactPanelId){dismissArtifact();setTab('Home');}logTriggerRef.current=trigger;setLogView('info');setUsageExpanded(true);setLogOpen(true);setLogFocusRequest(value=>value+1);}
  function openActivityLog(trigger:HTMLButtonElement){if(artifactPanelId){dismissArtifact();setTab('Home');}logTriggerRef.current=trigger;setLogView('activity');setLogOpen(true);setLogFocusRequest(value=>value+1);}
  function showSideView(view:SideView){
    dismissArtifact();setLogView(view);setLogOpen(true);
  }
  function showToday(){showSideView('my-page');}
  function showMyPage(setting=data?.myPage){
    if(setting?.mode==='artifact'&&setting.artifactId&&data?.artifacts?.some(app=>app.id===setting.artifactId&&!app.archived)){
      if(openArtifact(setting.artifactId,true))setArtifactIsMyPage(true);
    }else showToday();
  }
  async function pinMyPage(id:string|null){
    const saved=await api<MyPageSetting>('/my-page',{mode:id?'artifact':'today',artifactId:id,version:data?.myPage?.version||'absent'},'PUT');
    await refresh();showMyPage(saved);
  }
  useEffect(()=>{
    if((logOpen&&logView==='my-page'&&!artifactPanelId)||artifactIsMyPage)showMyPage();
  },[data?.myPage?.version]);
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
  function openConnectionSetup(target:'google'|'mcp',product?:string,runId?:string,focus=true){
    nav('Home');setMode('chat');setLogOpen(false);setSidebarExpanded(false);setConnectionSetup({target,product,runId});
    if(focus)requestAnimationFrame(()=>{const card=document.querySelector<HTMLElement>('[aria-label="Secure connection setup"]');card?.focus({preventScroll:true});card?.scrollIntoView({block:'nearest'});});
  }
  useEffect(()=>{const escape=(event:KeyboardEvent)=>{if(event.key==='Escape'){if(selected)return;else if(sidebarExpanded)closeSidebar();else if(logOpen)closeLog();else if(artifactPanelId&&!(event.target instanceof Element&&event.target.closest('input,textarea,select')))closeArtifact();}};window.addEventListener('keydown',escape);return()=>window.removeEventListener('keydown',escape);},[logOpen,sidebarExpanded,artifactPanelId,selected]);
  function ledger(items:Run[]){let previous='';return items.map(r=>{const date=new Date(r.created);const today=new Date();const yesterday=new Date();yesterday.setDate(today.getDate()-1);const group=date.toDateString()===today.toDateString()?'Today':date.toDateString()===yesterday.toDateString()?'Yesterday':date.toLocaleDateString(undefined,{month:'long',day:'numeric'});const heading=group!==previous;previous=group;return <React.Fragment key={r.id}>{heading&&<h3 className="time-group">{group}</h3>}<button data-run-id={r.id} aria-current={r.id===selected?'true':undefined} className="ledger-row" onClick={()=>{showRun(r.id);}}><span className={'state-icon '+r.state}><StateIcon state={r.state}/></span><span className="ledger-copy"><strong>{r.goal.objective.length>80?r.goal.objective.slice(0,77)+'…':r.goal.objective}</strong><small>{r.summary}</small></span><span className="ledger-time"><span className={'badge '+r.state}>{names[r.state]}</span><time>{date.toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'})}</time></span><ChevronRight size={16}/></button></React.Fragment>;});}
  const pinnedApp=data?.myPage?.mode==='artifact'?data.artifacts?.find(app=>app.id===data.myPage?.artifactId&&!app.archived):null;
  const sideTabs=<nav className="log-views" aria-label="Log views">
    <button type="button" aria-label="Today" title="Today's checklist" aria-pressed={!artifactPanelId&&logView==='my-page'} onClick={showToday}><ListTodo size={16}/></button>
    {pinnedApp&&<button type="button" className="pinned-page-tab" aria-label={'Pinned app: '+pinnedApp.title} title={pinnedApp.title} aria-pressed={artifactPanelId===pinnedApp.id} onClick={()=>showMyPage()}><Pin size={15}/><span>{pinnedApp.title}</span></button>}
    <button type="button" aria-label="Activity" title="Activity" aria-pressed={!artifactPanelId&&logView==='activity'} onClick={()=>showSideView('activity')}><List size={16}/></button>
    <button type="button" aria-label="Approvals" title="Approvals" aria-pressed={!artifactPanelId&&logView==='approvals'} onClick={()=>showSideView('approvals')}><ShieldCheck size={16}/></button>
    <button type="button" aria-label="Upcoming" title="Upcoming" aria-pressed={!artifactPanelId&&logView==='upcoming'} onClick={()=>showSideView('upcoming')}><Clock3 size={16}/></button>
    <button type="button" aria-label="Info" title="Info" aria-pressed={!artifactPanelId&&logView==='info'} onClick={()=>showSideView('info')}><CircleAlert size={16}/></button>
    <button type="button" aria-label="Profile" title="Identity, Soul, and User" aria-pressed={!artifactPanelId&&logView==='profile'} onClick={()=>showSideView('profile')}><Fingerprint size={16}/></button>
  </nav>;
  const taskActivity=<TaskActivity runs={data?.runs||[]} online={online} onCancel={id=>void act(()=>api('/runs/'+id+'/cancel',{}))} onDetails={showRun} onArtifact={id=>openArtifact(id,false)}/>;
  const connectionCard=connectionSetup&&<ConnectionSetupCard key={(connectionSetup.runId||'settings')+':'+connectionSetup.target+':'+connectionSetup.product} target={connectionSetup.target} initialProduct={connectionSetup.product} online={online} onClose={()=>setConnectionSetup(null)} onConnected={async result=>{await refresh();setConnectionSetup(null);const partial=result?.skippedProducts?.length?` Some selected access was not added: ${result.skippedProducts.join('; ')}.`:'';setDraftNotice(`Google connected${result?.account?' as '+result.account:''}.${partial} Thaddeus can now use the approved capabilities when you ask.`);}}/>;
  if(!loaded)return <main className="unlock"><Raven/><h1>Opening your company…</h1></main>;
  if(session&&draftSession!==session.id)return <main className="unlock"><Raven/><h1>Opening your company…</h1>{error&&<><p role="alert">{error}</p><button onClick={()=>void act(refresh)}>Try reconnecting</button></>}</main>;
  if(!session)return <main className="unlock"><div className="wordmark"><span className="mark">1</span>FIRST EMPLOYEE</div><Raven state="listening"/><p className="eyebrow">MARKETING WORKSPACE</p><h1>Your first hire.<br/><em>Under your direction.</em></h1><p>Unlock this browser with the host access key.<br/>Your company records stay on this computer.</p><form onSubmit={e=>{e.preventDefault();setError('');(pair?api('/pair/claim',{code:key,name:'Phone browser'}):api('/auth/login',{key})).then(s=>{if(pair){setError('Waiting for confirmation on the host. Then select Finish pairing.');}else{setCsrf(s.csrf);setSession(s);setKey('');}}).catch(e=>setError(e.message));}}><label>{pair?'One-time pairing code':'Host access key'}<input type="password" autoComplete="off" value={key} onChange={e=>setKey(e.target.value)} required/></label><button className="primary">{pair?'Request pairing':'Open workspace'} <ArrowUpRight size={17}/></button></form><button className="text-button" onClick={()=>setPair(!pair)}>{pair?'Use host access key':'Connect a phone instead'}</button>{pair&&<button onClick={()=>api('/pair/exchange',{}).then(s=>{if(s){setCsrf(s.csrf);setSession(s);}else setError('Host confirmation is still pending.');}).catch(e=>setError(e.message))}>Finish pairing</button>}<small>Host key: <code>.data/host-key.txt</code><br/>Phone access requires your host’s trusted HTTPS address.</small>{error&&<p role="alert" className="error">{error}</p>}</main>;
  return <MarketingWorkspace key={session.id} hostOnline={online} signedInName={session.name||'Signed-in device'}/>;
}

function Root(){
  const [maintenance,setMaintenance]=useState(location.pathname==='/maintenance'),[initial,setInitial]=useState<MaintenanceView>();
  const reopened=useCallback(()=>{history.replaceState(null,'','/');setMaintenance(false);setInitial(undefined);},[]);
  function started(view:MaintenanceView){history.replaceState(null,'','/maintenance');setInitial(view);setMaintenance(true);}
  return maintenance?<MaintenancePage initial={initial} onReopened={reopened}/>:<App onMaintenance={started}/>;
}
createRoot(document.getElementById('root')!).render(<Root/>);
if('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(()=>{});
