import {useEffect,useRef,useState} from 'react';
import {api} from '../api';
import type {ArtifactApp} from '../types';

export function ArtifactPreview({app,online,onSaved}:{app:ArtifactApp;online:boolean;onSaved:(app:ArtifactApp)=>void}){
  const iframe=useRef<HTMLIFrameElement>(null),port=useRef<MessagePort|null>(null),current=useRef({app,online,onSaved});
  current.current={app,online,onSaved};
  const [error,setError]=useState(''),[reload,setReload]=useState(0),[ready,setReady]=useState(false);
  const writes=useRef<number[]>([]),saving=useRef(false),timer=useRef<ReturnType<typeof setTimeout>|undefined>(undefined);
  const code=JSON.stringify(app.definition.page);
  // Only a design change remounts the frame. Data updates preserve its form drafts and tabs.
  const generation=useRef({code,id:crypto.randomUUID()});
  const loadedGeneration=useRef('');
  if(generation.current.code!==code)generation.current={code,id:crypto.randomUUID()};
  const frameKey=generation.current.id+':'+reload;
  function state(value=current.current.app){
    const day=new Date();
    return {title:value.definition.title,fields:value.definition.fields,entries:value.entries,version:value.version,
      readOnly:!current.current.online||value.archived,theme:document.documentElement.dataset.theme||'dark',
      localDate:`${day.getFullYear()}-${String(day.getMonth()+1).padStart(2,'0')}-${String(day.getDate()).padStart(2,'0')}`};
  }
  useEffect(()=>{port.current?.postMessage({type:'state',state:state()});},[app,online]);
  useEffect(()=>{
    const observer=new MutationObserver(()=>port.current?.postMessage({type:'state',state:state()}));
    observer.observe(document.documentElement,{attributes:true,attributeFilter:['data-theme']});
    return()=>{observer.disconnect();port.current?.close();clearTimeout(timer.current);};
  },[]);
  useEffect(()=>{
    setReady(false);setError('');
    timer.current=setTimeout(()=>setError('This page did not finish opening. Retry it, or use Data & history to recover your records.'),8000);
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
      if(message?.type==='ready'){clearTimeout(timer.current);setReady(true);reply({type:'state',state:state()});return;}
      if(message?.type==='error'){setError(String(message.message).slice(0,500));return;}
      if(message?.type!=='save'||typeof message.id!=='string'||!/^[a-f0-9]{32}$/.test(message.id))return;
      try{
        if(saving.current)throw new Error('A save is still in progress.');
        if(!current.current.online||current.current.app.archived)throw new Error('This app is read-only right now.');
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
  return <div className="generated-app">
    {error&&<div className="app-preview-error" role="alert"><strong>The app needs a little repair.</strong><p>{error}</p><p>Open chat to ask Thaddeus to fix it. Your saved records are still available in Data & history.</p><button onClick={()=>setReload(value=>value+1)}>Retry page</button></div>}
    {!ready&&!error&&<p role="status">Opening your app…</p>}
    <iframe key={frameKey} ref={iframe} title={app.definition.title+' app'} src={'/api/artifacts/'+app.id+'/page?view='+encodeURIComponent(frameKey)}
      sandbox="allow-scripts allow-forms" allow="camera 'none'; microphone 'none'; geolocation 'none'; clipboard-read 'none'; clipboard-write 'none'; fullscreen 'none'" referrerPolicy="no-referrer" onLoad={connect}/>
  </div>;
}
