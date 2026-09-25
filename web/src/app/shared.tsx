import {useCallback,useEffect,useRef,useState,type ReactNode} from 'react';
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
  const busy=useRef(false),sequence=useRef(0);
  const refresh=useCallback(async()=>{
    if(busy.current)return;busy.current=true;const current=++sequence.current;
    try{
      const [marketing,organization]=await Promise.all([api<MarketingState>('/marketing/state'),api<{directory:Directory}>('/organization')]);
      if(current!==sequence.current)return;
      setState(marketing);setDirectory(organization.directory);setError('');
    }catch(cause){if(current===sequence.current)setError((cause as Error).message);}
    finally{busy.current=false;setLoaded(true);}
  },[]);
  useEffect(()=>{
    void refresh();
    const timer=setInterval(()=>{if(document.visibilityState==='visible')void refresh();},8000);
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

export function plain(value:string){return value.replace(/[#*`>_\[\]]/g,'').replace(/\s+/g,' ').trim();}

export function Dialog({title,wide=false,onClose,children}:{title:string;wide?:boolean;onClose:()=>void;children:ReactNode}){
  const ref=useRef<HTMLDialogElement>(null);
  useEffect(()=>{const dialog=ref.current!;if(!dialog.open)dialog.showModal();return()=>dialog.close();},[]);
  return <dialog ref={ref} className={'fe-dialog'+(wide?' wide':'')} aria-label={title}
    onCancel={event=>{event.preventDefault();onClose();}}
    onClick={event=>{if(event.target===event.currentTarget)onClose();}}>
    <header><h2>{title}</h2><button type="button" className="fe-icon-button" onClick={onClose} aria-label="Close dialog"><X size={19}/></button></header>
    <div className="fe-dialog-body">{children}</div>
  </dialog>;
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
