import {useEffect,useLayoutEffect,useRef,useState} from 'react';
import {ArrowUp,CircleAlert,Lightbulb,LoaderCircle,NotebookPen,PenLine,Search,Sparkles,Target} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import {Raven} from '../components/Raven';
import {readableTime,requestId,type MarketingState,type MarketingTask} from '../components/MarketingPanels';
import {initials,type EmployeeStatus} from './shared';

const suggestions=[
  {icon:Target,text:'What should we focus on this week?',hint:'A short, prioritized plan'},
  {icon:Search,text:'Research what our audience is struggling with right now.',hint:'Cited public sources'},
  {icon:PenLine,text:'Draft three post angles for our offer.',hint:'Drafts only, nothing is posted'},
  {icon:Lightbulb,text:'Read my business brief and tell me what’s missing.',hint:'Sharpen the brief'}
];

/** The employee conversation. Used full-page in Chat and inside a task's detail view. */
export function Conversation({state,task,canWrite,status,prefill,autoSend=false,onPrefillUsed,onRefresh,onOpenBrief,compact=false}:{
  state:MarketingState;task?:MarketingTask;canWrite:boolean;status?:EmployeeStatus;prefill?:string;autoSend?:boolean;onPrefillUsed?:()=>void;
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
  useLayoutEffect(()=>{if(scroller.current&&stick.current)scroller.current.scrollTop=scroller.current.scrollHeight;},[messages.length,waiting]);
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
        </div>
      </article>;
    })}
    {waiting&&<article className="fe-msg assistant" aria-live="polite"><span className="fe-avatar" aria-hidden="true">{initials(name)}</span><div className="fe-msg-body"><div className="fe-msg-meta"><strong>{name}</strong><span>is writing…</span></div><div className="fe-typing" aria-label={name+' is writing'}><i/><i/><i/></div></div></article>}
  </div>;

  const hello=!messages.length&&!waiting&&!compact&&<div className="fe-hello">
    <Raven state={draft.trim()?'listening':'idle'}/>
    <h1>Hi, I’m <span>{name}</span>.</h1>
    <p>Your marketing employee. Ask for research, plans, or drafts. Nothing goes out without your approval.</p>
    {briefMissing&&<button type="button" className="fe-setup" onClick={onOpenBrief}><NotebookPen size={22}/><div><strong>Start with your business brief</strong><p>Tell {name} what you sell, who it’s for, and what matters now. It takes about two minutes.</p></div><Sparkles size={18}/></button>}
    <div className="fe-suggestions">{suggestions.map(({icon:Icon,text,hint})=><button type="button" className="fe-suggestion" key={text} disabled={!canWrite||!!blocked} onClick={()=>void send(text)}><Icon size={18}/><span>{text}<small>{hint}</small></span></button>)}</div>
  </div>;

  return <section className="fe-chat" aria-label={task?`Discussion for ${task.title}`:'Conversation with '+name}>
    {!compact&&status&&<header className="fe-chat-head"><span className="fe-avatar" aria-hidden="true">{initials(name)}</span><div><strong>{name}</strong><small><i className={'fe-dot '+status.tone}/>{status.label}</small></div></header>}
    <div className="fe-chat-scroll" ref={scroller} onScroll={event=>{const el=event.currentTarget;stick.current=el.scrollHeight-el.scrollTop-el.clientHeight<80;}}>
      {hello}
      {(messages.length>0||waiting)&&thread}
      {!messages.length&&!waiting&&compact&&<p className="fe-empty">No discussion on this task yet. Ask {name} for an update or give direction.</p>}
    </div>
    <div className="fe-composer-wrap">
      <div className="fe-composer-notes">
        {failed&&!sending&&<div className="fe-notice attn" role="alert"><CircleAlert size={17}/><span><strong>{name} didn’t answer</strong>Your message is still in the box. Trying again is safe: it won’t send twice.<details><summary>Details</summary>{failed}</details></span><button type="button" disabled={!canWrite} onClick={()=>void send(draft,true)}>Try again</button></div>}
        {unresolved&&!sending&&!failed&&<div className="fe-notice attn" role="status"><CircleAlert size={17}/><span>{unresolved.status==='pending'?`${name} is still answering your last message.`:`Your last message may not have reached ${name}. Check the conversation above before sending another.`}</span>{unresolved.status==='unknown'&&<button type="button" onClick={()=>{setReviewedUnknown(unresolved.requestId);lastAttempt.current=null;setNotice('Okay. Your next message will start a new turn.');}}>I checked it</button>}</div>}
        {blocked&&<div className="fe-notice" role="status"><LoaderCircle size={17} className="fe-spin"/><span><strong>{name} is busy with an assignment</strong>You can write your next message now and send it when the step settles.</span></div>}
        {notice&&<p className="fe-notice" role="status">{notice}</p>}
      </div>
      <form className="fe-composer" onSubmit={event=>{event.preventDefault();void send();}}>
        <label className="marketing-sr-only" htmlFor={task?'marketing-task-message':'marketing-main-message'}>Message to marketing employee</label>
        <textarea ref={input} id={task?'marketing-task-message':'marketing-main-message'} rows={1} value={draft}
          placeholder={task?`Discuss this task with ${name}…`:`Message ${name}…`} disabled={!canWrite||sending}
          onChange={event=>setDraft(event.target.value)}
          onKeyDown={event=>{if(event.key==='Enter'&&!event.shiftKey&&!event.nativeEvent.isComposing){event.preventDefault();void send();}}}/>
        <button className="fe-send" type="submit" aria-label="Send" disabled={!draft.trim()||!canWrite||sending||!!unresolved||!!blocked}>{sending?<LoaderCircle size={18} className="fe-spin"/>:<ArrowUp size={19}/>}</button>
      </form>
      {!compact&&<p className="fe-composer-hint">{canWrite?`${name} drafts and researches. Nothing is posted, sent or spent without your approval.`:'Chat is unavailable until the employee reconnects. Your draft is saved.'}</p>}
    </div>
  </section>;
}
