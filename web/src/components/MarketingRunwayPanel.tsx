import {useEffect,useRef,useState} from 'react';
import {api} from '../api';
import {requestId,readableTime,type MarketingProfile,type RunwayArtifact,type RunwaySnapshot} from './MarketingPanels';
import {CampaignBriefPanel} from './CampaignBriefPanel';
import '../runway.css';

const labels:Record<string,string>={audience_note:'Audience and problem note',post_angles:'Three draft post angles',review_packet:'Owner review packet',revision_angles:'Revised post angles'};
type SharedConversation={available:boolean;sessionKey?:string;sessionId?:string;collaboratorDevice?:string|null;collaboratorApprovedAt?:string|null;suggestions:{requestId:string;actorName:string;content:string;status:string;suggestionId?:string|null;error?:string|null;createdAt:string}[]};
type PairedDevice={id:string;name:string;owner:boolean;expires:string};
type ArchivedProject={id:string;goal:string;status:string;created_at:number;updated_at:number;artifact_count:number};

function SourceReference({url,label}:{url:string;label:string}){
  return url.startsWith('fixture://')?<small>SIMULATED source · {url}</small>:<a href={url} target="_blank" rel="noopener noreferrer">{label}</a>;
}

function proposedGoal(profile?:MarketingProfile){
  const offer=profile?.product_summary.trim()||'the personal brand selling configurable marketing agents';
  const audience=profile?.audience.trim()||'one provisional founder audience, clearly labeled as an assumption';
  const objective=profile?.goals.trim()||'learn which message is worth testing next';
  return `Prepare an internal campaign review packet for ${offer}. Consider ${audience}. The immediate goal is to ${objective}. Save a source-backed audience/problem note, three evidence-linked draft post angles, and a review packet identifying unsupported claims and the next owner decision.`;
}

function ArtifactBody({artifact}:{artifact:RunwayArtifact}){
  try{
    const data=JSON.parse(artifact.content) as Record<string,unknown>;
    if(artifact.kind==='audience_note'&&Array.isArray(data.evidence)){
      const evidence=data.evidence as {sourceUrl:string;quote:string;inference:string}[];
      return <div className="runway-artifact-body"><p><b>Audience hypothesis:</b> {String(data.audience||'')}</p><p><b>Problem:</b> {String(data.problem||'')}</p><h4>Checked evidence</h4>{evidence.map(item=><blockquote key={item.sourceUrl}><p>“{item.quote}”</p><SourceReference url={item.sourceUrl} label="Read source"/><small>{item.inference}</small></blockquote>)}<p><b>Limit:</b> {String(data.limitations||'')}</p></div>;
    }
    if((artifact.kind==='post_angles'||artifact.kind==='revision_angles')&&Array.isArray(data.angles)){
      const angles=data.angles as {title:string;hook:string;sourceUrl:string;why:string;claimLimit:string}[];
      return <div className="runway-artifact-body runway-angle-list">{typeof data.revisionOf==='string'&&<p>Simulated revision of asset {data.revisionOf.slice(0,12)}… · exact owner review pending</p>}{angles.map((angle,index)=><article key={index}><h4>{index+1}. {angle.title}</h4><p>{angle.hook}</p><small><b>Why:</b> {angle.why}</small><small><b>Claim limit:</b> {angle.claimLimit}</small><SourceReference url={angle.sourceUrl} label="Read supporting source"/></article>)}{data.qa!=null&&<p><b>Deterministic QA:</b> three saved source references checked. Claim truth and audience fit require owner review.</p>}{typeof data.qualitativeReview==='string'&&<p><b>Qualitative review:</b> {data.qualitativeReview}</p>}</div>;
    }
    if(artifact.kind==='review_packet'&&Array.isArray(data.unsupportedClaims)){
      const proposal=data.nextStepProposal as Record<string,unknown>|undefined;
      return <div className="runway-artifact-body"><p>{String(data.summary||'')}</p><h4>Claims to hold back</h4><ul>{data.unsupportedClaims.map((claim,index)=><li key={index}>{String(claim)}</li>)}</ul><p><b>Recommendation:</b> {String(data.recommendation||'')}</p><p><b>Your next decision:</b> {String(data.nextOwnerDecision||'')}</p>{proposal&&<div className="runway-proposal"><h4>Proposed next step · pending your grant</h4><p><b>Hypothesis:</b> {String(proposal.hypothesis||'')}</p><p><b>Evidence gap:</b> {String(proposal.evidenceGap||'')}</p><p><b>Audience:</b> {String(proposal.intendedAudience||'')}</p><p><b>Estimated work:</b> {String(proposal.estimatedWork||'')}</p><p><b>{String(proposal.continueOrStop||'')} because:</b> {String(proposal.reason||'')}</p></div>}</div>;
    }
  }catch{/* Earlier artifacts remain readable as source text. */}
  return <pre>{artifact.content}</pre>;
}

function savedReviewSummary(artifact?:RunwayArtifact){
  if(!artifact||artifact.kind!=='review_packet')return null;
  try{
    const packet=JSON.parse(artifact.content) as Record<string,unknown>;
    const recommendation=typeof packet.recommendation==='string'?packet.recommendation.trim():'';
    const decision=typeof packet.nextOwnerDecision==='string'?packet.nextOwnerDecision.trim():'';
    return recommendation||decision?{recommendation,decision}:null;
  }catch{return null;}
}

