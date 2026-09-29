import {useState} from 'react';
import {CornerDownLeft} from 'lucide-react';
import {api} from '../api';
import type {MarketingTask} from '../components/MarketingPanels';
import {announceQueued} from './shared';

/** Chip asked something it needs to go on (the task waits on you with its question): type the answer, press Enter, and the task
 * goes back to it with the answer, started on right away. */
export function AnswerBox({task,name='Chip',onRefresh,autoFocus=false}:{task:MarketingTask;name?:string;onRefresh:()=>Promise<void>;autoFocus?:boolean}){
  const [text,setText]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState(''),[sent,setSent]=useState(false);
  async function send(){
    const answer=text.trim();
    if(answer.length<2||busy)return;setBusy(true);setError('');
    const note=`${task.next_action?task.next_action+'\n\n':''}Your answer to “${(task.blocker||'').slice(0,200)}”: ${answer}`;
    try{
      await api('/marketing/tasks/'+task.id,{requestId:crypto.randomUUID(),version:task.version,status:'ready',action_state:'agent_ready',blocker:'',
        next_action:note.length>1000?note.slice(note.length-1000):note},'PUT');
      setSent(true);setText('');announceQueued();await onRefresh();
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(sent)return <p className="fe-answer-sent" role="status">Got it. {name} picks it up right away.</p>;
  return <form className="fe-answer" onSubmit={event=>{event.preventDefault();void send();}}>
    <input value={text} onChange={event=>setText(event.target.value)} disabled={busy} autoFocus={autoFocus} maxLength={600}
      aria-label={`Answer ${name}`} placeholder={`Answer ${name}, then press Enter`}/>
    <button type="submit" className="fe-icon-button" aria-label="Send the answer" disabled={busy||text.trim().length<2}><CornerDownLeft size={14}/></button>
    {error&&<small className="fe-alert" role="alert">{error}</small>}
  </form>;
}
