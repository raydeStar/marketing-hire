import {useEffect,useMemo,useRef,useState} from 'react';
import {CornerDownLeft,ListChecks,MessageCircle,Search,type LucideIcon} from 'lucide-react';
import type {MarketingState} from '../components/MarketingPanels';
import {searchLibrary,stem,type Library} from './library';
import {kindIcon} from './LibraryView';

type Entry={id:string;label:string;detail:string;hint:string;group:string;icon:LucideIcon;run:()=>void};

/** Ctrl/⌘+K: ranked search across the Library and tasks, or hand the question to the employee. */
export function CommandPalette({state,library,onClose,onOpen,onAsk}:{state:MarketingState|null;library:Library;onClose:()=>void;onOpen:(key:string)=>void;onAsk?:(text:string)=>void}){
  const [query,setQuery]=useState(''),[active,setActive]=useState(0);
  const input=useRef<HTMLInputElement>(null),dialog=useRef<HTMLDialogElement>(null);
  useEffect(()=>{dialog.current?.showModal();input.current?.focus();},[]);
  const entries=useMemo(()=>{
    const q=query.trim();
    const live=library.items.filter(item=>!item.archived);
    const found=q?searchLibrary(live,q,8):live.filter(item=>library.meta.pins.includes(item.key)||item.updated).sort((a,b)=>Number(library.meta.pins.includes(b.key))-Number(library.meta.pins.includes(a.key))||b.updated-a.updated).slice(0,6).map(item=>({item,snippet:item.summary}));
    const words=(q.toLowerCase().match(/[\p{L}\p{N}]+/gu)||[]).map(stem);
    const tasks=(state?.tasks||[]).filter(task=>task.status!=='done'&&(!q||words.every(word=>task.title.toLowerCase().includes(word)||task.next_action.toLowerCase().includes(word)))).slice(0,q?5:3);
    const list:Entry[]=[
      ...found.map(({item,snippet})=>({id:item.key,label:item.title,detail:snippet,hint:item.label,group:q?'Library':'Pinned and recent',icon:kindIcon[item.kind],run:()=>onOpen(item.key)})),
      ...tasks.map(task=>({id:'task:'+task.id,label:task.title,detail:task.next_action,hint:'Task',group:'Tasks',icon:ListChecks,run:()=>onOpen('task:'+task.id)}))
    ];
    if(q&&onAsk)list.push({id:'ask',label:`Ask ${state?.employee.name||'Marketing'}`,detail:q,hint:'Chat',group:'Ask',icon:MessageCircle,run:()=>onAsk(q)});
    return list;
  },[query,state,library.items,library.meta.pins]);
  useEffect(()=>setActive(0),[query]);
  function choose(entry?:Entry){if(!entry)return;onClose();entry.run();}
  return <dialog ref={dialog} className="fe-dialog fe-palette" aria-label="Search" onCancel={event=>{event.preventDefault();onClose();}} onClick={event=>{if(event.target===event.currentTarget)onClose();}}>
    <label className="fe-palette-input"><Search size={17}/><input ref={input} value={query} onChange={event=>setQuery(event.target.value)} placeholder="Search documents, pages, research and tasks" aria-label="Search"
      onKeyDown={event=>{if(event.key==='ArrowDown'){event.preventDefault();setActive(value=>Math.min(entries.length-1,value+1));}else if(event.key==='ArrowUp'){event.preventDefault();setActive(value=>Math.max(0,value-1));}else if(event.key==='Enter'){event.preventDefault();choose(entries[active]);}}}/>
      <kbd>Esc</kbd></label>
    <div className="fe-palette-list" role="listbox" aria-label="Results">
      {entries.map((entry,index)=>{const first=index===0||entries[index-1].group!==entry.group;const Icon=entry.icon;return <div key={entry.id}>
        {first&&<p className="fe-palette-group">{entry.group}</p>}
        <button type="button" role="option" aria-selected={index===active} className="fe-palette-item" onMouseEnter={()=>setActive(index)} onClick={()=>choose(entry)}>
          <Icon size={16}/><span><strong>{entry.label}</strong>{entry.detail&&<small>{entry.detail}</small>}</span><em>{entry.hint}</em>{index===active&&<CornerDownLeft size={14}/>}</button></div>;})}
      {!entries.length&&<p className="fe-muted fe-palette-empty">{query.trim()?'Nothing matches. Try fewer words.':'Nothing here yet.'}</p>}
    </div>
    <footer className="fe-palette-foot"><span><kbd>↑</kbd><kbd>↓</kbd> to move</span><span><kbd>Enter</kbd> to open</span><span>Matches related terms, like “customer” for “audience”</span></footer>
  </dialog>;
}
