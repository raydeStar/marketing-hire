import {useEffect,useRef,useState} from 'react';
import {Download,Film,ImagePlus,Upload,X} from 'lucide-react';
import {api,uploadFile} from '../api';
import type {MarketingDraft} from '../components/MarketingPanels';
import type {UploadFile} from '../types';
import {Dialog} from './shared';

type Attachment={id:string;name:string;mediaType:string;bytes:number};
const content=(id:string)=>'/api/uploads/'+id+'/content';
const isVideo=(type:string)=>type.startsWith('video/');

/** The images and videos that go with a draft: what the employee made for it, and whatever the owner attaches from the Library.
 * Composers can't take files from here, so each one downloads for attaching when the post goes out. */
export function DraftAttachments({draft,canEdit,uploads}:{draft:MarketingDraft;canEdit:boolean;uploads?:UploadFile[]}){
  const [items,setItems]=useState<Attachment[]|null>(null),[picking,setPicking]=useState(false),[error,setError]=useState(''),[busy,setBusy]=useState('');
  const load=()=>api<Record<string,Attachment[]>>('/drafts/media').then(all=>setItems(all[String(draft.id)]||[])).catch(()=>setItems([]));
  useEffect(()=>{void load();},[draft.id]);
  async function change(mediaId:string,attach:boolean){
    setBusy(mediaId);setError('');
    try{setItems(await api<Attachment[]>(`/drafts/${draft.id}/media`,{mediaId,attach}));setPicking(false);}
    catch(cause){setError((cause as Error).message);}finally{setBusy('');}
  }
  const open=draft.status!=='rejected'&&draft.status!=='withdrawn';
  if(!items||(items.length===0&&!(canEdit&&open)))return null;
  return <section className="fe-attachments" aria-label="Attachments">
    {items.length>0&&<ul>{items.map(item=><li key={item.id}>
      {isVideo(item.mediaType)?<video src={content(item.id)} controls playsInline preload="metadata"/>:<img src={content(item.id)} alt={item.name}/>}
      <div className="fe-attachment-meta"><small title={item.name}>{item.name}</small>
        <a className="fe-icon-button" href={content(item.id)+'?download'} download={item.name} aria-label={'Download '+item.name} title="Download to attach when you post"><Download size={14}/></a>
        {canEdit&&open&&<button type="button" className="fe-icon-button" aria-label={'Remove '+item.name} title="Remove from this draft" disabled={busy===item.id} onClick={()=>void change(item.id,false)}><X size={14}/></button>}</div>
    </li>)}</ul>}
    <div className="fe-attachments-foot">
      {canEdit&&open&&items.length<4&&<button type="button" className="fe-ghost" onClick={()=>setPicking(true)}><ImagePlus size={15}/> Attach an image or video</button>}
      {items.length>0&&<small className="fe-muted">Download each file to attach it when you post; the composer can't take files from here.</small>}
    </div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {picking&&<MediaPicker uploads={uploads} taken={items.map(item=>item.id)} busy={busy} onPick={id=>void change(id,true)} onClose={()=>setPicking(false)} onError={setError}/>}
  </section>;
}

/** Choose a Library image or video, or upload a new one. */
function MediaPicker({uploads,taken,busy,onPick,onClose,onError}:{uploads?:UploadFile[];taken:string[];busy:string;onPick:(id:string)=>void;onClose:()=>void;onError:(message:string)=>void}){
  const [files,setFiles]=useState<UploadFile[]|null>(uploads??null),[uploading,setUploading]=useState(false);
  const input=useRef<HTMLInputElement>(null);
  useEffect(()=>{if(!uploads)void api<{uploads?:UploadFile[]}>('/state').then(state=>setFiles(state.uploads||[])).catch(()=>setFiles([]));},[uploads]);
  const media=(files||[]).filter(file=>!file.archived&&(file.mediaType.startsWith('image/')||isVideo(file.mediaType))&&!taken.includes(file.id));
  async function upload(file:File){
    setUploading(true);
    try{const made=await uploadFile(file) as UploadFile;onPick(made.id);}
    catch(cause){onError((cause as Error).message);}finally{setUploading(false);}
  }
  return <Dialog title="Attach an image or video" wide onClose={onClose}>
    <div className="fe-media-picker">
      {files===null?<p className="fe-muted">Loading the Library…</p>:media.length===0?<p className="fe-muted">No images or videos in the Library yet. Upload one, or ask the employee to make one.</p>
        :<ul>{media.map(file=><li key={file.id}><button type="button" disabled={!!busy} onClick={()=>onPick(file.id)} title={file.name}>
          {isVideo(file.mediaType)?<span className="fe-media-video"><Film size={22}/></span>:<img src={content(file.id)} alt="" loading="lazy"/>}
          <small>{file.name}</small></button></li>)}</ul>}
    </div>
    <footer className="fe-media-picker-foot">
      <input ref={input} type="file" accept="image/png,image/jpeg,image/gif,image/webp,video/mp4,video/webm" hidden onChange={event=>{const file=event.target.files?.[0];if(file)void upload(file);event.target.value='';}}/>
      <button type="button" className="fe-ghost" disabled={uploading} onClick={()=>input.current?.click()}><Upload size={15}/> {uploading?'Uploading…':'Upload a file'}</button>
      <button type="button" onClick={onClose}>Done</button>
    </footer>
  </Dialog>;
}
