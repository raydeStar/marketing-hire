import {useEffect,useState} from 'react';
import {Check,ExternalLink,FileText,Wrench} from 'lucide-react';
import {api} from '../api';
import {requestId} from '../components/MarketingPanels';
import {ShiftFeed} from './ShiftFeed';

type Source={url:string;title:string;coverage:string};
type Piece={key:string;title:string;grade?:string|null;unmet:string[];sources:Source[]};
type Fix={severity:string;check:string;url:string;detail:string};
/** "the event's date, the sign-up link and 3 more": the host has already put each shortfall in plain words. */
const missing=(items:string[])=>items.length<=3?items.join(', ').replace(/, ([^,]*)$/,' and $1'):`${items[0]}, ${items[1]} and ${items.length-2} more`;
type Draft={id:number;status:string;revision:number;digest:string;channel:string};
type View={shiftId:string;status:string;endsAt:string;positioning?:string|null;prepared:Piece[];fixes:Fix[];site?:string|null;siteNote?:string|null;callToActionSet:boolean;pagesChecked:number;onlySuggestions:boolean;competitorNote?:string|null};

/** The first shift, narrated as it works, then its results in one place: what it works from, what it prepared (graded, with
 * sources), and the three fixes that matter most on the site. */
export function FirstShiftPanel({shiftId,running,onOpen}:{shiftId:string;running:boolean;onOpen:(key:string)=>void}){
  const [view,setView]=useState<View|null>(null);
  useEffect(()=>{
    let stop=false;
    const load=()=>api<View>('/first-shift/'+encodeURIComponent(shiftId)).then(next=>{if(!stop)setView(next);}).catch(()=>{});
    void load();const timer=setInterval(()=>{if(document.visibilityState==='visible')void load();},running?5000:30000);
    return()=>{stop=true;clearInterval(timer);};
  },[shiftId,running]);
  const done=view&&!['running','paused','finishing'].includes(view.status);
  // "A week of posts ready to approve in one tap": the posts it prepared that still wait on the owner, approved together.
  // Approving records the owner's decision on each; nothing is posted, and each post then offers its own way out.
  const [waiting,setWaiting]=useState<Draft[]>([]),[approving,setApproving]=useState(false),[approved,setApproved]=useState(0),[failed,setFailed]=useState('');
  const draftKeys=view?.prepared.map(piece=>piece.key).filter(key=>key.startsWith('draft:')).join(',')||'';
  useEffect(()=>{
    // The posts are ready as soon as they're made; the shift keeps its window open after that.
    if(!draftKeys)return;
    void api<{drafts:Draft[]}>('/marketing/state').then(state=>setWaiting(state.drafts.filter(draft=>draft.status==='pending'&&draftKeys.split(',').includes('draft:'+draft.id)))).catch(()=>{});
  },[draftKeys,approved]);
  async function approveAll(){
    if(approving)return;setApproving(true);setFailed('');
    let count=0;
    try{
      for(const draft of waiting){
        await api(`/marketing/drafts/${draft.id}/decision`,{requestId:requestId(),decision:'approved',revision:draft.revision,digest:draft.digest});
        await api('/feedback',{key:`draft:${draft.id}`,title:`${draft.channel} draft #${draft.id}`,verdict:'approved',note:''}).catch(()=>{});
        count++;
      }
    }catch(cause){setFailed((cause as Error).message);}
    finally{setApproved(current=>current+count);setApproving(false);}
  }
  return <div className="fe-first-shift">
    {running&&<><h4>Watching it work</h4><ShiftFeed shiftId={shiftId} running onOpen={onOpen} limit={5}/></>}
    {view&&(done||view.prepared.length>0||view.fixes.length>0)&&<div className="fe-first-shift-results" aria-label="First shift results">
      {view.positioning&&<section><h4>What it works from</h4><p>{view.positioning}</p></section>}
      {/* Once it's done, what it made is what to do next: each piece says where it stands and opens with one click. */}
      <section><h4>{done?'Your next steps':'What it prepared'} {view.prepared.length>0&&<span className="fe-count">{view.prepared.length}</span>}</h4>
        {done&&view.prepared.length>0&&<p className="fe-muted">Open each piece, then approve it or send it back with a note. What you send back, Chip finishes at its next shift.</p>}
        {view.prepared.length?<ul className="fe-next-steps">{view.prepared.map(piece=><li key={piece.key}>
          <button type="button" className="fe-link" onClick={()=>onOpen(piece.key)}><FileText size={13}/> {piece.title}</button>
          {piece.grade&&!piece.unmet.length&&<span className={'fe-grade g-'+piece.grade.toLowerCase()} title="Its grade on the marketing rubric">{piece.grade}</span>}
          <small className={piece.unmet.length?'fe-first-shift-unmet':'fe-first-shift-ready'}>{piece.unmet.length?`Not finished: still needs ${missing(piece.unmet)}`.replace(/([^….])$/,'$1.'):'Ready for your review.'}</small>
          {piece.sources.length>0&&<small className="fe-first-shift-sources">From {piece.sources.map((source,index)=><span key={index}>{index>0&&', '}{/^https?:\/\//.test(source.url)?<a href={source.url} target="_blank" rel="noopener noreferrer">{source.title} <ExternalLink size={10}/></a>:source.title}</span>)}</small>}
        </li>)}</ul>:<p className="fe-muted">{done?'Nothing was saved this shift; its report says why.':'Nothing saved yet.'}</p>}
        {waiting.length>1&&<div className="fe-approve-all"><button type="button" className="primary" disabled={approving} onClick={()=>void approveAll()}><Check size={14}/> {approving?'Approving…':`Approve all ${waiting.length} posts`}</button>
          <small>Approving records your decision. Nothing is posted: each post then offers to open its network’s composer or remind you at a time.</small></div>}
        {approved>0&&waiting.length===0&&<p className="fe-notice" role="status"><Check size={14}/> {approved} post{approved===1?'':'s'} approved. Post each from Chat, or have them reminded.</p>}
        {failed&&<p className="fe-alert" role="alert">{failed}</p>}
      </section>
      {/* No website and nothing to fix: nothing to say here (the first win already works on the page people find them by). */}
      {(view.site||view.fixes.length>0)&&<section><h4><Wrench size={13}/> {!view.site?'Your site':!view.fixes.length&&!view.pagesChecked?view.site:view.onlySuggestions||!view.fixes.length?`Nothing broken on ${view.site} (${view.pagesChecked} page${view.pagesChecked===1?'':'s'} checked)`:`${view.fixes.length===1?'One fix':view.fixes.length===2?'Two fixes':'Three fixes'} for ${view.site}`}</h4>
        {view.onlySuggestions&&<p className="fe-muted">Smaller things worth a look:</p>}
        {view.fixes.length?<ol>{view.fixes.map((fix,index)=><li key={index}><strong>{fix.check}</strong> <small>{fix.detail}{fix.url&&<> · <a href={fix.url} target="_blank" rel="noopener noreferrer">page</a></>}</small></li>)}</ol>
          :<p className="fe-muted">{view.siteNote||(view.pagesChecked?'Nothing to fix on the pages checked.':'The site check hasn’t finished yet.')}</p>}
          {view.competitorNote&&<p className="fe-muted">{view.competitorNote}</p>}
      </section>}
      {!view.callToActionSet&&<p className="fe-notice">Tip: say what customers should do next (like “Book a visit”) in your business brief, and every post ends on it.</p>}
    </div>}
  </div>;
}
