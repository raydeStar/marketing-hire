import {useEffect,useState} from 'react';
import {ExternalLink,FileText,Wrench} from 'lucide-react';
import {api} from '../api';
import {ShiftFeed} from './ShiftFeed';

type Source={url:string;title:string;coverage:string};
type Piece={key:string;title:string;grade?:string|null;unmet:string[];sources:Source[]};
type Fix={severity:string;check:string;url:string;detail:string};
const clip=(text:string,length=110)=>text.length>length?text.slice(0,length-1).trimEnd()+'…':text;
type View={shiftId:string;status:string;endsAt:string;positioning?:string|null;prepared:Piece[];fixes:Fix[];site?:string|null;siteNote?:string|null;callToActionSet:boolean};

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
  return <div className="fe-first-shift">
    {running&&<><h4>Watching it work</h4><ShiftFeed shiftId={shiftId} running onOpen={onOpen} limit={5}/></>}
    {view&&(done||view.prepared.length>0||view.fixes.length>0)&&<div className="fe-first-shift-results" aria-label="First shift results">
      {view.positioning&&<section><h4>What it works from</h4><p>{view.positioning}</p></section>}
      <section><h4>What it prepared {view.prepared.length>0&&<span className="fe-count">{view.prepared.length}</span>}</h4>
        {view.prepared.length?<ul>{view.prepared.map(piece=><li key={piece.key}>
          <button type="button" className="fe-link" onClick={()=>onOpen(piece.key)}><FileText size={13}/> {piece.title}</button>
          {piece.grade&&<span className={'fe-grade g-'+piece.grade.toLowerCase()} title="Its grade on the marketing rubric">{piece.grade}</span>}
          {piece.unmet.length>0&&<small className="fe-first-shift-unmet" title={piece.unmet.join('; ')}>Doesn’t meet yet: {clip(piece.unmet[0].replace(/^(asked|your note): /,''))}{piece.unmet.length>1?` (and ${piece.unmet.length-1} more)`:''}</small>}
          {piece.sources.length>0&&<small className="fe-first-shift-sources">From {piece.sources.map((source,index)=><span key={index}>{index>0&&', '}{/^https?:\/\//.test(source.url)?<a href={source.url} target="_blank" rel="noopener noreferrer">{source.title} <ExternalLink size={10}/></a>:source.title}</span>)}</small>}
        </li>)}</ul>:<p className="fe-muted">{done?'Nothing was saved this shift; its report says why.':'Nothing saved yet.'}</p>}
      </section>
      <section><h4><Wrench size={13}/> {view.site?`Three fixes for ${view.site}`:'Your site'}</h4>
        {view.fixes.length?<ol>{view.fixes.map((fix,index)=><li key={index}><strong>{fix.check}</strong> <small>{fix.detail}{fix.url&&<> · <a href={fix.url} target="_blank" rel="noopener noreferrer">page</a></>}</small></li>)}</ol>
          :<p className="fe-muted">{view.siteNote||'No problems found on the pages checked.'}</p>}
      </section>
      {!view.callToActionSet&&<p className="fe-notice">Set your call to action (Settings → Go live) so every public piece ends on the one next step you want.</p>}
    </div>}
  </div>;
}
