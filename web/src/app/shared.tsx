import {createContext,useCallback,useEffect,useRef,useState,type ReactNode} from 'react';
import {X} from 'lucide-react';
import {api} from '../api';
import {requestId,type MarketingState} from '../components/MarketingPanels';

export type Department={id:string;name:string;purpose:string};
export type Member={id:string;name:string;role:string;departmentId:string|null;kind:'employee'|'manager';runtimeKey:string|null};
export type Directory={version:number;departments:Department[];agents:Member[];updatedAt?:string};

export type View='today'|'chat'|'inbox'|'campaigns'|'tasks'|'assets'|'wiki'|'team'|'history'|'settings';
export const views:View[]=['today','chat','inbox','campaigns','tasks','assets','wiki','team','history','settings'];

/** One poll for the whole workspace, so every view reads the same snapshot. */
export function useWorkspaceData(){
  const [state,setState]=useState<MarketingState|null>(null),[directory,setDirectory]=useState<Directory|null>(null);
  const [error,setError]=useState(''),[loaded,setLoaded]=useState(false);
  const running=useRef<Promise<void>|null>(null),queued=useRef<Promise<void>|null>(null),sequence=useRef(0),directoryAt=useRef(0);
  const read=useCallback(async()=>{
    const current=++sequence.current;
    try{
      // The team directory rarely changes; re-read it once a minute instead of on every poll.
      const readDirectory=Date.now()-directoryAt.current>60000;
      const [marketing,organization]=await Promise.all([api<MarketingState>('/marketing/state'),readDirectory?api<{directory:Directory}>('/organization'):Promise.resolve(null)]);
      if(current!==sequence.current)return;
      setState(marketing);if(organization){setDirectory(organization.directory);directoryAt.current=Date.now();}setError('');
    }catch(cause){if(current===sequence.current)setError((cause as Error).message);}
    finally{setLoaded(true);}
  },[]);
  // A caller that just wrote must see its write: if a read is already in flight (it may predate the write),
  // wait for it and then read once more. Concurrent callers share that one follow-up read.
  const refresh=useCallback(():Promise<void>=>{
    if(!running.current){running.current=read().finally(()=>{running.current=null;});return running.current;}
    queued.current??=running.current.then(()=>{queued.current=null;running.current=read().finally(()=>{running.current=null;});return running.current;});
    return queued.current;
  },[read]);
  useEffect(()=>{
    void refresh();
    // Poll while visible; with Inbox notifications on, keep a slow background poll so they can fire.
    let tick=0;
    const timer=setInterval(()=>{
      tick++;
      const notify=(()=>{try{return localStorage.getItem('fe-notify-inbox')==='yes';}catch{return false;}})();
      if(document.visibilityState==='visible'||(notify&&tick%4===0))void refresh();
    },8000);
    const visible=()=>{if(document.visibilityState==='visible')void refresh();};
    document.addEventListener('visibilitychange',visible);
    return()=>{clearInterval(timer);document.removeEventListener('visibilitychange',visible);};
  },[refresh]);
  return {state,directory,error,loaded,refresh,setDirectory};
}

/** Reuse a request ID while the same change is retried, so a lost reply never duplicates work. */
export function useAttempt(){
  const attempt=useRef<{signature:string;id:string}|null>(null);
  return {
    id(signature:string){const id=attempt.current?.signature===signature?attempt.current.id:requestId();attempt.current={signature,id};return id;},
    done(){attempt.current=null;}
  };
}

export type EmployeeStatus={label:string;tone:'live'|'busy'|'warn'|'off'};
export function employeeStatus(state:MarketingState|null,hostOnline:boolean,readError:string):EmployeeStatus{
  if(!hostOnline)return {label:'Host offline',tone:'off'};
  if(!state)return {label:readError?'Can’t reach workspace':'Connecting…',tone:readError?'warn':'off'};
  if(state.canConfigure===false)return {label:'Shared review',tone:'live'};
  switch(state.connection.status){
    case 'connected':return {label:'Online',tone:'live'};
    case 'busy':return {label:'Working…',tone:'busy'};
    case 'auth_required':return {label:'Needs sign-in',tone:'warn'};
    default:return {label:'Offline',tone:'off'};
  }
}

export function initials(name:string){
  const parts=name.trim().split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0]||'M')+(parts[1]?.[0]||'')).toUpperCase();
}

