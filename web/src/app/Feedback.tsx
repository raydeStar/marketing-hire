import {useEffect,useState} from 'react';
import {ThumbsDown,ThumbsUp} from 'lucide-react';
import {api} from '../api';

export type FeedbackEntry={key:string;title:string;verdict:'useful'|'not_useful'|'approved'|'rejected';note:string;by:string;at:string};

/** Tell the employee whether a piece of its work was useful, and why. It reads the latest verdicts before planning and writing. */
export function RateWork({itemKey,title,canRate}:{itemKey:string;title:string;canRate:boolean}){
  const [current,setCurrent]=useState<FeedbackEntry|null>(null),[choice,setChoice]=useState<'useful'|'not_useful'|null>(null);
  const [note,setNote]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  useEffect(()=>{setChoice(null);setNote('');void api<{feedback:FeedbackEntry[]}>('/feedback').then(data=>setCurrent(data.feedback.find(item=>item.key===itemKey)||null)).catch(()=>setCurrent(null));},[itemKey]);
  async function send(){
    if(!choice||busy)return;setBusy(true);setError('');
    try{setCurrent(await api<FeedbackEntry>('/feedback',{key:itemKey,title,verdict:choice,note:note.trim()}));setChoice(null);setNote('');}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(!canRate&&!current)return null;
  return <section className="fe-rate" aria-label="Feedback for the employee">
    <div className="fe-rate-row"><span>Was this useful?</span>
      {canRate&&<><button type="button" aria-pressed={choice==='useful'} onClick={()=>setChoice('useful')}><ThumbsUp size={14}/> Useful</button>
        <button type="button" aria-pressed={choice==='not_useful'} onClick={()=>setChoice('not_useful')}><ThumbsDown size={14}/> Not useful</button></>}
      {current&&!choice&&<small>{current.verdict==='useful'?'Marked useful':'Marked not useful'}{current.note?`: “${current.note}”`:''}</small>}</div>
    {choice&&<div className="fe-rate-note"><label>Why? <span className="fe-muted">(optional; the employee reads this before its next piece of work)</span>
      <textarea rows={2} maxLength={600} value={note} onChange={event=>setNote(event.target.value)} placeholder={choice==='useful'?'What made it useful?':'What was missing or wrong?'} autoFocus/></label>
      <div className="fe-rate-actions"><button type="button" className="fe-ghost" onClick={()=>setChoice(null)}>Cancel</button><button type="button" className="primary" disabled={busy} onClick={()=>void send()}>{busy?'Saving…':'Send feedback'}</button></div></div>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}
