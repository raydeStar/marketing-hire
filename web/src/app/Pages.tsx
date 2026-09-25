import {useCallback,useEffect,useRef,useState} from 'react';
import {Code2,ExternalLink,FileText,Globe,Image as ImageIcon,LayoutTemplate,MessageCircle,RotateCcw,Table2,Trash2} from 'lucide-react';
import {PublishDialog,type Published} from './PublishPage';
import {api} from '../api';
import {ArtifactPreview} from '../components/ArtifactPreview';
import {readableTime} from '../components/MarketingPanels';
import type {ArtifactApp,UploadFile} from '../types';
import {pageTemplates,socialMockupDefinition,type PageTemplate} from './pageTemplates';
import {Dialog,Empty} from './shared';
import {fileSize} from './LibraryDocs';

export const newId=()=>crypto.randomUUID().replaceAll('-','');

function pageText(app:ArtifactApp){
  const html=app.definition.page?.html||'';
  return (new DOMParser().parseFromString(html,'text/html').body.textContent||'').replace(/\s+/g,' ').trim();
}

export async function createPage(template:PageTemplate,title:string){
  const id=newId();
  await api<ArtifactApp>('/artifacts/'+id,{operationId:newId(),version:'absent',definition:template.definition(title),upserts:[],deleteIds:[]},'PUT');
  return id;
}

/** Marketing's saved post angles become a reviewable mockup page in one step. */
export async function createMockups(title:string,content:string){
  const data=JSON.parse(content) as {angles?:{title:string;hook:string;why:string;claimLimit?:string}[]};
  if(!data.angles?.length)throw new Error('These drafts have no post angles to lay out.');
  const id=newId();
  await api<ArtifactApp>('/artifacts/'+id,{operationId:newId(),version:'absent',definition:socialMockupDefinition(title.slice(0,80),data.angles),upserts:[],deleteIds:[]},'PUT');
  return id;
}

export function NewPageDialog({onCreated,onClose}:{onCreated:(id:string)=>void;onClose:()=>void}){
  const [chosen,setChosen]=useState<PageTemplate|null>(null),[title,setTitle]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const groups=['Campaign pages','Working tools'] as const;
  if(chosen)return <Dialog title={'New '+chosen.label.toLowerCase()} onClose={onClose}><form className="fe-form" onSubmit={async event=>{event.preventDefault();if(!title.trim()||busy)return;setBusy(true);setError('');try{onCreated(await createPage(chosen,title.trim()));}catch(cause){setError((cause as Error).message);setBusy(false);}}}>
    <label>Name<input autoFocus required maxLength={80} value={title} onChange={event=>setTitle(event.target.value)} placeholder={chosen.group==='Working tools'?chosen.label:'e.g. Spring launch landing page'}/></label>
    <p className="fe-muted">{chosen.summary}. Everything is editable after it’s created.</p>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={()=>setChosen(null)}>Back</button><button className="primary" disabled={busy||!title.trim()}>{busy?'Creating…':'Create'}</button></footer>
  </form></Dialog>;
  return <Dialog title="New page or app" onClose={onClose}>{groups.map(group=><section key={group} className="fe-dialog-group"><h3>{group==='Working tools'?'Apps':'Pages'}</h3><div className="fe-row-list">{pageTemplates.filter(item=>item.group===group).map(item=>
    <button type="button" className="fe-row" key={item.id} onClick={()=>{setChosen(item);setTitle(item.group==='Working tools'?item.label:'');}}><span className="fe-row-icon">{item.group==='Working tools'?<Table2 size={17}/>:<LayoutTemplate size={17}/>}</span><span className="fe-row-body"><strong>{item.label}</strong><small>{item.summary}</small></span></button>)}</div></section>)}</Dialog>;
}

