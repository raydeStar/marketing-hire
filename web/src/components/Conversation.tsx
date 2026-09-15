import {useEffect,useRef} from 'react';
import Markdown from 'react-markdown';
import {User,Shapes,ArrowUpRight} from 'lucide-react';
import type {Run,AppSummary} from '../types';

type Message={id:string;role:string;content:string};
export function Conversation({messages,runs,online,busy,onCancel,onGoal,focusId,onArtifact,apps}:{apps:AppSummary[];onArtifact:(id:string)=>void;focusId?:string;messages:Message[];runs:Run[];online:boolean;busy:boolean;onCancel:(id:string)=>void;onGoal:(text:string)=>void}){
  const pending=runs.find(r=>r.goal.kind==='conversation'&&!r.background&&['queued','running'].includes(r.state));
  const feed=useRef<HTMLElement>(null);
  useEffect(()=>{if(focusId){const message=document.getElementById('chat-'+focusId);message?.focus();message?.scrollIntoView({block:'center'});}else if(feed.current)feed.current.scrollTop=feed.current.scrollHeight;},[messages.length,pending?.draftText,pending?.id,focusId]);
  // A transcript: a small mark for who is speaking, their name, then the words. No speech bubbles.
  return <section ref={feed} className="conversation-feed" aria-label="Conversation">
    {messages.map(message=><article id={'chat-'+message.id} tabIndex={-1} key={message.id} className={'chat '+message.role}>
      <span className="chat-avatar" aria-hidden="true">{message.role==='user'?<User size={16} strokeWidth={1.8}/>:'T'}</span>
      <div className="chat-body">
        <small>{message.role==='user'?'You':'Thaddeus'}</small>
        <Markdown>{message.content}</Markdown>
        {message.role==='assistant'&&(()=>{const result=runs.find(run=>run.state==='succeeded'&&message.id===run.id+'-assistant')?.artifactResult;return result&&!result.deleted&&<button className="chat-artifact" onClick={()=>onArtifact(result.id)}><Shapes size={23}/><span><strong>{apps.find(app=>app.id===result.id)?.title||'Open app'}</strong><small>{result.description}</small></span><ArrowUpRight size={16}/></button>;})()}
        {message.role==='user'&&<button className="text-button" disabled={busy||!online} onClick={()=>onGoal(message.content)}>Create a goal from this message →</button>}
        {message.role==='user'&&(()=>{const task=runs.find(run=>message.id===run.id+'-user');if(!task)return null;
          if(['queued','running'].includes(task.state))return <div className="chat-task-status" role="status"><p>{task.background?"I've begun work on this request. You can keep chatting; I'll let you know when it's done.":'Working on your request\u2026'}</p>{task.draftText&&<Markdown>{task.draftText}</Markdown>}<button disabled={!online||busy} onClick={()=>onCancel(task.id)}>Cancel task</button></div>;
          if(['failed','cancelled','needsAttention'].includes(task.state))return <p role="status" className="muted">{task.summary}</p>;
          return null;
        })()}
      </div>
    </article>)}
  </section>;
}
