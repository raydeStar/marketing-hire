import {useEffect,useMemo,useRef,useState} from 'react';
import {BookOpen,CornerDownLeft,LayoutTemplate,ListChecks,MessageCircle,Search} from 'lucide-react';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import type {AppSummary,State} from '../types';
import type {View} from './shared';

type Entry={id:string;label:string;hint:string;group:string;icon:typeof Search;run:()=>void};

/** Ctrl/⌘+K: jump anywhere, or ask Marketing about anything. */
export function CommandPalette({state,views,onClose,onView,onTask,onAsset,onWiki,onAsk}:{
  state:MarketingState|null;views:{view:View;label:string;icon:typeof Search}[];onClose:()=>void;onView:(view:View)=>void;
  onTask:(id:string)=>void;onAsset:(id:string)=>void;onWiki:(id:string)=>void;onAsk?:(text:string)=>void;
}){
  const [query,setQuery]=useState(''),[active,setActive]=useState(0);
  const [pages,setPages]=useState<{id:string;title:string;kind:string}[]>([]),[apps,setApps]=useState<AppSummary[]>([]);
  const input=useRef<HTMLInputElement>(null),dialog=useRef<HTMLDialogElement>(null);
  useEffect(()=>{dialog.current?.showModal();input.current?.focus();
    void api<{id:string;title:string;kind:string;status:string}[]>('/company-wiki').then(list=>setPages(list.filter(page=>page.status!=='archived'))).catch(()=>{});
    void api<State>('/state').then(legacy=>setApps((legacy.artifacts||[]).filter(app=>!app.archived))).catch(()=>{});},[]);
  const entries=useMemo(()=>{
    const q=query.trim().toLowerCase();
    const match=(text:string)=>!q||text.toLowerCase().includes(q);
    const list:Entry[]=[
      ...views.filter(item=>match(item.label)).map(item=>({id:'view:'+item.view,label:item.label,hint:'Go to',group:'Views',icon:item.icon,run:()=>onView(item.view)})),
      ...(state?.tasks||[]).filter(task=>task.status!=='done'&&match(task.title)).slice(0,6).map(task=>({id:'task:'+task.id,label:task.title,hint:'Task',group:'Tasks',icon:ListChecks,run:()=>onTask(task.id)})),
      ...pages.filter(page=>match(page.title)).slice(0,6).map(page=>({id:'wiki:'+page.id,label:page.title,hint:'Wiki',group:'Wiki',icon:BookOpen,run:()=>onWiki(page.id)})),
      ...apps.filter(app=>match(app.title)).slice(0,6).map(app=>({id:'asset:'+app.id,label:app.title,hint:'Page',group:'Assets',icon:LayoutTemplate,run:()=>onAsset(app.id)}))
    ];
    if(q&&onAsk)list.push({id:'ask',label:`Ask ${state?.employee.name||'Marketing'}: “${query.trim()}”`,hint:'Chat',group:'Ask',icon:MessageCircle,run:()=>onAsk(query.trim())});
    return list;
  },[query,state,pages,apps,views]);
  useEffect(()=>setActive(0),[query]);
  function choose(entry?:Entry){if(!entry)return;onClose();entry.run();}
  return <dialog ref={dialog} className="fe-dialog fe-palette" aria-label="Search and jump" onCancel={event=>{event.preventDefault();onClose();}} onClick={event=>{if(event.target===event.currentTarget)onClose();}}>
    <label className="fe-palette-input"><Search size={18}/><input ref={input} value={query} onChange={event=>setQuery(event.target.value)} placeholder="Search tasks, wiki, pages… or ask Marketing" aria-label="Search and jump"
      onKeyDown={event=>{if(event.key==='ArrowDown'){event.preventDefault();setActive(value=>Math.min(entries.length-1,value+1));}else if(event.key==='ArrowUp'){event.preventDefault();setActive(value=>Math.max(0,value-1));}else if(event.key==='Enter'){event.preventDefault();choose(entries[active]);}}}/>
      <kbd>Esc</kbd></label>
    <div className="fe-palette-list" role="listbox" aria-label="Results">
      {entries.map((entry,index)=>{const first=index===0||entries[index-1].group!==entry.group;const Icon=entry.icon;return <div key={entry.id}>
        {first&&<p className="fe-palette-group">{entry.group}</p>}
        <button type="button" role="option" aria-selected={index===active} className="fe-palette-item" onMouseEnter={()=>setActive(index)} onClick={()=>choose(entry)}>
          <Icon size={16}/><span>{entry.label}</span><small>{entry.hint}</small>{index===active&&<CornerDownLeft size={14}/>}</button></div>;})}
      {!entries.length&&<p className="fe-muted fe-palette-empty">Nothing matches. Keep typing to ask Marketing instead.</p>}
    </div>
  </dialog>;
}
