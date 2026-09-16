import {useState} from 'react';
import {WebSearch} from './WebSearch';
import {Search} from 'lucide-react';
import type {State} from '../types';

export function StudySearch({data,onPage,onRun,onCollection,onChat,onArtifact,onFile}:{data:State;onPage:(path:string)=>void;onRun:(id:string)=>void;onCollection:(kind:string,id:string)=>void;onChat:(id:string)=>void;onArtifact:(id:string)=>void;onFile:(id:string)=>void}){
  const [query,setQuery]=useState(''),[pane,setPane]=useState('study');
  const needle=query.trim().toLocaleLowerCase();
  const entries=[
    ...(data.artifacts||[]).map(app=>({id:app.id,kind:'App',title:app.title+(app.archived?' · in Trash':''),text:app.description,open:()=>onArtifact(app.id)})),
    ...(data.uploads||[]).map(file=>({id:file.id,kind:file.mediaType.startsWith('image/')?'Image':'File',title:file.name+(file.archived?' · in Trash':''),text:file.name,open:()=>onFile(file.id)})),
    ...data.pages.map(page=>({id:page.path,kind:'Artifact',title:page.path,text:page.content,open:()=>onPage(page.path)})),
    ...(data.library||[]).map(item=>({id:item.id,kind:item.kind==='todo'?'To-do':item.kind==='idea'?'Idea':'Feed',title:item.title+(item.status==='archived'?' · archived':''),text:item.content,open:()=>onCollection(item.kind,item.id)})),
    ...data.chats.map(chat=>({id:chat.id,kind:'Conversation',title:chat.role==='user'?'You':'Thaddeus',text:chat.content,open:()=>onChat(chat.id)})),
    ...data.runs.map(run=>({id:run.id,kind:'Run',title:run.goal.objective,text:run.summary,open:()=>onRun(run.id)}))
  ];
  const matches=needle?entries.filter(entry=>(entry.title+' '+entry.text).toLocaleLowerCase().includes(needle)):[];
  return <section className="study-search"><p className="eyebrow">SOMEWHERE IN THE STUDY</p><h1>Search</h1><nav className="collection-filters" aria-label="Search locations"><button aria-pressed={pane==='study'} onClick={()=>setPane('study')}>Your study</button><button aria-pressed={pane==='web'} onClick={()=>setPane('web')}>Web</button></nav>{pane==='web'?<WebSearch connection={data.search}/>:<><p className="lead">Find saved work, ideas, conversations, and receipts. No model calls.</p><label className="search-input"><Search size={19}/><input autoFocus aria-label="Search your study" placeholder="A phrase, a thought, a filename…" value={query} maxLength={200} onChange={e=>setQuery(e.target.value)}/></label>
    <p role="status">{needle?`${matches.length} result${matches.length===1?'':'s'}${matches.length>100?' · showing the first 100':''}`:'Type to search the history retained on this host.'}</p>
    <div className="search-results">{matches.slice(0,100).map(entry=>{const index=entry.text.toLocaleLowerCase().indexOf(needle),start=Math.max(0,index-70);return <button key={entry.kind+entry.id} onClick={entry.open}><small>{entry.kind}</small><strong>{entry.title}</strong><span>{start>0?'…':''}{entry.text.slice(start,start+240)}{entry.text.length>start+240?'…':''}</span></button>;})}</div>
  </>}</section>;
}
