import {useEffect,useState} from 'react';
import Markdown from 'react-markdown';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import type {Library,WikiPage} from './library';
import type {Campaign} from './campaigns';
import {DraftCard} from './InboxView';
import {PageProposalView,type PageProposal} from './PageCopy';
import {PolishedDraftComparison} from './ArtifactCompare';
import {draftText} from './draftText';
import {TaskDetail} from './TasksView';
import './magical-web.css';

type Attachment={id:string;name:string;mediaType:string;bytes:number};
type PieceMetadata={key:string;week:string|null;channel:string|null;grade:string|null;claims:{text:string;url?:string|null;sourceKey?:string|null}[];blockers:string[]};
type PackageMetadata={campaignId:string;angle:string|null;pieces:PieceMetadata[]};
const field=(text:string,name:string)=>new RegExp('^\\s*(?:[-*]\\s*)?(?:\\*\\*)?'+name+'(?:\\*\\*)?\\s*:\\s*(.+)$','im').exec(text)?.[1]?.trim()||'';
export function reviewSummary(text:string){const match=/(?:Marketing rubric|Review grade|Grade)\s*:?\s*([A-F][+-]?)(?:\s*→\s*([A-F][+-]?))?(?:\s|\(|[,.;]|$)/i.exec(text);return (match?.[2]||match?.[1])?.toUpperCase()||null;}
function withoutReview(text:string){return text.replace(/\n\n---\n\n_Marketing rubric[\s\S]*$/,'');}
function previewOf(text:string){return withoutReview(text).replace(/^\s*(?:Week|Channel|Claims|Blocker)\s*:[^\n]*\n?/gim,'').trim();}
const labels:Record<string,string>={pending:'Needs approval',approved:'Approved · not posted',posted:'Posted',rejected:'Sent back / rejected',withdrawn:'Withdrawn',ready:'Assigned',working:'In progress',needs_you:'Needs your decision',blocked:'Blocked',done:'Complete',draft:'Draft',active:'Shared with employee',applied:'Applied',replaced:'Replaced',archived:'Archived'};

export function CampaignPackage({campaign,keys,state,library,owner,onOpen,onRefresh,onChat}:{campaign:Campaign;keys:string[];state:MarketingState;library:Library;owner:boolean;onOpen:(key:string)=>void;onRefresh:()=>Promise<void>;onChat?:(text:string)=>void}){
  const [proposals,setProposals]=useState<PageProposal[]>([]),[media,setMedia]=useState<Record<string,Attachment[]>>({}),[error,setError]=useState('');
  const [metadata,setMetadata]=useState<PackageMetadata|null>(null);
  const [selected,setSelected]=useState<string|null>(null),[busy,setBusy]=useState(false),[note,setNote]=useState(''),[message,setMessage]=useState('');
  useEffect(()=>{if(selected)requestAnimationFrame(()=>document.querySelector('section[aria-label="Piece review"]')?.scrollIntoView({block:'start',behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'}));},[selected]);
  useEffect(()=>{let stop=false;setError('');void Promise.all([api<{proposals:PageProposal[]}>('/page-proposals'),api<Record<string,Attachment[]>>('/drafts/media'),api<PackageMetadata>('/campaigns/'+encodeURIComponent(campaign.id)+'/pieces').catch(()=>null)]).then(([pages,attachments,details])=>{if(!stop){setProposals(pages.proposals);setMedia(attachments);setMetadata(details);}}).catch(cause=>{if(!stop)setError('Some package details couldn’t load. '+(cause as Error).message);});return()=>{stop=true;};},[campaign.id,state.drafts.map(item=>item.id+':'+item.status).join('|'),library.wiki.map(item=>item.id+':'+item.version).join('|')]);
  const plan=library.wiki.find(page=>page.id===campaign.planWikiId);
  const attachmentKeys=keys.filter(key=>key.startsWith('draft:')).flatMap(key=>(media[key.slice(6)]||[]).map(file=>'media:'+file.id));
  const pieces=[...new Set([...keys,...attachmentKeys])].filter(key=>key!=='wiki:'+campaign.planWikiId).map(key=>{
    const [kind,...rest]=key.split(':'),id=rest.join(':');
    const draft=state.drafts.find(item=>kind==='draft'&&String(item.id)===id),task=state.tasks.find(item=>kind==='task'&&item.id===id),doc=library.wiki.find(item=>kind==='wiki'&&item.id===id),proposal=proposals.find(item=>kind==='pagecopy'&&item.id===id),item=library.items.find(item=>item.key===key),file=library.uploads.find(item=>kind==='media'&&item.id===id);
    const text=draft?draftText(draft):doc?.body||proposal?.after||task?.next_action||item?.body||'';
    const notes=draft?.rationale||proposal?.rationale||text;
    const detail=metadata?.pieces.find(item=>item.key===key);
    const parent=kind==='media'?state.drafts.find(draft=>keys.includes('draft:'+draft.id)&&media[String(draft.id)]?.some(file=>file.id===id)):undefined;
    const week=detail?.week||field(notes,'Week')||field(text,'Week')||(parent?metadata?.pieces.find(item=>item.key==='draft:'+parent.id)?.week||field(parent.rationale,'Week'):'')||'Week not set';
    const channel=detail?.channel||draft?.channel||field(text,'Channel')||parent?.channel||(kind==='pagecopy'?'Website':kind==='task'?'Tasks':kind==='media'?'Media':'Supporting documents');
    const status=draft?.status||task?.status||doc?.status||proposal?.status||(file?.archived?'archived':file?'Prepared':'Unavailable');
    return {key,kind,id,draft,task,doc,proposal,item,file,text,notes,week:/^\d+$/.test(week)?'Week '+week:/^\d{4}-\d{2}-\d{2}$/.test(week)?'Week of '+week:week,channel,status,title:doc?.title||proposal?.title||task?.title||item?.title||(draft?draft.channel+' draft #'+draft.id:key),grade:detail?.grade||reviewSummary(notes),claims:field(notes,'Claims')||field(text,'Claims'),claimRecords:detail?.claims||[],blocker:[...detail?.blockers||[],task?.blocker||field(notes,'Blocker')||field(text,'Blocker')].filter(Boolean).join('; '),available:!!(draft||task||doc||proposal||item||file)};
  });
  const weeks=[...new Set(pieces.map(piece=>piece.week))].sort((a,b)=>a==='Week not set'?1:b==='Week not set'?-1:a.localeCompare(b,undefined,{numeric:true}));
  const piece=pieces.find(item=>item.key===selected);
  async function docDecision(page:WikiPage,sendBack:boolean){
    if(busy)return;setBusy(true);setMessage('');
    try{if(sendBack){const result=await api<{message:string}>('/redrafts',{key:'wiki:'+page.id,feedback:note.trim()});setMessage(result.message);}else{await api('/company-wiki',{requestId:crypto.randomUUID(),id:page.id,version:page.version,scope:page.scope,scopeId:page.scopeId,title:page.title,body:page.body,kind:page.kind,status:'active'},'PUT');setMessage('Document shared with the employee.');}setNote('');await library.reload();await onRefresh();}catch(cause){setMessage((cause as Error).message);}finally{setBusy(false);}
  }
  return <div className="fe-package-review">
    <section className="fe-campaign-plan" aria-label="Campaign plan"><h3>The plan</h3>{plan?<><div className="fe-prose"><Markdown components={{img:()=>null}}>{withoutReview(plan.body)}</Markdown></div><button type="button" className="fe-link" onClick={()=>onOpen('wiki:'+plan.id)}>Open plan and version history →</button></>:<p className="fe-muted">No plan document attached yet.</p>}</section>
    <section aria-label="Campaign angle"><h3>The central angle</h3><p>{metadata?.angle||plan&&(field(plan.body,'Angle')||field(plan.body,'Central angle'))||'The plan has no explicit angle recorded yet.'}</p><p><strong>Goal:</strong> {campaign.goal||'Goal not set.'}</p></section>
    <section aria-label="Campaign package"><h3>Review the package</h3><p className="fe-muted">One piece at a time, in the context of the plan. Grades are the employee’s editorial review.</p>
      {!pieces.length&&<p className="fe-muted">The plan is ready; no other pieces have been filed yet.</p>}
      {weeks.map(week=><section className="fe-package-week" key={week} aria-label={week}><h4>{week}</h4>{[...new Set(pieces.filter(item=>item.week===week).map(item=>item.channel))].map(channel=><div className="fe-package-channel" key={channel}><h5>{channel}</h5><div className="fe-campaign-piece-grid">{pieces.filter(item=>item.week===week&&item.channel===channel).map(item=><article className="fe-campaign-piece" key={item.key} aria-label={item.title}>
        <div className="fe-piece-meta"><span>{item.kind==='wiki'?'Document':item.kind==='pagecopy'?'Page copy':item.kind==='draft'?'Draft':item.kind==='task'?'Task':'Media'}</span><span className="fe-pill">{labels[item.status]||item.status}</span></div><h4>{item.title}</h4>
        {item.file?.mediaType.startsWith('image/')&&<img className="fe-piece-image" src={'/api/uploads/'+item.id+'/content'} alt={item.file.name}/>}
        {item.file?.mediaType.startsWith('video/')&&<video className="fe-piece-image" src={'/api/uploads/'+item.id+'/content'} controls playsInline preload="metadata"/>}
        {item.kind!=='media'&&item.text&&<div className="fe-piece-preview fe-prose"><Markdown components={{img:()=>null}}>{previewOf(item.text).slice(0,350)+(previewOf(item.text).length>350?'…':'')}</Markdown></div>}
        {!!item.draft&&media[item.id]?.length>0&&<small>{media[item.id].length} media attachment{media[item.id].length===1?'':'s'}</small>}
        <dl className="fe-piece-facts"><div><dt>Review grade</dt><dd>{item.grade||'Not recorded'}</dd></div><div><dt>Claims relied on</dt><dd>{item.claimRecords.length?item.claimRecords.map((claim,index)=><div key={index}>{claim.text} {claim.sourceKey?<button type="button" className="fe-link" onClick={()=>onOpen(claim.sourceKey!)}>Evidence →</button>:claim.url&&/^https?:\/\//i.test(claim.url)?<a href={claim.url} target="_blank" rel="noopener noreferrer">Evidence ↗</a>:null}</div>):item.claims||'No claim references recorded'}</dd></div><div><dt>Blocker</dt><dd>{item.blocker||'None recorded'}</dd></div></dl>
        <div className="fe-actions"><button type="button" disabled={!item.available} className={item.status==='pending'?'primary':''} onClick={()=>{setSelected(item.key);setMessage('');setNote('');}}>Review piece</button><button type="button" className="fe-ghost" disabled={!item.available} onClick={()=>onOpen(item.key)}>Open work →</button></div>
      </article>)}</div></div>)}</section>)}
    </section>
    {error&&<p className="fe-alert" role="status">{error}</p>}
    {piece&&<section className="fe-package-piece-review" aria-label="Piece review"><div className="fe-card-head"><h3>{piece.title}</h3><button type="button" className="fe-ghost" onClick={()=>setSelected(null)}>Close review</button></div>
      {piece.draft?<><DraftCard draft={piece.draft} canDecide={owner} onRefresh={onRefresh} onAsk={onChat} onOpen={onOpen} uploads={library.uploads} reviewOnly/><PolishedDraftComparison draft={piece.draft} state={state}/></>
        :piece.proposal?<PageProposalView id={piece.id} owner={owner}/>:piece.task?<TaskDetail task={piece.task} state={state} canWrite={owner} canChat={false} onRefresh={onRefresh} onOpen={onOpen}/>:<><div className="fe-prose"><Markdown components={{img:()=>null}}>{piece.text}</Markdown></div>{piece.doc&&owner&&piece.doc.status!=='archived'&&<div className="fe-form"><label>What should change? <textarea value={note} maxLength={1000} rows={2} onChange={event=>setNote(event.target.value)}/></label><div className="fe-actions"><button type="button" disabled={busy||note.trim().length<3} onClick={()=>void docDecision(piece.doc!,true)}>Send back</button>{piece.doc.status==='draft'&&<button type="button" className="primary" disabled={busy} onClick={()=>void docDecision(piece.doc!,false)}>Approve document</button>}</div><small>Approving shares this document with the employee; it doesn’t publish externally.</small></div>}{piece.file&&<button type="button" onClick={()=>onOpen(piece.key)}>Open media preview →</button>}</>}
      {message&&<p className="fe-notice" role="status">{message}</p>}
    </section>}
  </div>;
}