function RevisionGrantReceipts({runway}:{runway?:RunwaySnapshot|null}){
  if(!runway?.revision_grants?.length)return null;
  return <details className="runway-executions"><summary>Linked revision grants</summary><ul>{runway.revision_grants.map(grant=><li key={grant.id}>
    <b>{grant.status.replaceAll('_',' ')}</b> · {grant.scope.replaceAll('_',' ')} · expires {readableTime(grant.deadline_at)}<br/>
    Source artifact {grant.source_artifact_id} · exact version {grant.source_artifact_digest.slice(0,12)}…<br/>
    {grant.budget_mode==='fresh_pilot'?'New pilot budget':'Existing pilot budget'}{grant.released_runway_id?' · linked assignment '+grant.released_runway_id.slice(0,12)+'…':''}<br/>
    Limits: {grant.max_runs} run, {grant.max_model_requests} model requests, {grant.token_limit.toLocaleString()} tokens, {Math.round(grant.max_active_seconds/60)} active minutes.
    {grant.status==='held_for_metering'&&<small> Saved authority only. No task or model request has been released.{grant.deadline_at<=Date.now()/1000?' This grant has expired; it cannot be released.':''}</small>}
  </li>)}</ul></details>;
}

function missingProviderRequestReceipts(runway?:RunwaySnapshot|null){
  if(runway?.campaign?.mode==='fixture')return false;
  return runway?.executions.some(item=>item.status!=='rejected'&&!runway.model_requests?.some(request=>request.execution_id===item.id))||false;
}

function ModelRequestReceipts({runway}:{runway:RunwaySnapshot}){
  if(runway.campaign?.mode==='fixture')return <p className="runway-reason">Fixture steps used zero model requests. Their saved usage is synthetic.</p>;
  const missing=missingProviderRequestReceipts(runway);
  if(!runway.model_requests?.length)return missing?<p className="runway-reason">Underlying provider request counts were not recorded for one or more executions.</p>:null;
  return <>{missing&&<p className="runway-reason">Some earlier executions lack provider request receipts. The recorded count is incomplete.</p>}<details className="runway-executions"><summary>Underlying model requests · {runway.model_requests.length}</summary><ul>{runway.model_requests.map(item=><li key={item.request_id}>{readableTime(item.created_at)} · {item.status} · {item.reported_tokens==null?`${item.reserved_tokens.toLocaleString()} tokens reserved`:item.reported_tokens.toLocaleString()+' tokens reported'} · request {item.request_id.slice(0,12)}…</li>)}</ul></details></>;
}

