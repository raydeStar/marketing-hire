import {useCallback,useEffect,useState} from 'react';
import {shiftedHeadings} from './shared';
import {Check,ClipboardCopy,ExternalLink,FileText,LayoutTemplate} from 'lucide-react';
import Markdown from 'react-markdown';
import {api} from '../api';
import {publicLink,readableTime} from '../components/MarketingPanels';
import {usePublishing} from './PublishingView';

export type PageProposal={id:string;url:string;title:string;before:string;after:string;rationale:string;status:'pending'|'approved'|'rejected'|'applied'|'replaced';createdAt:string;by:string;
  decidedAt:string|null;decidedBy:string|null;note:string|null;appliedUrl:string|null;appliedAt:string|null};
type PageProposalData={ownSite:string|null;proposals:PageProposal[]};

const statusLabel:Record<PageProposal['status'],string>={pending:'Waiting for you',approved:'Approved · apply it',rejected:'Rejected',applied:'Applied',replaced:'Replaced by a newer version'};
const tone:Record<PageProposal['status'],string>={pending:'attn',approved:'',rejected:'',applied:'ok',replaced:''};
const seconds=(value:string)=>Date.parse(value)/1000;
const shortUrl=(url:string)=>url.replace(/^https:\/\/(www\.)?/,'').replace(/\/$/,'');
type LandingSection={type:string;title?:string;eyebrow?:string;subtitle?:string;text?:string;intro?:string;items?:unknown[];bullets?:string[]};
/** A landing-page proposal carries the site's sections as JSON; page copy for anything else is Markdown. */
export function landingOf(after:string):{title?:string;description?:string;sections:LandingSection[]}|null{
  if(!after.trimStart().startsWith('{'))return null;
  try{const value=JSON.parse(after);return Array.isArray(value?.sections)?value:null;}catch{return null;}
}
function LandingSections({page}:{page:NonNullable<ReturnType<typeof landingOf>>}){
  const line=(item:unknown)=>typeof item==='string'?item:Object.values(item as Record<string,string>).filter(value=>typeof value==='string').join(' — ');
  return <div className="fe-landing-sections">{page.title&&<p><strong>Page title:</strong> {page.title}</p>}{page.description&&<p className="fe-muted">{page.description}</p>}
    <ol>{page.sections.map((section,index)=><li key={index}><span className="fe-pill">{section.type}</span> <strong>{section.title||section.eyebrow||''}</strong>
      {(section.subtitle||section.text||section.intro)&&<p>{section.subtitle||section.text||section.intro}</p>}
      {!!(section.items?.length||section.bullets?.length)&&<ul>{[...(section.items||[]),...(section.bullets||[])].slice(0,8).map((item,position)=><li key={position}>{line(item)}</li>)}</ul>}</li>)}</ol></div>;
}

export function usePageProposals(){
  const [data,setData]=useState<PageProposalData|null>(null),[error,setError]=useState('');
  const load=useCallback(async()=>{try{setData(await api<PageProposalData>('/page-proposals'));setError('');}catch(cause){setError((cause as Error).message);}},[]);
  useEffect(()=>{void load();},[load]);
  return {data,error,load};
}

