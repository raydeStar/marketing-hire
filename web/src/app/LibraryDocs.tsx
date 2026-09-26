import {useEffect,useState} from 'react';
import {Clapperboard,Download,ExternalLink,Eye,LayoutTemplate,Link2,Mic,Pencil,RotateCcw,Trash2} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import {ArtifactBody} from '../components/MarketingRunwayPanel';
import {publicLink,readableTime,type MarketingEvidence,type MarketingState,type RunwayArtifact} from '../components/MarketingPanels';
import type {UploadFile} from '../types';
import {actorLabel,type WikiPage} from './library';
import type {WikiTemplate} from './wikiTemplates';
import {useAttempt,type Directory} from './shared';
import {RateWork} from './Feedback';
import {NarrationDialog,parseStoryboard} from './Narration';

type Form={scope:string;scopeId:string;title:string;body:string;kind:string;status:string};
const typeLabel:Record<string,string>={fact:'Fact',policy:'Playbook',hypothesis:'Hypothesis',question:'Open question'};
const statusLabel:Record<string,string>={draft:'Draft',active:'Published',archived:'Archived'};
const statusTone:Record<string,string>={draft:'attn',active:'ok',archived:''};

function layersFor(directory:Directory){
  return [{value:'company:company',label:'Everyone'},...directory.departments.map(item=>({value:'department:'+item.id,label:item.name+' department'})),...directory.agents.map(item=>({value:'member:'+item.id,label:item.name+' only'}))];
}

