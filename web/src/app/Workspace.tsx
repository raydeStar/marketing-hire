import {useEffect,useMemo,useState} from 'react';
import {api} from '../api';
import {BookOpen,Coffee,History,Inbox,Search,LayoutTemplate,ListChecks,Megaphone,Menu,MessageCircle,PanelLeftClose,PanelLeftOpen,PanelRightClose,PanelRightOpen,Settings,Users,type LucideIcon} from 'lucide-react';
import {ContextPanel,initialContextWidth} from './ContextPanel';
import {CommandPalette} from './CommandPalette';
import {ViewBoundary} from './ErrorBoundary';
import {AssetsView} from './AssetsView';
import {CampaignsView} from './CampaignsView';
import {Conversation} from './ChatView';
import {HistoryView} from './HistoryView';
import {InboxView,inboxItems} from './InboxView';
import {Onboarding} from './Onboarding';
import {SettingsView,type StyleChoice,type ThemeChoice} from './SettingsView';
import {TaskDialog,TasksView} from './TasksView';
import {TeamView} from './TeamView';
import {TodayView,meetingPrompt} from './TodayView';
import {WikiView} from './WikiView';
import {briefComplete} from './BriefEditor';
import {employeeStatus,initials,useWorkspaceData,views,type View} from './shared';

type NavItem={view:View;label:string;icon:LucideIcon;count?:number};
const labels:Record<View,string>={today:'Today',chat:'Chat',inbox:'Inbox',campaigns:'Campaigns',tasks:'Tasks',assets:'Assets',wiki:'Wiki',team:'Team',history:'History',settings:'Settings'};
const themeKey='thaddeus-theme',railKey='fe-rail-collapsed',onboardingKey='fe-onboarding-dismissed';

