import {Fragment,useEffect,useRef,useState,type ReactNode} from 'react';
import Markdown from 'react-markdown';
import {WebsiteReadings} from './WebsiteReadings';
import {User,Shapes,ArrowUpRight,Copy,Check,RotateCcw,Pencil,ArrowDown,Cable} from 'lucide-react';
import type {Run,AppSummary,UploadFile} from '../types';
import './conversation.css';

type Message={id:string;role:string;content:string};
const active=(run?:Run)=>!!run&&['queued','running','awaitingApproval'].includes(run.state);
const retryable=(run:Run)=>run.goal.kind==='conversation'&&!run.execution&&!run.artifactResult&&!run.suggestIdeas&&!run.approval&&['failed','cancelled','needsAttention','succeeded'].includes(run.state);
export function Conversation({intro,connectionCard,connectionRunId,messages,runs,online,busy,onCancel,onRetry,onConnectionSetup,onEdit,onDetails,uploads,focusId,onArtifact,apps}:{intro?:ReactNode;connectionCard?:ReactNode;connectionRunId?:string;apps:AppSummary[];onArtifact:(id:string)=>void;focusId?:string;messages:Message[];runs:Run[];online:boolean;busy:boolean;onCancel:(id:string)=>void;onRetry:(run:Run)=>void;onConnectionSetup:(target:'google'|'mcp',product?:string,runId?:string)=>void;onEdit:(message:Message,run?:Run)=>void;onDetails:(id:string)=>void;uploads:UploadFile[]}){
  const pending=runs.find(r=>r.goal.kind==='conversation'&&!r.background&&active(r));
  const scroller=useRef<HTMLDivElement>(null),feed=useRef<HTMLElement>(null),following=useRef(true),scrollTimer=useRef<number|undefined>(undefined);
  const lastMessage=useRef<string|undefined>(undefined);
  const [away,setAway]=useState(false),[copied,setCopied]=useState<string|null>(null),[copyError,setCopyError]=useState('');
  const [selected,setSelected]=useState<Record<string,string>>({});
  const focusedRun=runs.find(run=>focusId===run.id+'-assistant');
  const focusedRoot=focusedRun?.conversationRetry?.rootId??focusedRun?.id;
  useEffect(()=>{if(focusedRun&&focusedRoot)setSelected(previous=>({...previous,[focusedRoot]:focusedRun.id}));},[focusId]);
  const byMessage=new Map(messages.map(m=>[m.id,m]));
  const roots=new Map(runs.filter(r=>!r.conversationRetry).map(r=>[r.id+'-user',r]));
  const connectionRoot=runs.find(run=>run.id===connectionRunId)?.conversationRetry?.rootId??connectionRunId;
  const connectionAttached=!!connectionRoot&&roots.has(connectionRoot+'-user');
  const ownedAnswers=new Set(runs.filter(r=>byMessage.has((r.conversationRetry?.rootId??r.id)+'-user')).map(r=>r.id+'-assistant'));
  const bottom=()=>{following.current=true;setAway(false);if(scroller.current)scroller.current.scrollTop=scroller.current.scrollHeight;};
  useEffect(()=>{
    const element=scroller.current,content=feed.current;if(!element||!content)return;
    const observer=new ResizeObserver(()=>{if(element.clientHeight&&following.current)element.scrollTop=element.scrollHeight;});
    observer.observe(element);observer.observe(content);return()=>observer.disconnect();
  },[]);
  useEffect(()=>{
    const element=scroller.current,workspace=element?.closest('.conversation-workspace');if(!element||!workspace)return;
    const forward=(event:Event)=>{const wheel=event as WheelEvent;if(element.contains(wheel.target as Node)||!wheel.deltaY)return;const scale=wheel.deltaMode===1?16:wheel.deltaMode===2?element.clientHeight:1;element.scrollTop+=wheel.deltaY*scale;wheel.preventDefault();};
    workspace.addEventListener('wheel',forward,{passive:false});return()=>{workspace.removeEventListener('wheel',forward);if(scrollTimer.current)window.clearTimeout(scrollTimer.current);};
  },[]);
  useEffect(()=>{
    if(focusId){following.current=false;const message=document.getElementById('chat-'+focusId);message?.focus();message?.scrollIntoView({block:'center'});}
    else if(scroller.current){if(messages.at(-1)?.role==='user'&&messages.at(-1)?.id!==lastMessage.current)following.current=true;if(following.current)bottom();}
    lastMessage.current=messages.at(-1)?.id;
  },[messages.length,pending?.draftText,pending?.id,focusId,focusedRoot?selected[focusedRoot]:undefined]);
  useEffect(()=>{if(!copied)return;const timer=setTimeout(()=>setCopied(null),2000);return()=>clearTimeout(timer);},[copied]);
  async function copy(message:Message){try{await navigator.clipboard.writeText(message.content);setCopied(message.id);setCopyError('');}catch{setCopyError('Clipboard access is unavailable. Select the message text to copy it.');}}
  const retry=(run:Run)=>{setSelected(previous=>{const next={...previous};delete next[run.conversationRetry?.rootId??run.id];return next;});onRetry(run);};
  const retryButton=(run:Run,family:Run[])=>!family.some(attempt=>attempt.artifactResult)&&<button type="button" disabled={!online||busy||!!pending||family.some(active)} onClick={()=>retry(run)} title="Start a new attempt with this message and its original context and limits, using your current model. This uses additional tokens."><RotateCcw size={14}/>{run.state==='succeeded'?'Try again':'Retry'}</button>;
  const connectionRun=(family:Run[])=>family.find(attempt=>attempt.connectionSetup);
  const connectionButton=(run:Run)=><button type="button" disabled={!online||busy} onClick={()=>onConnectionSetup(run.connectionSetup!,run.connectionSetupProduct,run.id)} title="Reopen the secure connection card. This does not call the model."><Cable size={14}/>Continue connection setup</button>;
  const standaloneConnection=()=> <article className="chat assistant"><span className="chat-avatar" aria-hidden="true">T</span><div className="chat-body"><small>Thaddeus</small>{connectionCard}</div></article>;
  function article(message:Message,run?:Run,family:Run[]=[],card?:ReactNode){return <article id={'chat-'+message.id} tabIndex={-1} className={'chat '+message.role}>
    <span className="chat-avatar" aria-hidden="true">{message.role==='user'?<User size={16} strokeWidth={1.8}/>:'T'}</span>
    <div className="chat-body"><small>{message.role==='user'?'You':'Thaddeus'}</small><Markdown>{message.content}</Markdown>
      {message.role==='assistant'&&<WebsiteReadings run={run}/>}
      {message.role==='assistant'&&run?.artifactResult&&!run.artifactResult.deleted&&<button className="chat-artifact" onClick={()=>onArtifact(run.artifactResult!.id)}><Shapes size={23}/><span><strong>{apps.find(app=>app.id===run.artifactResult!.id)?.title||'Open app'}</strong><small>{run.artifactResult.description}</small></span><ArrowUpRight size={16}/></button>}
      {message.role==='user'&&run?.uploadIds?.map(id=>{const file=uploads.find(f=>f.id===id);return file?<a className="chat-upload" key={id} href={'/api/uploads/'+id+'/content?download=1'}>{file.name}</a>:null;})}
      {card}
      <div className="chat-actions"><button type="button" aria-label={copied===message.id?'Copied message':'Copy message'} title="Copy message" onClick={()=>copy(message)}>{copied===message.id?<Check size={14}/>:<Copy size={14}/>}</button>
        {message.role==='user'&&<button type="button" aria-label="Edit and resend message" title="Edit a copy in the composer" disabled={busy} onClick={()=>onEdit(message,run)}><Pencil size={14}/></button>}
        {message.role==='assistant'&&run&&(connectionRun(family)?!card&&connectionButton(connectionRun(family)!):retryable(run)&&retryButton(run,family))}
      </div>
    </div>
  </article>;}
  return <div className="conversation-history"><div ref={scroller} className="conversation-scroll" onScroll={event=>{const element=event.currentTarget;element.classList.add('is-scrolling');if(scrollTimer.current)window.clearTimeout(scrollTimer.current);scrollTimer.current=window.setTimeout(()=>element.classList.remove('is-scrolling'),700);if(element.clientHeight){following.current=element.scrollHeight-element.clientHeight-element.scrollTop<80;setAway(!following.current);}}}>{intro}<section ref={feed} className="conversation-feed" aria-label="Conversation">
    {copyError&&<p role="status" className="chat-copy-error">{copyError}</p>}
    {messages.filter(m=>!ownedAnswers.has(m.id)).map(message=>{
      const root=roots.get(message.id);if(!root)return <Fragment key={message.id}>{article(message)}</Fragment>;
      const family=runs.filter(r=>(r.conversationRetry?.rootId??r.id)===root.id).sort((a,b)=>a.created.localeCompare(b.created));
      const current=family.find(r=>r.id===selected[root.id])??family.at(-1)??root;
      const answer=byMessage.get(current.id+'-assistant');
      return <Fragment key={message.id}>{article(message,root,family)}
        {family.length>1&&<div className="chat-attempts"><label>Reply <select aria-label="Reply attempt" value={current.id} onChange={e=>setSelected({...selected,[root.id]:e.target.value})}>{family.map((r,i)=><option key={r.id} value={r.id}>{i+1} of {family.length} · {r.state==='succeeded'?'complete':r.state==='needsAttention'?'stopped':r.state}</option>)}</select></label><small>Each attempt is kept in the log.</small></div>}
        {active(current)&&<div className="chat-recovery chat-task-status" role="status"><p>{current.state==='awaitingApproval'?current.summary:current.background?"I've begun work on this request. You can keep chatting; I'll let you know when it's done.":current.summary.startsWith('Reading ')?current.summary:'Working on your request\u2026'}</p>{current.draftText&&<Markdown>{current.draftText}</Markdown>}{current.state==='awaitingApproval'?<button className="primary" disabled={!online||busy} onClick={()=>onDetails(current.id)}>Review exact action</button>:<button disabled={!online||busy} onClick={()=>onCancel(current.id)}>Cancel task</button>}</div>}
        {['failed','cancelled','needsAttention'].includes(current.state)&&<div className="chat-recovery"><p role="status">{current.summary}</p>{connectionRun(family)?<>{connectionButton(connectionRun(family)!)}<small>This request uses the secure connection form; no model retry is needed.</small></>:retryable(current)&&<>{retryButton(current,family)}<small>A new attempt uses additional tokens.</small></>}</div>}
        {answer?article(answer,current,family,connectionRoot===root.id?connectionCard:undefined):connectionRoot===root.id&&connectionCard&&standaloneConnection()}
      </Fragment>;
    })}
    {connectionCard&&!connectionAttached&&standaloneConnection()}
  </section></div>{away&&<button className="chat-jump" onClick={bottom}><ArrowDown size={15}/>Latest messages</button>}</div>;
}
