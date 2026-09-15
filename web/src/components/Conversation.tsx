import {useEffect,useRef} from 'react';
import Markdown from 'react-markdown';
import {User} from 'lucide-react';
import type {Run} from '../types';
import {Raven} from './Raven';

type Message={id:string;role:string;content:string};
export function Conversation({messages,runs,online,busy,onCancel,onGoal,focusId}:{focusId?:string;messages:Message[];runs:Run[];online:boolean;busy:boolean;onCancel:(id:string)=>void;onGoal:(text:string)=>void}){
  const pending=runs.find(r=>r.goal.kind==='conversation'&&['queued','running'].includes(r.state));
  const stopped=runs.find(r=>r.goal.kind==='conversation'&&['failed','cancelled','needsAttention'].includes(r.state));
  const feed=useRef<HTMLElement>(null);
  useEffect(()=>{if(focusId){const message=document.getElementById('chat-'+focusId);message?.focus();message?.scrollIntoView({block:'center'});}else if(feed.current)feed.current.scrollTop=feed.current.scrollHeight;},[messages.length,pending?.draftText,pending?.id,focusId]);
  // A transcript: a small mark for who is speaking, their name, then the words. No speech bubbles.
  return <section ref={feed} className="conversation-feed" aria-label="Conversation">
    {messages.map(message=><article id={'chat-'+message.id} tabIndex={-1} key={message.id} className={'chat '+message.role}>
      <span className="chat-avatar" aria-hidden="true">{message.role==='user'?<User size={16} strokeWidth={1.8}/>:'T'}</span>
      <div className="chat-body">
        <small>{message.role==='user'?'You':'Thaddeus'}</small>
        <Markdown>{message.content}</Markdown>
        {message.role==='user'&&<button className="text-button" disabled={busy||!online} onClick={()=>onGoal(message.content)}>Create a goal from this message →</button>}
      </div>
    </article>)}
    {pending&&<article className="chat assistant" aria-live="polite">
      <span className="chat-avatar" aria-hidden="true"><Raven state={online?'running':'disconnected'}/></span>
      <div className="chat-body">
        <small>Thaddeus <span className="chat-model">{pending.goal.provider.model}</span></small>
        <Markdown>{pending.draftText||'Composing a reply…'}</Markdown>
        <button disabled={!online||busy} onClick={()=>onCancel(pending.id)}>Cancel reply</button>
      </div>
    </article>}
    {stopped&&<p role="status" className="muted">Last stopped conversation: {stopped.summary}</p>}
  </section>;
}
