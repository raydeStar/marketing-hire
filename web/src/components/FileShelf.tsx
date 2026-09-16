import {Modal} from './Modal';
import {useState,useEffect} from 'react';
import {FileText,Download,MessageCircle,Trash2,RotateCcw,X,Upload} from 'lucide-react';
import {api} from '../api';
import {useFileUploads} from '../use-file-uploads';
import {UploadFeedback} from './UploadFeedback';
import type {UploadFile} from '../types';

export const uploadAccept='.txt,.md,.csv,.json,.png,.jpg,.jpeg,.webp';
export function FileShelf({files,filter,query,archived,online,onChanged,onAttach,focusId}:{focusId?:string;files:UploadFile[];filter:'all'|'images'|'documents';query:string;archived:boolean;online:boolean;onChanged:()=>Promise<unknown>;onAttach:(file:UploadFile)=>void}){
 const [preview,setPreview]=useState<UploadFile|null>(()=>files.find(file=>file.id===focusId&&!file.archived)||null),[text,setText]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const visible=files.filter(f=>f.archived===archived&&f.name.toLowerCase().includes(query.toLowerCase())&&(filter==='all'||((filter==='images')===f.mediaType.startsWith('image/'))));
 const uploads=useFileUploads({disabled:busy||!online,onChanged});
 async function act(work:()=>Promise<unknown>){setBusy(true);setError('');try{await work();await onChanged();}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 function open(file:UploadFile){setPreview(file);setText('');}
 useEffect(()=>{if(!preview||preview.mediaType!=='text/plain')return;const controller=new AbortController();
  void fetch('/api/uploads/'+preview.id+'/content',{signal:controller.signal,cache:'no-store'}).then(async response=>{if(!response.ok)throw new Error('The file could not be opened.');return response.text();}).then(value=>{if(!controller.signal.aborted)setText(value);}).catch(e=>{if(!controller.signal.aborted)setError(e.message);});return()=>controller.abort();
 },[preview]);
 return <section className="file-shelf" aria-label="Uploaded files"><div className="section-heading"><h2>{filter==='images'?'Images':'Files'}</h2>{!archived&&<label className={'upload-control'+(busy||uploads.busy?' disabled':'')}><Upload size={15}/>Upload files<input type="file" multiple accept={uploadAccept} disabled={!online||busy||uploads.busy} onChange={e=>{const chosen=Array.from(e.target.files||[]);e.target.value='';void uploads.upload(chosen);}}/></label>}</div>
  <UploadFeedback state={uploads.state} onDismiss={uploads.clear}/>
  {error&&<p className="error" role="alert">{error}</p>}
  {!visible.length&&<p className="quiet-empty">{query?'No files match this search.':archived?'No deleted files here.':'Upload a file to the study. Text, Markdown, CSV, JSON, PNG, JPEG and WebP; up to 2 MiB each.'}</p>}
  <div className="file-grid">{visible.map(file=><article className="file-card" key={file.id}><button className="file-open" disabled={archived} onClick={()=>void open(file)}>{file.mediaType.startsWith('image/')&&!archived?<img loading="lazy" src={'/api/uploads/'+file.id+'/content'} alt={file.name}/>:<span className="document-thumb"><FileText size={36}/></span>}<strong>{file.name}</strong><small>{Math.max(1,Math.round(file.bytes/1024))} KB · {new Date(file.created).toLocaleDateString()}</small></button><footer>{archived?<button disabled={!online||busy} onClick={()=>void act(()=>api('/uploads/'+file.id,{version:file.version,archived:false},'PUT'))}><RotateCcw size={14}/>Restore</button>:<><button disabled={!online||busy} aria-label={'Attach '+file.name+' to chat'} onClick={()=>onAttach(file)}><MessageCircle size={14}/>Chat</button><a download={file.name} href={'/api/uploads/'+file.id+'/content?download=1'} aria-label={'Download '+file.name}><Download size={14}/></a><button disabled={!online||busy} aria-label={'Delete '+file.name} onClick={()=>void act(()=>api('/uploads/'+file.id,{version:file.version,archived:true},'PUT'))}><Trash2 size={14}/></button></>}</footer></article>)}</div>
  {preview&&<Modal title={preview.name} onClose={()=>setPreview(null)} className="file-preview">{preview.mediaType.startsWith('image/')?<img src={'/api/uploads/'+preview.id+'/content'} alt={preview.name}/>:<pre>{text}</pre>}<footer><button onClick={()=>{onAttach(preview);setPreview(null);}}>Discuss in chat</button><a download={preview.name} href={'/api/uploads/'+preview.id+'/content?download=1'}>Download</a></footer></Modal>}
 </section>;
}