export function MarketingRunwayPanel({runway,profile,canControl,canContribute,liveWorkEnabled,archiveEnabled,campaignBriefEnabled,fixtureCampaignEnabled,deferredRevisionEnabled,nativeSharedEnabled,onRefresh}:{runway?:RunwaySnapshot|null;profile?:MarketingProfile;canControl:boolean;canContribute:boolean;liveWorkEnabled:boolean;archiveEnabled:boolean;campaignBriefEnabled:boolean;fixtureCampaignEnabled:boolean;deferredRevisionEnabled:boolean;nativeSharedEnabled:boolean;onRefresh:()=>Promise<void>}){
  const [goalEdit,setGoalEdit]=useState<string|null>(null),[creating,setCreating]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const [note,setNote]=useState(''),[revision,setRevision]=useState<{artifactId:string;instruction:string}|null>(null);
  const [editingBrief,setEditingBrief]=useState(false),[briefFields,setBriefFields]=useState({product_summary:'',audience:'',goals:''});
  const [shared,setShared]=useState<SharedConversation|null>(null),[sharedBusy,setSharedBusy]=useState(false),[sharedError,setSharedError]=useState('');
  const [devices,setDevices]=useState<PairedDevice[]>([]),[selectedDevice,setSelectedDevice]=useState('');
  const [archive,setArchive]=useState<ArchivedProject[]>([]),[archived,setArchived]=useState<RunwaySnapshot|null>(null);
  const [archiveBusy,setArchiveBusy]=useState(false),[archiveError,setArchiveError]=useState('');
  const nativeLocalAddress=['localhost','127.0.0.1','::1','[::1]'].includes(window.location.hostname);
  const nativeSuggestionReady=shared?.available&&!nativeLocalAddress;
  const startAttempt=useRef<{signature:string;id:string}|null>(null),noteAttempt=useRef<{signature:string;id:string}|null>(null);
  const seedAttempt=useRef<string|null>(null);
  const reviewAttempt=useRef<{signature:string;id:string}|null>(null),briefAttempt=useRef<{signature:string;id:string}|null>(null);
  const adoptAttempt=useRef<{signature:string;id:string}|null>(null);
  const project=runway?.project;
  const goal=goalEdit??proposedGoal(profile);
  const current=runway?.steps.find(step=>step.status==='running');
  const next=runway?.steps.find(step=>step.status==='ready');
  const latest=runway?.executions.at(-1);
  const providerRequestCountKnown=!missingProviderRequestReceipts(runway);
  const providerRequestUsage=providerRequestCountKnown?`${runway?.model_requests?.length||0}/${project?.max_model_requests??'?'}`:'unknown';
  const savedReview=runway?.artifacts.find(artifact=>artifact.kind==='review_packet');
  const reviewSummary=savedReviewSummary(savedReview);
  const lastReview=runway?.reviews?.at(-1);
  const pendingReview=project?.status==='needs_review'?runway?.artifacts.find(artifact=>
    ['post_angles','revision_angles'].includes(artifact.kind)&&!runway.reviews.some(review=>review.artifact_id===artifact.id)):undefined;
  const approvedRevision=project?.source_runway_id&&project.status==='done'?
    runway?.artifacts.find(artifact=>artifact.kind==='revision_angles'&&runway.reviews.some(review=>
      review.artifact_id===artifact.id&&review.artifact_digest===artifact.digest&&review.decision==='approved')):undefined;
  const revisionSelected=archived?.project.id===project?.source_runway_id&&
    archived?.campaign?.asset_artifact_id===approvedRevision?.id&&
    archived?.campaign_actions?.some(item=>{
      if(item.action!=='adopt_revision'||!item.owner_verified)return false;
      try{const detail=JSON.parse(item.payload_json) as Record<string,unknown>;
        return detail.revision_artifact_id===approvedRevision?.id&&
          detail.brief_revision===archived.campaign_revisions?.at(-1)?.version;
      }catch{return false;}
    });
  const deferredRevision=project?.status==='needs_review'&&lastReview?.decision==='revision_requested'&&!lastReview.step_id;
  const nextCheck=deferredRevision?'When a metered, linked revision grant is available':project?.next_due?readableTime(project.next_due):project?.status==='needs_review'?'When you review the packet':project?.status==='paused'?'When you resume':project?.status==='unknown'?'After the original execution is reconciled':project?.status==='budget_exhausted'?'After a new bounded assignment':'After this step settles or relevant input arrives';
  const canStart=canControl&&liveWorkEnabled&&(!project||['needs_review','done','budget_exhausted'].includes(project.status));

  useEffect(()=>{
    if(!project||!nativeSharedEnabled){setShared(null);return;}
    let current=true;
    api<SharedConversation>(`/marketing/runway/${project.id}/shared`).then(value=>{if(current){setShared(value);setSharedError('');}})
      .catch(cause=>{if(current)setSharedError((cause as Error).message);});
    return ()=>{current=false;};
  },[project?.id,project?.version,nativeSharedEnabled]);

  useEffect(()=>{
    if(!nativeSharedEnabled||!canControl)return;
    let current=true;
    api<{devices:PairedDevice[]}>('/devices').then(value=>{if(current)setDevices(value.devices.filter(device=>!device.owner));})
      .catch(()=>{if(current)setDevices([]);});
    return ()=>{current=false;};
  },[nativeSharedEnabled,canControl,project?.id]);

  useEffect(()=>{
    if(!canControl||!archiveEnabled){setArchive([]);setArchived(null);return;}
    let current=true;
    api<{projects:ArchivedProject[]}>('/marketing/runways').then(value=>{if(current){setArchive(value.projects);setArchiveError('');}})
      .catch(cause=>{if(current)setArchiveError((cause as Error).message);});
    return ()=>{current=false;};
  },[canControl,archiveEnabled,project?.id,project?.version]);

  async function openArchive(id:string,force=false){
    if(archiveBusy)return;
    if(archived?.project.id===id){if(!force)setArchived(null);return;}
    setArchiveBusy(true);setArchiveError('');
    try{setArchived(await api<RunwaySnapshot>(`/marketing/runways/${id}`));if(force)requestAnimationFrame(()=>document.getElementById('previous-marketing-assignments')?.scrollIntoView({behavior:'smooth',block:'start'}));}
    catch(cause){setArchiveError((cause as Error).message);}finally{setArchiveBusy(false);}
  }

  async function startShared(){
    if(!project||!canControl||sharedBusy)return;
    setSharedBusy(true);setSharedError('');
    try{await api(`/marketing/runway/${project.id}/shared`,{});setShared(await api<SharedConversation>(`/marketing/runway/${project.id}/shared`));}
    catch(cause){setSharedError((cause as Error).message);}finally{setSharedBusy(false);}
  }

  async function addSharedSuggestion(){
    if(!project||!shared?.available||!canContribute||sharedBusy||!note.trim())return;
    const signature=project.id+':native:'+note.trim();
    const id=noteAttempt.current?.signature===signature?noteAttempt.current.id:requestId();noteAttempt.current={signature,id};
    setSharedBusy(true);setSharedError('');
    try{
      await api(`/marketing/runway/${project.id}/shared/suggestions`,{requestId:id,version:project.version,content:note.trim()});
      noteAttempt.current=null;setNote('');await onRefresh();
      setShared(await api<SharedConversation>(`/marketing/runway/${project.id}/shared`));
    }catch(cause){setSharedError((cause as Error).message);setShared(await api<SharedConversation>(`/marketing/runway/${project.id}/shared`).catch(()=>shared));}
    finally{setSharedBusy(false);}
  }

  async function approveCollaborator(){
    if(!project||!shared?.available||!canControl||!selectedDevice||sharedBusy)return;
    setSharedBusy(true);setSharedError('');
    try{
      await api(`/marketing/runway/${project.id}/shared/collaborator`,{deviceId:selectedDevice});
      setShared(await api<SharedConversation>(`/marketing/runway/${project.id}/shared`));
    }catch(cause){setSharedError((cause as Error).message);}finally{setSharedBusy(false);}
  }

  async function reconcileShared(requestId:string){
    if(!project||!canControl||sharedBusy)return;
    setSharedBusy(true);setSharedError('');
    try{
      await api(`/marketing/runway/${project.id}/shared/reconcile`,{requestId});
      await onRefresh();
      setShared(await api<SharedConversation>(`/marketing/runway/${project.id}/shared`));
    }catch(cause){setSharedError((cause as Error).message);}
    finally{setSharedBusy(false);}
  }

  async function saveBrief(event:React.FormEvent){
    event.preventDefault();if(!profile||!canControl||busy||!briefFields.product_summary.trim()||!briefFields.goals.trim())return;
    const change={...briefFields,version:profile.version},signature=JSON.stringify(change);
    const id=briefAttempt.current?.signature===signature?briefAttempt.current.id:requestId();briefAttempt.current={signature,id};
    setBusy(true);setError('');
    try{await api('/marketing/profile',{...change,requestId:id},'PUT');briefAttempt.current=null;setEditingBrief(false);setGoalEdit(null);await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }
  async function start(){
    if(!canStart||busy||!goal.trim()||!profile?.product_summary.trim()||!profile.goals.trim())return;
    const signature=goal.trim()+':'+profile.version;
    const id=startAttempt.current?.signature===signature?startAttempt.current.id:requestId();startAttempt.current={signature,id};
    setBusy(true);setError('');
    try{await api('/marketing/runway',{requestId:id,goal:goal.trim()});startAttempt.current=null;setCreating(false);setGoalEdit(null);await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }
  async function change(action:'pause'|'resume'){
    if(!project||!canControl||busy||action==='resume'&&!liveWorkEnabled)return;
    setBusy(true);setError('');
    try{await api('/marketing/runway/'+action,{id:project.id,version:project.version});await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }
  async function addNote(){
    if(!project||!canContribute||busy||!note.trim())return;
    const signature=project.id+':'+note.trim();
    const id=noteAttempt.current?.signature===signature?noteAttempt.current.id:requestId();noteAttempt.current={signature,id};
    setBusy(true);setError('');
    try{await api(`/marketing/runway/${project.id}/input`,{requestId:id,version:project.version,content:note.trim()});noteAttempt.current=null;setNote('');await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }
  async function reviewArtifact(artifact:RunwayArtifact,decision:'approved'|'rejected'|'revision_requested',target=runway){
    const reviewProject=target?.project;
    if(!reviewProject||!canControl||busy||decision==='revision_requested'&&!(liveWorkEnabled||deferredRevisionEnabled))return;
    const instruction=decision==='revision_requested'&&revision?.artifactId===artifact.id?revision.instruction.trim():'';
    if(decision==='revision_requested'&&!instruction)return;
    const payload={artifactId:artifact.id,digest:artifact.digest,decision,instruction,version:reviewProject.version};
    const signature=reviewProject.id+':'+JSON.stringify(payload);
    const id=reviewAttempt.current?.signature===signature?reviewAttempt.current.id:requestId();reviewAttempt.current={signature,id};
    setBusy(true);setError('');
    try{
      const saved=await api<RunwaySnapshot>(`/marketing/runway/${reviewProject.id}/review`,{requestId:id,...payload});
      reviewAttempt.current=null;setRevision(null);
      if(archived?.project.id===reviewProject.id){
        setArchived(saved);
        setArchive(items=>items.map(item=>item.id===reviewProject.id?{...item,status:saved.project.status,updated_at:Date.now()/1000}:item));
      }
      await onRefresh();
    }
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }

  async function seedFixture(){
    if(!fixtureCampaignEnabled||!canControl||busy)return;
    setBusy(true);setError('');
    try{seedAttempt.current??=requestId();await api('/marketing/runway/fixture/seed',{requestId:seedAttempt.current});seedAttempt.current=null;await onRefresh();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }

  async function adoptRevision(){
    if(!project?.source_runway_id||!approvedRevision||!runway||!canControl||busy)return;
    setBusy(true);setError('');
    try{
      const source=await api<RunwaySnapshot>(`/marketing/runways/${project.source_runway_id}`);
      if(!source.campaign?.owner_verified||source.campaign.mode!=='internal')throw new Error('Open the source assignment and save its internal campaign brief first.');
      if(source.campaign.asset_artifact_id===approvedRevision.id&&source.campaign_actions?.some(item=>{
        if(item.action!=='adopt_revision'||!item.owner_verified)return false;
        try{const detail=JSON.parse(item.payload_json) as Record<string,unknown>;
          return detail.revision_artifact_id===approvedRevision.id&&
            detail.brief_revision===source.campaign_revisions?.at(-1)?.version;
        }catch{return false;}
      })){setArchived(source);return;}
      if((source.campaign.asset_artifact_id!==project.source_artifact_id||
        source.campaign.asset_artifact_digest!==project.source_artifact_digest)&&
        (source.campaign.asset_artifact_id!==approvedRevision.id||
        source.campaign.asset_artifact_digest!==approvedRevision.digest))
        throw new Error('The source campaign selected asset changed; review its current version.');
      const approval=runway.reviews.find(review=>review.artifact_id===approvedRevision.id&&
        review.artifact_digest===approvedRevision.digest&&review.decision==='approved');
      if(!approval)throw new Error('The exact revised asset has no saved owner approval.');
      const payload={projectVersion:source.project.version,version:source.campaign.version,
        revisionRunwayId:project.id,revisionProjectVersion:project.version,
        revisionArtifactId:approvedRevision.id,revisionArtifactDigest:approvedRevision.digest,
        revisionReviewId:approval.id};
      const signature=source.project.id+JSON.stringify(payload);
      const id=adoptAttempt.current?.signature===signature?adoptAttempt.current.id:requestId();
      adoptAttempt.current={signature,id};
      const saved=await api<RunwaySnapshot>(`/marketing/runway/${source.project.id}/campaign-adopt-revision`,{requestId:id,...payload});
      adoptAttempt.current=null;setArchived(saved);await onRefresh();
      requestAnimationFrame(()=>document.getElementById('previous-marketing-assignments')?.scrollIntoView({behavior:'smooth',block:'start'}));
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }

  return <section className="marketing-runway" aria-label="Standing marketing assignment">
    <header><div><p className="eyebrow">YOUR FIRST EMPLOYEE</p><h2>Marketing project</h2></div>{project&&<span className={'runway-status '+project.status}>{project.status.replaceAll('_',' ')}</span>}</header>
    {fixtureCampaignEnabled?<p className="runway-reason" role="status">Isolated fixture ledger · no model calls, live publication, or real campaign data. All campaign launch receipts are simulated.</p>:!liveWorkEnabled&&<p className="runway-reason" role="status">New autonomous project work is disabled in this local host. Saved results, project notes, and owner decisions remain available. An unresolved execution stays held for reconciliation; a revision request records your instruction but starts no model work. Direct Chat is owner initiated and is outside this project budget.</p>}
    {fixtureCampaignEnabled&&!project&&canControl&&<button type="button" className="primary" disabled={busy} onClick={()=>void seedFixture()}>{busy?'Preparing…':'Create simulated campaign'}</button>}
    <div className="runway-brief" aria-label="Business brief"><div className="runway-subheading"><div><strong>1. Your business brief</strong><small>What your business sells is separate from the employee that markets it.</small></div>{canControl&&!editingBrief&&<button type="button" onClick={()=>{setBriefFields({product_summary:profile?.product_summary||'',audience:profile?.audience||'',goals:profile?.goals||''});setEditingBrief(true);}}>Edit</button>}</div>
      {editingBrief?<form onSubmit={event=>void saveBrief(event)}><label>What does your business sell?<textarea required maxLength={1200} value={briefFields.product_summary} onChange={event=>setBriefFields(current=>({...current,product_summary:event.target.value}))}/></label><label>Who is it for? <small>Leave open if still testing an audience.</small><textarea maxLength={800} value={briefFields.audience} onChange={event=>setBriefFields(current=>({...current,audience:event.target.value}))}/></label><label>What is the immediate goal?<textarea required maxLength={800} value={briefFields.goals} onChange={event=>setBriefFields(current=>({...current,goals:event.target.value}))}/></label><div className="runway-actions"><button type="button" disabled={busy} onClick={()=>setEditingBrief(false)}>Cancel</button><button className="primary" disabled={busy||!briefFields.product_summary.trim()||!briefFields.goals.trim()}>{busy?'Saving…':'Save business brief'}</button></div><small>Saved as brief version {profile?.version||'?'}. A material change stops further work under an older grant.</small></form>:<div className="runway-brief-summary"><p><b>Offer:</b> {profile?.product_summary||'Describe your offer to begin.'}</p><p><b>Audience:</b> {profile?.audience||'Open hypothesis; the employee must label its assumption.'}</p><p><b>Immediate goal:</b> {profile?.goals||'Add an immediate goal to start work.'}</p></div>}
    </div>
    {(!project||creating)&&!fixtureCampaignEnabled&&<div className="runway-setup"><div className="runway-subheading"><div><strong>2. First assignment</strong><small>Review this exact internal grant before starting.</small></div></div><label>Desired outcome<textarea value={goal} onChange={event=>setGoalEdit(event.target.value)} maxLength={1200} rows={5}/></label><div className="runway-grant"><p><b>Deliverables:</b> one checked audience/problem note, three evidence-linked draft post angles, and an owner review packet.</p><p><b>Allowed work:</b> internal research and local drafts from two restricted public sources. No publishing, outreach, spending, or account changes.</p><p><b>Limits:</b> six admitted agent runs, 15 minutes active execution, 30-minute assignment deadline, 150,000-token admission allowance, two repairs per failed step, and 1,800 output tokens per run.</p></div>{canControl&&<div className="runway-actions"><button className="primary" disabled={!canStart||busy||!goal.trim()||!profile?.product_summary.trim()||!profile?.goals.trim()} onClick={()=>void start()}>{busy?'Starting…':'Start bounded work'}</button>{project&&<button type="button" onClick={()=>setCreating(false)}>Keep reviewing current project</button>}</div>}{(!profile?.product_summary.trim()||!profile?.goals.trim())&&<p className="runway-reason">Save your offer and immediate goal in the business brief first.</p>}</div>}
    {project&&!creating&&<div className="runway-project"><div className="runway-subheading"><div><strong>2. Saved assignment</strong><small>Record version {project.version} · internal research and drafts only</small></div>{canStart&&<button type="button" onClick={()=>setCreating(true)}>New assignment</button>}</div><p className="runway-goal">{project.goal}</p>{project.source_runway_id&&<p className="runway-reason">Linked revision of artifact {project.source_artifact_id?.slice(0,12)}… {archiveEnabled&&canControl&&<button type="button" onClick={()=>void openArchive(project.source_runway_id!,true)}>Open source assignment</button>}</p>}<div className="runway-metrics"><span><strong>{project.run_count}/{project.max_runs}</strong> runs admitted</span><span><strong>{providerRequestUsage}</strong> provider requests recorded</span><span><strong>{project.token_used.toLocaleString()}</strong> reported model tokens</span><span><strong>{Math.max(0,project.token_limit-project.token_used-project.token_reserved).toLocaleString()}</strong> unused recorded tokens</span><span><strong>{runway?.artifacts.length||0}</strong> saved results</span></div>{fixtureCampaignEnabled?<small>SIMULATED steps and allowances · zero model requests · excluded from live usage.</small>:<small>Subscription use is reported in tokens; no cash charge is established by this display.</small>}{project.status==='unknown'&&<p className="runway-reason" role="status">Actual model use is unknown while this execution is held. The reported-token total excludes unresolved requests; {project.token_reserved.toLocaleString()} tokens remain reserved. Do not treat the remaining allowance as permission to retry.</p>}{project.deadline_at==null&&<p className="runway-reason">This legacy assignment has no recorded deadline. Its unused allowance does not authorize more work; a fresh owner grant is required.</p>}
      {campaignBriefEnabled&&canControl&&approvedRevision&&<div className="runway-proposal"><strong>Approved revision {revisionSelected?'selected for the original campaign':'ready for the original campaign'}</strong><p>The exact revised asset can be selected for the source campaign after its internal brief is saved. This records an owner decision and does not authorize launch.</p>{!revisionSelected&&<button type="button" className="primary" disabled={busy} onClick={()=>void adoptRevision()}>{busy?'Checking…':'Select for source campaign'}</button>}</div>}
      <div className="runway-next"><span><b>Current action</b>{project.status==='unknown'?'Reconcile held execution':current?labels[current.kind]||current.kind:project.status==='needs_review'?'Owner review':'No active step'}</span><span><b>Next action</b>{project.status==='unknown'?'No new step until reconciliation':next?labels[next.kind]||next.kind:project.status==='needs_review'?'Your decision':'None admitted'}</span><span><b>Next check</b>{nextCheck}</span></div>{project.deadline_at&&<small>Grant deadline: {readableTime(project.deadline_at)}</small>}{latest&&<p className="runway-progress">Latest recorded run: {latest.status} at {readableTime(latest.ended_at||latest.started_at)}{latest.error?' · '+latest.error:''}</p>}{project.wait_reason&&<p className="runway-reason">{project.wait_reason}</p>}
      {canControl&&pendingReview&&<div className="runway-actions"><button type="button" onClick={()=>{const target=document.getElementById(`runway-artifact-${pendingReview.id}`) as HTMLDetailsElement|null;if(target){target.open=true;target.scrollIntoView({behavior:'smooth',block:'start'});}}}>Review draft angles</button></div>}
      {canControl&&project.status==='needs_review'&&reviewSummary&&<div className="runway-proposal"><strong>Employee recommendation</strong>{reviewSummary.recommendation&&<p>{reviewSummary.recommendation}</p>}{reviewSummary.decision&&<p><b>Your next decision:</b> {reviewSummary.decision}</p>}{savedReview&&<button type="button" onClick={()=>{const target=document.getElementById(`runway-artifact-${savedReview.id}`) as HTMLDetailsElement|null;if(target){target.open=true;target.scrollIntoView({behavior:'smooth',block:'start'});}}}>Read full review packet</button>}</div>}
      <ol className="runway-steps">{runway?.steps.map(step=><li key={step.id}><span className={'runway-dot '+step.status}/><div><strong>{labels[step.kind]||step.kind}</strong><small>{step.status.replaceAll('_',' ')} · {step.attempts} attempt{step.attempts===1?'':'s'}</small></div></li>)}</ol>{canControl&&<div className="runway-actions">{['ready','running','waiting'].includes(project.status)&&<button disabled={busy} onClick={()=>void change('pause')}>Pause new work</button>}{project.status==='paused'&&<button disabled={busy||!liveWorkEnabled} onClick={()=>void change('resume')}>Resume project</button>}</div>}
      <div className="runway-results"><h3>3. Saved work and review</h3>{runway?.artifacts.length?runway.artifacts.map(artifact=>{const decision=runway.reviews?.find(item=>item.artifact_id===artifact.id);const canReview=canControl&&project.status==='needs_review'&&!decision&&['post_angles','revision_angles'].includes(artifact.kind);return <details className="runway-artifact" id={`runway-artifact-${artifact.id}`} key={artifact.id}><summary>{labels[artifact.kind]||artifact.kind} · saved {readableTime(artifact.created_at)}</summary><ArtifactBody artifact={artifact}/><small>Artifact {artifact.id} · exact version {artifact.digest.slice(0,12)}…</small>{decision&&<p className="runway-decision"><b>{decision.actor_name}:</b> {decision.decision.replaceAll('_',' ')}{decision.instruction?' · '+decision.instruction:''}</p>}{canReview&&<div className="runway-review"><p>Review this exact draft version. Approval records a decision and does not publish it.</p><div className="runway-actions"><button type="button" disabled={busy} onClick={()=>void reviewArtifact(artifact,'rejected')}>Reject idea</button><button type="button" disabled={busy||!(liveWorkEnabled||deferredRevisionEnabled)} onClick={()=>setRevision({artifactId:artifact.id,instruction:''})}>Request revision</button><button type="button" className="primary" disabled={busy} onClick={()=>void reviewArtifact(artifact,'approved')}>Approve exact draft</button></div>{revision?.artifactId===artifact.id&&<form onSubmit={event=>{event.preventDefault();void reviewArtifact(artifact,'revision_requested');}}><label>What should change?<textarea required maxLength={1000} value={revision.instruction} onChange={event=>setRevision({artifactId:artifact.id,instruction:event.target.value})} placeholder="Point to the angle, claim, or audience assumption to revise."/></label><button disabled={busy||!revision.instruction.trim()}>{busy?'Recording…':'Save revision request'}</button></form>}</div>}</details>;}):<p>{project.status==='unknown'?'No result was saved. Reconcile the held execution before more work.':'No saved results yet. Refresh when the first step finishes.'}</p>}</div>
      {campaignBriefEnabled&&runway&&<CampaignBriefPanel runway={runway} canControl={canControl} onSaved={async()=>{await onRefresh();}}/>}
      <div className="runway-inputs"><strong>Project conversation</strong>
        {nativeSharedEnabled&&(shared?.available?<p className="runway-reason">{nativeLocalAddress?'Native conversation is connected, but this localhost view can save project notes only. Open the configured secure HTTPS address to add a verified native suggestion.':'Native shared session ready. Suggestions carry a verified Gateway profile and remain pending owner review. No model turn starts from this form.'}</p>:<p className="runway-reason">Native shared conversation is not connected to this project yet. {nativeLocalAddress?'Set up secure HTTPS access for this cockpit, then open that address. Localhost cannot supply the client address OpenClaw needs.':'Existing project notes remain available.'}</p>)}
        {nativeSharedEnabled&&!shared?.available&&canControl&&<button type="button" disabled={sharedBusy||nativeLocalAddress} onClick={()=>void startShared()}>{sharedBusy?'Connecting…':'Connect native conversation'}</button>}
        {nativeSharedEnabled&&shared?.available&&canControl&&<div className="runway-actions"><label>Approved collaborator device<select value={selectedDevice||shared.collaboratorDevice||''} onChange={event=>setSelectedDevice(event.target.value)}><option value="">Choose a paired device</option>{devices.map(device=><option key={device.id} value={device.id}>{device.name} · {device.id.slice(0,8)}</option>)}</select></label><button type="button" disabled={sharedBusy||!selectedDevice} onClick={()=>void approveCollaborator()}>Allow suggestions</button>{shared.collaboratorApprovedAt&&<small>Approved {new Date(shared.collaboratorApprovedAt).toLocaleString()}</small>}{devices.length===0&&<small>Pair a collaborator in Settings → Access before enabling shared suggestions.</small>}</div>}
        {shared?.suggestions.filter(item=>item.status!=='recorded').map(item=><p key={item.requestId}><b>{item.actorName}</b> · {readableTime(item.createdAt)} · {item.status.replaceAll('_',' ')}<br/>{item.content}{item.error&&<small> · {item.error}</small>}{canControl&&['gateway_recorded','ledger_conflict'].includes(item.status)&&<button type="button" disabled={sharedBusy} onClick={()=>void reconcileShared(item.requestId)}>Reconcile saved receipt</button>}</p>)}
        {runway?.inputs.map(item=><p key={item.id}><b>{item.actor_name}</b> · {readableTime(item.created_at)}{item.source_input_id&&<small> · linked source input {item.source_input_id.slice(0,12)}…</small>}<br/>{item.content}</p>)}
        {canContribute&&<form onSubmit={event=>{event.preventDefault();void (nativeSuggestionReady?addSharedSuggestion():addNote());}}><label>Constraint or context for the next eligible step<textarea value={note} maxLength={1000} rows={2} onChange={event=>setNote(event.target.value)} placeholder="Add a specific source limit or customer concern"/></label><button disabled={busy||sharedBusy||!note.trim()}>{nativeSuggestionReady?'Suggest in native conversation':'Add to project'}</button><small>{nativeSuggestionReady?'A verified suggestion is copied into the project ledger; it cannot approve spending or reopen finished work.':'This note is attributed to your signed-in session. It cannot approve spending or reopen finished work; use Request revision for a new step.'}</small></form>}
        {nativeSharedEnabled&&sharedError&&<p className="company-error" role="alert">{sharedError}</p>}
      </div><RevisionGrantReceipts runway={runway}/>{runway?.executions.length?<details className="runway-executions"><summary>Execution and usage receipts</summary><ul>{runway.executions.map(item=><li key={item.id}>{readableTime(item.started_at)} · {item.status} · {item.reported_tokens==null?`usage unavailable; ${item.reserved_tokens.toLocaleString()} reserved`:item.reported_tokens.toLocaleString()+' reported tokens'}{item.error?' · '+item.error:''}</li>)}</ul></details>:null}<ModelRequestReceipts runway={runway}/>
    </div>}
    {archiveEnabled&&canControl&&archive.some(item=>item.id!==project?.id)&&<div id="previous-marketing-assignments" className="runway-results" aria-label="Previous marketing assignments">
      <h3>Previous assignments</h3><p>Saved work stays available after a new assignment becomes current. You can still decide on an exact draft that is awaiting review; a decision never publishes it or starts model work.</p>
      {archive.filter(item=>item.id!==project?.id).map(item=><div className="runway-artifact" key={item.id}>
        <button type="button" disabled={archiveBusy} onClick={()=>void openArchive(item.id)}>{readableTime(item.created_at)} · {item.status.replaceAll('_',' ')} · {item.artifact_count} saved result{item.artifact_count===1?'':'s'}</button><p>{item.goal}</p>
      </div>)}
      {archived&&<div className="runway-project"><h4>Saved assignment · {readableTime(archived.project.created_at)}</h4><p className="runway-goal">{archived.project.goal}</p>
        {campaignBriefEnabled&&<CampaignBriefPanel runway={archived} canControl={canControl} onSaved={async saved=>{setArchived(saved);await onRefresh();}}/>}
        {archived.artifacts.map(artifact=>{const decision=archived.reviews?.find(item=>item.artifact_id===artifact.id);const canReview=archived.project.status==='needs_review'&&!decision&&['post_angles','revision_angles'].includes(artifact.kind);return <details className="runway-artifact" key={artifact.id}><summary>{labels[artifact.kind]||artifact.kind} · saved {readableTime(artifact.created_at)}</summary><ArtifactBody artifact={artifact}/><small>Artifact {artifact.id} · exact version {artifact.digest.slice(0,12)}…</small>{decision&&<p className="runway-decision"><b>{decision.actor_name}:</b> {decision.decision.replaceAll('_',' ')}{decision.instruction?' · '+decision.instruction:''}</p>}{canReview&&<div className="runway-review"><p>Review this exact saved draft. Approval records an internal decision and does not publish it.</p><div className="runway-actions"><button type="button" disabled={busy} onClick={()=>void reviewArtifact(artifact,'rejected',archived)}>Reject idea</button><button type="button" disabled={busy||!(liveWorkEnabled||deferredRevisionEnabled)} onClick={()=>setRevision({artifactId:artifact.id,instruction:''})}>Request revision</button><button type="button" className="primary" disabled={busy} onClick={()=>void reviewArtifact(artifact,'approved',archived)}>Approve exact draft</button></div>{revision?.artifactId===artifact.id&&<form onSubmit={event=>{event.preventDefault();void reviewArtifact(artifact,'revision_requested',archived);}}><label>What should change?<textarea required maxLength={1000} value={revision.instruction} onChange={event=>setRevision({artifactId:artifact.id,instruction:event.target.value})} placeholder="Point to the angle, claim, or audience assumption to revise."/></label><button disabled={busy||!revision.instruction.trim()}>{busy?'Recording…':'Save revision request'}</button></form>}</div>}</details>;})}
        <RevisionGrantReceipts runway={archived}/>
        {archived.inputs.length>0&&<details className="runway-executions"><summary>Project notes</summary>{archived.inputs.map(item=><p key={item.id}><b>{item.actor_name}</b> · {readableTime(item.created_at)}<br/>{item.content}</p>)}</details>}
        {archived.executions.length>0&&<details className="runway-executions"><summary>Execution and usage receipts</summary><ul>{archived.executions.map(item=><li key={item.id}>{readableTime(item.started_at)} · {item.status} · {item.reported_tokens==null?`usage unavailable; ${item.reserved_tokens.toLocaleString()} reserved`:item.reported_tokens.toLocaleString()+' reported tokens'}{item.error?' · '+item.error:''}</li>)}</ul></details>}<ModelRequestReceipts runway={archived}/>
      </div>}
    </div>}
    {archiveError&&<p className="company-error" role="alert">Past assignments: {archiveError}</p>}
    {error&&<p className="company-error" role="alert">{error}</p>}
  </section>;
}
