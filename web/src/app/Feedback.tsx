import {useEffect,useState} from 'react';
import {RotateCcw,ThumbsDown,ThumbsUp} from 'lucide-react';
import {api} from '../api';

export type FeedbackEntry={key:string;title:string;verdict:'useful'|'not_useful'|'approved'|'rejected'|'redraft';note:string;by:string;at:string};

/** Tell the employee whether a piece of its work was useful, and why. It reads the latest verdicts before planning and writing. */
export function RateWork({itemKey,title,canRate,canRedraft=false}:{itemKey:string;title:string;canRate:boolean;canRedraft?:boolean}){
  const [current,setCurrent]=useState<FeedbackEntry|null>(null),[choice,setChoice]=useState<'useful'|'not_useful'|'redraft'|null>(null);
  const [note,setNote]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState(''),[sent,setSent]=useState('');
  useEffect(()=>{setChoice(null);setNote('');void api<{feedback:FeedbackEntry[]}>('/feedback').then(data=>setCurrent(data.feedback.find(item=>item.key===itemKey)||null)).catch(()=>setCurrent(null));},[itemKey]);
  async function send(){
    if(!choice||busy)return;setBusy(true);setError('');
    try{
      // Sending it back records the feedback and queues the rewrite; the employee answers it at its next cycle.
      if(choice==='redraft'){const result=await api<{message:string}>('/redrafts',{key:itemKey,feedback:note.trim()});setSent(result.message);setCurrent({key:itemKey,title,verdict:'redraft',note:note.trim(),by:'',at:new Date().toISOString()});}
      else setCurrent(await api<FeedbackEntry>('/feedback',{key:itemKey,title,verdict:choice,note:note.trim()}));
      setChoice(null);setNote('');
    }
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(!canRate&&!current)return null;
  return <section className="fe-rate" aria-label="Feedback for the employee">
    <div className="fe-rate-row"><span>Was this useful?</span>
      {canRate&&<><button type="button" aria-pressed={choice==='useful'} onClick={()=>setChoice('useful')}><ThumbsUp size={14}/> Useful</button>
        <button type="button" aria-pressed={choice==='not_useful'} onClick={()=>setChoice('not_useful')}><ThumbsDown size={14}/> Not useful</button>
        {canRedraft&&<button type="button" aria-pressed={choice==='redraft'} onClick={()=>setChoice('redraft')}><RotateCcw size={14}/> Redraft it</button>}</>}
      {current&&!choice&&<small>{sent||(current.verdict==='useful'?'Marked useful':current.verdict==='redraft'?'Sent back for a redraft':'Marked not useful')}{current.note?`: “${current.note}”`:''}</small>}</div>
    {choice&&<div className="fe-rate-note"><label>{choice==='redraft'?'What should change?':'Why?'} <span className="fe-muted">{choice==='redraft'?'(the employee rewrites it to answer this, as a new version of this document)':'(optional; the employee reads this before its next piece of work)'}</span>
      <textarea rows={2} maxLength={choice==='redraft'?1000:600} value={note} onChange={event=>setNote(event.target.value)} placeholder={choice==='useful'?'What made it useful?':choice==='redraft'?'e.g. Lead with the customer story, and cut the second half':'What was missing or wrong?'} autoFocus/></label>
      <div className="fe-rate-actions"><button type="button" className="fe-ghost" onClick={()=>setChoice(null)}>Cancel</button><button type="button" className="primary" disabled={busy||(choice==='redraft'&&note.trim().length<3)} onClick={()=>void send()}>{busy?'Saving…':choice==='redraft'?'Send back for a redraft':'Send feedback'}</button></div></div>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}
