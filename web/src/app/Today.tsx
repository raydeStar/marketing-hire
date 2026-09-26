import {useEffect,useState} from 'react';
import {ArrowRight,ChevronRight,CircleCheckBig,FileText,Lightbulb} from 'lucide-react';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import {inboxItems,type InboxItem} from './InboxView';
import {useCampaigns} from './campaigns';
import './magical-web.css';

export type Opportunity={id:string;headline:string;why:string;recommendation:string;prepared:{key:string;kind:string;title:string}[];evidence:{title:string;url?:string;key?:string}[];decisions:{id:string;label:string;primary?:boolean}[]};
export type TodayPayload={opportunity:Opportunity|null;today:InboxItem[];later:InboxItem[]};
export function useToday(state:MarketingState){
  const [data,setData]=useState<TodayPayload|null>(null),[error,setError]=useState('');
  const stamp=state.tasks.map(item=>item.id+':'+item.version).join('|')+state.drafts.map(item=>item.id+':'+item.status+':'+item.revision).join('|');
  useEffect(()=>{
    let disposed=false;
    const load=async()=>{try{const result=await api<TodayPayload>('/today');if(!disposed){setData(result);setError('');}}catch{if(!disposed)setData(null);}};
    void load();const timer=setInterval(()=>{if(!document.hidden)void load();},15000);
    return()=>{disposed=true;clearInterval(timer);};
  },[stamp]);
  async function decide(id:string,decision:string,note?:string){
    try{setData(await api<TodayPayload>('/today/'+encodeURIComponent(id)+'/decision',{decision,...(note?.trim()?{note:note.trim()}:{})}));setError('');}
    catch(cause){setError((cause as Error).message);throw cause;}
  }
  const fallback=inboxItems(state);
  return {data:data||{opportunity:null,today:fallback.slice(0,3),later:fallback.slice(3)},legacy:data===null,error,decide};
}

/** The contract determines judgment. The cockpit presents one choice and its saved work. */
export function TodayDesk({state,owner,onOpen,onOpenItem,onChat,next}:{state:MarketingState;owner:boolean;onOpen:(key:string)=>void;onOpenItem:(item:InboxItem)=>void;onChat?:(text:string)=>void;next:string}){
  const {data,legacy,error,decide}=useToday(state);
  const book=useCampaigns();
  const [busy,setBusy]=useState(''),[note,setNote]=useState(''),[noting,setNoting]=useState<'park'|'change'|null>(null),[saved,setSaved]=useState('');
  const item=data.opportunity;
  useEffect(()=>{setNoting(null);setNote('');},[item?.id]);
  async function act(decision:string){
    if(!item||busy)return;
    if((decision==='park'||decision==='change')&&noting!==decision){setNoting(decision);setNote('');return;}
    setBusy(decision);
    try{
      if(owner)await decide(item.id,decision,note);
      if(decision==='review'&&item.prepared[0]){const campaign=book?.of(item.prepared[0].key);onOpen(campaign&&item.prepared.every(piece=>book?.of(piece.key)?.id===campaign.id)?'campaign:'+campaign.id:item.prepared[0].key);}
      if(decision==='change')setSaved('Direction saved. The employee will address it in the next authorized shift.');
      if(decision==='park')setSaved('Opportunity parked. Its prepared work remains in the workspace.');
      setNoting(null);setNote('');
    }catch{/* Keep the host's error visible; a failed decision is never celebrated. */}finally{setBusy('');}
  }
  const todays=data.today.slice(0,3),later=[...data.today.slice(3),...data.later];
  const row=(entry:InboxItem)=><button type="button" className="fe-cockpit-item" key={entry.id} onClick={()=>onOpenItem(entry)}><FileText size={14}/><span><strong>{entry.title}</strong><small>{entry.detail}</small></span><ChevronRight size={14}/></button>;
  return <div className="fe-today-desk">
    {item&&<section className="fe-opportunity" aria-label="Prepared opportunity" key={item.id}>
      <span className="fe-opportunity-label"><Lightbulb size={14}/> Prepared for you</span>
      <h3 title={item.headline}>{item.headline}</h3><p>{item.why}</p>
      <ul className="fe-opportunity-pieces">{item.prepared.map(piece=><li key={piece.key}><button type="button" onClick={()=>onOpen(piece.key)}><FileText size={13}/><span>{piece.title}</span><ArrowRight size={13}/></button></li>)}</ul>
      <p className="fe-opportunity-choice">{item.recommendation}</p>
      <div className="fe-opportunity-decisions">{item.decisions.map(decision=><button type="button" className={decision.primary?'primary':'fe-ghost'} key={decision.id} disabled={!!busy||!owner&&decision.id!=='review'} onClick={()=>void act(decision.id)}>{busy===decision.id?'Saving…':decision.label}</button>)}</div>
      {noting&&<div className="fe-opportunity-note"><label>{noting==='park'?'Reason to park':'Which direction should the employee take?'} {noting==='park'&&<span className="fe-muted">(optional)</span>}<textarea rows={2} maxLength={600} value={note} onChange={event=>setNote(event.target.value)}/></label><div className="fe-actions"><button type="button" className="fe-ghost" onClick={()=>setNoting(null)}>Cancel</button><button type="button" disabled={!!busy||noting==='change'&&note.trim().length<3} onClick={()=>void act(noting)}>{noting==='park'?'Park opportunity':'Save direction'}</button></div></div>}
      <details><summary>Evidence ({item.evidence.length})</summary>{item.evidence.length?<ul>{item.evidence.map((entry,index)=>{const url=entry.url&&/^https?:\/\//i.test(entry.url)?entry.url:null;return <li key={index}>{entry.key?<button type="button" className="fe-link" onClick={()=>onOpen(entry.key!)}>{entry.title}</button>:url?<a href={url} target="_blank" rel="noopener noreferrer">{entry.title}</a>:entry.title}</li>;})}</ul>:<p>No evidence attached yet.</p>}</details>
      {error&&<p className="fe-alert" role="alert">{error}</p>}
    </section>}
    {saved&&<p className="fe-today-next" role="status">{saved}</p>}
    <section className="fe-cockpit-section" aria-label={legacy?(owner?'Needs your decision':'Waiting on the owner'):'Today'}>
      <h3>Today <span className="fe-count">{todays.length}</span></h3>
      {todays.length?<div className="fe-cockpit-list">{todays.map(row)}</div>:<p className="fe-cockpit-clear"><CircleCheckBig size={15}/> {item?'This is the one decision to make.':`Nothing needs ${owner?'you':'the owner'} right now.`}</p>}
      {!item&&!todays.length&&<p className="fe-today-next">{next}</p>}
      {later.length>0&&<details className="fe-today-later"><summary>Later ({later.length})</summary><div className="fe-cockpit-list">{later.map(row)}</div></details>}
    </section>
  </div>;
}
