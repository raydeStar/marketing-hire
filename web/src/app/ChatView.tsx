import {useContext,useEffect,useLayoutEffect,useRef,useState,type ReactNode} from 'react';
import {ArrowUp,BookOpen,Check,CircleAlert,Copy,Lightbulb,ListChecks,LoaderCircle,NotebookPen,PenLine,Search,Sparkles,Target} from 'lucide-react';
import Markdown,{defaultUrlTransform} from 'react-markdown';
import {tablesToLists} from './markdownTables';
import {api} from '../api';
import {readableTime,requestId,type MarketingMessage,type MarketingState,type MarketingTask} from '../components/MarketingPanels';
import {MeContext,initials,plain,type EmployeeStatus} from './shared';
import {ReplyActionCards,UpdateCard,parseActions,useUpdates} from './ChatActions';
import type {ShiftView} from './shifts';

const timeZone=(()=>{try{return Intl.DateTimeFormat().resolvedOptions().timeZone;}catch{return undefined;}})();

/** Turn a reply into lasting work: copy it, keep it in the wiki, or make it a task. */
function ReplyActions({content,canWrite,onRefresh}:{content:string;canWrite:boolean;onRefresh:()=>Promise<void>}){
  const [done,setDone]=useState<string|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const title=plain(content.split(/\r?\n/).find(line=>line.trim())||'Saved reply').replace(/[:.]+$/,'').slice(0,80)||'Saved reply';
  async function run(kind:string,work:()=>Promise<unknown>){
    if(busy)return;setBusy(true);setError('');
    try{await work();setDone(kind);setTimeout(()=>setDone(current=>current===kind?null:current),2500);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <div className="fe-msg-actions">
    <button type="button" className="fe-ghost" onClick={()=>void run('copy',()=>navigator.clipboard.writeText(content))}>{done==='copy'?<Check size={14}/>:<Copy size={14}/>} {done==='copy'?'Copied':'Copy'}</button>
    <button type="button" className="fe-ghost" disabled={busy} onClick={()=>void run('wiki',()=>api('/company-wiki',{requestId:requestId(),id:null,version:0,scope:'company',scopeId:'company',title,body:content.slice(0,12000),kind:'fact',status:'draft'},'PUT'))}>{done==='wiki'?<Check size={14}/>:<BookOpen size={14}/>} {done==='wiki'?'Saved to the Library':'Save to Library'}</button>
    <button type="button" className="fe-ghost" disabled={busy||!canWrite} onClick={()=>void run('task',async()=>{await api('/marketing/tasks',{requestId:requestId(),title:title.slice(0,160),status:'ready',priority:'normal',next_action:plain(content).slice(0,2000),action_state:'agent_ready'});await onRefresh();})}>{done==='task'?<Check size={14}/>:<ListChecks size={14}/>} {done==='task'?'Task created':'Make a task'}</button>
    {error&&<small className="fe-msg-action-error" role="alert">{error}</small>}
  </div>;
}

const suggestions=[
  {icon:Target,text:'What should we focus on this week?',hint:'A short, prioritized plan'},
  {icon:Search,text:'Research what our audience is struggling with right now.',hint:'Cited public sources'},
  {icon:PenLine,text:'Draft three post angles for our offer.',hint:'Drafts only, nothing is posted'},
  {icon:Lightbulb,text:'Read my business brief and tell me what’s missing.',hint:'Sharpen the brief'}
];

/** Group each onboarding exchange: from its kickoff to the reply that carries the drafted brief. */
function foldOnboarding(list:MarketingMessage[]):({message:MarketingMessage}|{group:MarketingMessage[]})[]{
  const out:({message:MarketingMessage}|{group:MarketingMessage[]})[]=[];
  let group:MarketingMessage[]|null=null,closing=false;
  for(const message of list){
    const user=message.role==='user';
    if(!group&&user&&message.content.startsWith('Onboarding:')){group=[message];closing=message.content.startsWith('Onboarding: please read');continue;}
    if(group){
      group.push(message);
      if(user&&message.content.startsWith('Thanks. Now turn our onboarding'))closing=true;
      else if(closing&&!user){out.push({group});group=null;closing=false;}
      continue;
    }
    out.push({message});
  }
  if(group)out.push({group});
  return out;
}

/** The employee conversation. Used full-page in Chat and inside a task's detail view. */
/** The employee cites what it used as [Title](wiki:id); those open the item here instead of being dropped as unknown links. */
const itemLink=/^(wiki|source|campaign|media|task|draft):[A-Za-z0-9_-]+$/;
const keepItemLinks=(url:string)=>itemLink.test(url)?url:defaultUrlTransform(url);

export function Conversation({state,task,canWrite,status,prefill,autoSend=false,onPrefillUsed,onRefresh,onOpenBrief,compact=false,headerActions,introExtra,owner=false,shifts=null,onNavigate}:{
  state:MarketingState;task?:MarketingTask;canWrite:boolean;status?:EmployeeStatus;prefill?:string;autoSend?:boolean;onPrefillUsed?:()=>void;headerActions?:ReactNode;introExtra?:ReactNode;
  onRefresh:()=>Promise<void>;onOpenBrief?:()=>void;compact?:boolean;owner?:boolean;shifts?:ShiftView|null;onNavigate?:(target:string)=>void;
}){
  const sessionKey=task?.conversation_key||state.employee.sessionKey;
  const draftKey='employee-draft:'+sessionKey;
  const [draft,setDraft]=useState(()=>{try{return localStorage.getItem(draftKey)||'';}catch{return '';}});
  const [sending,setSending]=useState(false),[notice,setNotice]=useState(''),[failed,setFailed]=useState('');
  const [reviewedUnknown,setReviewedUnknown]=useState<string|null>(null);
  const lastAttempt=useRef<{id:string;content:string}|null>(null);
  const scroller=useRef<HTMLDivElement>(null),input=useRef<HTMLTextAreaElement>(null),stick=useRef(true);
  const name=state.employee.name||'Marketing';
  const messages=state.messages.filter(message=>message.sessionKey===sessionKey&&(task?message.taskId===task.id:!message.taskId));
  const latest=[...state.requests].reverse().find(item=>item.sessionKey===sessionKey);
  const unresolved=latest&&(latest.status==='pending'||(latest.status==='unknown'&&reviewedUnknown!==latest.requestId))?latest:undefined;
  const blocked=state.chatBlockedReason||(state.runway?.project.active_execution?'Marketing is finishing an assignment step.':null);
  const briefMissing=!task&&(!state.profile.product_summary.trim()||!state.profile.goals.trim());
  const waiting=sending||unresolved?.status==='pending';
  // Updates from the host's own records (drafts, posts, shifts) are told in the main conversation, with one-click answers.
  const me=useContext(MeContext);
  const feed=useUpdates(state,shifts,!task&&!compact&&!!onNavigate);
  const navigate=onNavigate||(()=>{});

  useEffect(()=>{
    if(!prefill)return;onPrefillUsed?.();
    if(autoSend){void send(prefill);return;}
    setDraft(prefill);requestAnimationFrame(()=>{input.current?.focus();grow();});
  },[prefill]);
  useEffect(()=>{try{if(draft)localStorage.setItem(draftKey,draft);else localStorage.removeItem(draftKey);}catch{}},[draft,draftKey]);
  // Follow the conversation, but let an empty chat show its greeting from the top.
  const updateIds=feed.updates.map(item=>item.id).join();
  useLayoutEffect(()=>{if(scroller.current&&stick.current&&(messages.length||waiting||feed.updates.length))scroller.current.scrollTop=scroller.current.scrollHeight;},[messages.length,waiting,updateIds]);
  // When the chat is resized (a panel opens beside it), stay with the latest message.
  useEffect(()=>{const el=scroller.current;if(!el||typeof ResizeObserver==='undefined')return;
    const observer=new ResizeObserver(()=>{if(stick.current)el.scrollTop=el.scrollHeight;});observer.observe(el);for(const child of Array.from(el.children))observer.observe(child);
    return()=>observer.disconnect();},[messages.length>0||waiting||feed.updates.length>0]);
  useLayoutEffect(grow,[draft]);
  function grow(){const el=input.current;if(!el)return;el.style.height='auto';el.style.height=Math.min(el.scrollHeight,220)+'px';}

  async function send(text=draft,retry=false){
    const content=text.trim();
    if(!content||sending||!canWrite||(unresolved&&!retry)||blocked)return;
    if(retry&&unresolved)setReviewedUnknown(unresolved.requestId);
    // Retrying the same words reuses the request ID; the host then answers with the saved turn.
    const id=lastAttempt.current?.content===content?lastAttempt.current.id:requestId();
    lastAttempt.current={id,content};stick.current=true;
    setSending(true);setNotice('');setFailed('');setDraft('');
    try{await api('/marketing/chat',{requestId:id,content,timeZone,...(task?{taskId:task.id}:{})});lastAttempt.current=null;}
    catch(error){
      setDraft(content);
      setFailed((error as Error).message);
    }finally{
      try{await onRefresh();}catch{}
      setSending(false);requestAnimationFrame(()=>input.current?.focus());
    }
  }

  const render=(message:MarketingMessage)=>{
      const mine=message.role==='user';
      const record=mine?state.requests.find(item=>item.requestId===message.id.replace(/:user$/,'')):undefined;
      return <article className={'fe-msg '+(mine?'user':'assistant')} key={message.id}>
        {!mine&&<span className="fe-avatar" aria-hidden="true">{initials(name)}</span>}
        <div className="fe-msg-body">
          <div className="fe-msg-meta"><strong>{mine?(!message.actorName||message.actorId&&message.actorId===me?.id||message.actorName===me?.name?'You':message.actorName):name}</strong><time>{readableTime(message.createdAt)}</time>
            {record&&record.status!=='succeeded'&&<span className={'fe-pill fe-msg-status '+(record.status==='failed'?'bad':'attn')}>{record.status==='unknown'?'Unconfirmed':record.status}</span>}</div>
          {(()=>{const {text,actions}=mine?{text:message.content,actions:[]}:parseActions(message.content);return <>
            <div className="fe-msg-content"><Markdown urlTransform={keepItemLinks} components={{a:({href,children})=>href&&itemLink.test(href)
              ?<button type="button" className="fe-link fe-cite" onClick={()=>navigate(href)}>{children}</button>
              :<a href={href} target="_blank" rel="noopener noreferrer">{children}</a>}}>{tablesToLists(text)}</Markdown></div>
            {actions.length>0&&onNavigate&&<ReplyActionCards messageId={message.id} actions={actions} text={text} state={state} owner={owner} onNavigate={navigate} onRefresh={onRefresh}/>}
            {!mine&&!compact&&<ReplyActions content={text} canWrite={canWrite} onRefresh={onRefresh}/>}</>;})()}
        </div>
      </article>;
  };
  // Onboarding runs in the main conversation; in Chat it folds into one entry you can expand.
  const segments=compact?messages.map(message=>({message})):foldOnboarding(messages);
  const seconds=(value:number|string)=>typeof value==='number'?value:new Date(value).getTime()/1000;
  const entries=[
    ...segments.map(segment=>({at:seconds('group' in segment?segment.group[0].createdAt:segment.message.createdAt),node:'group' in segment
      ?<details className="fe-msg-group" key={segment.group[0].id}><summary>Onboarding conversation · {segment.group.length} messages · {readableTime(segment.group[0].createdAt)}</summary><div className="fe-thread">{segment.group.map(render)}</div></details>
      :render(segment.message)})),
    ...feed.updates.map(update=>({at:update.at,node:<UpdateCard key={update.id} update={update} name={name} state={state} owner={owner} publishing={feed.publishing} reloadPublishing={feed.reloadPublishing}
      onNavigate={navigate} onRefresh={onRefresh} onDismiss={()=>feed.dismiss(update.id)}/>}))
  ].sort((a,b)=>a.at-b.at);
  const thread=<div className="fe-thread">
    {entries.map(entry=>entry.node)}
    {waiting&&<article className="fe-msg assistant" aria-live="polite"><span className="fe-avatar" aria-hidden="true">{initials(name)}</span><div className="fe-msg-body"><div className="fe-msg-meta"><strong>{name}</strong><span>is writing…</span></div><div className="fe-typing" aria-label={name+' is writing'}><i/><i/><i/></div></div></article>}
  </div>;

  const hello=!messages.length&&!waiting&&!compact&&<div className="fe-hello">
    <h2>What should {name} work on?</h2>
    <p>{name} researches, plans and drafts. Nothing is published, sent or spent without your approval.</p>
    {introExtra??(briefMissing&&<button type="button" className="fe-setup" onClick={onOpenBrief}><NotebookPen size={20}/><div><strong>Start with your business brief</strong><p>Tell {name} what you sell, who it’s for and what matters now. About two minutes.</p></div><Sparkles size={16}/></button>)}
    <div className="fe-suggestions">{suggestions.map(({icon:Icon,text,hint})=><button type="button" className="fe-suggestion" key={text} disabled={!canWrite||!!blocked} onClick={()=>void send(text)}><Icon size={18}/><span>{text}<small>{hint}</small></span></button>)}</div>
  </div>;

  return <section className="fe-chat" aria-label={task?`Discussion for ${task.title}`:'Conversation with '+name}>
    {!compact&&status&&<header className="fe-chat-head"><span className="fe-avatar" aria-hidden="true">{initials(name)}</span><div><strong>{name}</strong><small><i className={'fe-dot '+status.tone}/>{status.label}</small></div>{headerActions}</header>}
    <div className="fe-chat-scroll" ref={scroller} onScroll={event=>{const el=event.currentTarget;stick.current=el.scrollHeight-el.scrollTop-el.clientHeight<80;}}>
      {hello}
      {(messages.length>0||waiting||feed.updates.length>0)&&thread}
      {!messages.length&&!waiting&&compact&&<p className="fe-empty">No discussion on this task yet. Ask {name} for an update or give direction.</p>}
    </div>
    <div className="fe-composer-wrap">
      <div className="fe-composer-notes">
        {failed&&!sending&&<div className="fe-notice attn" role="alert"><CircleAlert size={17}/><span><strong>{name} didn’t answer</strong>Your message is still in the box. Trying again is safe: it won’t send twice.<details><summary>Details</summary>{failed}</details></span><button type="button" disabled={!canWrite} onClick={()=>void send(draft,true)}>Try again</button></div>}
        {unresolved&&!sending&&!failed&&<div className="fe-notice attn" role="status"><CircleAlert size={17}/><span>{unresolved.status==='pending'?`${name} is still answering your last message.`:`No reply came back to your last message, so it may not have reached ${name}. If there’s no answer above, send it again.`}</span>{unresolved.status==='unknown'&&<>
          <button type="button" onClick={()=>{setReviewedUnknown(unresolved.requestId);lastAttempt.current=null;const original=state.messages.find(message=>message.id===unresolved.requestId+':user')?.content;if(original){setDraft(original);requestAnimationFrame(()=>input.current?.focus());}}}>Send it again</button>
          <button type="button" className="fe-ghost" onClick={()=>{setReviewedUnknown(unresolved.requestId);lastAttempt.current=null;}}>Dismiss</button></>}</div>}
        {blocked&&<div className="fe-notice" role="status"><LoaderCircle size={17} className="fe-spin"/><span><strong>{state.chatBlockedReason?'Chat is paused for now':`${name} is busy with an assignment`}</strong>{state.chatBlockedReason?`${state.chatBlockedReason} `:''}You can write your next message now and send it when this clears.</span></div>}
        {notice&&<p className="fe-notice" role="status">{notice}</p>}
      </div>
      <form className="fe-composer" onSubmit={event=>{event.preventDefault();void send();}}>
        <label className="marketing-sr-only" htmlFor={task?'marketing-task-message':'marketing-main-message'}>Message to marketing employee</label>
        <textarea ref={input} id={task?'marketing-task-message':'marketing-main-message'} aria-label="Message to marketing employee" rows={1} value={draft}
          placeholder={task?`Discuss this task with ${name}…`:`Message ${name}…`} disabled={!canWrite||sending}
          onChange={event=>setDraft(event.target.value)}
          onKeyDown={event=>{if(event.key==='Enter'&&!event.shiftKey&&!event.nativeEvent.isComposing){event.preventDefault();void send();}}}/>
        <button className="fe-send" type="submit" aria-label="Send" disabled={!draft.trim()||!canWrite||sending||!!unresolved||!!blocked}>{sending?<LoaderCircle size={18} className="fe-spin"/>:<ArrowUp size={19}/>}</button>
      </form>
      {!compact&&<p className="fe-composer-hint">{canWrite?`${name} drafts and researches. Nothing is posted, sent or spent without your approval.`:'Chat is unavailable until the employee reconnects. Your draft is saved.'}</p>}
    </div>
  </section>;
}