function readView():View{const value=new URLSearchParams(location.search).get('view');return views.includes(value as View)?value as View:'today';}
function readStyle():StyleChoice{try{return localStorage.getItem('fe-style')==='muse'?'muse':'clean';}catch{return 'clean';}}
function readPanel(look:StyleChoice){try{const saved=localStorage.getItem('fe-context-open-'+look);if(saved)return saved==='yes';}catch{}return look==='muse';}
function readTheme():ThemeChoice{try{const saved=localStorage.getItem(themeKey);if(saved==='light'||saved==='dark'||saved==='system')return saved;}catch{}return 'system';}
function applyTheme(choice:ThemeChoice){
  const resolved=choice==='system'?(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light'):choice;
  document.documentElement.dataset.theme=resolved;
  document.querySelector('meta[name=theme-color]')?.setAttribute('content',resolved==='dark'?'#111113':'#faf9f7');
}

export function Workspace({hostOnline,signedInName,signedInId,onSignOut}:{hostOnline:boolean;signedInName:string;signedInId:string;onSignOut?:()=>Promise<void>}){
  const {state,directory,error,refresh,setDirectory}=useWorkspaceData();
  const [view,setView]=useState<View>(readView);
  const [theme,setTheme]=useState<ThemeChoice>(readTheme);
  const [look,setLook]=useState<StyleChoice>(readStyle);
  const [panelOpen,setPanelOpen]=useState(()=>readPanel(readStyle())),[panelWidth,setPanelWidth]=useState(initialContextWidth);
  const [collapsed,setCollapsed]=useState(()=>{try{return localStorage.getItem(railKey)==='yes';}catch{return false;}});
  const [railOpen,setRailOpen]=useState(false);
  const [taskId,setTaskId]=useState<string|null>(null);
  const [prefill,setPrefill]=useState<{text:string;send:boolean}|undefined>();
  const [focusReview,setFocusReview]=useState<{id:string;key:number}|undefined>();
  const [member,setMember]=useState<{id:string|null;tab:'files'|'brief'}>({id:null,tab:'files'});
  const [onboarding,setOnboarding]=useState(false);
  const [palette,setPalette]=useState(false),[deepLink,setDeepLink]=useState<{view:View;id:string;key:number}|null>(null);
  const [pastMeetingTaskIds,setPastMeetingTaskIds]=useState<Set<string>>(new Set());

  const owner=state?.canConfigure===true;
  // Meetings are paused: tasks they assigned are historical records, not decisions for today.
  useEffect(()=>{if(!owner)return;void api<{plan:{actions:{taskId:string|null}[]}|null}[]>('/meetings')
    .then(meetings=>setPastMeetingTaskIds(new Set(meetings.flatMap(meeting=>meeting.plan?.actions.flatMap(action=>action.taskId?[action.taskId]:[])||[])))).catch(()=>{});},[owner]);
  const live=useMemo(()=>state&&pastMeetingTaskIds.size?{...state,tasks:state.tasks.filter(task=>!pastMeetingTaskIds.has(task.id))}:state,[state,pastMeetingTaskIds]);
  const pastTasks=state?.tasks.filter(task=>pastMeetingTaskIds.has(task.id))||[];
  const status=employeeStatus(state,hostOnline,error);
  const canChat=hostOnline&&!error&&owner&&state?.connection.status==='connected';
  const canWrite=hostOnline&&!error&&owner&&state?.taskStoreAvailable===true;
  const name=state?.employee.name||'Marketing';
  const marketingMember=directory?.agents.find(item=>item.runtimeKey==='marketing');

  useEffect(()=>{applyTheme(theme);try{localStorage.setItem(themeKey,theme);}catch{}
    if(theme!=='system')return;const media=matchMedia('(prefers-color-scheme: dark)');const change=()=>applyTheme('system');media.addEventListener('change',change);return()=>media.removeEventListener('change',change);},[theme]);
  useEffect(()=>{try{localStorage.setItem(railKey,collapsed?'yes':'no');}catch{}},[collapsed]);
  useEffect(()=>{document.documentElement.dataset.style=look;try{localStorage.setItem('fe-style',look);}catch{}setPanelOpen(readPanel(look));},[look]);
  function togglePanel(){const next=!panelOpen;setPanelOpen(next);try{localStorage.setItem('fe-context-open-'+look,next?'yes':'no');}catch{}}
  useEffect(()=>{const open=(event:KeyboardEvent)=>{if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='k'){event.preventDefault();setPalette(value=>!value);}};addEventListener('keydown',open);return()=>removeEventListener('keydown',open);},[]);
  useEffect(()=>{const back=()=>setView(readView());addEventListener('popstate',back);return()=>removeEventListener('popstate',back);},[]);
  useEffect(()=>{if(state?.canConfigure===false&&!['campaigns','settings'].includes(view))go('campaigns',true);},[state?.canConfigure]);
  // First run: an owner with an empty brief is invited through onboarding once.
  useEffect(()=>{if(!owner||!state||briefComplete(state.profile))return;try{if(localStorage.getItem(onboardingKey)!=='yes')setOnboarding(true);}catch{}},[owner,!!state]);
  useEffect(()=>{if(!railOpen)return;const close=(event:KeyboardEvent)=>{if(event.key==='Escape')setRailOpen(false);};addEventListener('keydown',close);return()=>removeEventListener('keydown',close);},[railOpen]);

  function go(next:View,replace=false){
    setView(next);setRailOpen(false);
    const url=new URL(location.href);url.searchParams.set('view',next);url.searchParams.delete('meeting');
    history[replace?'replaceState':'pushState'](history.state,'',url);
  }
  function chatWith(text:string,send=false){setPrefill({text,send});go('chat');}
  function openBrief(){if(marketingMember)setMember({id:marketingMember.id,tab:'brief'});go('team');}
  function closeOnboarding(){setOnboarding(false);try{localStorage.setItem(onboardingKey,'yes');}catch{}if(view!=='today')go('today');}
  async function signOut(){if(onSignOut)await onSignOut();}

  const inboxCount=inboxItems(live).length;
  // A background tab still shows what's waiting.
  useEffect(()=>{document.title=`${inboxCount&&owner?`(${inboxCount}) `:''}${labels[view]} · First Employee`;},[view,inboxCount,owner]);
  // Until the host answers, assume the owner layout; only a confirmed collaborator gets the shared view.
  const collaborator=state?.canConfigure===false;
  const primary:NavItem[]=!collaborator?[{view:'today',label:'Today',icon:Coffee},{view:'chat',label:'Chat',icon:MessageCircle},{view:'inbox',label:'Inbox',icon:Inbox,count:inboxCount},{view:'campaigns',label:'Campaigns',icon:Megaphone},{view:'assets',label:'Assets',icon:LayoutTemplate}]
    :[{view:'campaigns',label:'Shared campaigns',icon:Megaphone}];
  const company:NavItem[]=!collaborator?[{view:'tasks',label:'Tasks',icon:ListChecks,count:live?.tasks.filter(task=>task.status==='working').length||undefined},{view:'wiki',label:'Wiki',icon:BookOpen},{view:'team',label:'Team',icon:Users},{view:'history',label:'History',icon:History}]:[];
  const navButton=(item:NavItem)=><button type="button" key={item.view} className="fe-nav-item" aria-current={view===item.view?'page':undefined} title={collapsed?item.label:undefined}
    data-count={item.count&&item.view==='inbox'?item.count:undefined} onClick={()=>{if(item.view==='team')setMember({id:null,tab:'files'});go(item.view);}}>
    <item.icon size={19}/><span className="fe-nav-label">{item.label}</span>{!!item.count&&<span className="fe-badge" aria-label={`${item.count} ${item.view==='inbox'?'waiting':'in progress'}`}>{item.count}</span>}</button>;

  const task=state?.tasks.find(item=>item.id===taskId);
  let page:React.ReactNode=<div className="fe-page"><div className="fe-empty"><p>{error?'The workspace couldn’t load. '+error:'Opening your workspace…'}</p></div></div>;
  if(state&&live&&directory){
    if(view==='today'&&owner)page=<TodayView state={live} status={status} ownerName={signedInName} canWrite={!!canChat} onMeeting={()=>chatWith(meetingPrompt,true)} onPrompt={text=>chatWith(text)} onInbox={()=>go('inbox')} onOpenBrief={openBrief} onOnboard={()=>setOnboarding(true)}/>;
    else if(view==='chat'&&owner)page=<div className="fe-chat-layout">
      <Conversation key={state.employee.sessionKey} state={live} canWrite={!!canChat} status={status} prefill={prefill?.text} autoSend={prefill?.send} onPrefillUsed={()=>setPrefill(undefined)} onRefresh={refresh} onOpenBrief={()=>setOnboarding(true)}
        headerActions={<button type="button" className="fe-icon-button fe-panel-toggle" aria-label={panelOpen?'Hide context panel':'Show context panel'} aria-expanded={panelOpen} aria-controls="context-panel" title="At a glance" onClick={togglePanel}>{panelOpen?<PanelRightClose size={18}/>:<PanelRightOpen size={18}/>}</button>}/>
      {panelOpen&&<ContextPanel state={live} status={status} width={panelWidth} onWidth={setPanelWidth} onClose={togglePanel} onTask={setTaskId}
        onItem={item=>item.kind==='task'?setTaskId(item.id.slice(5)):item.kind==='review'?(setFocusReview({id:item.id.slice(7),key:Date.now()}),go('campaigns')):item.kind==='draft'?go('inbox'):setOnboarding(true)}
        onGo={target=>target==='brief'?openBrief():go(target)}/>}
    </div>;
    else if(view==='inbox'&&owner)page=<InboxView state={live} canWrite={!!canWrite} onOpenTask={setTaskId} onOpenReview={id=>{setFocusReview({id,key:Date.now()});go('campaigns');}} onOpenBrief={()=>setOnboarding(true)} onRefresh={refresh}/>;
    else if(view==='tasks'&&owner)page=<TasksView state={live} pastMeetingTasks={pastTasks} canWrite={!!canWrite} onOpenTask={setTaskId} onRefresh={refresh}/>;
    else if(view==='assets'&&owner)page=<AssetsView key={deepLink?.view==='assets'?deepLink.key:'assets'} initialOpen={deepLink?.view==='assets'?deepLink.id:null} state={state} online={hostOnline} onDiscuss={text=>chatWith(text)} onOpenCampaigns={()=>go('campaigns')}/>;
    else if(view==='wiki'&&owner)page=<WikiView key={deepLink?.view==='wiki'?deepLink.key:'wiki'} initialSelected={deepLink?.view==='wiki'?deepLink.id:null} directory={directory} canEdit={hostOnline}/>;
    else if(view==='team'&&owner)page=<TeamView state={state} directory={directory} status={status} canWrite={hostOnline} memberId={member.id} tab={member.tab} onOpen={(id,tab='files')=>setMember({id,tab})} onDirectory={setDirectory} onRefresh={refresh} onStartOnboarding={()=>setOnboarding(true)}/>;
    else if(view==='history'&&owner)page=<HistoryView state={state} onOpenTask={setTaskId}/>;
    else if(view==='settings')page=<SettingsView owner={owner} hostOnline={hostOnline} theme={theme} onTheme={setTheme} look={look} onLook={setLook} signedInName={signedInName} onSignOut={onSignOut?()=>void signOut():undefined}/>;
    else page=<CampaignsView state={state} hostOnline={hostOnline} readError={error} signedInId={signedInId} customerAccount={Boolean(onSignOut)} focusReview={focusReview} onOpenBrief={openBrief} onRefresh={refresh}/>;
  }

  return <div className={'fe-app'+(collapsed?' rail-collapsed':'')+(railOpen?' rail-open':'')}>
    <button type="button" className="fe-scrim" aria-label="Close menu" onClick={()=>setRailOpen(false)}/>
    <aside className="fe-rail" aria-label="Main navigation">
      <div className="fe-brand"><span className="fe-brand-mark" aria-hidden="true">1</span><span>First Employee<small>{collaborator?'Shared workspace':'Marketing'}</small></span></div>
      {!collaborator&&<button type="button" className="fe-nav-item fe-search-button" title={collapsed?'Search (Ctrl+K)':undefined} onClick={()=>setPalette(true)}><Search size={19}/><span className="fe-nav-label">Search</span><kbd className="fe-nav-label">Ctrl K</kbd></button>}
      <nav aria-label="Main views">{primary.map(navButton)}
        {company.length>0&&<><p className="fe-rail-section">Company</p>{company.map(navButton)}</>}</nav>
      <div className="fe-rail-foot">
        <button type="button" className="fe-nav-item" aria-current={view==='settings'?'page':undefined} title={collapsed?'Settings':undefined} onClick={()=>go('settings')}><Settings size={19}/><span className="fe-nav-label">Settings</span></button>
        <button type="button" className="fe-nav-item fe-rail-collapse" onClick={()=>setCollapsed(!collapsed)} aria-label={collapsed?'Expand sidebar':'Collapse sidebar'} title={collapsed?'Expand sidebar':undefined}>{collapsed?<PanelLeftOpen size={19}/>:<PanelLeftClose size={19}/>}<span className="fe-nav-label">Collapse</span></button>
        <div className="fe-status" role="status" title={name+': '+status.label}><i className={'fe-dot '+status.tone}/><span>{owner?`${name} · ${status.label}`:status.label}</span></div>
        <div className="fe-user"><span className="fe-avatar muted" aria-hidden="true">{initials(signedInName)}</span><span>{signedInName}<small>{owner?'Owner':'Collaborator'}</small></span></div>
      </div>
    </aside>
    <main className="fe-main" aria-label={labels[view]}>
      <div className="fe-mobile-bar"><button type="button" className="fe-icon-button" aria-label="Open menu" onClick={()=>setRailOpen(true)}><Menu size={20}/></button><strong>{labels[view]}</strong>{owner&&inboxCount>0&&view!=='inbox'&&<button type="button" className="fe-icon-button" aria-label={`Inbox, ${inboxCount} waiting`} onClick={()=>go('inbox')}><Inbox size={19}/></button>}<i className={'fe-dot '+status.tone} title={status.label}/></div>
      {(error&&state||!hostOnline)&&<div className="fe-alert" role="alert"><span>{!hostOnline?'The host is offline. Changes are paused until it reconnects.':'Couldn’t refresh: '+error+' Showing the last saved view.'}</span><button type="button" onClick={()=>void refresh()}>Retry</button></div>}
      <ViewBoundary view={view}>{page}</ViewBoundary>
    </main>
    {task&&state&&<TaskDialog task={task} state={state} canWrite={!!canWrite&&!pastMeetingTaskIds.has(task.id)} canChat={!!canChat&&!pastMeetingTaskIds.has(task.id)} pastMeeting={pastMeetingTaskIds.has(task.id)} onClose={()=>setTaskId(null)} onRefresh={refresh}/>}
    {palette&&owner&&<CommandPalette state={live} views={[...primary,...company,{view:'settings',label:'Settings',icon:Settings}]} onClose={()=>setPalette(false)} onView={next=>go(next)} onTask={setTaskId}
      onAsset={id=>{setDeepLink({view:'assets',id,key:Date.now()});go('assets');}} onWiki={id=>{setDeepLink({view:'wiki',id,key:Date.now()});go('wiki');}} onAsk={text=>chatWith(text)}/>}
    {onboarding&&state&&owner&&<Onboarding state={state} canWrite={!!canChat} onClose={closeOnboarding} onRefresh={refresh}/>}
  </div>;
}
