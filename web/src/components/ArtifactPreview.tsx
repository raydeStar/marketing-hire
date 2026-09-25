import {useEffect,useRef,useState} from 'react';
import {api} from '../api';
import type {ArtifactApp} from '../types';

export function ArtifactPreview({app,online,active=true,onSaved,onDirty}:{app:ArtifactApp;online:boolean;active?:boolean;onSaved:(app:ArtifactApp)=>void;onDirty?:(dirty:boolean)=>void}){
  const iframe=useRef<HTMLIFrameElement>(null),port=useRef<MessagePort|null>(null),current=useRef({app,online,active,onSaved,onDirty});
  current.current={app,online,active,onSaved,onDirty};
  const [error,setError]=useState(''),[reload,setReload]=useState(0),[ready,setReady]=useState(false),[dirty,setDirty]=useState(false);
  const writes=useRef<number[]>([]),saving=useRef(false),timer=useRef<ReturnType<typeof setTimeout>|undefined>(undefined);
  const scroll=useRef({x:0,y:0}),wasVisible=useRef(false);
  const code=JSON.stringify(app.definition.page);
  // New records can arrive quietly; a new room must wait for unfinished ink.
  const generation=useRef({code,id:crypto.randomUUID()});
  const loadedGeneration=useRef('');
  const pendingDesign=generation.current.code!==code&&dirty;
  if(generation.current.code!==code&&!dirty)generation.current={code,id:crypto.randomUUID()};
  const frameKey=generation.current.id+':'+reload;
  function state(value=current.current.app){
    const day=new Date();
    const visible=current.current.active&&!!iframe.current?.getClientRects().length;
    return {title:value.definition.title,fields:value.definition.fields,entries:value.entries,version:value.version,
      active:visible,readOnly:!current.current.online||!visible||value.archived||generation.current.code!==JSON.stringify(value.definition.page),theme:document.documentElement.dataset.theme||'dark',
      localDate:`${day.getFullYear()}-${String(day.getMonth()+1).padStart(2,'0')}-${String(day.getDate()).padStart(2,'0')}`};
  }
  function publishState(){
    const next=state();port.current?.postMessage({type:'state',state:next});
    if(next.active&&!wasVisible.current&&(scroll.current.x||scroll.current.y))port.current?.postMessage({type:'restore-scroll',...scroll.current});
    wasVisible.current=next.active;
  }
  useEffect(publishState,[app,online,active]);
  useEffect(()=>{
    const observer=new MutationObserver(publishState);
    observer.observe(document.documentElement,{attributes:true,attributeFilter:['data-theme']});
    return()=>{observer.disconnect();port.current?.close();clearTimeout(timer.current);};
  },[]);
  useEffect(()=>{
    const size=new ResizeObserver(publishState);
    if(iframe.current)size.observe(iframe.current);
    return()=>size.disconnect();
  },[frameKey]);
  useEffect(()=>{
    setReady(false);setError('');
    scroll.current={x:0,y:0};wasVisible.current=false;
    timer.current=setTimeout(()=>setError('It didn’t finish loading. Retry it; your saved content is unaffected.'),8000);
    return()=>clearTimeout(timer.current);
  },[frameKey]);
  function connect(){
    port.current?.close();port.current=null;
    if(loadedGeneration.current===frameKey){clearTimeout(timer.current);setReady(false);setError('The page left its app document. Retry the page to return to your app.');return;}
    loadedGeneration.current=frameKey;
    const channel=new MessageChannel();port.current=channel.port1;
    const reply=(message:unknown)=>{if(port.current===channel.port1)channel.port1.postMessage(message);};
    channel.port1.onmessage=async event=>{
      if(port.current!==channel.port1)return;
      const message=event.data;
      if(message?.type==='ready'){clearTimeout(timer.current);setReady(true);publishState();return;}
      if(message?.type==='error'){setError(String(message.message).slice(0,500));return;}
      if(message?.type==='dirty'&&typeof message.dirty==='boolean'){setDirty(message.dirty);current.current.onDirty?.(message.dirty);return;}
      // Chromium reports a zero scroll while hiding an iframe. Only the visible view sets its bookmark.
      if(message?.type==='scroll'){
        if(state().active&&[message.x,message.y].every(value=>typeof value==='number'&&Number.isFinite(value)&&value>=0&&value<=10000000))scroll.current={x:message.x,y:message.y};
        return;
      }
      if(message?.type!=='save'||typeof message.id!=='string'||!/^[a-f0-9]{32}$/.test(message.id))return;
      try{
        if(saving.current)throw new Error('A save is still in progress.');
        if(state().readOnly)throw new Error('This app is read-only right now.');
        const change=message.change;
        if(typeof message.version!=='string'||!/^[a-f0-9]{32}$/.test(message.version)||!change||Array.isArray(change)||
          Object.keys(change).some(key=>!['upserts','deleteIds'].includes(key))||!Array.isArray(change.upserts)||!Array.isArray(change.deleteIds)||
          change.upserts.length+change.deleteIds.length>100||JSON.stringify(change).length>60000)throw new Error('The app requested an unsupported data change.');
        writes.current=writes.current.filter(at=>at>Date.now()-60000);
        if(writes.current.length>=30)throw new Error('This app is saving too often. Wait a minute before trying again.');
        writes.current.push(Date.now());saving.current=true;
        try{
          // The frame supplies records, never a host URL, app id, definition or credential.
          const saved=await api<ArtifactApp>('/artifacts/'+current.current.app.id,{operationId:message.id,version:message.version,upserts:change.upserts,deleteIds:change.deleteIds},'PUT');
          current.current.onSaved(saved);reply({type:'result',id:message.id,state:state(saved)});
        }finally{saving.current=false;}
      }catch(reason){reply({type:'result',id:message.id,error:(reason as Error).message});}
    };
    iframe.current?.contentWindow?.postMessage({type:'thaddeus-connect'},'*',[channel.port2]);
  }
  function reloadPage(){if(dirty&&!window.confirm('Reload this app and discard its unfinished input? Saved records will stay.'))return;setDirty(false);onDirty?.(false);setReload(value=>value+1);}
  return <div className="generated-app">
    {pendingDesign&&<div className="app-notice" role="status">An updated design is ready. Your unfinished input is kept here; copy anything you need before loading it. <button onClick={reloadPage}>Load updated design</button></div>}
    {error&&<div className="app-preview-error" role="alert"><strong>This page didn’t open.</strong><p>{error}</p><p>Your saved records are safe. Open Edit to fix the page code, or restore an earlier version from History.</p><button onClick={reloadPage}>Retry page</button></div>}
    {!ready&&!error&&<p role="status">Opening your app…</p>}
    <iframe key={frameKey} ref={iframe} title={app.definition.title+' app'} src={'/api/artifacts/'+app.id+'/page?view='+encodeURIComponent(frameKey)}
      sandbox="allow-scripts allow-forms" allow="camera 'none'; microphone 'none'; geolocation 'none'; clipboard-read 'none'; clipboard-write 'none'; fullscreen 'none'" referrerPolicy="no-referrer" onLoad={connect}/>
  </div>;
}
