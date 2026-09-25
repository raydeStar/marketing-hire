import {useState,type ReactNode} from 'react';
import {BookOpen,FileText,FolderInput,Image as ImageIcon,LayoutTemplate,Link2,ListChecks,Megaphone,NotebookPen,Pin,PinOff,ShieldCheck,Table2,UserRound,X,type LucideIcon} from 'lucide-react';
import {BriefEditor} from './BriefEditor';
import {CampaignSharedWorkspace} from '../components/CampaignSharedWorkspace';
import {MarketingRunwayPanel,campaignTitle} from '../components/MarketingRunwayPanel';
import type {MarketingState} from '../components/MarketingPanels';
import {EmployeeProfile} from './Employee';
import {DraftCard} from './InboxView';
import {DeliverableView,MediaView,SourceView,WikiDoc,isImage} from './LibraryDocs';
import {folderTree,kindLabel,leafOf,type Library,type LibraryItem} from './library';
import {createMockups,PageDetail} from './Pages';
import {TaskDetail} from './TasksView';
import {ObjectivesEditor} from './ObjectivesEditor';
import type {ObjectivesView} from './objectives';
import {wikiTemplates} from './wikiTemplates';
import {Dialog,type Directory,type EmployeeStatus} from './shared';

export type Perms={owner:boolean;reads:boolean;talks:boolean;viewer:boolean;canWrite:boolean;canChat:boolean;canDecide:boolean;hostOnline:boolean};
const icons:Record<string,LucideIcon>={task:ListChecks,campaign:Megaphone,draft:ShieldCheck,brief:NotebookPen,wiki:BookOpen,page:LayoutTemplate,tool:Table2,media:ImageIcon,source:Link2,deliverable:FileText,employee:UserRound};

