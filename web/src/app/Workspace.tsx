import {useEffect,useMemo,useRef,useState} from 'react';
import {ExperienceProvider,useExperienceData} from './Experience';
import {api} from '../api';
import {BookOpen,Columns2,Keyboard,LogOut,Maximize2,Menu,MessageSquareText,Monitor,Moon,PanelLeftClose,PanelLeftOpen,PanelRightOpen,Search,Settings,Sun,Users} from 'lucide-react';
import {CampaignSharedWorkspace} from '../components/CampaignSharedWorkspace';
import {Cockpit} from './Cockpit';
import {CommandPalette} from './CommandPalette';
import {ViewBoundary} from './ErrorBoundary';
import {Conversation} from './ChatView';
import {GettingStarted} from './GettingStarted';
import {inboxItems,useAttention,type InboxItem} from './InboxView';
import {LibraryView} from './LibraryView';
import {Onboarding} from './Onboarding';
import {SettingsView,notifyKey,type ThemeChoice} from './SettingsView';
import {TeamView} from './TeamView';
import {WorkView,openWorkTab,sectionTab} from './WorkView';
import {WorkWindow,itemTitle,type Perms} from './WorkWindow';
import {UsageHoverCard,useEmployeeUsage} from './EmployeeUsage';
import type {EmployeeTab} from './Employee';
import {briefComplete} from './BriefEditor';
import {useLibrary} from './library';
import {CampaignsProvider,useCampaignBook} from './campaigns';
import {MeContext} from './shared';
import {ShiftPanel} from './ShiftPanel';
import {SiteConnectHost,openSiteConnect} from './PublishingView';
import {useShifts} from './shifts';
import {NorthStarCard} from './ObjectivesEditor';
import {hasGoals,useObjectives} from './objectives';
import {employeeStatus,initials,queuedEvent,useMenuKeys,useWorkspaceData} from './shared';
import {BrandMark} from '../components/BrandMark';

export const meetingPrompt=`Morning meeting. Work through your heartbeat checklist and give me a short brief:
1. What changed since yesterday (signals, replies, results)?
2. Anything that needs my decision today?
3. Which tasks are blocked, and on whom?
4. Today's top 3 priorities, each with your recommended next step.
5. Drafts waiting for my approval.
6. One learning worth writing down.
Lead with decisions, keep it under 200 words.`;

type View='home'|'library'|'team'|'settings';
type Pane='chat'|'work';
type Access='viewer'|'collaborator'|'contributor'|'manager'|'owner';
type Route={view:View;pane:Pane;open:string|null};
const rank:Record<Access,number>={viewer:0,collaborator:1,contributor:2,manager:3,owner:4};
const roleLabel:Record<Access,string>={viewer:'Viewer',collaborator:'Reviewer',contributor:'Contributor',manager:'Manager',owner:'Owner'};
const themeKey='thaddeus-theme',cockpitKey='fe-cockpit-open',onboardingKey='fe-onboarding-dismissed';
// Items that belong to the Library open there; everything else opens in the work window beside chat.
const libraryKinds=['brief','wiki','page','media','source','deliverable'];

