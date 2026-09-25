import {useCallback,useEffect,useRef,useState} from 'react';
import {ArrowLeft,Code2,Download,ExternalLink,FileText,Film,Globe,Image as ImageIcon,LayoutTemplate,MessageCircle,Plus,RotateCcw,Table2,Trash2,Upload} from 'lucide-react';
import {PublishDialog,type Published} from './PublishPage';
import {api,uploadFile} from '../api';
import {ArtifactPreview} from '../components/ArtifactPreview';
import {readableTime,type MarketingState} from '../components/MarketingPanels';
import type {AppSummary,ArtifactApp,State,UploadFile} from '../types';
import {pageTemplates,socialMockupDefinition,type PageTemplate} from './pageTemplates';
import {campaignTitle} from '../components/MarketingRunwayPanel';
import {Dialog,Empty,PageHead,plain} from './shared';

type Filter='all'|'pages'|'media'|'drafts';
const newId=()=>crypto.randomUUID().replaceAll('-','');
const kindLabel:Record<string,string>={audience_note:'Audience & problem note',post_angles:'Draft post angles',review_packet:'Review packet',revision_angles:'Revised post angles'};

function isVideo(file:UploadFile){return file.mediaType.startsWith('video/');}
function isImage(file:UploadFile){return file.mediaType.startsWith('image/');}
function size(bytes:number){return bytes>1048576?(bytes/1048576).toFixed(1)+' MB':Math.max(1,Math.round(bytes/1024))+' KB';}
function pageText(app:ArtifactApp){
  const html=app.definition.page?.html||'';
  const text=new DOMParser().parseFromString(html,'text/html').body.textContent||'';
  return text.replace(/\s+/g,' ').trim();
}

function NewPage({onCreate,onClose}:{onCreate:(template:PageTemplate,title:string)=>Promise<void>;onClose:()=>void}){
  const [chosen,setChosen]=useState<PageTemplate|null>(null),[title,setTitle]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const groups=['Campaign pages','Working tools'] as const;
  if(chosen)return <Dialog title={'New '+chosen.label.toLowerCase()} onClose={onClose}><form className="fe-form" onSubmit={async event=>{event.preventDefault();if(!title.trim()||busy)return;setBusy(true);setError('');try{await onCreate(chosen,title.trim());}catch(cause){setError((cause as Error).message);setBusy(false);}}}>
    <label>Name<input autoFocus required maxLength={80} value={title} onChange={event=>setTitle(event.target.value)} placeholder={chosen.group==='Working tools'?chosen.label:'e.g. Spring launch landing page'}/></label>
    <p className="fe-muted">{chosen.summary}. You can edit everything after it’s created.</p>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={()=>setChosen(null)}>Back</button><button className="primary" disabled={busy||!title.trim()}>{busy?'Creating…':'Create'}</button></footer>
  </form></Dialog>;
  return <Dialog title="Create" onClose={onClose}>{groups.map(group=><section key={group} className="fe-inbox-group"><h3>{group}</h3><div className="fe-row-list">{pageTemplates.filter(item=>item.group===group).map(item=>
    <button type="button" className="fe-row" key={item.id} onClick={()=>{setChosen(item);setTitle(item.group==='Working tools'?item.label:'');}}><span className="fe-row-icon accent">{item.group==='Working tools'?<Table2 size={18}/>:<LayoutTemplate size={18}/>}</span><span className="fe-row-body"><strong>{item.label}</strong><small>{item.summary}</small></span></button>)}</div></section>)}</Dialog>;
}