/** Where an item is filed and how it's tagged. Folders can be created on the spot. */
export function FileDialog({item,library,onClose}:{item:LibraryItem;library:Library;onClose:()=>void}){
  const folders=folderTree(library.meta,library.items);
  const [folder,setFolder]=useState(item.folder),[fresh,setFresh]=useState(''),[tags,setTags]=useState(item.tags.join(', '));
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  async function save(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    const target=fresh.trim()?(folder?folder+'/':'')+fresh.trim().replaceAll('/','-'):folder;
    try{await library.file(item.key,target,tags.split(',').map(tag=>tag.trim()).filter(Boolean));onClose();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <Dialog title={'File “'+item.title+'”'} onClose={onClose}><form className="fe-form" onSubmit={event=>void save(event)}>
    <label>Folder<select value={folder} onChange={event=>setFolder(event.target.value)}>{folders.map(path=><option key={path} value={path}>{'   '.repeat(path.split('/').length-1)+leafOf(path)}</option>)}</select></label>
    <label>New sub-folder (optional)<input value={fresh} maxLength={60} onChange={event=>setFresh(event.target.value)} placeholder={`Inside ${folder}`}/></label>
    <label>Tags<input value={tags} maxLength={400} onChange={event=>setTags(event.target.value)} placeholder="Comma separated, e.g. launch, q3, pricing"/></label>
    <small>Folders and tags are shared with everyone who can read the Library.</small>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy}>{busy?'Saving…':'Save'}</button></footer>
  </form></Dialog>;
}

export function itemTitle(key:string,state:MarketingState,library:Library,directory:Directory|null){
  const [kind,...rest]=key.split(':');const id=rest.join(':');
  const found=library.items.find(item=>item.key===key);
  if(found)return found.title;
  if(kind==='task')return state.tasks.find(task=>task.id===id)?.title||'Task';
  if(kind==='campaign')return state.runway?campaignTitle(state.runway.project.goal):'Campaign';
  if(kind==='draft'){const draft=state.drafts.find(item=>String(item.id)===id);return draft?`${draft.channel} draft`:'Draft';}
  if(kind==='employee')return directory?.agents.find(item=>item.id===id)?.name||'AI employee';
  if(kind==='wiki'&&id.startsWith('new'))return 'New document';
  return 'Item';
}

export function WorkWindow({itemKey,state,library,objectives,directory,status,perms,signedInId,customerAccount,readError,focusReview,pastMeetingTaskIds,layoutActions,fileNewInto,onOpen,onClose,onChat,onRefresh,onOnboard}:{
  itemKey:string;state:MarketingState;library:Library;objectives:{view:ObjectivesView|null;setView:(view:ObjectivesView)=>void};directory:Directory;status:EmployeeStatus;perms:Perms;signedInId:string;customerAccount:boolean;readError:string;
  focusReview?:{id:string;key:number};pastMeetingTaskIds:Set<string>;layoutActions?:ReactNode;fileNewInto?:string;
  onOpen:(key:string)=>void;onClose:()=>void;onChat:(text:string)=>void;onRefresh:()=>Promise<void>;onOnboard:()=>void;
}){
  const [filing,setFiling]=useState(false),[error,setError]=useState('');
  const [employeeTab,setEmployeeTab]=useState<'files'|'permissions'|'brief'>('files');
  const [kind,...rest]=itemKey.split(':');const id=rest.join(':');
  const item=library.items.find(entry=>entry.key===itemKey);
  const pinned=library.meta.pins.includes(itemKey);
  const Icon=icons[item?.kind||kind]||FileText;
  const title=itemTitle(itemKey,state,library,directory);
  const subtitle=item?`${item.label} · ${item.folder.replaceAll('/',' / ')}`:kind==='task'?'Task':kind==='campaign'?'Campaign':kind==='draft'?'Waiting for approval':kind==='employee'?'AI employee':kindLabel[kind as keyof typeof kindLabel]||'';
  async function pin(){try{await library.pin(pinned?library.meta.pins.filter(key=>key!==itemKey):[itemKey,...library.meta.pins].slice(0,24));}catch(cause){setError((cause as Error).message);}}

  let body:ReactNode=<p className="fe-muted">This item is no longer available. It may have been removed.</p>;
  if(kind==='task'){const task=state.tasks.find(entry=>entry.id===id);if(task)body=<TaskDetail task={task} state={state} canWrite={perms.canWrite} canChat={perms.canChat} pastMeeting={pastMeetingTaskIds.has(task.id)} onRefresh={onRefresh}/>;}
  else if(kind==='campaign')body=perms.owner?<MarketingRunwayPanel runway={state.runway} profile={state.profile} evidenceEnabled={state.businessBriefEvidenceEnabled===true}
      canControl={perms.hostOnline} canContribute={perms.hostOnline&&!readError} liveWorkEnabled={state.runwayLiveEnabled===true}
      archiveEnabled={state.runwayArchiveEnabled===true} campaignBriefEnabled={state.campaignBriefEnabled===true}
      fixtureCampaignEnabled={state.fixtureCampaignEnabled===true} deferredRevisionEnabled={state.deferredRevisionEnabled===true}
      nativeSharedEnabled={state.sharedGatewayEnabled===true} onRefresh={onRefresh} onOpenBrief={()=>onOpen('brief:profile')} key={focusReview?.key}/>
    :<CampaignSharedWorkspace deviceId={signedInId} customerAccount={customerAccount} readOnly={perms.viewer}/>;
  else if(kind==='draft'){const draft=state.drafts.find(entry=>String(entry.id)===id);if(draft)body=<DraftCard draft={draft} canDecide={perms.canDecide} onRefresh={onRefresh}/>;}
  else if(kind==='brief'&&id==='objectives')body=<ObjectivesEditor view={objectives.view} canEdit={perms.talks&&perms.hostOnline} onSaved={next=>objectives.setView(next)}/>;
  else if(kind==='brief')body=<div className="fe-brief-page"><BriefEditor profile={state.profile} evidenceEnabled={state.businessBriefEvidenceEnabled===true} canEdit={perms.talks&&perms.hostOnline} onSaved={()=>void onRefresh()}/>
    {perms.owner&&<button type="button" className="fe-ghost" onClick={onOnboard}>Rebuild the brief from your website or a conversation</button>}</div>;
  else if(kind==='wiki'){
    if(id.startsWith('new'))body=<WikiDoc key={id} template={wikiTemplates.find(template=>'new:'+template.title===id)||null} directory={directory} canEdit={perms.reads&&perms.hostOnline}
      onSaved={page=>{void library.reload().then(async()=>{if(fileNewInto&&fileNewInto!=='Company')await library.file('wiki:'+page.id,fileNewInto,[]).catch(()=>{});onOpen('wiki:'+page.id);});}} onCancel={onClose}/>;
    else{const page=library.wiki.find(entry=>entry.id===id);if(page)body=<WikiDoc key={page.id+page.version} page={page} directory={directory} canEdit={perms.reads&&perms.hostOnline} onSaved={()=>void library.reload()}/>;}
  }
  else if(kind==='page'){if(library.apps.some(app=>app.id===id))body=<PageDetail key={id} id={id} online={perms.hostOnline} canEdit={perms.reads} canPublish={perms.talks} canAsk={perms.talks}
      published={library.published.find(entry=>entry.artifactId===id)} images={library.uploads.filter(file=>!file.archived&&isImage(file))} onDiscuss={onChat} onChanged={()=>void library.reload()}/>;}
  else if(kind==='media'){const file=library.uploads.find(entry=>entry.id===id);if(file)body=<MediaView file={file} canEdit={perms.reads&&perms.hostOnline} onChanged={()=>void library.reload()}/>;}
  else if(kind==='source'){const source=state.evidence?.find(entry=>entry.id===id);if(source)body=<SourceView source={source} state={state} onOpenTask={taskId=>onOpen('task:'+taskId)}/>;}
  else if(kind==='deliverable'){const artifact=state.runway?.artifacts.find(entry=>entry.id===id);if(artifact)body=<DeliverableView artifact={artifact} canMakeMockups={perms.reads&&perms.hostOnline}
      onOpenCampaign={()=>onOpen('campaign:current')} onMakeMockups={()=>void createMockups(`${campaignTitle(state.runway?.project.goal||'Campaign')} · post mockups`,artifact.content).then(async page=>{await library.reload();onOpen('page:'+page);}).catch(cause=>setError((cause as Error).message))}/>;}
  else if(kind==='employee'){const member=directory.agents.find(entry=>entry.id===id);if(member)body=<EmployeeProfile member={member} state={state} status={status} canEdit={perms.talks&&perms.hostOnline} tab={employeeTab} onTab={setEmployeeTab} onRefresh={onRefresh} onOnboard={perms.owner?onOnboard:undefined}/>;}

  return <section className="fe-window" aria-label={title}>
    <header className="fe-window-head">
      <span className="fe-window-icon"><Icon size={16}/></span>
      <div className="fe-window-title"><strong>{title}</strong>{subtitle&&<small>{subtitle}</small>}</div>
      {item&&perms.reads&&<button type="button" className="fe-icon-button" aria-label={pinned?'Unpin from sidebar':'Pin to sidebar'} title={pinned?'Unpin from sidebar':'Pin to sidebar'} onClick={()=>void pin()}>{pinned?<PinOff size={16}/>:<Pin size={16}/>}</button>}
      {item&&perms.reads&&perms.hostOnline&&<button type="button" className="fe-icon-button" aria-label="Folder and tags" title="Folder and tags" onClick={()=>setFiling(true)}><FolderInput size={16}/></button>}
      {layoutActions}
      <button type="button" className="fe-icon-button" aria-label="Close" title="Close" onClick={onClose}><X size={17}/></button>
    </header>
    {item&&item.tags.length>0&&<div className="fe-window-tags">{item.tags.map(tag=><span className="fe-tag" key={tag}>{tag}</span>)}</div>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <div className="fe-window-body">{body}</div>
    {filing&&item&&<FileDialog item={item} library={library} onClose={()=>setFiling(false)}/>}
  </section>;
}