/** Something was just added to the employee's queue: the cockpit refreshes at once, so it lands in Up next where it can be seen. */
export const queuedEvent='fe-queued';
/** Something tagged in chat (@): its key and the name it shows by. The item itself goes with the message. */
export type ChatRef={key:string;title:string};
export const discussEvent='fe-discuss';
/** "Discuss with Zero": tags the item in the main chat and opens it there, instead of a separate thread. */
export const discussInChat=(ref:ChatRef)=>window.dispatchEvent(new CustomEvent(discussEvent,{detail:ref}));
export const announceQueued=()=>window.dispatchEvent(new Event(queuedEvent));
export function plain(value:string){return value.replace(/[#*`>_\[\]]/g,'').replace(/\s+/g,' ').trim();}

export function Dialog({title,wide=false,onClose,children}:{title:string;wide?:boolean;onClose:()=>void;children:ReactNode}){
  const ref=useRef<HTMLDialogElement>(null);
  useEffect(()=>{
    const dialog=ref.current!;const opener=document.activeElement instanceof HTMLElement&&document.activeElement!==document.body?document.activeElement:null;
    if(!dialog.open)dialog.showModal();
    // Start where the work is: the first field, else the first control in the body; the close button is last resort.
    const body=dialog.querySelector('.fe-dialog-body');
    const first=body?.querySelector<HTMLElement>('[autofocus], input:not([type=hidden]):not(:disabled), textarea:not(:disabled), select:not(:disabled)')||body?.querySelector<HTMLElement>('button:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])');
    first?.focus();
    return()=>{dialog.close();if(opener?.isConnected)opener.focus();};
  },[]);
  return <dialog ref={ref} className={'fe-dialog'+(wide?' wide':'')} aria-label={title}
    onCancel={event=>{event.preventDefault();onClose();}}
    onClick={event=>{if(event.target===event.currentTarget)onClose();}}>
    <header><h2>{title}</h2><button type="button" className="fe-icon-button" onClick={onClose} aria-label="Close dialog"><X size={19}/></button></header>
    <div className="fe-dialog-body">{children}</div>
  </dialog>;
}

/** A menu of buttons from the keyboard: focus starts on the first item, arrows and Home/End move, Escape closes and gives
 * focus back to the button that opened it, Tab closes and moves on. */
export function useMenuKeys(open:boolean,onClose:()=>void){
  const ref=useRef<HTMLDivElement>(null),opener=useRef<HTMLElement|null>(null);
  useEffect(()=>{if(!open)return;opener.current=document.activeElement instanceof HTMLElement?document.activeElement:null;requestAnimationFrame(()=>ref.current?.querySelector<HTMLElement>('[role=menuitem]:not(:disabled)')?.focus());},[open]);
  function close(){onClose();opener.current?.focus();}
  function onKeyDown(event:React.KeyboardEvent){
    const items=[...(ref.current?.querySelectorAll<HTMLElement>('[role=menuitem]:not(:disabled)')||[])];const at=items.indexOf(document.activeElement as HTMLElement);
    const move=(index:number)=>{event.preventDefault();items[(index+items.length)%items.length]?.focus();};
    if(event.key==='ArrowDown')move(at+1);else if(event.key==='ArrowUp')move(at<0?items.length-1:at-1);else if(event.key==='Home')move(0);else if(event.key==='End')move(items.length-1);
    else if(event.key==='Escape'){event.preventDefault();event.stopPropagation();close();}
    else if(event.key==='Tab')onClose();
  }
  return {ref,onKeyDown,close};
}

/** Markdown headings placed under the heading they sit beneath: a document's "# Title" inside a window is that window's
 * h2, and so on. The class keeps the size the author's level had. */
export function shiftedHeadings(by:number){
  const at=(level:number)=>({children}:{children?:ReactNode})=>{const Tag=('h'+Math.min(6,level+by)) as 'h2';return <Tag className={'md-h'+level}>{children}</Tag>;};
  return {h1:at(1),h2:at(2),h3:at(3),h4:at(4),h5:at(5),h6:at(6)};
}

export function PageHead({title,subtitle,children}:{title:string;subtitle?:ReactNode;children?:ReactNode}){
  return <header className="fe-page-head"><div><h1>{title}</h1>{subtitle&&<p>{subtitle}</p>}</div>{children&&<div className="fe-page-actions">{children}</div>}</header>;
}

export function Empty({icon,title,children}:{icon:ReactNode;title:string;children?:ReactNode}){
  return <div className="fe-empty">{icon}<h3>{title}</h3>{children}</div>;
}

export function download(name:string,content:string,type='text/markdown;charset=utf-8'){
  const url=URL.createObjectURL(new Blob([content],{type}));
  const link=document.createElement('a');link.href=url;link.download=name;link.click();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
}

/** Who is signed in, so the conversation can call their own messages “You”. */
export const MeContext=createContext<{id:string;name:string}|null>(null);
