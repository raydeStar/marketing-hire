import {useState} from 'react';
import {api} from '../api';

type Candidate={url:string;title:string;publishedAt:number;comments:number|null};
export function MarketingSourcePicker({selected,onSelect}:{selected:string[];onSelect:(urls:string[])=>void}){
  const [query,setQuery]=useState('marketing'),[items,setItems]=useState<Candidate[]|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  async function search(){
    if(busy||query.trim().length<2)return;
    setBusy(true);setError('');setItems(null);
    try{setItems((await api<{candidates:Candidate[]}>('/marketing/sources/search?query='+encodeURIComponent(query.trim()))).candidates);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  function choose(url:string){
    const next=[...selected];const existing=next.indexOf(url);
    if(existing>=0)next[existing]='';else{const empty=next.findIndex(item=>!item.trim());if(empty<0)return;next[empty]=url;}
    onSelect(next);
  }
  return <details className="marketing-source-picker"><summary>Find current discussions</summary>
    <p>Search public Hacker News posts from the last 90 days. Search uses no model tokens. Choose two candidates; their pages are checked when the assignment starts.</p>
    <label>Public search terms<input value={query} maxLength={120} onChange={e=>setQuery(e.target.value)} onKeyDown={e=>{if(e.key==='Enter'){e.preventDefault();void search();}}}/></label>
    <button type="button" disabled={busy||query.trim().length<2} onClick={()=>void search()}>{busy?'Searching…':'Search discussions'}</button>
    {error&&<p role="alert">{error}</p>}{items?.length===0&&<p>No matching recent posts. Try different public terms or paste a discussion URL.</p>}
    {items&&<ul>{items.map(item=><li key={item.url}><a href={item.url} target="_blank" rel="noopener noreferrer">{item.title}</a><small>{new Date(item.publishedAt*1000).toLocaleDateString()}{item.comments===null?'':` · ${item.comments} comments`}</small><button type="button" aria-pressed={selected.includes(item.url)} disabled={!selected.includes(item.url)&&selected.every(s=>s.trim())} onClick={()=>choose(item.url)}>{selected.includes(item.url)?'Remove source':'Use source'}</button></li>)}</ul>}
  </details>;
}