/** One proposal: the live page as it reads now beside the proposed copy, the reason, and the owner's decision. */
export function PageProposalView({id,owner}:{id:string;owner:boolean}){
  const {data,error,load}=usePageProposals();
  const publishing=usePublishing();
  const [why,setWhy]=useState(''),[busy,setBusy]=useState(false),[failure,setFailure]=useState(''),[copied,setCopied]=useState(false),[link,setLink]=useState(''),[sentBack,setSentBack]=useState('');
  const proposal=data?.proposals.find(item=>item.id===id);
  if(!data)return error?<p className="fe-alert">{error}</p>:null;
  if(!proposal)return <p className="fe-muted">This proposal is no longer available.</p>;
  const wordpress=(publishing.data?.connections||[]).filter(item=>item.kind==='wordpress'&&item.status==='ready');
  const sites=(publishing.data?.connections||[]).filter(item=>item.kind==='hirezero'&&item.status==='ready');
  const landing=landingOf(proposal.after);
  async function act(path:string,body:object){setBusy(true);setFailure('');try{await api(`/page-proposals/${id}/${path}`,body);await load();}catch(cause){setFailure((cause as Error).message);}finally{setBusy(false);}}
  // Sent back: the rewrite is queued with the reason first, then this version is rejected, so a failure never leaves it rejected with nothing coming.
  async function sendBack(){
    setBusy(true);setFailure('');
    try{const sent=await api<{message:string}>('/redrafts',{key:`pagecopy:${id}`,feedback:why.trim()});await api(`/page-proposals/${id}/decision`,{decision:'rejected',note:why.trim()});setSentBack(sent.message);await load();}
    catch(cause){setFailure((cause as Error).message);}finally{setBusy(false);}
  }
  async function copy(){try{await navigator.clipboard.writeText(proposal!.after);setCopied(true);}catch{setFailure('The browser blocked the clipboard; select the text and copy it instead.');}}
  const page=publicLink(proposal.url);
  return <article className="fe-page-proposal" aria-label={`New copy for ${shortUrl(proposal.url)}`}>
    <div className="fe-card-head"><div><span className={'fe-pill '+tone[proposal.status]}>{statusLabel[proposal.status]}</span> <small>Proposed {readableTime(seconds(proposal.createdAt))}</small></div>
      {page&&<a href={page} target="_blank" rel="noopener noreferrer">{shortUrl(proposal.url)} <ExternalLink size={13}/></a>}</div>
    <p className="fe-draft-why"><strong>Why:</strong> {proposal.rationale}</p>
    <div className="fe-before-after">
      <section aria-label="Now"><h4>Now on the page</h4><div className="fe-before">{proposal.before||<span className="fe-muted">The live page could not be read.</span>}</div></section>
      <section aria-label="Proposed"><h4>Proposed{landing?' · landing-page sections':''}</h4>{landing?<LandingSections page={landing}/>:<div className="fe-prose"><Markdown components={{...shiftedHeadings(4),img:()=>null}}>{proposal.after}</Markdown></div>}</section>
    </div>
    {proposal.status==='pending'&&owner&&<>
      <label className="fe-draft-feedback">Your reason <span className="fe-muted">(the employee learns from it; needed to send it back)</span><input maxLength={600} value={why} onChange={event=>setWhy(event.target.value)} placeholder="e.g. Keep the current headline; tighten the pricing section"/></label>
      <div className="fe-decision-bar"><small>Approving records your decision. It doesn’t change your site.</small>
        <button type="button" disabled={busy} onClick={()=>void act('decision',{decision:'rejected',note:why})}>Reject</button>
        <button type="button" disabled={busy||why.trim().length<3} title={why.trim().length<3?'Say what to change first':undefined} onClick={()=>void sendBack()}>Send back</button>
        <button type="button" className="primary" disabled={busy} onClick={()=>void act('decision',{decision:'approved',note:why})}>Approve</button></div></>}
    {(proposal.status==='approved'||proposal.status==='applied')&&owner&&<div className="fe-publish" aria-label="Apply the new copy">
      {proposal.status==='applied'?<p className="fe-notice" role="status"><Check size={14}/> Applied {proposal.appliedAt?readableTime(seconds(proposal.appliedAt)):''}.{proposal.appliedUrl&&<> <a href={proposal.appliedUrl} target="_blank" rel="noopener noreferrer">Open it <ExternalLink size={12}/></a></>}</p>
        :<p className="fe-muted">Put the new copy on your site yourself, then mark it applied. Nothing is changed on the live page from here.{!wordpress.length&&!sites.length?' Connect WordPress in Settings → Connections to save it as a draft page instead of copying.':''}</p>}
      <div className="fe-publish-row">
        <button type="button" onClick={()=>void copy()}><ClipboardCopy size={14}/> {copied?'Copied':'Copy the new text'}</button>
        {landing&&sites.map(connection=><button key={connection.id} type="button" className="primary" disabled={busy} onClick={()=>void act('site',{connectionId:connection.id})}><FileText size={14}/> Save as a draft on {connection.account}</button>)}
        {!landing&&wordpress.map(connection=><button key={connection.id} type="button" disabled={busy} onClick={()=>void act('wordpress',{connectionId:connection.id})}><FileText size={14}/> Save as a WordPress draft page ({connection.account})</button>)}
        {proposal.status==='approved'&&<><input value={link} onChange={event=>setLink(event.target.value)} placeholder="Link to the updated page (optional)" aria-label="Link to the updated page"/>
          <button type="button" className="primary" disabled={busy} onClick={()=>void act('applied',{url:link.trim()||null})}>It’s applied</button></>}
      </div>
    </div>}
    {proposal.status==='rejected'&&<p className="fe-muted">{sentBack||`Rejected${proposal.note?`: ${proposal.note}`:'.'}`}</p>}
    {proposal.status==='replaced'&&<p className="fe-muted">The employee replaced this with a better version; the newer proposal is in Work → Page changes.</p>}
    {failure&&<p className="fe-alert" role="alert">{failure}</p>}
  </article>;
}

/** Work → Page changes: proposed copy for pages on your site, waiting for you first. */
export function PageChangesSection({onOpen}:{onOpen:(key:string)=>void}){
  const {data}=usePageProposals();
  if(!data||data.proposals.length===0)return null;
  const order={pending:0,approved:1,applied:2,rejected:3,replaced:4};
  return <section className="fe-section" aria-label="Page changes">
    <div className="fe-section-head"><div><h2>Page changes</h2><small>New copy the employee proposes for pages on {data.ownSite||'your site'}, with what the page says now. You decide and apply it; nothing changes on the site from here.</small></div></div>
    {data.proposals.filter(item=>item.status!=='replaced').sort((a,b)=>order[a.status]-order[b.status]||b.createdAt.localeCompare(a.createdAt)).slice(0,8).map(item=>
      <button key={item.id} type="button" className="fe-list-row" onClick={()=>onOpen('pagecopy:'+item.id)}>
        <span className="fe-row-icon"><LayoutTemplate size={16}/></span>
        <span className="fe-list-main"><strong>{item.title}</strong><small>{shortUrl(item.url)} · {readableTime(seconds(item.createdAt))}</small></span>
        <span className={'fe-status-chip '+(item.status==='pending'?'warn':item.status==='applied'?'live':'')}>{statusLabel[item.status]}</span></button>)}
  </section>;
}