/** A wiki document: read it, edit it with a live preview, and see its versions. */
export function WikiDoc({page,template,directory,canEdit,onSaved,onCancel}:{page?:WikiPage;template?:WikiTemplate|null;directory:Directory;canEdit:boolean;onSaved:(page:WikiPage)=>void;onCancel?:()=>void}){
  const blank=!page;
  const [form,setForm]=useState<Form|null>(blank?{scope:'company',scopeId:'company',title:template?.title||'',body:template?.body||'',kind:template?.kind||'policy',status:'draft'}:null);
  const [preview,setPreview]=useState(false),[history,setHistory]=useState<WikiPage[]>([]);
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[narrating,setNarrating]=useState(false),[rendering,setRendering]=useState(''),[rendered,setRendered]=useState('');
  // A storyboard the host renders as branded cards: render it again once narration is recorded.
  const cards=page?/```(?:json)?\s*\n?[\s\S]*?"renderer"\s*:\s*"cards"[\s\S]*?```/.test(page.body):false;
  async function render(){if(!page)return;setRendering('busy');setRendered('');try{const result=await api<{media:string;note:string;narrated:number}>('/videos/render',{page:page.id});setRendered(`${result.note}${result.narrated?` With ${result.narrated} narration clip(s).`:''} It's in Library → Campaigns → Videos.`);}catch(cause){setRendered((cause as Error).message);}finally{setRendering('');}}
  const attempt=useAttempt();
  const layers=layersFor(directory);
  const layerName=(value:{scope:string;scopeId:string})=>layers.find(item=>item.value===value.scope+':'+value.scopeId)?.label||'Restricted';
  useEffect(()=>{if(!page){setHistory([]);return;}void api<WikiPage[]>('/company-wiki/'+encodeURIComponent(page.id)+'/history').then(setHistory).catch(()=>setHistory([]));},[page?.id,page?.version]);
  async function save(event:React.FormEvent){
    event.preventDefault();if(!form||busy)return;setBusy(true);setError('');
    const fields={id:page?.id??null,version:page?.version??0,...form};
    try{const saved=await api<WikiPage>('/company-wiki',{...fields,requestId:attempt.id(JSON.stringify(fields))},'PUT');attempt.done();setForm(null);onSaved(saved);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(form)return <form className="fe-doc fe-form" onSubmit={event=>void save(event)} aria-label={blank?'New document':'Edit document'}>
    <div className="fe-doc-toolbar"><nav className="fe-tabs" aria-label="Editor mode"><button type="button" aria-pressed={!preview} onClick={()=>setPreview(false)}><Pencil size={14}/> Write</button><button type="button" aria-pressed={preview} onClick={()=>setPreview(true)}><Eye size={14}/> Preview</button></nav></div>
    <label>Title<input required maxLength={160} value={form.title} onChange={event=>setForm({...form,title:event.target.value})} autoFocus={blank}/></label>
    {preview?<div className="fe-prose fe-doc-preview"><Markdown components={{img:()=>null}}>{form.body||'*Nothing written yet*'}</Markdown></div>
      :<label>Content<textarea className="fe-editor" required maxLength={12000} value={form.body} onChange={event=>setForm({...form,body:event.target.value})} placeholder="Markdown. Say what’s known, what’s uncertain, and where facts come from."/></label>}
    <div className="fe-form-row">
      <label>Visible to<select disabled={!blank} value={form.scope+':'+form.scopeId} onChange={event=>{const [scope,...rest]=event.target.value.split(':');setForm({...form,scope,scopeId:rest.join(':')});}}>{layers.map(item=><option key={item.value} value={item.value}>{item.label}</option>)}</select></label>
      <label>Type<select value={form.kind} onChange={event=>setForm({...form,kind:event.target.value})}>{Object.entries(typeLabel).map(([value,label])=><option key={value} value={value}>{label}</option>)}</select></label>
      <label>Status<select value={form.status} onChange={event=>setForm({...form,status:event.target.value})}><option value="draft">Draft</option><option value="active">Published</option><option value="archived">Archived</option></select></label>
    </div>
    <small>Published documents are shared with the employee’s meetings. Drafts stay in the Library only.</small>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={()=>{if(blank)onCancel?.();else setForm(null);}}>Cancel</button><button className="primary" disabled={busy||!canEdit||!form.title.trim()||!form.body.trim()}>{busy?'Saving…':blank?'Create document':'Save changes'}</button></footer>
  </form>;
  if(!page)return null;
  return <article className="fe-doc">
    <div className="fe-doc-meta"><span className={'fe-pill '+statusTone[page.status]}>{statusLabel[page.status]}</span><span className="fe-pill">{typeLabel[page.kind]||page.kind}</span><span className="fe-pill">Visible to {layerName(page)}</span>
      <small>Version {page.version} · {readableTime(page.updatedAt)} · {actorLabel(page.author)}</small>
      {canEdit&&parseStoryboard(page.body)&&<button type="button" className="fe-doc-edit" onClick={()=>setNarrating(true)}><Mic size={14}/> Record narration</button>}
      {canEdit&&cards&&<button type="button" className="fe-doc-edit" disabled={!!rendering} onClick={()=>void render()}><Clapperboard size={14}/> {rendering?'Rendering…':'Render video'}</button>}
      {canEdit&&<button type="button" className="fe-doc-edit" onClick={()=>setForm({scope:page.scope,scopeId:page.scopeId,title:page.title,body:page.body,kind:page.kind,status:page.status})}><Pencil size={14}/> Edit</button>}</div>
    {narrating&&<NarrationDialog page={page} onSaved={onSaved} onClose={()=>setNarrating(false)}/>}
    {rendered&&<p className="fe-notice" role="status">{rendered}</p>}
    <div className="fe-prose"><Markdown components={{img:()=>null}}>{page.body}</Markdown></div>
    {page.author.startsWith('Marketing employee')&&page.title!=='Marketing notebook'&&<RateWork itemKey={'wiki:'+page.id} title={page.title} canRate={canEdit}/>}
    {history.length>1&&<details className="fe-history"><summary>Version history ({history.length})</summary>{history.map(item=><details key={item.version} className="fe-history-row"><summary>Version {item.version} · {statusLabel[item.status]} · {readableTime(item.updatedAt)} · {actorLabel(item.author)}</summary><div className="fe-prose"><Markdown components={{img:()=>null}}>{item.body}</Markdown></div></details>)}</details>}
  </article>;
}

export function isVideo(file:UploadFile){return file.mediaType.startsWith('video/');}
export function isImage(file:UploadFile){return file.mediaType.startsWith('image/');}
export function isAudio(file:UploadFile){return file.mediaType.startsWith('audio/');}
export function fileSize(bytes:number){return bytes>1048576?(bytes/1048576).toFixed(1)+' MB':Math.max(1,Math.round(bytes/1024))+' KB';}

export function MediaView({file,canEdit,onChanged}:{file:UploadFile;canEdit:boolean;onChanged:()=>void}){
  const [error,setError]=useState('');
  async function archive(archived:boolean){try{await api('/uploads/'+file.id,{version:file.version,archived},'PUT');onChanged();}catch(cause){setError((cause as Error).message);}}
  return <div className="fe-doc">
    <div className="fe-media-view">{isImage(file)?<img src={'/api/uploads/'+file.id+'/content'} alt={file.name}/>:isVideo(file)?<video src={'/api/uploads/'+file.id+'/content'} controls playsInline/>:isAudio(file)?<audio src={'/api/uploads/'+file.id+'/content'} controls/>:<iframe title={file.name} src={'/api/uploads/'+file.id+'/content'} sandbox=""/>}</div>
    <dl className="fe-facts"><div><dt>Type</dt><dd>{file.mediaType}</dd></div><div><dt>Size</dt><dd>{fileSize(file.bytes)}</dd></div><div><dt>Uploaded</dt><dd>{readableTime(file.created)}</dd></div><div><dt>SHA-256</dt><dd className="fe-mono" title={file.sha256}>{file.sha256.slice(0,16)}…</dd></div></dl>
    <div className="fe-actions"><a className="fe-button" href={'/api/uploads/'+file.id+'/content?download'} download={file.name}><Download size={15}/> Download</a>
      {canEdit&&(file.archived?<button type="button" onClick={()=>void archive(false)}><RotateCcw size={15}/> Restore</button>:<button type="button" className="fe-ghost" onClick={()=>void archive(true)}><Trash2 size={15}/> Move to Trash</button>)}</div>
    {isImage(file)&&<small>Use this image on a page: open the page, choose Edit, then Insert image.</small>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </div>;
}

export function SourceView({source,state,onOpenTask}:{source:MarketingEvidence;state:MarketingState;onOpenTask:(id:string)=>void}){
  const link=publicLink(source.url);const task=state.tasks.find(item=>item.id===source.task_id);
  return <article className="fe-doc">
    <dl className="fe-facts"><div><dt>Found by</dt><dd>{source.source||'Marketing'}</dd></div><div><dt>Recorded</dt><dd>{readableTime(source.created_at)}</dd></div>{source.query&&<div><dt>Search</dt><dd>{source.query}</dd></div>}</dl>
    {link?<p><a href={link} target="_blank" rel="noopener noreferrer"><Link2 size={14}/> {new URL(link).hostname} <ExternalLink size={12}/></a></p>:<p className="fe-muted">{source.url}</p>}
    <div className="fe-prose"><Markdown components={{img:()=>null}}>{source.note||'_No note recorded._'}</Markdown></div>
    {task&&<button type="button" onClick={()=>onOpenTask(task.id)}>Open the task it supports: {task.title}</button>}
  </article>;
}

export function DeliverableView({artifact,canMakeMockups,onMakeMockups,onOpenCampaign}:{artifact:RunwayArtifact;canMakeMockups:boolean;onMakeMockups:()=>void;onOpenCampaign:()=>void}){
  const angles=['post_angles','revision_angles'].includes(artifact.kind);
  return <article className="fe-doc">
    <dl className="fe-facts"><div><dt>Recorded</dt><dd>{readableTime(artifact.created_at)}</dd></div><div><dt>Digest</dt><dd className="fe-mono" title={artifact.digest}>{artifact.digest.slice(0,16)}…</dd></div></dl>
    <div className="fe-actions"><button type="button" className="primary" onClick={onOpenCampaign}>Review in campaign</button>
      {angles&&<button type="button" disabled={!canMakeMockups} onClick={onMakeMockups}><LayoutTemplate size={15}/> Make social mockups</button>}</div>
    <ArtifactBody artifact={artifact}/>
  </article>;
}

export type {MarketingState};
