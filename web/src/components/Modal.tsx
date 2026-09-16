import {useEffect,useRef,type ReactNode} from 'react';
import {X} from 'lucide-react';
export function Modal({title,onClose,children,className=''}:{title:string;onClose:()=>void;children:ReactNode;className?:string}){
 const dialog=useRef<HTMLDialogElement>(null),close=useRef(onClose);close.current=onClose;
 useEffect(()=>{const prior=document.activeElement as HTMLElement|null;dialog.current?.showModal();return()=>{dialog.current?.close();if(prior?.isConnected)prior.focus({preventScroll:true});};},[]);
 return <dialog className={'study-modal '+className} ref={dialog} aria-label={title} onCancel={e=>{e.preventDefault();e.stopPropagation();close.current();}} onKeyDown={e=>{if(e.key==='Escape')e.stopPropagation();}} onClick={e=>{if(e.target===e.currentTarget){const r=e.currentTarget.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)close.current();}}}><header><h2>{title}</h2><button autoFocus aria-label="Close dialog" onClick={onClose}><X size={18}/></button></header>{children}</dialog>;
}
