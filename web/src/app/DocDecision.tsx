import {useState} from 'react';
import {Check,CircleSlash,RotateCcw} from 'lucide-react';
import {api} from '../api';
import type {WikiPage} from './library';
import {missingLine} from './Rubric';

/** A section of the document by its heading ("## Recommendation"), up to the next heading. */
function section(body:string,names:string[]){
  for(const name of names){
    const match=new RegExp(`^#{2,3}\\s*${name}\\s*\\n([\\s\\S]*?)(?=\\n#{1,3}\\s|\\n---\\n|$)`,'im').exec(body);
    if(match?.[1]?.trim())return match[1].trim();
  }
  return '';
}
const plain=(text:string)=>text.replace(/\[(\d{1,2})\]/g,'').replace(/[*_`>#]/g,'').replace(/\s+/g,' ').trim();
const firstSentences=(text:string,count=1)=>plain(text).split(/(?<=[.!?])\s+/).slice(0,count).join(' ');

/** The review the host appends under what a shift wrote ("---" then "_Marketing rubric …_"): shown folded, not as a paragraph. */
export function splitReview(body:string){
  const match=/\n\n---\n\n_(Marketing rubric[\s\S]*?)_\n?(?=\n#{1,3} |\s*$)/.exec(body);
  return match?{body:body.slice(0,match.index)+body.slice(match.index+match[0].length),review:match[1].replace(/\\_/g,'_')}:{body,review:''};
}

const approvedLine=(body:string)=>/\n#{2,3}\s*(Before|After)\b/i.test(body)?'Approved. Next, make the change: the new wording is under “After”.':'Approved. It builds on this from now on.';
const asideLine='Set aside. It won’t bring this up again, and it learns from your reason.';

/** Once decided, the decision stays said where it was made. */
export function DecidedNote({page}:{page:WikiPage}){
  return <section className="fe-doc-decision decided" aria-label="Your decision"><p role="status"><Check size={15}/> {page.status==='archived'?asideLine:approvedLine(page.body)}</p></section>;
}

/** What a shift brought the owner, decided at the top of it: what it proposes in a sentence, what approving means, and the three
 * ways to answer: approve it, send it back with a note, or say it isn't being done (with why, so the employee learns). */
export function DocDecision({page,missing,onDecided}:{page:WikiPage;missing:string[];onDecided:()=>void}){
  const [mode,setMode]=useState<'back'|'park'|null>(null),[note,setNote]=useState(''),[busy,setBusy]=useState(false),[done,setDone]=useState(''),[error,setError]=useState('');
  const body=splitReview(page.body).body;
  const proposal=firstSentences(section(body,['Recommendation','Proposal','The fix','Summary','What to do']),1)
    ||firstSentences(body.replace(/^#.*$/m,''),1);
  // "If you approve: Approve an accessibility-first pass…" said it twice: the section's own "Approve" goes.
  const approving=firstSentences(section(body,['Owner decision','Your decision','Decision','Next step']),2)
    .replace(/^(?:(?:please\s+)?(?:approve|decide whether to approve|choose whether to approve)\s+)/i,'').replace(/^./,letter=>letter.toUpperCase());
  const finishNote=`Please finish this. It still needs: ${missing.join('; ')}.`.slice(0,600);
  async function status(next:'active'|'archived'){
    await api('/company-wiki',{requestId:crypto.randomUUID(),id:page.id,version:page.version,scope:page.scope,scopeId:page.scopeId,title:page.title,body:page.body,kind:page.kind,status:next},'PUT');
  }
  async function act(kind:'approve'|'back'|'park',text=note){
    if(busy)return;setBusy(true);setError('');
    try{
      if(kind==='back'){setDone((await api<{message:string}>('/redrafts',{key:'wiki:'+page.id,feedback:text.trim()})).message);}
      else if(kind==='approve'){
        await status('active');
        await api('/feedback',{key:'wiki:'+page.id,title:page.title,verdict:'useful',note:'Approved.'}).catch(()=>{});
        setDone(approvedLine(body));
      }else{
        await status('archived');
        await api('/feedback',{key:'wiki:'+page.id,title:page.title,verdict:'not_useful',note:('Not doing this. '+text.trim()).trim()}).catch(()=>{});
        setDone(asideLine);
      }
      setMode(null);setNote('');onDecided();
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(done)return <section className="fe-doc-decision decided" aria-label="Your decision"><p role="status"><Check size={15}/> {done}</p></section>;
  return <section className="fe-doc-decision" aria-label="Your decision">
    <span className="fe-experience-eyebrow">For your decision</span>
    {proposal&&<p className="fe-doc-proposal">{proposal}</p>}
    {missing.length>0?<p className="fe-doc-approving attn"><strong>Not finished yet:</strong> it still needs {missingLine(missing)}{/[….]$/.test(missingLine(missing))?'':'.'}</p>
      :approving&&<p className="fe-doc-approving"><strong>If you approve:</strong> {approving}</p>}
    {!mode&&<div className="fe-actions">
      {missing.length>0?<button type="button" className="primary" disabled={busy} onClick={()=>void act('back',finishNote)}><RotateCcw size={14}/> {busy?'Sending…':'Send it back to finish'}</button>
        :<button type="button" className="primary" disabled={busy} onClick={()=>void act('approve')}><Check size={14}/> {busy?'Saving…':'Approve'}</button>}
      <button type="button" disabled={busy} onClick={()=>setMode('back')}><RotateCcw size={14}/> Send back with a note</button>
      <button type="button" className="fe-ghost" disabled={busy} onClick={()=>setMode('park')}><CircleSlash size={14}/> Not doing this</button>
    </div>}
    {mode&&<div className="fe-form">
      <label>{mode==='back'?'What should change?':'Why not? '}{mode==='park'&&<span className="fe-muted">(optional; it learns from this)</span>}
        <textarea rows={2} maxLength={mode==='back'?1000:600} autoFocus value={note} onChange={event=>setNote(event.target.value)} placeholder={mode==='back'?'e.g. Lead with the booking link, and keep it under 100 words':'e.g. We’re redesigning the page next month'}/></label>
      <div className="fe-actions"><button type="button" className="fe-ghost" onClick={()=>{setMode(null);setNote('');}}>Cancel</button>
        <button type="button" className="primary" disabled={busy||mode==='back'&&note.trim().length<3} onClick={()=>void act(mode)}>{busy?'Saving…':mode==='back'?'Send back':'Set it aside'}</button></div>
    </div>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}
