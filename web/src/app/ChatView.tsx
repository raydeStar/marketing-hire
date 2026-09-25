import {useEffect,useLayoutEffect,useRef,useState,type ReactNode} from 'react';
import {ArrowUp,BookOpen,Check,CircleAlert,Copy,Lightbulb,ListChecks,LoaderCircle,NotebookPen,PenLine,Search,Sparkles,Target} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import {readableTime,requestId,type MarketingState,type MarketingTask} from '../components/MarketingPanels';
import {initials,plain,type EmployeeStatus} from './shared';

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

/** The employee conversation. Used full-page in Chat and inside a task's detail view. */
export function Conversation({state,task,canWrite,status,prefill,autoSend=false,onPrefillUsed,onRefresh,onOpenBrief,compact=false,headerActions,introExtra}:{
  state:MarketingState;task?:MarketingTask;canWrite:boolean;status?:EmployeeStatus;prefill?:string;autoSend?:boolean;onPrefillUsed?:()=>void;headerActions?:ReactNode;introExtra?:ReactNode;
  onRefresh:()=>Promise<void>;onOpenBrief?:()=>void;compact?:boolean;
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

  useEffect(()=>{
    if(!prefill)return;onPrefillUsed?.();
    if(autoSend){void send(prefill);return;}
    setDraft(prefill);requestAnimationFrame(()=>{input.current?.focus();grow();});
  },[prefill]);
  useEffect(()=>{try{if(draft)localStorage.setItem(draftKey,draft);else localStorage.removeItem(draftKey);}catch{}},[draft,draftKey]);
  // Follow the conversation, but let an empty chat show its greeting from the top.
  useLayoutEffect(()=>{if(scroller.current&&stick.current&&(messages.length||waiting))scroller.current.scrollTop=scroller.current.scrollHeight;},[messages.length,waiting]);
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
    try{await api('/marketing/chat',{requestId:id,content,...(task?{taskId:task.id}:{})});lastAttempt.current=null;}
    catch(error){
      setDraft(content);
      setFailed((error as Error).message);
    }finally{
      try{await onRefresh();}catch{}
      setSending(false);requestAnimationFrame(()=>input.current?.focus());
    }
  }

  const thread=<div className="fe-thread">
    {messages.map(message=>{
      const mine=message.role==='user';
      const record=mine?state.requests.find(item=>item.requestId===message.id.replace(/:user$/,'')):undefined;
      return <article className={'fe-msg '+(mine?'user':'assistant')} key={message.id}>
        {!mine&&<span className="fe-avatar" aria-hidden="true">{initials(name)}</span>}
        <div className="fe-msg-body">
          <div className="fe-msg-meta"><strong>{mine?message.actorName||'You':name}</strong><time>{readableTime(message.createdAt)}</time>
            {record&&record.status!=='succeeded'&&<span className={'fe-pill fe-msg-status '+(record.status==='failed'?'bad':'attn')}>{record.status==='unknown'?'Unconfirmed':record.status}</span>}</div>
          <div className="fe-msg-content"><Markdown>{message.content}</Markdown></div>
          {!mine&&!compact&&<ReplyActions content={message.content} canWrite={canWrite} onRefresh={onRefresh}/>}
        </div>
      </article>;
    })}
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
      {(messages.length>0||waiting)&&thread}
      {!messages.length&&!waiting&&compact&&<p className="fe-empty">No discussion on this task yet. Ask {name} for an update or give direction.</p>}
    </div>
    <div className="fe-composer-wrap">
      <div className="fe-composer-notes">
        {failed&&!sending&&<div className="fe-notice attn" role="alert"><CircleAlert size={17}/><span><strong>{name} didn’t answer</strong>Your message is still in the box. Trying again is safe: it won’t send twice.<details><summary>Details</summary>{failed}</details></span><button type="button" disabled={!canWrite} onClick={()=>void send(draft,true)}>Try again</button></div>}
        {unresolved&&!sending&&!failed&&<div className="fe-notice attn" role="status"><CircleAlert size={17}/><span>{unresolved.status==='pending'?`${name} is still answering your last message.`:`Your last message may not have reached ${name}. Check the conversation above before sending another.`}</span>{unresolved.status==='unknown'&&<button type="button" onClick={()=>{setReviewedUnknown(unresolved.requestId);lastAttempt.current=null;setNotice('Okay. Your next message will start a new turn.');}}>I checked it</button>}</div>}
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