function PageDetail({id,online,published,images,onBack,onDiscuss,onChanged}:{id:string;online:boolean;published?:Published;images:UploadFile[];onBack:()=>void;onDiscuss:(text:string)=>void;onChanged:()=>void}){
  const [publishing,setPublishing]=useState(false),[picking,setPicking]=useState(false);
  const htmlField=useRef<HTMLTextAreaElement>(null);
  // Pages refer to uploads as media:<id>; the host inlines them when the page is shown or published.
  function insertImage(file:UploadFile){
    const tag=`<img src="media:${file.id}" alt="${file.name.replace(/\.[a-z0-9]+$/i,'').replace(/["<>&]/g,'')}">`;
    const field=htmlField.current,start=field?.selectionStart??code.html.length,end=field?.selectionEnd??code.html.length;
    setCode(current=>({...current,html:current.html.slice(0,start)+tag+current.html.slice(end)}));setPicking(false);
  }
  const [app,setApp]=useState<ArtifactApp|null>(null),[tab,setTab]=useState<'preview'|'code'|'history'>('preview');
  const [code,setCode]=useState({html:'',css:'',javaScript:''}),[meta,setMeta]=useState({title:'',description:''});
  const [history,setHistory]=useState<{id:string;description:string;at:string;version:string;title:string}[]>([]);
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  const operation=useRef<{signature:string;id:string}|null>(null);
  const load=useCallback(async()=>{try{const next=await api<ArtifactApp>('/artifacts/'+id);setApp(next);setCode(next.definition.page||{html:'',css:'',javaScript:''});setMeta({title:next.definition.title,description:next.definition.description});}catch(cause){setError((cause as Error).message);}},[id]);
  useEffect(()=>{void load();},[load]);
  useEffect(()=>{if(tab==='history')void api<typeof history>('/artifacts/'+id+'/history').then(setHistory).catch(cause=>setError((cause as Error).message));},[tab,app?.version]);
  function opId(signature:string){if(operation.current?.signature!==signature)operation.current={signature,id:newId()};return operation.current.id;}
  async function edit(change:Record<string,unknown>,done:string){
    if(!app||busy)return;setBusy(true);setError('');setNotice('');
    const body={version:app.version,...change};
    try{const saved=await api<ArtifactApp>('/artifacts/'+id,{operationId:opId(JSON.stringify(body)),...body},'PUT');operation.current=null;setApp(saved);setNotice(done);onChanged();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(!app)return <div className="fe-page-inner">{error?<p className="fe-alert" role="alert">{error}</p>:<p className="fe-muted">Opening…</p>}</div>;
  const dirty=JSON.stringify(code)!==JSON.stringify(app.definition.page||{html:'',css:'',javaScript:''})||meta.title!==app.definition.title||meta.description!==app.definition.description;
  const chars=code.html.length+code.css.length+code.javaScript.length;
  return <div className="fe-page-inner fe-page-detail">
    <button type="button" className="fe-ghost fe-back" onClick={()=>{if(!dirty||window.confirm('Leave without saving your code changes?'))onBack();}}><ArrowLeft size={16}/> Assets</button>
    <header className="fe-page-head"><div><h1>{app.definition.title}</h1>{published&&<div className="fe-reader-meta"><span className={'fe-pill '+(published.artifactVersion===app.version?'ok':'attn')}><Globe size={12}/> {published.artifactVersion===app.version?'Published':'Changed since publishing'}</span></div>}<p>{app.archived?'In Trash. Restore it to use it again.':app.definition.description||'Page'} · updated {readableTime(app.updated)}</p></div>
      <div className="fe-page-actions">
        {app.definition.page&&<button type="button" className="primary" onClick={()=>setPublishing(true)}><Globe size={15}/> {published?'Publishing':'Publish'}</button>}
        {app.definition.page&&<a className="fe-button" href={'/api/artifacts/'+app.id+'/page'} target="_blank" rel="noopener"><ExternalLink size={15}/> Preview in a tab</a>}
        <button type="button" onClick={()=>onDiscuss(`Take a look at our page "${app.definition.title}" and suggest improvements to the headline, structure and call to action. Here is its text:\n\n${pageText(app).slice(0,3000)}`)}><MessageCircle size={15}/> Ask Marketing</button>
        {app.archived?<button type="button" disabled={busy||!online} onClick={()=>void edit({archived:false},'Restored.')}><RotateCcw size={15}/> Restore</button>
          :<button type="button" className="fe-ghost" disabled={busy||!online} onClick={()=>{if(window.confirm('Move this page to Trash? You can restore it later.'))void edit({archived:true},'Moved to Trash.');}}><Trash2 size={15}/> Trash</button>}
      </div></header>
    <nav className="fe-segmented" aria-label="Page views"><button type="button" aria-pressed={tab==='preview'} onClick={()=>setTab('preview')}>Preview</button><button type="button" aria-pressed={tab==='code'} onClick={()=>setTab('code')}><Code2 size={14}/> Edit</button><button type="button" aria-pressed={tab==='history'} onClick={()=>setTab('history')}>History</button></nav>
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {publishing&&<PublishDialog app={app} published={published} online={online} onClose={()=>setPublishing(false)} onChanged={onChanged}/>}
    {tab==='preview'&&(app.definition.page?<div className="fe-page-frame"><ArtifactPreview app={app} online={online&&!app.archived} onSaved={setApp}/></div>:<Empty icon={<FileText size={30}/>} title="This app has no page">It stores records only. Add HTML in Edit to give it one.</Empty>)}
    {tab==='code'&&<form className="fe-card fe-form" onSubmit={event=>{event.preventDefault();void edit({definition:{...app.definition,title:meta.title.trim(),description:meta.description.trim(),page:code}},'Saved. The preview now shows your changes.');}}>
      <div className="fe-form-row"><label>Name<input required maxLength={80} value={meta.title} onChange={event=>setMeta({...meta,title:event.target.value})}/></label><label>Description<input maxLength={400} value={meta.description} onChange={event=>setMeta({...meta,description:event.target.value})}/></label></div>
      <div className="fe-editor-tools"><button type="button" className="fe-ghost" onClick={()=>setPicking(true)}><ImageIcon size={15}/> Insert image</button></div>
      <label>HTML<textarea ref={htmlField} className="fe-editor" rows={14} value={code.html} onChange={event=>setCode({...code,html:event.target.value})} spellCheck={false}/></label>
      <label>CSS<textarea className="fe-editor short" rows={8} value={code.css} onChange={event=>setCode({...code,css:event.target.value})} spellCheck={false}/></label>
      <label>JavaScript<textarea className="fe-editor short" rows={6} value={code.javaScript} onChange={event=>setCode({...code,javaScript:event.target.value})} spellCheck={false}/></label>
      <small>{chars.toLocaleString()} / 40,000 characters. Pages run sandboxed with no network access; use Insert image for your uploaded images.</small>
      {picking&&<Dialog title="Insert an image" onClose={()=>setPicking(false)}>{images.length?<div className="fe-grid fe-image-picker">{images.map(file=><button type="button" className="fe-tile" key={file.id} onClick={()=>insertImage(file)}><div className="fe-tile-art"><img src={'/api/uploads/'+file.id+'/content'} alt=""/></div><div className="fe-tile-body"><strong>{file.name}</strong><small>{size(file.bytes)}</small></div></button>)}</div>:<Empty icon={<ImageIcon size={30}/>} title="No images yet">Upload images from Assets → Upload media, then insert them here.</Empty>}</Dialog>}
      <footer><button type="button" className="fe-ghost" disabled={!dirty} onClick={()=>{setCode(app.definition.page||{html:'',css:'',javaScript:''});setMeta({title:app.definition.title,description:app.definition.description});}}>Discard</button><button className="primary" disabled={busy||!dirty||!online||chars>40000||!code.html.trim()}>{busy?'Saving…':'Save changes'}</button></footer>
    </form>}
    {tab==='history'&&<div className="fe-card"><div className="fe-row-list">{history.map((item,index)=><div className="fe-history-row" key={item.id}><span><strong>{item.description}</strong><small>{readableTime(item.at)}{index===0?' · current':''}</small></span>{index>0&&<button type="button" disabled={busy||!online} onClick={()=>void api<ArtifactApp>('/artifacts/'+id+'/restore',{operationId:newId(),version:app.version,targetVersion:item.version}).then(saved=>{setApp(saved);setCode(saved.definition.page||{html:'',css:'',javaScript:''});setNotice('Restored that version.');onChanged();}).catch(cause=>setError((cause as Error).message))}>Restore</button>}</div>)}</div></div>}
  </div>;
}

export function AssetsView({state,online,initialOpen=null,onDiscuss,onOpenCampaigns}:{state:MarketingState;online:boolean;initialOpen?:string|null;onDiscuss:(text:string)=>void;onOpenCampaigns:()=>void}){
  const [filter,setFilter]=useState<Filter>('all'),[trash,setTrash]=useState(false);
  const [apps,setApps]=useState<AppSummary[]|null>(null),[uploads,setUploads]=useState<UploadFile[]>([]);
  const [openId,setOpenId]=useState<string|null>(initialOpen),[creating,setCreating]=useState(false),[viewing,setViewing]=useState<UploadFile|null>(null);
  const [uploading,setUploading]=useState(0),[error,setError]=useState('');
  const [tools,setTools]=useState<Record<string,boolean>>({}),[published,setPublished]=useState<Published[]>([]);
  const picker=useRef<HTMLInputElement>(null);
  const load=useCallback(async()=>{try{
    const legacy=await api<State>('/state');setApps(legacy.artifacts||[]);setUploads(legacy.uploads||[]);setError('');
    void api<Published[]>('/published-pages').then(setPublished).catch(()=>setPublished([]));
    // Record-keeping tools draw their table only once connected, so their tiles use an icon instead of a live preview.
    setTools(Object.fromEntries((legacy.artifacts||[]).map(app=>[app.id,(app.fieldCount??1)>1||app.hasPage===false])));
  }catch(cause){setError((cause as Error).message);}},[]);
  useEffect(()=>{void load();},[load]);
  async function create(template:PageTemplate,title:string){
    const id=newId();
    await api<ArtifactApp>('/artifacts/'+id,{operationId:newId(),version:'absent',definition:template.definition(title),upserts:[],deleteIds:[]},'PUT');
    setCreating(false);await load();setOpenId(id);
  }
  // Marketing's saved angles become a reviewable mockup page in one step.
  async function makeMockups(artifact:{id:string;kind:string;content:string}){
    setError('');
    try{
      const data=JSON.parse(artifact.content) as {angles?:{title:string;hook:string;why:string;claimLimit?:string}[]};
      if(!data.angles?.length)throw new Error('These drafts have no post angles to lay out.');
      const id=newId(),title=`${campaignTitle(state.runway?.project.goal||'Campaign')} · post mockups`.slice(0,80);
      await api<ArtifactApp>('/artifacts/'+id,{operationId:newId(),version:'absent',definition:socialMockupDefinition(title,data.angles),upserts:[],deleteIds:[]},'PUT');
      await load();setOpenId(id);
    }catch(cause){setError((cause as Error).message);}
  }
  async function upload(files:FileList|null){
    if(!files?.length)return;setError('');
    for(const file of Array.from(files)){
      setUploading(count=>count+1);
      try{await uploadFile(file);}catch(cause){setError(`${file.name}: ${(cause as Error).message}`);}finally{setUploading(count=>count-1);}
    }
    await load();
  }
  async function archiveUpload(file:UploadFile,archived:boolean){
    try{await api('/uploads/'+file.id,{version:file.version,archived},'PUT');setViewing(null);await load();}catch(cause){setError((cause as Error).message);}
  }
  if(openId)return <div className="fe-page"><PageDetail id={openId} online={online} published={published.find(item=>item.artifactId===openId)} images={uploads.filter(file=>!file.archived&&isImage(file))} onBack={()=>{setOpenId(null);void load();}} onDiscuss={onDiscuss} onChanged={()=>void load()}/></div>;
  const pages=(apps||[]).filter(app=>app.archived===trash);
  const media=uploads.filter(file=>file.archived===trash);
  const runway=state.runway;
  const deliverables=trash?[]:(runway?.artifacts||[]);
  const drafts=trash?[]:state.drafts;
  const show=(kind:Filter)=>filter==='all'||filter===kind;
  const total=pages.length+media.length+deliverables.length+drafts.length;
  return <div className="fe-page"><div className="fe-page-inner">
    <PageHead title="Assets" subtitle="Campaign pages, media and drafts in one place. Everything Marketing makes shows up here too.">
      <input ref={picker} type="file" multiple hidden accept=".png,.jpg,.jpeg,.webp,.gif,.mp4,.webm,.txt,.md,.csv,.json" onChange={event=>{void upload(event.target.files);event.target.value='';}}/>
      <button type="button" disabled={!online||uploading>0} onClick={()=>picker.current?.click()}><Upload size={16}/> {uploading?'Uploading…':'Upload media'}</button>
      <button type="button" className="primary" disabled={!online} onClick={()=>setCreating(true)}><Plus size={16}/> New page</button>
    </PageHead>
    <div className="fe-toolbar"><nav className="fe-segmented" aria-label="Asset types">{(['all','pages','media','drafts'] as Filter[]).map(item=><button type="button" key={item} aria-pressed={filter===item} onClick={()=>setFilter(item)}>{item==='all'?'All':item==='pages'?'Pages':item==='media'?'Media':'Drafts'}</button>)}</nav>
      <span className="fe-toolbar-spacer"/><button type="button" className="fe-ghost" aria-pressed={trash} onClick={()=>setTrash(!trash)}><Trash2 size={15}/> {trash?'Back to assets':'Trash'}</button></div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {apps&&!total&&<Empty icon={<LayoutTemplate size={34}/>} title={trash?'Trash is empty':'Nothing here yet'}>{trash?'Deleted pages and files wait here until you restore them.':'Create a landing page or a working tool, or upload images and video for your campaigns.'}</Empty>}
    {show('pages')&&pages.length>0&&<section className="fe-inbox-group" aria-label="Pages"><h3>Pages & tools</h3><div className="fe-grid">{pages.map(app=><button type="button" className="fe-tile" key={app.id} onClick={()=>setOpenId(app.id)}>
      <div className="fe-tile-art">{tools[app.id]?<Table2 size={34}/>:<iframe title={'Preview of '+app.title} src={'/api/artifacts/'+app.id+'/page'} sandbox="allow-scripts" tabIndex={-1} loading="lazy" aria-hidden="true"/>}<span className="fe-pill fe-tile-kind">{tools[app.id]?`Tool · ${app.entryCount} record${app.entryCount===1?'':'s'}`:published.some(item=>item.artifactId===app.id)?<><Globe size={12}/> Published</>:'Page'}</span></div>
      <div className="fe-tile-body"><strong>{app.title}</strong><small>{app.description||'Page'}</small></div></button>)}</div></section>}
    {show('media')&&media.length>0&&<section className="fe-inbox-group" aria-label="Media"><h3>Media & files</h3><div className="fe-grid">{media.map(file=><button type="button" className="fe-tile" key={file.id} onClick={()=>setViewing(file)}>
      <div className="fe-tile-art">{isImage(file)?<img src={'/api/uploads/'+file.id+'/content'} alt="" loading="lazy"/>:isVideo(file)?<video src={'/api/uploads/'+file.id+'/content'} muted preload="metadata"/>:<FileText size={30}/>}<span className="fe-pill fe-tile-kind">{isVideo(file)?<><Film size={12}/> Video</>:isImage(file)?<><ImageIcon size={12}/> Image</>:'File'}</span></div>
      <div className="fe-tile-body"><strong>{file.name}</strong><small>{size(file.bytes)} · {readableTime(file.created)}</small></div></button>)}</div></section>}
    {show('drafts')&&(deliverables.length>0||drafts.length>0)&&<section className="fe-inbox-group" aria-label="Drafts"><h3>Drafts from Marketing</h3><div className="fe-row-list">
      {deliverables.map(item=><div className="fe-row-split" key={item.id}><button type="button" className="fe-row" onClick={onOpenCampaigns}><span className="fe-row-icon accent"><FileText size={18}/></span><span className="fe-row-body"><strong>{kindLabel[item.kind]||item.kind.replaceAll('_',' ')}</strong><small>{runway?campaignTitle(runway.project.goal):'Campaign'} · {readableTime(item.created_at)}</small></span></button>
        {['post_angles','revision_angles'].includes(item.kind)&&<button type="button" className="fe-ghost" disabled={!online} onClick={()=>void makeMockups(item)}><LayoutTemplate size={15}/> Make social mockups</button>}</div>)}
      {drafts.map(draft=><button type="button" className="fe-row" key={draft.id} onClick={onOpenCampaigns}><span className="fe-row-icon"><FileText size={18}/></span><span className="fe-row-body"><strong>{draft.channel} draft · {draft.status}</strong><small>{plain(draft.content).slice(0,110)}</small></span></button>)}
    </div></section>}
    {creating&&<NewPage onCreate={create} onClose={()=>setCreating(false)}/>}
    {viewing&&<Dialog title={viewing.name} wide={isVideo(viewing)||isImage(viewing)} onClose={()=>setViewing(null)}>
      <div className="fe-media-view">{isImage(viewing)?<img src={'/api/uploads/'+viewing.id+'/content'} alt={viewing.name}/>:isVideo(viewing)?<video src={'/api/uploads/'+viewing.id+'/content'} controls autoPlay playsInline/>:<iframe title={viewing.name} src={'/api/uploads/'+viewing.id+'/content'} sandbox=""/>}</div>
      <div className="fe-decision-bar"><small>{size(viewing.bytes)} · uploaded {readableTime(viewing.created)}</small>
        <a className="fe-button" href={'/api/uploads/'+viewing.id+'/content?download'} download={viewing.name}><Download size={15}/> Download</a>
        {viewing.archived?<button type="button" onClick={()=>void archiveUpload(viewing,false)}><RotateCcw size={15}/> Restore</button>:<button type="button" className="fe-ghost" onClick={()=>void archiveUpload(viewing,true)}><Trash2 size={15}/> Move to Trash</button>}</div>
    </Dialog>}
  </div></div>;
}