/** A page or app: live preview, code editor, version history, publishing. */
export function PageDetail({id,online,canEdit,canPublish,canAsk,published,images,onDiscuss,onChanged}:{id:string;online:boolean;canEdit:boolean;canPublish:boolean;canAsk:boolean;published?:Published;images:UploadFile[];onDiscuss:(text:string)=>void;onChanged:()=>void}){
  const [publishing,setPublishing]=useState(false),[picking,setPicking]=useState(false);
  const htmlField=useRef<HTMLTextAreaElement>(null);
  const [app,setApp]=useState<ArtifactApp|null>(null),[tab,setTab]=useState<'preview'|'code'|'history'>('preview');
  const [code,setCode]=useState({html:'',css:'',javaScript:''}),[meta,setMeta]=useState({title:'',description:''});
  const [history,setHistory]=useState<{id:string;description:string;at:string;version:string;title:string}[]>([]);
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  const operation=useRef<{signature:string;id:string}|null>(null);
  const load=useCallback(async()=>{try{const next=await api<ArtifactApp>('/artifacts/'+id);setApp(next);setCode(next.definition.page||{html:'',css:'',javaScript:''});setMeta({title:next.definition.title,description:next.definition.description});}catch(cause){setError((cause as Error).message);}},[id]);
  useEffect(()=>{void load();},[load]);
  useEffect(()=>{if(tab==='history')void api<typeof history>('/artifacts/'+id+'/history').then(setHistory).catch(cause=>setError((cause as Error).message));},[tab,app?.version]);
  // Pages refer to uploads as media:<id>; the host inlines them when the page is shown or published.
  function insertImage(file:UploadFile){
    const tag=`<img src="media:${file.id}" alt="${file.name.replace(/\.[a-z0-9]+$/i,'').replace(/["<>&]/g,'')}">`;
    const field=htmlField.current,start=field?.selectionStart??code.html.length,end=field?.selectionEnd??code.html.length;
    setCode(current=>({...current,html:current.html.slice(0,start)+tag+current.html.slice(end)}));setPicking(false);
  }
  function opId(signature:string){if(operation.current?.signature!==signature)operation.current={signature,id:newId()};return operation.current.id;}
  async function edit(change:Record<string,unknown>,done:string){
    if(!app||busy)return;setBusy(true);setError('');setNotice('');
    const body={version:app.version,...change};
    try{const saved=await api<ArtifactApp>('/artifacts/'+id,{operationId:opId(JSON.stringify(body)),...body},'PUT');operation.current=null;setApp(saved);setNotice(done);onChanged();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(!app)return error?<p className="fe-alert" role="alert">{error}</p>:<p className="fe-muted">Opening…</p>;
  const dirty=JSON.stringify(code)!==JSON.stringify(app.definition.page||{html:'',css:'',javaScript:''})||meta.title!==app.definition.title||meta.description!==app.definition.description;
  const chars=code.html.length+code.css.length+code.javaScript.length;
  const live=published&&published.artifactVersion===app.version;
  return <div className="fe-page-detail">
    <div className="fe-doc-meta">
      {published?<span className={'fe-pill '+(live?'ok':'attn')}><Globe size={12}/> {live?'Published':'Changed since publishing'}</span>:<span className="fe-pill">Not published</span>}
      {app.archived&&<span className="fe-pill attn">In Trash</span>}
      <small>Updated {readableTime(app.updated)}</small>
    </div>
    <div className="fe-doc-toolbar">
      <nav className="fe-tabs" aria-label="Page views"><button type="button" aria-pressed={tab==='preview'} onClick={()=>setTab('preview')}>Preview</button>{canEdit&&<button type="button" aria-pressed={tab==='code'} onClick={()=>setTab('code')}><Code2 size={14}/> Edit</button>}<button type="button" aria-pressed={tab==='history'} onClick={()=>setTab('history')}>History</button></nav>
      <span className="fe-toolbar-spacer"/>
      {app.definition.page&&<a className="fe-button" href={'/api/artifacts/'+app.id+'/page'} target="_blank" rel="noopener"><ExternalLink size={15}/> Open in a tab</a>}
      {canAsk&&<button type="button" onClick={()=>onDiscuss(`Take a look at our page "${app.definition.title}" and suggest improvements to the headline, structure and call to action. Here is its text:\n\n${pageText(app).slice(0,3000)}`)}><MessageCircle size={15}/> Ask for feedback</button>}
      {canEdit&&(app.archived?<button type="button" disabled={busy||!online} onClick={()=>void edit({archived:false},'Restored.')}><RotateCcw size={15}/> Restore</button>
        :<button type="button" className="fe-ghost" disabled={busy||!online} onClick={()=>{if(window.confirm('Move this page to Trash? You can restore it later.'))void edit({archived:true},'Moved to Trash.');}}><Trash2 size={15}/> Trash</button>)}
      {app.definition.page&&canPublish&&<button type="button" className="primary" onClick={()=>setPublishing(true)}><Globe size={15}/> {published?'Publishing…':'Publish'}</button>}
    </div>
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {publishing&&<PublishDialog app={app} published={published} online={online} onClose={()=>setPublishing(false)} onChanged={onChanged}/>}
    {tab==='preview'&&(app.definition.page?<div className="fe-page-frame"><ArtifactPreview app={app} online={online&&!app.archived&&canEdit} onSaved={setApp}/></div>:<Empty icon={<FileText size={30}/>} title="This app has no page">It stores records only. Add HTML in Edit to give it one.</Empty>)}
    {tab==='code'&&canEdit&&<form className="fe-form" onSubmit={event=>{event.preventDefault();void edit({definition:{...app.definition,title:meta.title.trim(),description:meta.description.trim(),page:code}},'Saved. The preview shows your changes.');}}>
      <div className="fe-form-row"><label>Name<input required maxLength={80} value={meta.title} onChange={event=>setMeta({...meta,title:event.target.value})}/></label><label>Description<input maxLength={400} value={meta.description} onChange={event=>setMeta({...meta,description:event.target.value})}/></label></div>
      <div className="fe-editor-tools"><button type="button" className="fe-ghost" onClick={()=>setPicking(true)}><ImageIcon size={15}/> Insert image</button></div>
      <label>HTML<textarea ref={htmlField} className="fe-editor" rows={14} value={code.html} onChange={event=>setCode({...code,html:event.target.value})} spellCheck={false}/></label>
      <label>CSS<textarea className="fe-editor short" rows={8} value={code.css} onChange={event=>setCode({...code,css:event.target.value})} spellCheck={false}/></label>
      <label>JavaScript<textarea className="fe-editor short" rows={6} value={code.javaScript} onChange={event=>setCode({...code,javaScript:event.target.value})} spellCheck={false}/></label>
      <small>{chars.toLocaleString()} / 40,000 characters. Pages run sandboxed with no network access; use Insert image for uploaded images.</small>
      {picking&&<Dialog title="Insert an image" onClose={()=>setPicking(false)}>{images.length?<div className="fe-grid fe-image-picker">{images.map(file=><button type="button" className="fe-tile" key={file.id} onClick={()=>insertImage(file)}><div className="fe-tile-art"><img src={'/api/uploads/'+file.id+'/content'} alt=""/></div><div className="fe-tile-body"><strong>{file.name}</strong><small>{fileSize(file.bytes)}</small></div></button>)}</div>:<Empty icon={<ImageIcon size={30}/>} title="No images yet">Upload images to the Library’s Media folder, then insert them here.</Empty>}</Dialog>}
      <footer><button type="button" className="fe-ghost" disabled={!dirty} onClick={()=>{setCode(app.definition.page||{html:'',css:'',javaScript:''});setMeta({title:app.definition.title,description:app.definition.description});}}>Discard</button><button className="primary" disabled={busy||!dirty||!online||chars>40000||!code.html.trim()}>{busy?'Saving…':'Save changes'}</button></footer>
    </form>}
    {tab==='history'&&<div className="fe-table-wrap"><table className="fe-table"><thead><tr><th>Change</th><th>When</th><th/></tr></thead><tbody>{history.map((item,index)=><tr key={item.id}><td>{item.description}{index===0&&<span className="fe-pill ok">Current</span>}</td><td>{readableTime(item.at)}</td><td>{index>0&&canEdit&&<button type="button" disabled={busy||!online} onClick={()=>void api<ArtifactApp>('/artifacts/'+id+'/restore',{operationId:newId(),version:app.version,targetVersion:item.version}).then(saved=>{setApp(saved);setCode(saved.definition.page||{html:'',css:'',javaScript:''});setNotice('Restored that version.');onChanged();}).catch(cause=>setError((cause as Error).message))}>Restore</button>}</td></tr>)}</tbody></table></div>}
  </div>;
}