function readRoute():Route{
  const params=new URLSearchParams(location.search);
  const view=params.get('view') as View;const pane=params.get('pane') as Pane;
  return {view:['home','library','team','settings'].includes(view)?view:'home',pane:pane==='work'?'work':'chat',open:params.get('open')};
}
function readTheme():ThemeChoice{try{const saved=localStorage.getItem(themeKey);if(saved==='light'||saved==='dark'||saved==='system')return saved;}catch{}return 'system';}
function applyTheme(choice:ThemeChoice){
  const resolved=choice==='system'?(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light'):choice;
  document.documentElement.dataset.theme=resolved;
  document.querySelector('meta[name=theme-color]')?.setAttribute('content',resolved==='dark'?'#161618':'#f6f6f7');
}
function useWide(query:string){
  const [match,setMatch]=useState(()=>matchMedia(query).matches);
  useEffect(()=>{const media=matchMedia(query);const change=()=>setMatch(media.matches);media.addEventListener('change',change);return()=>media.removeEventListener('change',change);},[query]);
  return match;
}

export function Workspace({hostOnline,signedInName,signedInId,onSignOut}:{hostOnline:boolean;signedInName:string;signedInId:string;onSignOut?:()=>Promise<void>}){
  const {state,directory,error,refresh,setDirectory}=useWorkspaceData();
  useEffect(()=>{const on=()=>void refresh();window.addEventListener(queuedEvent,on);return()=>window.removeEventListener(queuedEvent,on);},[refresh]);
  const [route,setRoute]=useState<Route>(readRoute);
  const [theme,setTheme]=useState<ThemeChoice>(readTheme);
  const [cockpitOpen,setCockpitOpen]=useState(()=>{try{return localStorage.getItem(cockpitKey)!=='no';}catch{return true;}});
  // Both side panels are the person's to size: the rail can show labels, the cockpit can be dragged wider.
  const [railWide,setRailWide]=useState(()=>{try{return localStorage.getItem('fe-rail-wide')==='yes';}catch{return false;}});
  const [cockpitWidth,setCockpitWidth]=useState(()=>{try{const saved=Number(localStorage.getItem('fe-cockpit-width'));return saved>=280&&saved<=720?saved:320;}catch{return 320;}});
  const drag=useRef<{x:number;width:number}|null>(null);
  const clampCockpit=(value:number)=>Math.round(Math.min(Math.max(280,value),Math.min(720,innerWidth*.5)));
  function resizeCockpit(value:number){const next=clampCockpit(value);setCockpitWidth(next);try{localStorage.setItem('fe-cockpit-width',String(next));}catch{}}
  function toggleRail(){setRailWide(value=>{try{localStorage.setItem('fe-rail-wide',value?'no':'yes');}catch{}return !value;});}
  const [sheet,setSheet]=useState(false),[menu,setMenu]=useState(false),[shortcuts,setShortcuts]=useState(false);
  const [prefill,setPrefill]=useState<{text:string;send:boolean}|undefined>();
  const [focusReview,setFocusReview]=useState<{id:string;key:number}|undefined>();
  const [member,setMember]=useState<{id:string|null;tab:EmployeeTab}>({id:null,tab:'brief'});
  const [onboarding,setOnboarding]=useState(false),[palette,setPalette]=useState(false);
  const [newFolder,setNewFolder]=useState<string|undefined>();
  const [pastMeetingTaskIds,setPastMeetingTaskIds]=useState<Set<string>>(new Set());
  const wide=useWide('(min-width: 1100px)'),roomy=useWide('(min-width: 1280px)');

  // One access level from the host decides what this person sees: viewer < collaborator < contributor < manager < owner.
  const access:Access=(state?.access as Access|undefined)??(state?(state.canConfigure?'owner':'collaborator'):'owner');
  const owner=access==='owner',reads=rank[access]>=rank.contributor,talks=rank[access]>=rank.manager;
  const library=useLibrary(state,reads&&!!state);
  const campaigns=useCampaignBook(reads&&!!state,()=>void library.reload());
  const me=useMemo(()=>({id:signedInId,name:signedInName}),[signedInId,signedInName]);
  const shifts=useShifts(reads&&!!state);
  const experience=useExperienceData(reads&&!!state);
  const objectives=useObjectives(reads&&!!state);
  // Items saved elsewhere (a reply kept as a document, onboarding's ethos page) must show when the Library or search opens.
  useEffect(()=>{if(route.view==='library'||palette)void library.reload();},[route.view,palette]);
  useEffect(()=>{if(!owner)return;void api<{plan:{actions:{taskId:string|null}[]}|null}[]>('/meetings')
    .then(meetings=>setPastMeetingTaskIds(new Set(meetings.flatMap(meeting=>meeting.plan?.actions.flatMap(action=>action.taskId?[action.taskId]:[])||[])))).catch(()=>{});},[owner]);
  // Tasks from past meetings are historical records, not decisions for today.
  const live=useMemo(()=>state&&pastMeetingTaskIds.size?{...state,tasks:state.tasks.filter(task=>!pastMeetingTaskIds.has(task.id))}:state,[state,pastMeetingTaskIds]);
  const pastTasks=state?.tasks.filter(task=>pastMeetingTaskIds.has(task.id))||[];
  // "Online" while it works a shift left owners guessing why chat was slow; say it's on shift, and until when.
  const onShift=shifts.view?.current&&['running','finishing'].includes(shifts.view.current.status)?shifts.view.current:null;
  const base=employeeStatus(state,hostOnline,error);
  const status=onShift&&base.tone==='live'&&state?.canConfigure!==false
    ?{label:onShift.requests?'Working on what you asked':onShift.status==='finishing'?'Wrapping up its shift':`On shift until ${new Date(onShift.endsAt).toLocaleTimeString([],{hour:'numeric',minute:'2-digit'})}`,tone:'busy' as const}:base;
  const canChat=hostOnline&&!error&&talks&&state?.connection.status==='connected';
  const canWrite=hostOnline&&!error&&reads&&state?.taskStoreAvailable===true;
  const perms:Perms={owner,reads,talks,viewer:access==='viewer',canWrite:!!canWrite,canChat:!!canChat,canDecide:!!canWrite&&owner,hostOnline};
  const name=state?.employee.name||'Marketing';

  useEffect(()=>{applyTheme(theme);try{localStorage.setItem(themeKey,theme);}catch{}
    if(theme!=='system')return;const media=matchMedia('(prefers-color-scheme: dark)');const change=()=>applyTheme('system');media.addEventListener('change',change);return()=>media.removeEventListener('change',change);},[theme]);
  useEffect(()=>{try{localStorage.setItem(cockpitKey,cockpitOpen?'yes':'no');}catch{}},[cockpitOpen]);
  useEffect(()=>{const key=(event:KeyboardEvent)=>{if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='k'){event.preventDefault();setPalette(value=>!value);}};addEventListener('keydown',key);return()=>removeEventListener('keydown',key);},[]);
  useEffect(()=>{const back=()=>setRoute(readRoute());addEventListener('popstate',back);return()=>removeEventListener('popstate',back);},[]);
  useEffect(()=>{if(!owner||!state||briefComplete(state.profile))return;try{if(localStorage.getItem(onboardingKey)!=='yes')setOnboarding(true);}catch{}},[owner,!!state]);
  // Keep each person inside what their role allows.
  useEffect(()=>{
    if(!state)return;
    if(route.view!=='home'&&route.view!=='settings'&&!reads)go({view:'home',pane:'work',open:null},true);
    else if(route.view==='home'&&route.pane==='chat'&&!talks)go({...route,pane:'work'},true);
  },[access,route.view,route.pane,!!state]);
  useEffect(()=>{if(!menu)return;const close=(event:KeyboardEvent)=>{if(event.key==='Escape')setMenu(false);};addEventListener('keydown',close);return()=>removeEventListener('keydown',close);},[menu]);
  const accountMenu=useMenuKeys(menu,()=>setMenu(false));

  function go(next:Route,replace=false){
    setRoute(next);setMenu(false);setSheet(false);
    const url=new URL(location.href);url.search='';
    if(next.view!=='home')url.searchParams.set('view',next.view);
    if(next.view==='home'&&next.pane==='work')url.searchParams.set('pane','work');
    if(next.open)url.searchParams.set('open',next.open);
    history[replace?'replaceState':'pushState'](history.state,'',url);
  }
  /** Open an item where it belongs: Library items in the Library, work items beside the chat. */
  // Today's spend on the employee chip, for the owner; its details live on the employee's Usage tab.
  const usage=useEmployeeUsage(owner);
  function openUsage(){const employee=directory?.agents.find(agent=>agent.runtimeKey==='marketing');if(!employee)return;setMember({id:employee.id,tab:'usage'});go({view:'team',pane:route.pane,open:null});}
  function open(key:string,from:'auto'|'home'='auto'){
    const kind=key.split(':')[0];
    if(kind==='exp'){navigate('section:scorecard');return;}
    if(from==='auto'&&reads&&libraryKinds.includes(kind)&&route.view==='library')go({view:'library',pane:route.pane,open:key});
    else go({view:'home',pane:route.view==='home'?route.pane:talks?'chat':'work',open:key});
  }
  /** Anywhere chat can point: an item (beside the chat when there's room), a view, or a section of Work. */
  function navigate(target:string){
    const [kind,...rest]=target.split(':');const id=rest.join(':');
    if(target==='connect:site'){openSiteConnect();return;}
    if(kind==='view'){
      if(id==='work'||id==='chat')go({view:'home',pane:id==='chat'&&talks?'chat':'work',open:null});
      else if(id==='library'||id==='team'||id==='settings')go({view:id,pane:route.pane,open:null});
      return;
    }
    if(kind==='section'){
      const label=({calendar:'Content calendar',scorecard:'Scorecard',listening:'Listening',shifts:'Shift log',board:'Board',weekly:'This week'} as Record<string,string>)[id];
      if(sectionTab[id])openWorkTab(sectionTab[id]);
      go({view:'home',pane:'work',open:null});
      if(label)setTimeout(()=>document.querySelector(`section[aria-label="${label}"]`)?.scrollIntoView({behavior:'smooth',block:'start'}),250);
      return;
    }
    if(kind==='wiki'||kind==='page'){go({view:'home',pane:route.view==='home'?route.pane:'chat',open:target});return;}
    open(target,'home');
  }
  function chatWith(text:string,send=false){setPrefill({text,send});go({view:'home',pane:'chat',open:route.view==='home'?route.open:null});}
  function openInbox(item:InboxItem){
    if(item.kind==='brief'){if(owner)setOnboarding(true);else open('brief:profile','home');}
    else if(item.kind==='review'){setFocusReview({id:item.id.slice(7),key:Date.now()});open('campaign:current','home');}
    else if(item.target)(item.target.startsWith('pagecopy:')?open(item.target,'home'):navigate(item.target));
    else open(item.id.replace(/^(task|draft):/,'$1:'),'home');
  }
  function closeOnboarding(){setOnboarding(false);try{localStorage.setItem(onboardingKey,'yes');}catch{}}
  // The meeting starts from this morning's numbers: the brief is written (or refreshed) first and named, so chat reads it in full.
  const meeting=()=>{void (owner?api<{wikiId:string}>('/weekly/brief',{}).catch(()=>null):Promise.resolve(null)).then(doc=>chatWith(meetingPrompt+(doc?`

Start from this morning's brief (wiki:${doc.wikiId}): its KPIs, what worked, what didn't, and the push-or-pivot call. Say whether you agree with each call and what you'd do today.`:''),true));};

  useAttention(!!live&&talks,live?.drafts.length+':'+live?.tasks.length+':'+(shifts.view?.current?.cycles?.length??0)+':'+(shifts.view?.current?.status??'')+':'+route.open);
  const items=inboxItems(live);
  const inboxCount=talks?items.length:0;
  // Opt-in: a desktop notice for new decisions while the tab is in the background.
  const seenItems=useRef<Set<string>|null>(null);
  useEffect(()=>{
    if(!state)return;
    const ids=new Set(items.map(item=>item.id));
    const previous=seenItems.current;seenItems.current=ids;
    if(!previous||!document.hidden||typeof Notification==='undefined'||Notification.permission!=='granted')return;
    try{if(localStorage.getItem(notifyKey)!=='yes')return;}catch{return;}
    const fresh=items.filter(item=>!previous.has(item.id));
    if(fresh.length){const notice=new Notification(`${name} needs a decision`,{body:fresh.length===1?fresh[0].title:`${fresh.length} new items need a decision`,tag:'fe-inbox'});notice.onclick=()=>{window.focus();openInbox(fresh[0]);notice.close();};}
  },[!!state,items.map(item=>item.id).join('|')]);
  const viewLabel=route.view==='library'?'Library':route.view==='team'?'Team':route.view==='settings'?'Settings':route.pane==='work'?'Work':'Chat';
  useEffect(()=>{document.title=`${inboxCount?`(${inboxCount}) `:''}${viewLabel} · HireZero`;},[viewLabel,inboxCount]);

  const showCockpit=reads&&!!live&&route.view!=='settings';
  const cockpit=live&&<Cockpit state={live} status={status} owner={owner} canChat={!!canChat} onOpen={navigate} onChat={text=>chatWith(text)} shiftView={shifts.view}
    northStar={<NorthStarCard view={objectives.view} owner={owner} onOpen={()=>open('brief:objectives','home')}/>}
    shifts={<ShiftPanel view={shifts.view} owner={owner} onChanged={()=>{void shifts.reload();void refresh();void library.reload();}} onOpenReport={id=>go({view:'library',pane:route.pane,open:'wiki:'+id})}
      onOpenLog={()=>{go({view:'home',pane:'work',open:null});setTimeout(()=>document.querySelector('section[aria-label="Shift log"]')?.scrollIntoView({behavior:'smooth',block:'start'}),250);}}/>} onOpenItem={openInbox} onOpenTask={id=>open('task:'+id,'home')} onMeeting={meeting}
    onBoard={()=>go({view:'home',pane:'work',open:null})} onClose={()=>{if(sheet)setSheet(false);else setCockpitOpen(false);}}/>;

  const layoutActions=(split:boolean)=>route.view==='home'&&talks&&wide?(split
    ?<button type="button" className="fe-icon-button" aria-label="Expand to full width" title="Expand to full width" onClick={()=>go({...route,pane:'work'})}><Maximize2 size={15}/></button>
    :<button type="button" className="fe-icon-button" aria-label="Show beside chat" title="Show beside chat" onClick={()=>go({...route,pane:'chat'})}><Columns2 size={15}/></button>):null;
  const windowFor=(key:string,split:boolean)=>state&&directory&&<WorkWindow key={key} itemKey={key} state={state} library={library} objectives={objectives} directory={directory} status={status} perms={perms}
    signedInId={signedInId} customerAccount={Boolean(onSignOut)} readError={error} focusReview={focusReview} pastMeetingTaskIds={pastMeetingTaskIds} layoutActions={layoutActions(split)}
    onOpen={next=>next.startsWith('section:')||next.startsWith('view:')?navigate(next):open(next)} onClose={()=>{setNewFolder(undefined);go({...route,open:null});}} onChat={(text,send)=>chatWith(text,send)} onRefresh={refresh} onOnboard={()=>setOnboarding(true)}
    fileNewInto={newFolder}/>;

  let page:React.ReactNode=<div className="fe-loading"><p>{error?'The workspace couldn’t load. '+error:'Opening your workspace…'}</p></div>;
  if(state&&live&&directory){
    if(route.view==='library'&&reads)page=<LibraryView library={library} canEdit={reads&&hostOnline} online={hostOnline} openKey={route.open}
      reader={route.open&&windowFor(route.open,false)} onOpen={(key,folder)=>{setNewFolder(folder);go({view:'library',pane:route.pane,open:key});}}/>;
    else if(route.view==='team'&&reads)page=<TeamView state={state} directory={directory} status={status} owner={owner} canEditEmployees={talks&&hostOnline} hostOnline={hostOnline} accessLabel={roleLabel[access]}
      memberId={member.id} tab={member.tab} onOpen={(id,tab='brief')=>setMember({id,tab})} usage={usage} onDirectory={setDirectory} onRefresh={refresh} onOnboard={()=>setOnboarding(true)}/>;
    else if(route.view==='settings')page=<SettingsView owner={owner} canNotify={talks} accessLabel={roleLabel[access]} theme={theme} onTheme={setTheme} signedInName={signedInName}
      onTeam={()=>go({view:'team',pane:route.pane,open:null})} onSignOut={onSignOut?()=>void onSignOut():undefined} onNavigate={navigate} usage={usage} onUsage={openUsage}/>;
    else{
      const chat=talks&&<Conversation key={state.employee.sessionKey} state={live} canWrite={!!canChat} prefill={prefill?.text} autoSend={prefill?.send} onPrefillUsed={()=>setPrefill(undefined)} onRefresh={refresh}
        owner={owner} shifts={shifts.view} onNavigate={navigate}
        onOpenBrief={()=>owner?setOnboarding(true):open('brief:profile','home')}
        introExtra={owner?<GettingStarted state={live} onRefresh={refresh} onOpen={navigate} goalsSet={hasGoals(objectives.view?.revision.content)} onGoals={()=>open('brief:objectives','home')} onBrief={()=>setOnboarding(true)} onMeeting={meeting} onPage={()=>go({view:'library',pane:route.pane,open:null})} onInvite={()=>go({view:'team',pane:route.pane,open:null})}/>:undefined}/>;
      const work=reads?<WorkView state={live} pastMeetingTasks={pastTasks} canWrite={!!canWrite} owner={owner} shifts={shifts.view} onOpen={navigate} onRefresh={refresh}/>
        :<div className="fe-view"><div className="fe-view-inner"><header className="fe-view-head"><div><h1>Shared campaigns</h1><p>{access==='viewer'?'Campaigns the owner has shared with you to read.':'Campaigns the owner has shared with you for review.'}</p></div></header>
          <CampaignSharedWorkspace deviceId={signedInId} customerAccount={Boolean(onSignOut)} readOnly={access==='viewer'}/></div></div>;
      const split=route.pane==='chat'&&!!route.open&&talks&&wide;
      page=route.pane==='chat'&&talks?(route.open?(split?<div className="fe-split-pane"><div className="fe-split-chat">{chat}</div>{windowFor(route.open,true)}</div>:windowFor(route.open,false)):chat)
        :route.open?windowFor(route.open,false):<div className="fe-view"><div className="fe-view-inner wide">{work}</div></div>;
    }
  }

  const pins=library.meta.pins.map(key=>library.items.find(item=>item.key===key)).filter(item=>item&&!item.archived);
  const railButton=(label:string,Icon:typeof Search,active:boolean,onClick:()=>void,count?:number)=><button type="button" className="fe-rail-button" aria-label={count?`${label}, ${count} ${count===1?'needs':'need'} a decision`:label} data-tip={label} aria-current={active?'page':undefined} onClick={onClick}>
    <Icon size={19}/>{!!count&&<span className="fe-rail-badge">{count}</span>}<span className="fe-rail-caption">{label}</span></button>;
  const themeIcon=theme==='dark'?Moon:theme==='light'?Sun:Monitor;

  return <MeContext.Provider value={me}><CampaignsProvider value={campaigns}><ExperienceProvider value={experience}><div className={'fe-app'+(showCockpit&&cockpitOpen&&roomy?' with-cockpit':'')+(railWide?' rail-wide':'')} style={{['--fe-cockpit-w' as string]:cockpitWidth+'px'}}>
    <a className="fe-skip" href="#fe-content" onClick={event=>{event.preventDefault();document.getElementById('fe-content')?.focus();}}>Skip to content</a>
    <aside className="fe-rail" aria-label="Main navigation">
      <BrandMark className="fe-rail-mark"/>
      {state&&<nav className="fe-rail-nav" aria-label="Main views">
        {railButton(talks?'Chat':'Work',MessageSquareText,route.view==='home',()=>go({view:'home',pane:talks?route.view==='home'?route.pane:'chat':'work',open:null}),inboxCount&&!(showCockpit&&cockpitOpen&&roomy)?inboxCount:undefined)}
        {reads&&railButton('Search',Search,false,()=>setPalette(true))}
        {reads&&railButton('Library',BookOpen,route.view==='library',()=>go({view:'library',pane:route.pane,open:null}))}
        {reads&&railButton('Team',Users,route.view==='team',()=>{setMember({id:null,tab:'brief'});go({view:'team',pane:route.pane,open:null});})}
      </nav>}
      {pins.length>0&&<nav className="fe-rail-pins" aria-label="Pinned">{railWide&&<p className="fe-rail-heading">Pinned</p>}{pins.slice(0,railWide?16:8).map(item=>item&&<button type="button" key={item.key} className="fe-rail-pin" aria-label={item.title} data-tip={item.title} aria-current={route.open===item.key?'page':undefined}
        onClick={()=>go({view:'library',pane:route.pane,open:item.key})}><span className="fe-rail-pin-mark" aria-hidden="true">{initials(item.title)}</span><span className="fe-rail-pin-title">{item.title}</span></button>)}</nav>}
      <div className="fe-rail-foot">
        <button type="button" className="fe-rail-button fe-rail-expand" aria-label={railWide?'Collapse sidebar':'Expand sidebar'} data-tip={railWide?'Collapse sidebar':'Expand sidebar'} aria-expanded={railWide} onClick={toggleRail}>{railWide?<PanelLeftClose size={18}/>:<PanelLeftOpen size={18}/>}<span className="fe-rail-caption">Collapse</span></button>
        <button type="button" className="fe-rail-button" aria-label="Settings and account" data-tip="Settings" aria-haspopup="menu" aria-expanded={menu} aria-current={route.view==='settings'?'page':undefined} onClick={()=>setMenu(!menu)}><Menu size={19}/><span className="fe-rail-caption">{railWide?'Settings and account':'Menu'}</span></button>
        {menu&&<><button type="button" className="fe-menu-scrim" aria-label="Close menu" onClick={()=>setMenu(false)}/><div className="fe-menu fe-account-menu" role="menu" aria-label="Settings and account" ref={accountMenu.ref} onKeyDown={accountMenu.onKeyDown}>
          <div className="fe-menu-account"><span className="fe-avatar small">{initials(signedInName)}</span><span><strong>{signedInName}</strong><small>{roleLabel[access]} · {status.label}</small></span></div>
          <button type="button" role="menuitem" onClick={()=>go({view:'settings',pane:route.pane,open:null})}><Settings size={15}/> Settings</button>
          <button type="button" role="menuitem" onClick={()=>setTheme(theme==='system'?'dark':theme==='dark'?'light':'system')}>{(()=>{const Icon=themeIcon;return <Icon size={15}/>;})()} Theme: {theme==='system'?'System':theme==='dark'?'Dark':'Light'}</button>
          <button type="button" role="menuitem" onClick={()=>{setMenu(false);setShortcuts(true);}}><Keyboard size={15}/> Keyboard shortcuts</button>
          {onSignOut&&<button type="button" role="menuitem" onClick={()=>void onSignOut()}><LogOut size={15}/> Sign out</button>}
        </div></>}
      </div>
    </aside>
    <main className="fe-main" id="fe-content" tabIndex={-1} aria-label={viewLabel}>
      {(route.view==='home'||route.view==='library'&&route.open)&&<h1 className="marketing-sr-only">{viewLabel}</h1>}
      <header className="fe-topbar">
        {route.view==='home'?<nav className="fe-tabs fe-mode" aria-label="Chat or work">
          {talks&&<button type="button" aria-pressed={route.pane==='chat'} onClick={()=>go({...route,pane:'chat'})}>Chat</button>}
          <button type="button" aria-pressed={route.pane==='work'} onClick={()=>go({...route,pane:'work'})}>Work</button>
          {route.open&&<span className="fe-mode-open">{state?itemTitle(route.open,state,library,directory):''}</span>}
        </nav>:<strong className="fe-topbar-title">{viewLabel}</strong>}
        <span className="fe-topbar-spacer"/>
        <span className="fe-status-wrap" tabIndex={owner?0:undefined}><span className={'fe-status-chip '+status.tone} title={owner?undefined:name+': '+status.label}><i className={'fe-dot '+status.tone}/>{talks?`${name} · ${status.label}`:status.label}{owner&&usage&&<span className="fe-status-tokens">{usage.today.toLocaleString()} today</span>}</span>
          {owner&&<UsageHoverCard summary={usage} onOpen={openUsage}/>}</span>
        {showCockpit&&!(cockpitOpen&&roomy)&&<button type="button" className="fe-cockpit-toggle" aria-label={`Show cockpit${inboxCount?`, ${inboxCount} ${inboxCount===1?'needs':'need'} a decision`:''}`} onClick={()=>{if(roomy)setCockpitOpen(true);else setSheet(true);}}><PanelRightOpen size={16}/>{inboxCount>0&&<span className="fe-count attn">{inboxCount}</span>}</button>}
      </header>
      {(error&&state||!hostOnline)&&<div className="fe-banner" role="alert"><span>{!hostOnline?'The host is offline. Changes are paused until it reconnects.':'Couldn’t refresh: '+error+' Showing the last saved view.'}</span><button type="button" onClick={()=>void refresh()}>Retry</button></div>}
      <div className="fe-main-body"><ViewBoundary view={route.view+route.pane}>{page}</ViewBoundary></div>
    </main>
    {showCockpit&&cockpitOpen&&roomy&&<div className="fe-cockpit-dock">
      <div className="fe-cockpit-resize" role="separator" aria-orientation="vertical" aria-label="Resize cockpit" aria-valuemin={280} aria-valuemax={720} aria-valuenow={cockpitWidth} tabIndex={0}
        title="Drag to resize · double-click to reset"
        onPointerDown={event=>{if(event.button!==0)return;event.preventDefault();drag.current={x:event.clientX,width:cockpitWidth};event.currentTarget.setPointerCapture(event.pointerId);document.body.classList.add('fe-resizing');}}
        onPointerMove={event=>{if(drag.current)resizeCockpit(drag.current.width+drag.current.x-event.clientX);}}
        onPointerUp={event=>{drag.current=null;document.body.classList.remove('fe-resizing');if(event.currentTarget.hasPointerCapture(event.pointerId))event.currentTarget.releasePointerCapture(event.pointerId);}}
        onDoubleClick={()=>resizeCockpit(320)}
        onKeyDown={event=>{const next=event.key==='ArrowLeft'?cockpitWidth+24:event.key==='ArrowRight'?cockpitWidth-24:event.key==='Home'?280:event.key==='End'?720:null;if(next!==null){event.preventDefault();resizeCockpit(next);}}}/>
      {cockpit}</div>}
    {sheet&&showCockpit&&<div className="fe-sheet" role="dialog" aria-label="Cockpit"><button type="button" className="fe-sheet-scrim" aria-label="Close cockpit" onClick={()=>setSheet(false)}/>{cockpit}</div>}
    {owner&&<SiteConnectHost/>}
    {palette&&reads&&<CommandPalette state={live} library={library} onClose={()=>setPalette(false)} onOpen={key=>{const kind=key.split(':')[0];if(libraryKinds.includes(kind))go({view:'library',pane:route.pane,open:key});else open(key,'home');}} onAsk={talks?text=>chatWith(text):undefined}/>}
    {shortcuts&&<dialog open className="fe-dialog fe-shortcuts" aria-label="Keyboard shortcuts"><header><h2>Keyboard shortcuts</h2><button type="button" className="fe-icon-button" aria-label="Close" onClick={()=>setShortcuts(false)}>×</button></header>
      <dl><div><dt><kbd>Ctrl</kbd> <kbd>K</kbd></dt><dd>Search the Library and tasks</dd></div><div><dt><kbd>Enter</kbd></dt><dd>Send a message</dd></div><div><dt><kbd>Shift</kbd> <kbd>Enter</kbd></dt><dd>New line in a message</dd></div><div><dt><kbd>Esc</kbd></dt><dd>Close a dialog or menu</dd></div></dl></dialog>}
    {onboarding&&state&&owner&&<Onboarding state={state} canWrite={!!canChat} onClose={closeOnboarding} onRefresh={refresh} onOpen={key=>{closeOnboarding();navigate(key);}}/>}
  </div></ExperienceProvider></CampaignsProvider></MeContext.Provider>;
}
