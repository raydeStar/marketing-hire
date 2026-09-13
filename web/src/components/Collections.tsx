import {useEffect,useState} from 'react';
import {Plus, Check, ArrowUpRight, Archive, RotateCcw, Pencil, X} from 'lucide-react';
import {api} from '../api';
import type {LibraryItem} from '../types';

const copy = {
  todo: {title:'To-do', eyebrow:'ONE SENSIBLE STEP', lead:'Your intentions, on your terms. You decide when something is done.', add:'Add a to-do', empty:'Nothing on the list. A little breathing room.', done:'Completed'},
  idea: {title:'Ideas', eyebrow:'ROOM TO WANDER', lead:'Keep the thought. It doesn’t have to become a task just yet.', add:'Save an idea', empty:'A place for the not-quite-formed and the worth-remembering.', done:'Completed'},
  feed: {title:'Feed', eyebrow:'ON THE READING PILE', lead:'Save links and reading notes here. Automatic source subscriptions are not connected yet.', add:'Save something to read', empty:'Your reading pile is clear. Save a link or a note to return to.', done:'Read'}
};
type Kind = keyof typeof copy;
export function Collections({kind,items,online,onChanged,onDiscuss,focusId}:{kind:Kind;focusId?:string;items:LibraryItem[];online:boolean;onChanged:()=>Promise<unknown>;onDiscuss:(text:string)=>void}) {
  const [filter,setFilter]=useState(items.find(item=>item.id===focusId)?.status||'open'),[draft,setDraft]=useState<LibraryItem|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const info=copy[kind],mine=items.filter(item=>item.kind===kind),visible=mine.filter(item=>item.status===filter);
  const stale=!!draft&&draft.version!=='absent'&&mine.find(item=>item.id===draft.id)?.version!==draft.version;
  useEffect(()=>{if(focusId){const target=document.querySelector<HTMLElement>(`[data-item-id="${focusId}"]`);target?.focus();target?.scrollIntoView({block:'center'});}},[focusId]);
  function create(){setError('');setDraft({id:crypto.randomUUID().replaceAll('-',''),kind,title:'',content:'',status:'open',url:null,due:null,version:'absent',created:'',updated:''});}
  async function save(item:LibraryItem,close=false){
    setBusy(true);setError('');
    try{await api('/library/'+item.id,item,'PUT');if(close)setDraft(null);await onChanged();}
    catch(e){setError((e as Error).message);}finally{setBusy(false);}
  }
  return <section className="collection" aria-label={info.title}>
    <p className="eyebrow">{info.eyebrow}</p><div className="page-heading"><h1>{info.title}</h1><button className="primary" disabled={!online||busy} onClick={create}><Plus size={16}/>{info.add}</button></div><p className="lead">{info.lead}</p>
    <div className="collection-filters" aria-label="Item status">{[['open',kind==='todo'?'To do':kind==='feed'?'Unread':'Saved'],...(kind==='idea'?[]:[['done',info.done]]),['archived','Archived']].map(([value,label])=><button key={value} aria-pressed={filter===value} onClick={()=>setFilter(value)}>{label}<small>{mine.filter(item=>item.status===value).length}</small></button>)}</div>
    {error&&<p role="alert" className="error">{error}</p>}
    {draft&&<form className="collection-editor" aria-label="Edit collection item" onSubmit={event=>{event.preventDefault();void save(draft,true);}}>
      <div className="page-heading"><h2>{draft.version==='absent'?info.add:'Edit saved item'}</h2><button type="button" aria-label="Close item editor" onClick={()=>setDraft(null)}><X size={16}/></button></div>
      <label>Title<input autoFocus required maxLength={160} value={draft.title} onChange={e=>setDraft({...draft,title:e.target.value})}/></label>
      <label>Notes<textarea aria-label="Notes" maxLength={12000} value={draft.content} onChange={e=>setDraft({...draft,content:e.target.value})}/></label>
      <div className="collection-fields"><label>Link (optional)<input type="url" maxLength={2048} placeholder="https://…" value={draft.url||''} onChange={e=>setDraft({...draft,url:e.target.value||null})}/></label>{kind==='todo'&&<label>Due date (optional)<input type="date" value={draft.due||''} onChange={e=>setDraft({...draft,due:e.target.value||null})}/></label>}</div>
      {stale&&<p role="status">This item changed in another window. Your draft is still here. <button type="button" onClick={()=>{setDraft(mine.find(item=>item.id===draft.id)||null);setError('');}}>Reload saved item</button></p>}
      <button className="primary" disabled={busy||!online||stale||!draft.title.trim()}><Check size={16}/>Save item</button>
    </form>}
    {!visible.length&&<div className="collection-empty"><span className="empty-rule"/><h2>{filter==='open'?info.empty:`No ${filter==='done'?info.done.toLowerCase():'archived'} items.`}</h2><p>Saving and organizing items uses no model tokens.</p></div>}
    <div className="collection-list">{visible.map(item=><article data-item-id={item.id} tabIndex={-1} className={'collection-item '+item.status} key={item.id}>
      {kind!=='idea'&&item.status!=='archived'&&<button className="item-check" aria-label={(item.status==='done'?'Mark unfinished: ':'Mark '+(kind==='feed'?'read: ':'complete: '))+item.title} aria-pressed={item.status==='done'} disabled={!online||busy} onClick={()=>void save({...item,status:item.status==='done'?'open':'done'})}>{item.status==='done'?<Check size={17}/>:<span/>}</button>}
      <div className="item-content"><h2>{item.title}</h2>{item.content&&<p className="item-notes">{item.content}</p>}<div className="item-meta">{item.due&&<span>Due {new Date(item.due+'T12:00:00').toLocaleDateString(undefined,{month:'short',day:'numeric',year:'numeric'})}</span>}{item.url&&<a href={item.url} target="_blank" rel="noreferrer">Open saved link <ArrowUpRight size={13}/></a>}</div>
      <div className="item-actions"><button disabled={!online||busy} onClick={()=>{setDraft(item);setError('');}}><Pencil size={13}/>Edit</button><button onClick={()=>onDiscuss([item.title,item.content,item.url].filter(Boolean).join('\n\n'))}>Discuss <ArrowUpRight size={13}/></button><button disabled={!online||busy} onClick={()=>void save({...item,status:item.status==='archived'?'open':'archived'})}>{item.status==='archived'?<><RotateCcw size={13}/>Restore</>:<><Archive size={13}/>Archive</>}</button></div></div>
    </article>)}</div>
  </section>;
}
