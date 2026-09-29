import {useState} from 'react';
import {Check,CircleSlash,ClipboardCopy,Globe,RotateCcw} from 'lucide-react';
import {api} from '../api';
import type {WikiPage} from './library';
import {missingLine} from './Rubric';
import {SiteConnection,openSiteConnect,usePublishing} from './PublishingView';

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

const sitePage=(body:string)=>/\n#{2,3}\s*(Before|After)\b/i.test(body);
const approvedLine=(body:string)=>sitePage(body)?'Approved.':'Approved. It builds on this from now on.';
const asideLine='Set aside. It won’t bring this up again, and it learns from your reason.';
const fixKey=(id:string)=>'fe-site-fix:'+id;

/** After approving a change to the owner's site: says plainly that the site hasn't changed yet, and the way forward: Chip saving
 * it on the connected site as a draft (at the next check-in), or copying the new wording to paste in by hand. */
function SiteNextStep({page}:{page:WikiPage}){
  const {data}=usePublishing();
  const [asked,setAsked]=useState(()=>{try{return localStorage.getItem(fixKey(page.id))==='1';}catch{return false;}}),[copied,setCopied]=useState(false),[error,setError]=useState('');
  if(!data)return null;
  const site=data.connections.find(item=>(item.kind==='hirezero'||item.kind==='wordpress')&&item.status==='ready');
  const after=section(page.body,['After']);
  const url=/\n#{2,3}\s*Sources[\s\S]*?\((https?:\/\/[^\s)]+)\)/i.exec(page.body)?.[1]||site?.address||'';
  const short=url.replace(/^https?:\/\/(www\.)?/,'').replace(/\/$/,'')||'your site';
  async function queue(){
    try{
      await api('/marketing/tasks',{requestId:crypto.randomUUID(),title:`New copy for ${short}`.slice(0,160),status:'ready',priority:'high',action_state:'agent_ready',
        next_action:`The owner approved “${page.title}”. Put its approved wording on ${url||'the page'} as a page deliverable (page: ${url}), keeping what already works there. The approved wording: ${plain(after)}`.slice(0,990)});
      try{localStorage.setItem(fixKey(page.id),'1');}catch{/* a per-browser note only */}
      setAsked(true);
    }catch(cause){setError((cause as Error).message);}
  }
  async function copy(){try{await navigator.clipboard.writeText(after.replace(/\*\*/g,''));setCopied(true);}catch{setError('The browser blocked the clipboard; select the wording under “After” and copy it.');}}
  return <div className="fe-site-next" role="note">
    {asked?<p><Globe size={14}/> <strong>On it.</strong> At the next check-in Chip writes this for {site?.account||'your site'}. It comes back to you in Work with a button to save it there as a draft; nothing goes live on its own.</p>
      :<p><Globe size={14}/> <strong>Your site hasn’t changed yet.</strong> {site?`Chip can save this on ${site.account} as a draft for you to publish.`:'Copy the new wording into your site yourself, or connect your site and Chip saves it there as a draft.'}</p>}
    {!asked&&<div className="fe-actions">
      {site?<button type="button" className="primary" onClick={()=>void queue()}><Globe size={14}/> Put it on my site as a draft</button>
        :<button type="button" className="primary" onClick={openSiteConnect}><Globe size={14}/> Connect my site</button>}
      {after&&<button type="button" onClick={()=>void copy()}><ClipboardCopy size={14}/> {copied?'Copied':'Copy the new wording'}</button>}
    </div>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </div>;
}

/** Once decided, the decision stays said where it was made. */
export function DecidedNote({page}:{page:WikiPage}){
  return <section className="fe-doc-decision decided" aria-label="Your decision"><p role="status"><Check size={15}/> {page.status==='archived'?asideLine:approvedLine(page.body)}</p>
    {page.status==='active'&&sitePage(page.body)&&<SiteNextStep page={page}/>}</section>;
}

/** What a shift brought the owner, decided at the top of it: what it proposes in a sentence, what approving means, and the three
 * ways to answer: approve it, send it back with a note, or say it isn't being done (with why, so the employee learns). */
export function DocDecision({page,missing,onDecided,onOpen}:{page:WikiPage;missing:string[];onDecided:()=>void;onOpen?:(key:string)=>void}){
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
  if(done)return <section className="fe-doc-decision decided" aria-label="Your decision"><p role="status"><Check size={15}/> {done}</p>
    {done!==asideLine&&sitePage(body)&&<SiteNextStep page={page}/>}</section>;
  return <section className="fe-doc-decision" aria-label="Your decision">
    <span className="fe-experience-eyebrow">For your decision</span>
    {proposal&&<p className="fe-doc-proposal">{proposal}</p>}
    {missing.length>0?<p className="fe-doc-approving attn"><strong>Not finished yet:</strong> it still needs {missingLine(missing)}{/[….]$/.test(missingLine(missing))?'':'.'}</p>
      :approving&&<p className="fe-doc-approving"><strong>If you approve:</strong> {approving}</p>}
    {sitePage(body)&&<SiteConnection onOpen={onOpen} compact/>}
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
