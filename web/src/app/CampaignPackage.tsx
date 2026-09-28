import {useEffect,useState} from 'react';
import Markdown from 'react-markdown';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import type {Library,WikiPage} from './library';
import {useCampaigns,type Campaign} from './campaigns';
import {DraftCard} from './InboxView';
import {PageProposalView,type PageProposal} from './PageCopy';
import {PolishedDraftComparison} from './ArtifactCompare';
import {draftText} from './draftText';
import {TaskDetail} from './TasksView';
import {shiftedHeadings} from './shared';
import type {ScorecardData} from './ScorecardView';
import './magical-web.css';

type Attachment={id:string;name:string;mediaType:string;bytes:number};
type PieceMetadata={key:string;week:string|null;channel:string|null;grade:string|null;claims:{text:string;url?:string|null;sourceKey?:string|null}[];blockers:string[]};
type PackageMetadata={campaignId:string;angle:string|null;pieces:PieceMetadata[]};
const field=(text:string,name:string)=>new RegExp('^\\s*(?:[-*]\\s*)?(?:\\*\\*)?'+name+'(?:\\*\\*)?\\s*:\\s*(.+)$','im').exec(text)?.[1]?.trim()||'';
export function reviewSummary(text:string){const match=/(?:Marketing rubric|Review grade|Grade)\s*:?\s*([A-F][+-]?)(?:\s*→\s*([A-F][+-]?))?(?:\s|\(|[,.;]|$)/i.exec(text);return (match?.[2]||match?.[1])?.toUpperCase()||null;}
function withoutReview(text:string){return text.replace(/\n\n---\n\n_Marketing rubric[\s\S]*$/,'');}
function previewOf(text:string){return withoutReview(text).replace(/^\s*(?:Week|Channel|Claims|Blocker)\s*:[^\n]*\n?/gim,'').trim();}
const labels:Record<string,string>={pending:'Needs approval',approved:'Approved · ready to post',posted:'Posted',rejected:'Sent back',withdrawn:'Withdrawn',ready:'Assigned',working:'In progress',needs_you:'Needs your decision',blocked:'Stuck',done:'Complete',draft:'Needs review',active:'Approved',applied:'Applied',replaced:'Replaced',archived:'Archived'};

/** A campaign with no plan yet: one click has the employee write it, filed with the campaign, at its next check-in. */
function PlanThisCampaign({campaign,state,onRefresh}:{campaign:Campaign;state:MarketingState;onRefresh:()=>Promise<void>}){
  const book=useCampaigns();
  const title=`Campaign plan: ${campaign.name}`;
  const queued=state.tasks.find(task=>task.title===title&&task.status!=='done');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  async function plan(){
    if(busy||!book)return;setBusy(true);setError('');
    try{
      const facts=[campaign.goal&&`The goal: ${campaign.goal}.`,(campaign.starts||campaign.ends)&&`It runs ${[campaign.starts&&'from '+campaign.starts,campaign.ends&&'to '+campaign.ends].filter(Boolean).join(' ')}.`,campaign.channels.length&&`Channels: ${campaign.channels.join(', ')}.`,campaign.moves&&`Measured by: ${campaign.moves}.`].filter(Boolean).join(' ');
      const made=await api<{id:string}>('/marketing/tasks',{requestId:crypto.randomUUID(),title,status:'ready',priority:'high',action_state:'agent_ready',
        next_action:`Deliver: the plan for the campaign “${campaign.name}”. ${facts} The plan: the central angle, the channels, a week-by-week (or day-by-day) list of the posts and emails with what each says, and what to measure; end on the owner's decision. Guidance: only the brief's offers, prices and proof points; the dates as dates; nothing is posted or sent until the owner approves it.`.slice(0,1000)});
      await book.assign('task:'+made.id,campaign.id);await onRefresh();
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <div className="fe-plan-this">
    {queued?<p className="fe-notice" role="status">The plan is assigned. {state.employee.name||'Your employee'} writes it at its next check-in{queued.status==='working'?' (it’s on it now)':''}, and it appears here.</p>
      :<><p className="fe-muted">No plan yet.</p><button type="button" className="primary" disabled={busy} onClick={()=>void plan()}>{busy?'Assigning…':'Have it plan this campaign'}</button>
        <small className="fe-muted">It writes the angle, the channels and each week’s posts and emails for your review. Nothing goes out until you approve it.</small></>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </div>;
}

export function CampaignPackage({campaign,keys,state,library,owner,onOpen,onRefresh,onChat}:{campaign:Campaign;keys:string[];state:MarketingState;library:Library;owner:boolean;onOpen:(key:string)=>void;onRefresh:()=>Promise<void>;onChat?:(text:string)=>void}){
  const [proposals,setProposals]=useState<PageProposal[]>([]),[media,setMedia]=useState<Record<string,Attachment[]>>({}),[error,setError]=useState('');
  const [metadata,setMetadata]=useState<PackageMetadata|null>(null);
  const [scorecard,setScorecard]=useState<ScorecardData|null>(null);
  const [selected,setSelected]=useState<string|null>(null),[busy,setBusy]=useState(false),[note,setNote]=useState(''),[message,setMessage]=useState('');
  useEffect(()=>{if(selected)requestAnimationFrame(()=>document.querySelector('section[aria-label="Piece review"]')?.scrollIntoView({block:'start',behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'instant':'smooth'}));},[selected]);
  useEffect(()=>{let stop=false;setError('');void Promise.all([api<{proposals:PageProposal[]}>('/page-proposals'),api<Record<string,Attachment[]>>('/drafts/media'),api<PackageMetadata>('/campaigns/'+encodeURIComponent(campaign.id)+'/pieces').catch(()=>null)]).then(([pages,attachments,details])=>{if(!stop){setProposals(pages.proposals);setMedia(attachments);setMetadata(details);}}).catch(cause=>{if(!stop)setError('Some package details couldn’t load. '+(cause as Error).message);});return()=>{stop=true;};},[campaign.id,state.drafts.map(item=>item.id+':'+item.status).join('|'),library.wiki.map(item=>item.id+':'+item.version).join('|')]);
  const experimentKeys=keys.filter(key=>key.startsWith('exp:')).join('|');
  useEffect(()=>{if(!experimentKeys)return;let stop=false;void api<ScorecardData>('/scorecard').then(next=>{if(!stop)setScorecard(next);}).catch(cause=>{if(!stop)setError('Experiment details couldn’t load. '+(cause as Error).message);});return()=>{stop=true;};},[campaign.id,experimentKeys]);
  const plan=library.wiki.find(page=>page.id===campaign.planWikiId);
  const attachmentKeys=keys.filter(key=>key.startsWith('draft:')).flatMap(key=>(media[key.slice(6)]||[]).map(file=>'media:'+file.id));
  const pieces=[...new Set([...keys,...attachmentKeys])].filter(key=>key!=='wiki:'+campaign.planWikiId).map(key=>{
    const [kind,...rest]=key.split(':'),id=rest.join(':');
    const draft=state.drafts.find(item=>kind==='draft'&&String(item.id)===id),task=state.tasks.find(item=>kind==='task'&&item.id===id),doc=library.wiki.find(item=>kind==='wiki'&&item.id===id),proposal=proposals.find(item=>kind==='pagecopy'&&item.id===id),item=library.items.find(item=>item.key===key),file=library.uploads.find(item=>kind==='media'&&item.id===id);
    const measured=kind==='exp'?scorecard?.experiments.find(item=>item.experiment.id===id):undefined;
    const experiment=measured?.experiment;
    const text=draft?draftText(draft):doc?.body||proposal?.after||task?.next_action||experiment?.hypothesis||item?.body||'';
    const notes=draft?.rationale||proposal?.rationale||text;
    const detail=metadata?.pieces.find(item=>item.key===key);
    const parent=kind==='media'?state.drafts.find(draft=>keys.includes('draft:'+draft.id)&&media[String(draft.id)]?.some(file=>file.id===id)):undefined;
    const week=detail?.week||field(notes,'Week')||field(text,'Week')||(parent?metadata?.pieces.find(item=>item.key==='draft:'+parent.id)?.week||field(parent.rationale,'Week'):'')||'Week not set';
    const channel=detail?.channel||draft?.channel||field(text,'Channel')||parent?.channel||(kind==='pagecopy'?'Website':kind==='task'?'Tasks':kind==='media'?'Media':kind==='exp'?'Measurement':'Documents');
    const status=draft?.status||task?.status||doc?.status||proposal?.status||experiment?.status||(file?.archived?'archived':file?'Prepared':'Unavailable');
    return {key,kind,id,draft,task,doc,proposal,item,file,measured,text,notes,week:/^\d+$/.test(week)?'Week '+week:/^\d{4}-\d{2}-\d{2}$/.test(week)?'Week of '+week:week,channel,status,title:doc?.title||proposal?.title||task?.title||experiment?.title||item?.title||(draft?draft.channel+' draft #'+draft.id:key),grade:detail?.grade||reviewSummary(notes),claims:field(notes,'Claims')||field(text,'Claims'),claimRecords:detail?.claims||[],blocker:[...detail?.blockers||[],task?.blocker||field(notes,'Blocker')||field(text,'Blocker')].filter(Boolean).join('; '),available:!!(draft||task||doc||proposal||item||file||experiment)};
  });
  const weeks=[...new Set(pieces.map(piece=>piece.week))].sort((a,b)=>a==='Week not set'?1:b==='Week not set'?-1:a.localeCompare(b,undefined,{numeric:true}));
  const piece=pieces.find(item=>item.key===selected);
  async function docDecision(page:WikiPage,sendBack:boolean){
    if(busy)return;setBusy(true);setMessage('');
    try{if(sendBack){const result=await api<{message:string}>('/redrafts',{key:'wiki:'+page.id,feedback:note.trim()});setMessage(result.message);}else{await api('/company-wiki',{requestId:crypto.randomUUID(),id:page.id,version:page.version,scope:page.scope,scopeId:page.scopeId,title:page.title,body:page.body,kind:page.kind,status:'active'},'PUT');setMessage('Document shared with the employee.');}setNote('');await library.reload();await onRefresh();}catch(cause){setMessage((cause as Error).message);}finally{setBusy(false);}
  }
  return <div className="fe-package-review">
    <section className="fe-campaign-plan" aria-label="Campaign plan"><h2>The plan</h2>{plan?<><div className="fe-prose"><Markdown components={{img:()=>null,...shiftedHeadings(2)}}>{withoutReview(plan.body)}</Markdown></div><button type="button" className="fe-link" onClick={()=>onOpen('wiki:'+plan.id)}>Open plan and version history →</button></>:owner?<PlanThisCampaign campaign={campaign} state={state} onRefresh={onRefresh}/>:<p className="fe-muted">No plan yet.</p>}</section>
    <section aria-label="Campaign angle"><h2>The central angle</h2><p>{metadata?.angle||plan&&(field(plan.body,'Angle')||field(plan.body,'Central angle'))||(plan?'The plan doesn’t name one yet.':'It comes with the plan.')}</p><p><strong>Goal:</strong> {campaign.goal||'Goal not set.'}</p></section>
    <section aria-label="Campaign package"><h2>Review the package</h2><p className="fe-muted">{pieces.length} piece{pieces.length===1?'':'s'} · {pieces.filter(item=>['pending','draft','needs_you','proposed'].includes(item.status)).length} waiting for review. Grades are the employee’s own review of its work.</p>
      {!pieces.length&&<p className="fe-muted">{plan?'The plan is ready; the pieces come next.':'Nothing yet: pieces arrive as it works on this campaign.'}</p>}
      {weeks.map(week=><section className="fe-package-week" key={week} aria-label={week}><h3>{week}</h3>{[...new Set(pieces.filter(item=>item.week===week).map(item=>item.channel))].map(channel=><div className="fe-package-channel" key={channel}><h4>{channel}</h4><div className="fe-campaign-piece-grid">{pieces.filter(item=>item.week===week&&item.channel===channel).map(item=><article className="fe-campaign-piece" key={item.key} aria-label={item.title}>
        <div className="fe-piece-meta"><span>{item.kind==='wiki'?'Document':item.kind==='pagecopy'?'Page copy':item.kind==='draft'?'Draft':item.kind==='task'?'Task':item.kind==='exp'?'Experiment':'Media'}</span><span className="fe-pill">{labels[item.status]||item.status}</span></div><h5>{item.title}</h5>
        {item.file?.mediaType.startsWith('image/')&&<img className="fe-piece-image" src={'/api/uploads/'+item.id+'/content'} alt={item.file.name}/>}
        {item.file?.mediaType.startsWith('video/')&&<video className="fe-piece-image" src={'/api/uploads/'+item.id+'/content'} controls playsInline preload="metadata"/>}
        {item.kind!=='media'&&item.text&&<div className="fe-piece-preview fe-prose"><Markdown components={{img:()=>null,...shiftedHeadings(5)}}>{previewOf(item.text).slice(0,350)+(previewOf(item.text).length>350?'…':'')}</Markdown></div>}
        {!!item.draft&&media[item.id]?.length>0&&<small>{media[item.id].length} media attachment{media[item.id].length===1?'':'s'}</small>}
        <dl className="fe-piece-facts">{item.grade&&<div><dt>Grade</dt><dd>{item.grade}</dd></div>}{(item.claimRecords.length>0||item.claims)&&<div><dt>Facts it relies on</dt><dd>{item.claimRecords.length?item.claimRecords.map((claim,index)=><div key={index}>{claim.text} {claim.sourceKey?<button type="button" className="fe-link" onClick={()=>onOpen(claim.sourceKey!)}>Source →</button>:claim.url&&/^https?:\/\//i.test(claim.url)?<a href={claim.url} target="_blank" rel="noopener noreferrer">Source ↗</a>:null}</div>):item.claims}</dd></div>}{item.blocker&&<div><dt>Stuck on</dt><dd>{item.blocker}</dd></div>}</dl>
        <div className="fe-actions"><button type="button" className={item.status==='pending'||item.status==='approved'||item.status==='draft'?'primary':''} disabled={!item.available} onClick={()=>{setSelected(item.key);setMessage('');setNote('');}}>{item.status==='approved'?'Post or schedule':item.status==='pending'||item.status==='draft'?'Review':'Open'}</button><button type="button" className="fe-ghost" disabled={!item.available} onClick={()=>onOpen(item.key)}>Open work →</button></div>
      </article>)}</div></div>)}</section>)}
    </section>
    {error&&<p className="fe-alert" role="status">{error}</p>}
    {piece&&<section className="fe-package-piece-review" aria-label="Piece review"><div className="fe-card-head"><h2>{piece.title}</h2><button type="button" className="fe-ghost" onClick={()=>setSelected(null)}>Close review</button></div>
      {piece.draft?<><DraftCard draft={piece.draft} canDecide={owner} onRefresh={onRefresh} onAsk={onChat} onOpen={onOpen} uploads={library.uploads} reviewOnly/><PolishedDraftComparison draft={piece.draft} state={state} level={3}/></>
        :piece.proposal?<PageProposalView id={piece.id} owner={owner}/>:piece.task?<TaskDetail level={3} task={piece.task} state={state} canWrite={owner} canChat={false} onRefresh={onRefresh} onOpen={onOpen}/>:piece.measured?<><p><strong>Hypothesis:</strong> {piece.measured.experiment.hypothesis}</p><p><strong>Decision rule:</strong> {piece.measured.experiment.metric}, {piece.measured.experiment.rule.direction} {piece.measured.experiment.rule.thresholdPercent}% by {piece.measured.experiment.reviewDate}.</p><p><strong>Result:</strong> {piece.measured.experiment.outcome||'Not decided'}{piece.measured.measurement.changePercent===null?' · No measured change yet':` · ${piece.measured.measurement.changePercent.toFixed(1)}% measured change`}</p>{piece.measured.experiment.outcomeNote&&<p>{piece.measured.experiment.outcomeNote}</p>}<button type="button" className="fe-link" onClick={()=>onOpen('section:scorecard')}>Open measurement and decision controls →</button></>:<><div className="fe-prose"><Markdown components={{img:()=>null}}>{piece.text}</Markdown></div>{piece.doc&&owner&&piece.doc.status!=='archived'&&<div className="fe-form"><label>What should change? <textarea value={note} maxLength={1000} rows={2} onChange={event=>setNote(event.target.value)}/></label><div className="fe-actions"><button type="button" disabled={busy||note.trim().length<3} onClick={()=>void docDecision(piece.doc!,true)}>Send back</button>{piece.doc.status==='draft'&&<button type="button" className="primary" disabled={busy} onClick={()=>void docDecision(piece.doc!,false)}>Approve document</button>}</div><small>Approving marks it ready and lets the employee build on it; nothing is published.</small></div>}{piece.file&&<button type="button" onClick={()=>onOpen(piece.key)}>Open media preview →</button>}</>}
      {message&&<p className="fe-notice" role="status">{message}</p>}
    </section>}
  </div>;
}
