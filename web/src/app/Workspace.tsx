import {useEffect,useMemo,useRef,useState} from 'react';
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
import {SettingsView,notifyKey,type StyleChoice,type ThemeChoice} from './SettingsView';
import {TaskDialog,TasksView} from './TasksView';
import {TeamView} from './TeamView';
import {TodayView,meetingPrompt} from './TodayView';
import {WikiView} from './WikiView';
import {briefComplete} from './BriefEditor';
import {employeeStatus,initials,useWorkspaceData,views,type View} from './shared';

type NavItem={view:View;label:string;icon:LucideIcon;count?:number};
const labels:Record<View,string>={today:'Today',chat:'Chat',inbox:'Inbox',campaigns:'Campaigns',tasks:'Tasks',assets:'Assets',wiki:'Wiki',team:'Team',history:'History',settings:'Settings'};
const themeKey='thaddeus-theme',railKey='fe-rail-collapsed',onboardingKey='fe-onboarding-dismissed';
type Access='viewer'|'collaborator'|'contributor'|'manager'|'owner';
const rank:Record<Access,number>={viewer:0,collaborator:1,contributor:2,manager:3,owner:4};
const roleLabel:Record<Access,string>={viewer:'Viewer',collaborator:'Reviewer',contributor:'Contributor',manager:'Manager',owner:'Owner'};

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
  const [member,setMember]=useState<{id:string|null;tab:'files'|'brief'|'permissions'}>({id:null,tab:'files'});
  const [onboarding,setOnboarding]=useState(false);
  const [palette,setPalette]=useState(false),[deepLink,setDeepLink]=useState<{view:View;id:string;key:number}|null>(null);
  const [pastMeetingTaskIds,setPastMeetingTaskIds]=useState<Set<string>>(new Set());

  // One access level from the host decides what this person sees: viewer < collaborator < contributor < manager < owner.
  const access:Access=(state?.access as Access|undefined)??(state?(state.canConfigure?'owner':'collaborator'):'owner');
  const owner=access==='owner';
  const reads=rank[access]>=rank.contributor,talks=rank[access]>=rank.manager;
  // Meetings are paused: tasks they assigned are historical records, not decisions for today.
  useEffect(()=>{if(!owner)return;void api<{plan:{actions:{taskId:string|null}[]}|null}[]>('/meetings')
    .then(meetings=>setPastMeetingTaskIds(new Set(meetings.flatMap(meeting=>meeting.plan?.actions.flatMap(action=>action.taskId?[action.taskId]:[])||[])))).catch(()=>{});},[owner]);
  const live=useMemo(()=>state&&pastMeetingTaskIds.size?{...state,tasks:state.tasks.filter(task=>!pastMeetingTaskIds.has(task.id))}:state,[state,pastMeetingTaskIds]);
  const pastTasks=state?.tasks.filter(task=>pastMeetingTaskIds.has(task.id))||[];
  const status=employeeStatus(state,hostOnline,error);
  const canChat=hostOnline&&!error&&talks&&state?.connection.status==='connected';
  const canWrite=hostOnline&&!error&&reads&&state?.taskStoreAvailable===true;
  const canDecide=canWrite&&owner;
  const allowed=(target:View)=>target==='campaigns'||target==='settings'||(['today','chat','inbox'].includes(target)?talks:reads);
  const name=state?.employee.name||'Marketing';
  const marketingMember=directory?.agents.find(item=>item.runtimeKey==='marketing');

  useEffect(()=>{applyTheme(theme);try{localStorage.setItem(themeKey,theme);}catch{}
    if(theme!=='system')return;const media=matchMedia('(prefers-color-scheme: dark)');const change=()=>applyTheme('system');media.addEventListener('change',change);return()=>media.removeEventListener('change',change);},[theme]);
  useEffect(()=>{try{localStorage.setItem(railKey,collapsed?'yes':'no');}catch{}},[collapsed]);
  useEffect(()=>{document.documentElement.dataset.style=look;try{localStorage.setItem('fe-style',look);}catch{}setPanelOpen(readPanel(look));},[look]);
  function togglePanel(){const next=!panelOpen;setPanelOpen(next);try{localStorage.setItem('fe-context-open-'+look,next?'yes':'no');}catch{}}
  useEffect(()=>{const open=(event:KeyboardEvent)=>{if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='k'){event.preventDefault();setPalette(value=>!value);}};addEventListener('keydown',open);return()=>removeEventListener('keydown',open);},[]);
  useEffect(()=>{const back=()=>setView(readView());addEventListener('popstate',back);return()=>removeEventListener('popstate',back);},[]);
  useEffect(()=>{if(state&&!allowed(view))go(reads?'tasks':'campaigns',true);},[access,view,!!state]);
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
  // Opt-in: tell the owner about new Inbox items while the tab is in the background.
  const seenItems=useRef<Set<string>|null>(null);
  const items=inboxItems(live);
  useEffect(()=>{
    if(!state)return;
    const ids=new Set(items.map(item=>item.id));
    const previous=seenItems.current;seenItems.current=ids;
    if(!previous||!document.hidden||typeof Notification==='undefined'||Notification.permission!=='granted')return;
    try{if(localStorage.getItem(notifyKey)!=='yes')return;}catch{return;}
    const fresh=items.filter(item=>!previous.has(item.id));
    if(fresh.length){const notice=new Notification(`${name} needs you`,{body:fresh.length===1?fresh[0].title:`${fresh.length} new items in your Inbox`,tag:'fe-inbox'});notice.onclick=()=>{window.focus();go('inbox');notice.close();};}
  },[!!state,items.map(item=>item.id).join('|')]);
  // A background tab still shows what's waiting.
  useEffect(()=>{document.title=`${inboxCount&&talks?`(${inboxCount}) `:''}${labels[view]} · First Employee`;},[view,inboxCount,talks]);
  // Until the host answers, assume the owner layout; the rail narrows once the host confirms a teammate's role.
  const collaborator=!reads;
  const campaignsItem:NavItem={view:'campaigns',label:owner?'Campaigns':'Shared campaigns',icon:Megaphone};
  const primary:NavItem[]=talks?[{view:'today',label:'Today',icon:Coffee},{view:'chat',label:'Chat',icon:MessageCircle},{view:'inbox',label:'Inbox',icon:Inbox,count:inboxCount},campaignsItem,{view:'assets',label:'Assets',icon:LayoutTemplate}]
    :reads?[campaignsItem,{view:'assets',label:'Assets',icon:LayoutTemplate}]:[campaignsItem];
  const company:NavItem[]=reads?[{view:'tasks',label:'Tasks',icon:ListChecks,count:live?.tasks.filter(task=>task.status==='working').length||undefined},{view:'wiki',label:'Wiki',icon:BookOpen},{view:'team',label:'Team',icon:Users},{view:'history',label:'History',icon:History}]:[];
  const navButton=(item:NavItem)=><button type="button" key={item.view} className="fe-nav-item" aria-current={view===item.view?'page':undefined} title={collapsed?item.label:undefined}
    data-count={item.count&&item.view==='inbox'?item.count:undefined} onClick={()=>{if(item.view==='team')setMember({id:null,tab:'files'});go(item.view);}}>
    <item.icon size={19}/><span className="fe-nav-label">{item.label}</span>{!!item.count&&<span className="fe-badge" aria-label={`${item.count} ${item.view==='inbox'?'waiting':'in progress'}`}>{item.count}</span>}</button>;

  const task=state?.tasks.find(item=>item.id===taskId);
  let page:React.ReactNode=<div className="fe-page"><div className="fe-empty"><p>{error?'The workspace couldn’t load. '+error:'Opening your workspace…'}</p></div></div>;
  if(state&&live&&directory){
    if(view==='today'&&talks)page=<TodayView state={live} status={status} ownerName={signedInName} canWrite={!!canChat} showGettingStarted={owner} onMeeting={()=>chatWith(meetingPrompt,true)} onPrompt={text=>chatWith(text)} onInbox={()=>go('inbox')} onOpenBrief={openBrief} onOnboard={()=>owner?setOnboarding(true):openBrief()} onHistory={()=>go('history')} onNewPage={()=>go('assets')} onInvite={()=>go('settings')}/>;
    else if(view==='chat'&&talks)page=<div className="fe-chat-layout">
      <Conversation key={state.employee.sessionKey} state={live} canWrite={!!canChat} status={status} prefill={prefill?.text} autoSend={prefill?.send} onPrefillUsed={()=>setPrefill(undefined)} onRefresh={refresh} onOpenBrief={()=>owner?setOnboarding(true):openBrief()}
        headerActions={<button type="button" className="fe-icon-button fe-panel-toggle" aria-label={panelOpen?'Hide context panel':'Show context panel'} aria-expanded={panelOpen} aria-controls="context-panel" title="At a glance" onClick={togglePanel}>{panelOpen?<PanelRightClose size={18}/>:<PanelRightOpen size={18}/>}</button>}/>
      {panelOpen&&<ContextPanel state={live} status={status} showUsage={owner} width={panelWidth} onWidth={setPanelWidth} onClose={togglePanel} onTask={setTaskId}
        onItem={item=>item.kind==='task'?setTaskId(item.id.slice(5)):item.kind==='review'?(setFocusReview({id:item.id.slice(7),key:Date.now()}),go('campaigns')):item.kind==='draft'?go('inbox'):setOnboarding(true)}
        onGo={target=>target==='brief'?openBrief():go(target)}/>}
    </div>;
    else if(view==='inbox'&&talks)page=<InboxView state={live} canWrite={!!canDecide} owner={owner} onOpenTask={setTaskId} onOpenReview={id=>{setFocusReview({id,key:Date.now()});go('campaigns');}} onOpenBrief={()=>owner?setOnboarding(true):openBrief()} onRefresh={refresh}/>;
    else if(view==='tasks'&&reads)page=<TasksView state={live} pastMeetingTasks={pastTasks} canWrite={!!canWrite} onOpenTask={setTaskId} onRefresh={refresh}/>;
    else if(view==='assets'&&reads)page=<AssetsView canPublish={talks} canAsk={talks} key={deepLink?.view==='assets'?deepLink.key:'assets'} initialOpen={deepLink?.view==='assets'?deepLink.id:null} state={state} online={hostOnline} onDiscuss={text=>chatWith(text)} onOpenCampaigns={()=>go('campaigns')}/>;
    else if(view==='wiki'&&reads)page=<WikiView key={deepLink?.view==='wiki'?deepLink.key:'wiki'} initialSelected={deepLink?.view==='wiki'?deepLink.id:null} directory={directory} canEdit={hostOnline}/>;
    else if(view==='team'&&reads)page=<TeamView state={state} directory={directory} status={status} canWrite={hostOnline&&talks} canManageTeam={hostOnline&&owner} memberId={member.id} tab={member.tab} onOpen={(id,tab='files')=>setMember({id,tab})} onDirectory={setDirectory} onRefresh={refresh} onStartOnboarding={()=>setOnboarding(true)}/>;
    else if(view==='history'&&reads)page=<HistoryView state={state} canReadChat={talks} onOpenTask={setTaskId}/>;
    else if(view==='settings')page=<SettingsView owner={owner} canNotify={talks} accessLabel={roleLabel[access]} hostOnline={hostOnline} theme={theme} onTheme={setTheme} look={look} onLook={setLook} signedInName={signedInName} onSignOut={onSignOut?()=>void signOut():undefined}/>;
    else page=<CampaignsView state={state} readOnly={access==='viewer'} hostOnline={hostOnline} readError={error} signedInId={signedInId} customerAccount={Boolean(onSignOut)} focusReview={focusReview} onOpenBrief={openBrief} onRefresh={refresh}/>;
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
        <div className="fe-status" role="status" title={name+': '+status.label}><i className={'fe-dot '+status.tone}/><span>{talks?`${name} · ${status.label}`:status.label}</span></div>
        <div className="fe-user"><span className="fe-avatar muted" aria-hidden="true">{initials(signedInName)}</span><span>{signedInName}<small>{roleLabel[access]}</small></span></div>
      </div>
    </aside>
    <main className="fe-main" aria-label={labels[view]}>
      <div className="fe-mobile-bar"><button type="button" className="fe-icon-button" aria-label="Open menu" onClick={()=>setRailOpen(true)}><Menu size={20}/></button><strong>{labels[view]}</strong>{talks&&inboxCount>0&&view!=='inbox'&&<button type="button" className="fe-icon-button" aria-label={`Inbox, ${inboxCount} waiting`} onClick={()=>go('inbox')}><Inbox size={19}/></button>}<i className={'fe-dot '+status.tone} title={status.label}/></div>
      {(error&&state||!hostOnline)&&<div className="fe-alert" role="alert"><span>{!hostOnline?'The host is offline. Changes are paused until it reconnects.':'Couldn’t refresh: '+error+' Showing the last saved view.'}</span><button type="button" onClick={()=>void refresh()}>Retry</button></div>}
      <ViewBoundary view={view}>{page}</ViewBoundary>
    </main>
    {task&&state&&<TaskDialog task={task} state={state} canWrite={!!canWrite&&!pastMeetingTaskIds.has(task.id)} canChat={!!canChat&&!pastMeetingTaskIds.has(task.id)} pastMeeting={pastMeetingTaskIds.has(task.id)} onClose={()=>setTaskId(null)} onRefresh={refresh}/>}
    {palette&&reads&&<CommandPalette state={live} views={[...primary,...company,{view:'settings',label:'Settings',icon:Settings}]} onClose={()=>setPalette(false)} onView={next=>go(next)} onTask={setTaskId}
      onAsset={id=>{setDeepLink({view:'assets',id,key:Date.now()});go('assets');}} onWiki={id=>{setDeepLink({view:'wiki',id,key:Date.now()});go('wiki');}} onAsk={talks?text=>chatWith(text):undefined}/>}
    {onboarding&&state&&owner&&<Onboarding state={state} canWrite={!!canChat} onClose={closeOnboarding} onRefresh={refresh}/>}
  </div>;
}
