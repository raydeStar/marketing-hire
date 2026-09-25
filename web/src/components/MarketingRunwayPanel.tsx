import {useEffect,useRef,useState} from 'react';
import {api} from '../api';
import {requestId,readableTime,type MarketingProfile,type RunwayArtifact,type RunwaySnapshot} from './MarketingPanels';
import {CampaignBriefPanel,nextCampaignAction} from './CampaignBriefPanel';
import type {SharedCampaign} from './CampaignSharedWorkspace';
import {CampaignInvitations} from './CampaignInvitations';
import {RevisionRunGrant} from './RevisionRunGrant';
import {MarketingSourcePicker} from './MarketingSourcePicker';
import {WorkerResponseRecords} from './WorkerResponseRecords';
import '../runway.css';
import '../campaign-review.css';

const labels:Record<string,string>={audience_note:'Audience and problem note',post_angles:'Three draft post angles',review_packet:'Owner review packet',revision_angles:'Revised post angles'};
function atUsageCheckpoint(runway?:RunwaySnapshot|null){
  return runway?.project.accounting_mode==='post_response'&&runway.project.request_allowance===1&&(runway.project.max_model_requests??0)>1&&
    runway.project.status==='needs_review'&&!runway.project.active_execution&&
    runway.model_requests?.length===1&&runway.model_requests[0].status==='reported';
}
type SharedConversation={available:boolean;sessionKey?:string;sessionId?:string;collaboratorDevice?:string|null;collaboratorApprovedAt?:string|null;suggestions:{requestId:string;actorName:string;content:string;status:string;suggestionId?:string|null;error?:string|null;createdAt:string}[]};
type PairedDevice={id:string;name:string;owner:boolean;expires:string;accountId?:string|null};
type ArchivedProject={id:string;goal:string;status:string;created_at:number;updated_at:number;artifact_count:number};

export function campaignTitle(goal:string){
  const first=goal.split(/(?<=[.!?])\s/)[0].replace(/^(Prepare|Create|Build|Draft)\s+(an?|the)\s+/i,'').replace(/[.!?]$/,'');
  const text=first.charAt(0).toUpperCase()+first.slice(1);
  return text.length>90?text.slice(0,87).trimEnd()+'…':text;
}

function SourceReference({url,label}:{url:string;label:string}){
  return url.startsWith('fixture://')?<small>SIMULATED source · {url}</small>:<a href={url} target="_blank" rel="noopener noreferrer">{label}</a>;
}

function proposedGoal(profile?:MarketingProfile){
  const offer=profile?.product_summary.trim()||'the personal brand selling configurable marketing agents';
  const audience=profile?.audience.trim()||'one provisional founder audience, clearly labeled as an assumption';
  const objective=profile?.goals.trim()||'learn which message is worth testing next';
  return `Prepare an internal campaign review packet for ${offer}. Consider ${audience}. The immediate goal is to ${objective}. Save a source-backed audience/problem note, three evidence-linked draft post angles, and a review packet identifying unsupported claims and the next owner decision.`;
}

export function ArtifactBody({artifact}:{artifact:RunwayArtifact}){
  try{
    const data=JSON.parse(artifact.content) as Record<string,unknown>;
    if(artifact.kind==='audience_note'&&Array.isArray(data.evidence)){
      const evidence=data.evidence as {sourceUrl:string;quote:string;inference:string}[];
      return <div className="runway-artifact-body"><p><b>Audience hypothesis:</b> {String(data.audience||'')}</p><p><b>Problem:</b> {String(data.problem||'')}</p><h4>Checked evidence</h4>{evidence.map(item=><blockquote key={item.sourceUrl}><p>“{item.quote}”</p><SourceReference url={item.sourceUrl} label="Read source"/><small>{item.inference}</small></blockquote>)}<p><b>Limit:</b> {String(data.limitations||'')}</p></div>;
    }
    if((artifact.kind==='post_angles'||artifact.kind==='revision_angles')&&Array.isArray(data.angles)){
      const angles=data.angles as {title:string;hook:string;sourceUrl:string;why:string;claimLimit:string}[];
      return <div className="runway-artifact-body runway-angle-list">{typeof data.revisionOf==='string'&&<p>{data.fixtureOnly===true?'Simulated revision':'Revision'} of asset {data.revisionOf.slice(0,12)}…</p>}{angles.map((angle,index)=><article key={index}><h4>{index+1}. {angle.title}</h4><p>{angle.hook}</p><small><b>Why:</b> {angle.why}</small><small><b>Claim limit:</b> {angle.claimLimit}</small><SourceReference url={angle.sourceUrl} label="Read supporting source"/></article>)}{data.qa!=null&&<p><b>Deterministic QA:</b> three saved source references checked. Claim truth and audience fit remain owner judgments.</p>}{typeof data.qualitativeReview==='string'&&<p><b>Assessment at creation:</b> {data.qualitativeReview}</p>}</div>;
    }
    if(artifact.kind==='review_packet'&&Array.isArray(data.unsupportedClaims)){
      const proposal=data.nextStepProposal as Record<string,unknown>|undefined;
      const qualitative=data.qualitativeReview&&typeof data.qualitativeReview==='object'&&!Array.isArray(data.qualitativeReview)
        ?data.qualitativeReview as Record<string,unknown>:null;
      const checks=[['audienceFit','Audience fit'],['clarity','Clarity'],['productTruth','Product truth'],['channelSuitability','Channel suitability'],['desiredAction','Desired action']] as const;
      return <div className="runway-artifact-body"><p>{String(data.summary||'')}</p>
        {qualitative&&<><h4>Employee qualitative assessment · owner review pending</h4><dl>{checks.map(([key,label])=><div key={key}><dt>{label}</dt><dd>{String(qualitative[key]||'Not assessed')}</dd></div>)}</dl></>}
        <h4>Claims to hold back</h4><ul>{data.unsupportedClaims.map((claim,index)=><li key={index}>{String(claim)}</li>)}</ul>
        <p><b>Recommendation:</b> {String(data.recommendation||'')}</p><p><b>Your next decision:</b> {String(data.nextOwnerDecision||'')}</p>
        {proposal&&<div className="runway-proposal"><h4>Proposed next step · pending your grant</h4><p><b>Hypothesis:</b> {String(proposal.hypothesis||'')}</p><p><b>Evidence gap:</b> {String(proposal.evidenceGap||'')}</p><p><b>Audience:</b> {String(proposal.intendedAudience||'')}</p><p><b>Estimated work:</b> {String(proposal.estimatedWork||'')}</p><p><b>{String(proposal.continueOrStop||'')} because:</b> {String(proposal.reason||'')}</p></div>}
      </div>;
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

function artifactData(artifact?:RunwayArtifact):Record<string,unknown>{
  try{return artifact?JSON.parse(artifact.content) as Record<string,unknown>:{};}catch{return {};}
}

function ReviewDesk({selected,current,archive,archiveBusy,canControl,busy,revisionAllowed,fixtureEnabled,onOpen,onCurrent,onDecision,onSharedChange}:{
  selected?:RunwaySnapshot|null;current?:RunwaySnapshot|null;archive:ArchivedProject[];archiveBusy:boolean;
  canControl:boolean;busy:boolean;revisionAllowed:boolean;fixtureEnabled:boolean;
  onOpen:(id:string)=>void;onCurrent:()=>void;
  onDecision:(artifact:RunwayArtifact,decision:'approved'|'rejected'|'revision_requested',instruction?:string)=>void;
  onSharedChange:()=>Promise<void>;
}){
  const [section,setSection]=useState<'draft'|'brief'|'activity'>('draft');
  const [artifactId,setArtifactId]=useState('');
  const [changeOpen,setChangeOpen]=useState(false);
  const [changeText,setChangeText]=useState('');
  const [contextOpen,setContextOpen]=useState(()=>window.innerWidth>=1500);
  const [compare,setCompare]=useState(false);
  const [sharedReview,setSharedReview]=useState<SharedCampaign|null>(null);
  const [access,setAccess]=useState<{deviceId:string;name:string;active:boolean;grantedAt:string;revokedAt:string|null}[]>([]);
  const [devices,setDevices]=useState<PairedDevice[]>([]);
  const [selectedDevice,setSelectedDevice]=useState('');
  const [sharing,setSharing]=useState(false),[shareError,setShareError]=useState('');
  useEffect(()=>{setSection('draft');setArtifactId('');setChangeOpen(false);setChangeText('');setCompare(false);},[selected?.project.id]);
  useEffect(()=>{
    setSharedReview(null);setAccess([]);setDevices([]);
    if(!selected?.campaign||!canControl){setSharedReview(null);setAccess([]);return;}
    let active=true;
    const id=selected.project.id;
    const load=async()=>{
      const [reviewResult,accessResult,devicesResult]=await Promise.allSettled([
        api<SharedCampaign>(`/marketing/campaigns/${id}/review`),
        api<{members:typeof access}>(`/marketing/campaigns/${id}/access`),
        api<{devices:PairedDevice[]}>('/devices')
      ]);
      if(!active)return;
      if(reviewResult.status==='fulfilled')setSharedReview(current=>!current||current.project.id!==id||
        reviewResult.value.project.version>=current.project.version?reviewResult.value:current);
      if(accessResult.status==='fulfilled')setAccess(accessResult.value.members);
      if(devicesResult.status==='fulfilled')setDevices([...new Map(devicesResult.value.devices.filter(item=>!item.owner&&Date.parse(item.expires)>Date.now()).map(item=>[item.accountId||item.id,{...item,id:item.accountId||item.id}])).values()]);
    };
    void load();
    const timer=window.setInterval(()=>{if(document.visibilityState==='visible')void load();},8000);
    return()=>{active=false;clearInterval(timer);};
  },[selected?.project.id,Boolean(selected?.campaign),canControl]);

  async function changeAccess(deviceId:string,action:'grant'|'revoke'){
    if(!selected?.campaign||sharing)return;
    setSharing(true);setShareError('');
    try{
      await api(`/marketing/campaigns/${selected.project.id}/access`,{deviceId,action});
      const members=await api<{members:typeof access}>(`/marketing/campaigns/${selected.project.id}/access`);
      setAccess(members.members);setSelectedDevice('');await onSharedChange();
    }catch(cause){setShareError((cause as Error).message);}finally{setSharing(false);}
  }

  async function connectNative(){
    if(!selected?.campaign||sharing)return;
    setSharing(true);setShareError('');
    try{
      await api(`/marketing/campaigns/${selected.project.id}/native/start`,{});
      setSharedReview(await api<SharedCampaign>(`/marketing/campaigns/${selected.project.id}/review`));
    }catch(cause){setShareError((cause as Error).message);}
    finally{setSharing(false);}
  }

  async function authorizeRequest(requestId:string){
    if(!sharedReview||sharing)return;
    setSharing(true);setShareError('');
    try{
      await api(`/marketing/campaigns/${sharedReview.project.id}/requests/${requestId}/authorize`,
        {projectVersion:sharedReview.project.version});
      setSharedReview(await api<SharedCampaign>(`/marketing/campaigns/${sharedReview.project.id}/review`));
      await onSharedChange();
    }catch(cause){setShareError((cause as Error).message);}finally{setSharing(false);}
  }
  const project=selected?.project;
  const sourceDrafts=selected?.artifacts.filter(item=>item.kind==='post_angles'||item.kind==='revision_angles')||[];
  const campaign=selected?.campaign;
  const sharedArtifact=sharedReview&&sharedReview.project.id===selected?.project.id&&
    sharedReview.artifact.id===campaign?.asset_artifact_id&&
    !sourceDrafts.some(item=>item.id===sharedReview.artifact.id)
    ?{id:sharedReview.artifact.id,kind:sharedReview.artifact.kind,content:sharedReview.artifact.content,
      digest:sharedReview.artifact.digest,created_at:sharedReview.artifact.createdAt,source_urls:'',step_id:''}
    :null;
  const drafts=sharedArtifact?[...sourceDrafts,sharedArtifact]:sourceDrafts;
  const chosen=drafts.find(item=>item.id===artifactId)||drafts.find(item=>item.id===campaign?.asset_artifact_id)||drafts.at(-1);
  const chosenData=artifactData(chosen);
  const predecessorId=typeof chosenData.revisionOf==='string'?chosenData.revisionOf:project?.source_artifact_id;
  const predecessor=selected?.artifacts.find(item=>item.id===predecessorId);
  const packet=selected?.artifacts.find(item=>item.kind==='review_packet');
  const packetData=artifactData(packet);
  const assessments=packetData.qualitativeReview&&typeof packetData.qualitativeReview==='object'&&!Array.isArray(packetData.qualitativeReview)
    ?packetData.qualitativeReview as Record<string,unknown>:null;
  const checks=[['audienceFit','Audience fit'],['clarity','Clarity'],['productTruth','Product truth'],['channelSuitability','Channel suitability'],['desiredAction','Desired action']] as const;
  const brief=campaign?artifactData({content:campaign.brief_json} as RunwayArtifact):{};
  const experiment=campaign?artifactData({content:campaign.experiment_json} as RunwayArtifact):{};
  const ledgerDecision=chosen?selected?.reviews.find(item=>item.artifact_id===chosen.id&&item.artifact_digest===chosen.digest):undefined;
  const decision=ledgerDecision?.owner_verified?ledgerDecision:chosen&&sharedReview?.artifact.id===chosen.id&&sharedReview.artifact.digest===chosen.digest&&sharedReview.review?{
      decision:sharedReview.review.decision,actor_name:sharedReview.review.actorName,
      instruction:sharedReview.review.instruction
    }:undefined;
  const canDecide=Boolean(canControl&&chosen&&project?.status==='needs_review'&&!ledgerDecision&&!decision);
  const sourceUrls=new Set<string>();
  for(const item of selected?.artifacts||[]){
    const data=artifactData(item);
    if(Array.isArray(data.evidence))for(const row of data.evidence){if(row&&typeof row.sourceUrl==='string')sourceUrls.add(row.sourceUrl);}
    if(Array.isArray(data.angles))for(const row of data.angles){if(row&&typeof row.sourceUrl==='string')sourceUrls.add(row.sourceUrl);}
  }
  const activity=[...(selected?.reviews||[]).map(item=>({id:item.id,date:item.created_at,title:`Ledger decision · ${item.decision.replaceAll('_',' ')}`,detail:`${item.instruction||item.actor_name} · human authority requires a host receipt`})),
    ...(selected?.inputs||[]).map(item=>({id:item.id,date:item.created_at,title:`Project input · ${item.actor_name}`,detail:item.content})),
    ...(selected?.campaign_actions||[]).map(item=>({id:item.id,date:item.created_at,title:item.action.replaceAll('_',' '),detail:item.status}))]
    .sort((a,b)=>b.date-a.date);
  const worker=atUsageCheckpoint(selected)?'Usage checkpoint':selected?.terminal_receipts?.length?'Run failed · stopped':project?.status==='unknown'?'Outcome unknown · held':project?.status==='needs_review'?'Waiting for owner review':project?.status?.replaceAll('_',' ')||'No assignment';
  const currentUnknown=current?.project.status==='unknown'&&current.project.id!==project?.id;
  return <section className={'campaign-desk'+(contextOpen&&project?'':' context-closed')+(project?'':' empty')} aria-label="Campaign review workspace">
    <aside className="campaign-desk-list" aria-label="Campaign list">
      <div className="campaign-desk-list-heading"><strong>Campaigns</strong></div>
      {current?.project&&<button type="button" className={project?.id===current.project.id?'selected':''} onClick={onCurrent}><span className="campaign-list-title">{current.project.goal}</span><small>Current · {current.project.status==='unknown'?'Held for review':current.project.status.replaceAll('_',' ')}</small></button>}
      {archive.filter(item=>item.id!==current?.project.id).map(item=><button type="button" className={project?.id===item.id?'selected':''} key={item.id} disabled={archiveBusy} onClick={()=>onOpen(item.id)}><span className="campaign-list-title">{item.goal}</span><small>{item.status.replaceAll('_',' ')} · {readableTime(item.updated_at)}</small></button>)}
      {!current&&!archive.length&&<p className="campaign-desk-muted">No campaigns yet.</p>}
    </aside>
    <div className="campaign-desk-main">
      <header className="campaign-desk-header"><p className="eyebrow">{project?.id===current?.project.id?'Current campaign':'Saved campaign'}</p><h2>{project?campaignTitle(project.goal):'No campaign yet'}</h2>{project?campaignTitle(project.goal)!==project.goal.replace(/[.!?]$/,'')&&<p className="campaign-desk-goal">{project.goal}</p>:<p>When Marketing finishes an assignment, its drafts show up here for your review.</p>}</header>
      {currentUnknown&&<p className="campaign-desk-hold" role="status">A newer assignment is on hold while its last step is checked. You can still review this saved campaign.</p>}
      {project&&<div className="campaign-desk-status" aria-label="Campaign status"><div><span>Stage</span><strong>{campaign?.stage?.replaceAll('_',' ')||'Brief pending'}</strong></div><div><span>Marketing</span><strong>{worker}</strong></div><div><span>Next step</span><strong>{selected?nextCampaignAction(selected):'Open an assignment'}</strong></div><div><span>Review by</span><strong>{String(brief.review_timing||'Your call')}</strong></div></div>}
      {campaign&&<p className="campaign-desk-provisional">Marketing drafted this campaign’s brief. Check it in the Brief tab before relying on it.</p>}
      {project&&<><div className="campaign-desk-tabs" aria-label="Review sections"><button type="button" aria-pressed={section==='draft'} onClick={()=>setSection('draft')}>Draft</button><button type="button" aria-pressed={section==='brief'} onClick={()=>setSection('brief')}>Brief</button><button type="button" aria-pressed={section==='activity'} onClick={()=>setSection('activity')}>Activity & sharing</button></div>
        {section==='draft'&&<div className="campaign-desk-reading"><div className="campaign-desk-reading-head"><div><p className="eyebrow">Draft</p><h3>{chosen?labels[chosen.kind]||'Creative draft':'No draft saved yet'}</h3><small>{chosen?`Saved ${readableTime(chosen.created_at)} · version ${drafts.indexOf(chosen)+1} of ${drafts.length}`:'Marketing hasn’t saved a draft for this campaign yet.'}</small></div>{drafts.length>1&&<label>Version<select value={chosen?.id||''} onChange={event=>{setArtifactId(event.target.value);setCompare(false);}}>{drafts.map((item,index)=><option value={item.id} key={item.id}>Version {index+1} · {readableTime(item.created_at)}</option>)}</select></label>}</div>
          {chosen&&<><div className={compare&&predecessor?'campaign-desk-comparison':''}>{compare&&predecessor&&<div><h4>Predecessor</h4><ArtifactBody artifact={predecessor}/></div>}<div>{compare&&predecessor&&<h4>Selected revision</h4>}<ArtifactBody artifact={chosen}/></div></div>{predecessor&&<button type="button" className="campaign-desk-text-button" onClick={()=>setCompare(value=>!value)}>{compare?'Close comparison':'Compare with predecessor'}</button>}
            {decision?<div className="campaign-desk-decision"><strong>{decision.decision==='approved'?'Approved':decision.decision==='rejected'?'Rejected':'Changes requested'} by {decision.actor_name||'you'}</strong><p>{decision.instruction||'This decision is tied to the exact saved version.'}</p></div>:ledgerDecision?<div className="campaign-desk-hold" role="status">A decision is recorded for this version, but this host can’t confirm it came from you. Check the version details before relying on it.</div>:canDecide&&<div className="campaign-desk-actions"><p>Your call on this version. Approving records the decision; nothing is published.{!revisionAllowed&&' Change requests open once new work is enabled on this host.'}</p><div><button type="button" disabled={busy} onClick={()=>onDecision(chosen,'rejected')}>Reject</button><button type="button" disabled={busy||!revisionAllowed} onClick={()=>setChangeOpen(value=>!value)}>Request changes</button><button type="button" className="primary" disabled={busy} onClick={()=>onDecision(chosen,'approved')}>{busy?'Saving…':'Approve'}</button></div>{changeOpen&&<form onSubmit={event=>{event.preventDefault();if(changeText.trim())onDecision(chosen,'revision_requested',changeText.trim());}}><label>What should change?<textarea required maxLength={1000} value={changeText} onChange={event=>setChangeText(event.target.value)} placeholder="Name the angle, claim, audience assumption, or source that should change."/></label><button type="submit" className="primary" disabled={busy||!changeText.trim()}>Send change request</button></form>}</div>}
            <details className="campaign-desk-provenance"><summary>Version details</summary><dl><div><dt>Artifact</dt><dd>{chosen.id}</dd></div><div><dt>Digest</dt><dd>{chosen.digest}</dd></div><div><dt>Predecessor</dt><dd>{predecessorId||'Original draft'}</dd></div></dl></details></>}
          {chosen&&assessments&&<section className="campaign-desk-assessment"><h4>Marketing’s self-review</h4><p>How Marketing rated its own draft. Your judgment wins.</p><dl>{checks.map(([key,label])=><div key={key}><dt>{label}</dt><dd>{String(assessments[key]||'Not assessed')}</dd></div>)}</dl></section>}
        </div>}
        {section==='brief'&&<div className="campaign-desk-reading"><p className="eyebrow">Campaign brief</p><h3>{campaign?'Campaign brief':'No brief saved yet'}</h3>{campaign&&<><p>Marketing drafted this brief. Check the audience and proposition before approving work against it.</p><dl className="campaign-desk-brief">{[['Audience',brief.audience],['Problem',brief.problem],['Proposition',brief.proposition],['Desired behavior',brief.desired_behavior],['Primary metric',brief.primary_metric],['Priority rationale',brief.priority_rationale],['Review timing',brief.review_timing],['Decision rule',experiment.decision_rule]].map(([label,value])=><div key={String(label)}><dt>{String(label)}</dt><dd>{String(value||'Not recorded')}</dd></div>)}</dl><small>Brief version {campaign.version}</small></>}{!campaign&&<p>Save a brief from Assignment details below once you’ve reviewed Marketing’s research note.</p>}</div>}
        {section==='activity'&&<div className="campaign-desk-reading"><p className="eyebrow">Activity & sharing</p><h3>Who’s involved and what changed</h3>
          {canControl&&campaign&&<CampaignInvitations key={selected.project.id} campaignId={selected.project.id}/>}
          <section className="campaign-desk-sharing" aria-label="Campaign access"><h4>Share this campaign</h4><p>Invite a teammate to review this campaign and its draft. Your chat with Marketing stays private.</p><div className="campaign-desk-access-controls"><label>{devices.some(item=>item.accountId)?'Collaborator account or browser':'Paired collaborator device'}<select value={selectedDevice} onChange={event=>setSelectedDevice(event.target.value)}><option value="">Choose a collaborator</option>{devices.map(item=><option key={item.id} value={item.id}>{item.name} · {item.accountId?'account · ':''}{item.id.slice(0,8)}</option>)}</select></label><button type="button" disabled={sharing||!selectedDevice} onClick={()=>void changeAccess(selectedDevice,'grant')}>Grant campaign access</button></div>{devices.length===0&&<p>Have your teammate sign in, or pair their browser in Settings → Team access first.</p>}{access.filter(item=>item.active).map(item=><div className="campaign-desk-member" key={item.deviceId}><span>{item.name} · campaign member</span><button type="button" disabled={sharing} onClick={()=>void changeAccess(item.deviceId,'revoke')}>Revoke</button></div>)}<div className="campaign-desk-access-controls"><button type="button" disabled={sharing||sharedReview?.native.sessionConnected} onClick={()=>void connectNative()}>{sharedReview?.native.sessionConnected?'Native conversation connected':'Connect native conversation'}</button></div><small>{sharedReview?.native.sessionConnected?'Each new shared campaign note is recorded under its Gateway profile before the project ledger.':'Your local owner session can connect this saved campaign without starting the worker. Collaborators need an authenticated HTTPS address to add native-linked notes.'}</small></section>
          {sharedReview&&<section className="campaign-desk-sharing" aria-label="Shared discussion"><h4>Team discussion</h4>{!sharedReview.discussion.length&&<p>No comments yet.</p>}{sharedReview.discussion.map(item=><article key={item.requestId}><strong>{item.actorName} · {item.kind==='revision_request'?'Change request':'Comment'}</strong><small>{readableTime(item.createdAt)} · draft {item.artifactDigest.slice(0,12)}… · {item.status.replaceAll('_',' ')}{item.nativeRecorded&&item.nativeProfileId?` · Gateway profile ${item.nativeProfileId.slice(0,8)}…`:''}</small><p>{item.content}</p>{item.kind==='revision_request'&&item.status==='awaiting_owner_authorization'&&item.artifactId===sharedReview.artifact.id&&<button type="button" disabled={sharing} onClick={()=>void authorizeRequest(item.requestId)}>Authorize this exact change request</button>}</article>)}</section>}
          {shareError&&<p className="company-error" role="alert">{shareError}</p>}
          {activity.length?<ol className="campaign-desk-activity">{activity.map(item=><li key={item.id}><time>{readableTime(item.date)}</time><strong>{item.title}</strong><p>{item.detail}</p></li>)}</ol>:<p>No other campaign activity has been saved.</p>}
        </div>}
      </>}
    </div>
    {!project?null:contextOpen?<aside className="campaign-desk-context" aria-label="Campaign context"><div className="campaign-desk-context-head"><strong>Context</strong><button type="button" className="fe-icon-button" onClick={()=>setContextOpen(false)} aria-label="Collapse campaign context">×</button></div><div><span>Access</span><strong>{access.filter(item=>item.active).length?`${access.filter(item=>item.active).length} teammate${access.filter(item=>item.active).length===1?'':'s'}`:'Just you'}</strong><button type="button" onClick={()=>setSection('activity')}>Manage access</button></div><div><span>Team</span><strong>You · Marketing</strong>{access.filter(item=>item.active).map(item=><p key={item.deviceId}>{item.name} · campaign member</p>)}<p>{sharedReview?.native.sessionConnected?'Native session connected; Gateway profile receipts are recorded per note.':'Native session not connected.'}</p></div><div><span>Sources</span>{sourceUrls.size?<ul>{[...sourceUrls].map(url=><li key={url}><SourceReference url={url} label={url}/></li>)}</ul>:<p>No source references saved.</p>}</div><div><span>Discussion</span><p>{sharedReview?.discussion.length||0} comments · {selected?.inputs.length||0} notes</p></div>{fixtureEnabled&&<p className="fe-pill">Demo data</p>}</aside>:<button type="button" className="campaign-desk-reopen" onClick={()=>setContextOpen(true)}>Show context</button>}
  </section>;
}

function outcomeMetric(runway?:RunwaySnapshot|null){
  if(!runway?.campaign)return 'Not recorded';
  try{return String((JSON.parse(runway.campaign.brief_json) as Record<string,unknown>).primary_metric||'Not recorded');}
  catch{return 'Not recorded';}
}

function ModelRequestReceipts({runway}:{runway:RunwaySnapshot}){
  if(runway.campaign?.mode==='fixture')return <p className="runway-reason">Fixture steps used zero model requests. Their saved usage is synthetic.</p>;
  const missing=missingProviderRequestReceipts(runway);
  if(!runway.model_requests?.length)return missing?<p className="runway-reason">Underlying provider request counts were not recorded for one or more executions.</p>:null;
  return <>
    {missing&&<p className="runway-reason">Some earlier executions lack provider request receipts. The recorded count is incomplete.</p>}
    {!!runway.terminal_receipts?.length&&<p className="runway-reason" role="status">The Gateway confirmed this run ended. Chat is available again. Actual usage remains unknown and its reservation is retained; this assignment will not automatically retry.</p>}
    <details className="runway-executions"><summary>Underlying model requests · {runway.model_requests.length}</summary>
      <ul>{runway.model_requests.map(item=><li key={item.request_id}>{readableTime(item.created_at)} · {item.status} · {item.reported_tokens==null?`${item.reserved_tokens.toLocaleString()} tokens reserved`:item.reported_tokens.toLocaleString()+' tokens reported'} · request {item.request_id.slice(0,12)}…</li>)}</ul>
      {runway.terminal_receipts?.map(item=><p key={item.execution_id}>Run ended · {readableTime(item.recorded_at)} · audit receipt {item.audit_event_id} · evidence SHA-256 {item.evidence_digest}</p>)}
    </details>
  </>;
}

export function MarketingRunwayPanel({runway,profile,evidenceEnabled,canControl,canContribute,liveWorkEnabled,archiveEnabled,campaignBriefEnabled,fixtureCampaignEnabled,deferredRevisionEnabled,nativeSharedEnabled,onRefresh,onOpenBrief}:{onOpenBrief?:()=>void;runway?:RunwaySnapshot|null;profile?:MarketingProfile;evidenceEnabled?:boolean;canControl:boolean;canContribute:boolean;liveWorkEnabled:boolean;archiveEnabled:boolean;campaignBriefEnabled:boolean;fixtureCampaignEnabled:boolean;deferredRevisionEnabled:boolean;nativeSharedEnabled:boolean;onRefresh:()=>Promise<void>}){
  const [goalEdit,setGoalEdit]=useState<string|null>(null),[creating,setCreating]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const [sourceUrls,setSourceUrls]=useState(['','']);
  const [acceptAccounting,setAcceptAccounting]=useState(false),[usageReviewed,setUsageReviewed]=useState(false);
  const checkpointAttempt=useRef<{signature:string;id:string}|null>(null);
  useEffect(()=>{setUsageReviewed(false);},[runway?.project.id,runway?.project.version]);
  const [note,setNote]=useState(''),[revision,setRevision]=useState<{artifactId:string;instruction:string}|null>(null);
  const [shared,setShared]=useState<SharedConversation|null>(null),[sharedBusy,setSharedBusy]=useState(false),[sharedError,setSharedError]=useState('');
  const [archive,setArchive]=useState<ArchivedProject[]>([]),[archived,setArchived]=useState<RunwaySnapshot|null>(null);
  const [archiveBusy,setArchiveBusy]=useState(false),[archiveError,setArchiveError]=useState('');
  const autoReviewSelected=useRef(false);
  const startAttempt=useRef<{signature:string;id:string}|null>(null),noteAttempt=useRef<{signature:string;id:string}|null>(null);
  const seedAttempt=useRef<string|null>(null);
  const reviewAttempt=useRef<{signature:string;id:string}|null>(null);
  const adoptAttempt=useRef<{signature:string;id:string}|null>(null);
  const project=runway?.project;
  const selectedReview=archived||runway;
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
  const expiredCheckpoint=atUsageCheckpoint(runway)&&(!project?.deadline_at||project.deadline_at*1000<=Date.now());
  const terminalFailure=Boolean(runway?.terminal_receipts?.length);
  const usageCheckpoint=atUsageCheckpoint(runway)&&!expiredCheckpoint;
  const nextCheck=expiredCheckpoint?'The original grant expired; choose a new bounded assignment':usageCheckpoint?'After you review first-request usage':terminalFailure?'No automatic retry':deferredRevision?'When a metered, linked revision grant is available':project?.next_due?readableTime(project.next_due):project?.status==='needs_review'?'When you review the packet':project?.status==='paused'?'When you resume':project?.status==='unknown'?'After the original execution is reconciled':project?.status==='budget_exhausted'?'After a new bounded assignment':'After this step settles or relevant input arrives';
  const canStart=canControl&&liveWorkEnabled&&(!project||['needs_review','done','budget_exhausted'].includes(project.status));

  useEffect(()=>{
    if(!project||!nativeSharedEnabled){setShared(null);return;}
    let current=true;
    api<SharedConversation>(`/marketing/runway/${project.id}/shared`).then(value=>{if(current){setShared(value);setSharedError('');}})
      .catch(cause=>{if(current)setSharedError((cause as Error).message);});
    return ()=>{current=false;};
  },[project?.id,project?.version,nativeSharedEnabled]);

  useEffect(()=>{
    if(!canControl||!archiveEnabled){setArchive([]);setArchived(null);return;}
    let current=true;
    api<{projects:ArchivedProject[]}>('/marketing/runways').then(value=>{if(current){setArchive(value.projects);setArchiveError('');}})
      .catch(cause=>{if(current)setArchiveError((cause as Error).message);});
    return ()=>{current=false;};
  },[canControl,archiveEnabled,project?.id,project?.version]);

  useEffect(()=>{
    if(autoReviewSelected.current||project?.status!=='unknown'||archived||archiveBusy)return;
    const candidate=archive.find(item=>item.id!==project.id&&item.artifact_count>0);
    if(candidate){autoReviewSelected.current=true;void openArchive(candidate.id);}
  },[archive,archiveBusy,archived,project?.id,project?.status]);

  async function openArchive(id:string,force=false){
    if(archiveBusy)return;
    if(archived?.project.id===id){if(!force)setArchived(null);return;}
    setArchiveBusy(true);setArchiveError('');
    try{setArchived(await api<RunwaySnapshot>(`/marketing/runways/${id}`));if(force)requestAnimationFrame(()=>document.getElementById('previous-marketing-assignments')?.scrollIntoView({behavior:'smooth',block:'start'}));}
    catch(cause){setArchiveError((cause as Error).message);}finally{setArchiveBusy(false);}
  }

  useEffect(()=>{
    const id=archived?.project.id;
    if(!id)return;
    let active=true;
    const timer=window.setInterval(()=>{
      if(document.visibilityState!=='visible')return;
      void api<RunwaySnapshot>(`/marketing/runways/${id}`).then(saved=>{
        if(active)setArchived(current=>current?.project.id===id&&saved.project.version>=current.project.version?saved:current);
      }).catch(()=>{/* Keep the last confirmed record until the next authenticated read. */});
    },8000);
    return()=>{active=false;clearInterval(timer);};
  },[archived?.project.id]);

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

  async function start(){
    if(!canStart||!acceptAccounting||busy||sourceUrls.some(url=>!/^https:\/\/news\.ycombinator\.com\/item\?id=[0-9]{1,12}$/.test(url.trim()))||sourceUrls[0].trim()===sourceUrls[1].trim()||!goal.trim()||!profile?.product_summary.trim()||!profile.goals.trim())return;
    const signature=JSON.stringify({goal:goal.trim(),version:profile.version,sourceUrls:sourceUrls.map(url=>url.trim())});
    const id=startAttempt.current?.signature===signature?startAttempt.current.id:requestId();startAttempt.current={signature,id};
    setBusy(true);setError('');
    try{await api('/marketing/runway',{requestId:id,goal:goal.trim(),sourceUrls:sourceUrls.map(url=>url.trim()),acceptPostResponseAccounting:true});startAttempt.current=null;setCreating(false);setGoalEdit(null);setAcceptAccounting(false);await onRefresh();}
    catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}finally{setBusy(false);}
  }
  async function continuePilot(){
    if(!project||!canControl||!liveWorkEnabled||!usageReviewed||busy)return;
    const signature=JSON.stringify({id:project.id,version:project.version});
    const id=checkpointAttempt.current?.signature===signature?checkpointAttempt.current.id:requestId();
    checkpointAttempt.current={signature,id};setBusy(true);setError('');
    try{await api('/marketing/runway/continue-pilot',{id:project.id,version:project.version,requestId:id,usageReviewed:true});checkpointAttempt.current=null;setUsageReviewed(false);await onRefresh();}
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
  async function reviewArtifact(artifact:RunwayArtifact,decision:'approved'|'rejected'|'revision_requested',target=runway,instructionOverride?:string){
    const reviewProject=target?.project;
    if(!reviewProject||!canControl||busy||decision==='revision_requested'&&!(liveWorkEnabled||deferredRevisionEnabled))return;
    const instruction=decision==='revision_requested'?(instructionOverride??(revision?.artifactId===artifact.id?revision.instruction:'')).trim():'';
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
    <ReviewDesk selected={selectedReview} current={runway} archive={archive} archiveBusy={archiveBusy}
      canControl={canControl} busy={busy} revisionAllowed={liveWorkEnabled||deferredRevisionEnabled}
      fixtureEnabled={fixtureCampaignEnabled} onOpen={id=>void openArchive(id)} onCurrent={()=>setArchived(null)}
      onDecision={(artifact,decision,instruction)=>void reviewArtifact(artifact,decision,selectedReview,instruction)}
      onSharedChange={async()=>{if(archived)setArchived(await api<RunwaySnapshot>(`/marketing/runways/${archived.project.id}`));await onRefresh();}}/>
    <RevisionRunGrant runway={selectedReview} enabled={canControl&&liveWorkEnabled} onReleased={async()=>{setArchived(null);await onRefresh();}}/>
    {(error||archiveError)&&<p className="company-error" role="alert">{error||archiveError}</p>}
    <details className="runway-management" open={!project||creating?true:undefined}><summary><span><strong>Assignment details & history</strong><small>Start work, add notes, and see every run and receipt.</small></span>{project&&<span className={'runway-status '+project.status}>{project.status.replaceAll('_',' ')}</span>}</summary>
    {fixtureCampaignEnabled?<p className="runway-reason" role="status">Demo workspace: sample data, no model calls, nothing is published.</p>:!liveWorkEnabled&&<p className="runway-reason" role="status">Starting new assignments is turned off on this host. Your saved work, notes and decisions are all still here, and chat works as usual.</p>}
    {fixtureCampaignEnabled&&!project&&canControl&&<button type="button" className="primary" disabled={busy} onClick={()=>void seedFixture()}>{busy?'Preparing…':'Create simulated campaign'}</button>}
    {(!project||creating)&&!fixtureCampaignEnabled&&<div className="runway-setup"><div className="runway-subheading"><div><strong>Start an assignment</strong><small>Check exactly what Marketing may do before it starts.</small></div></div><label>Desired outcome<textarea value={goal} onChange={event=>setGoalEdit(event.target.value)} maxLength={1200} rows={5}/></label><fieldset className="runway-sources"><legend>Evidence for this assignment</legend><p>Choose two relevant public Hacker News discussions. The employee will read these pages and cite the saved evidence. Search for current candidates below, or paste their URLs.</p><MarketingSourcePicker selected={sourceUrls} onSelect={setSourceUrls}/>{sourceUrls.map((url,index)=><label key={index}>Source {index+1}<input type="url" value={url} onChange={event=>setSourceUrls(current=>current.map((value,i)=>i===index?event.target.value:value))} placeholder="https://news.ycombinator.com/item?id=…" maxLength={200}/></label>)}</fieldset><div className="runway-grant"><p><b>Deliverables:</b> one checked audience/problem note, three evidence-linked draft post angles, and an owner review packet.</p><p><b>Allowed work:</b> internal research and local drafts from two restricted public sources. No publishing, outreach, spending, or account changes.</p><p><b>Limits:</b> one request first, then a usage checkpoint. At most three requests and three runs within 15 minutes; no hidden retries. The 75,000-token allowance is checked between requests. A single response can exceed its 25,000-token reservation and will stop further work.</p><label><input type="checkbox" checked={acceptAccounting} onChange={event=>setAcceptAccounting(event.target.checked)}/>I understand usage is measured after each response and one response can exceed its reservation.</label></div>{canControl&&<div className="runway-actions"><button className="primary" disabled={!canStart||!acceptAccounting||busy||sourceUrls.some(url=>!/^https:\/\/news\.ycombinator\.com\/item\?id=[0-9]{1,12}$/.test(url.trim()))||sourceUrls[0].trim()===sourceUrls[1].trim()||!goal.trim()||!profile?.product_summary.trim()||!profile?.goals.trim()} onClick={()=>void start()}>{busy?'Starting…':'Start bounded work'}</button>{project&&<button type="button" onClick={()=>setCreating(false)}>Keep reviewing current project</button>}</div>}{(!profile?.product_summary.trim()||!profile?.goals.trim())&&<p className="runway-reason">Add your offer and goal to the business brief first.{onOpenBrief&&<> <button type="button" className="fe-ghost" onClick={onOpenBrief}>Open the brief</button></>}</p>}</div>}
    {project&&!creating&&<div className="runway-project"><div className="runway-subheading"><div><strong>Current assignment</strong><small>Research and drafts only · version {project.version}</small></div>{canStart&&<button type="button" onClick={()=>setCreating(true)}>New assignment</button>}</div><p className="runway-goal">{project.goal}</p>{project.source_runway_id&&<p className="runway-reason">Linked revision of artifact {project.source_artifact_id?.slice(0,12)}… {archiveEnabled&&canControl&&<button type="button" onClick={()=>void openArchive(project.source_runway_id!,true)}>Open source assignment</button>}</p>}<div className="runway-metrics"><span><strong>{project.run_count}/{project.max_runs}</strong> runs admitted</span><span><strong>{providerRequestUsage}</strong> provider requests recorded</span><span><strong>{project.token_used.toLocaleString()}</strong> reported model tokens</span><span><strong>{Math.max(0,project.token_limit-project.token_used-project.token_reserved).toLocaleString()}</strong> unused recorded tokens</span><span><strong>{runway?.artifacts.length||0}</strong> saved results</span></div>{fixtureCampaignEnabled?<small>SIMULATED steps and allowances · zero model requests · excluded from live usage.</small>:<small>Subscription use is reported in tokens; no cash charge is established by this display.</small>}{project.status==='unknown'&&<p className="runway-reason" role="status">Actual model use is unknown while this execution is held. The reported-token total excludes unresolved requests; {project.token_reserved.toLocaleString()} tokens remain reserved. Do not treat the remaining allowance as permission to retry.</p>}{project.deadline_at==null&&<p className="runway-reason">This legacy assignment has no recorded deadline. Its unused allowance does not authorize more work; a fresh owner grant is required.</p>}
      {campaignBriefEnabled&&runway&&<p className="runway-reason"><b>Outcome metric:</b> {outcomeMetric(runway)} · <b>Campaign next action:</b> {nextCampaignAction(runway)}</p>}
      {campaignBriefEnabled&&canControl&&approvedRevision&&<div className="runway-proposal"><strong>Approved revision {revisionSelected?'selected for the original campaign':'ready for the original campaign'}</strong><p>The exact revised asset can be selected for the source campaign after its internal brief is saved. This records an owner decision and does not authorize launch.</p>{!revisionSelected&&<button type="button" className="primary" disabled={busy} onClick={()=>void adoptRevision()}>{busy?'Checking…':'Select for source campaign'}</button>}</div>}
      <div className="runway-next"><span><b>Current action</b>{expiredCheckpoint?'Review expired grant':usageCheckpoint?'Review observed usage':terminalFailure?'Review failed run':project.status==='unknown'?'Reconcile held execution':current?labels[current.kind]||current.kind:project.status==='needs_review'?'Owner review':'No active step'}</span><span><b>Next action</b>{expiredCheckpoint?'New bounded assignment':usageCheckpoint?'Awaiting usage checkpoint':terminalFailure?'None admitted':project.status==='unknown'?'No new step until reconciliation':next?labels[next.kind]||next.kind:project.status==='needs_review'?'Your decision':'None admitted'}</span><span><b>Next check</b>{nextCheck}</span></div>{project.deadline_at&&<small>Grant deadline: {readableTime(project.deadline_at)}</small>}{latest&&<p className="runway-progress">Latest recorded run: {latest.status} at {readableTime(latest.ended_at||latest.started_at)}{latest.error?' · '+latest.error:''}</p>}{project.wait_reason&&!expiredCheckpoint&&<p className="runway-reason">{project.wait_reason}</p>}
      {canControl&&pendingReview&&<div className="runway-actions"><button type="button" onClick={()=>{const target=document.getElementById(`runway-artifact-${pendingReview.id}`) as HTMLDetailsElement|null;if(target){target.open=true;target.scrollIntoView({behavior:'smooth',block:'start'});}}}>Review draft angles</button></div>}
      {canControl&&project.status==='needs_review'&&reviewSummary&&<div className="runway-proposal"><strong>Employee recommendation</strong>{reviewSummary.recommendation&&<p>{reviewSummary.recommendation}</p>}{reviewSummary.decision&&<p><b>Your next decision:</b> {reviewSummary.decision}</p>}{savedReview&&<button type="button" onClick={()=>{const target=document.getElementById(`runway-artifact-${savedReview.id}`) as HTMLDetailsElement|null;if(target){target.open=true;target.scrollIntoView({behavior:'smooth',block:'start'});}}}>Read full review packet</button>}</div>}
      <ol className="runway-steps">{runway?.steps.map(step=><li key={step.id}><span className={'runway-dot '+step.status}/><div><strong>{labels[step.kind]||step.kind}</strong><small>{step.status.replaceAll('_',' ')} · {step.attempts} attempt{step.attempts===1?'':'s'}</small></div></li>)}</ol>{canControl&&<div className="runway-actions">{['ready','running','waiting'].includes(project.status)&&<button disabled={busy} onClick={()=>void change('pause')}>Pause new work</button>}{project.status==='paused'&&<button disabled={busy||!liveWorkEnabled} onClick={()=>void change('resume')}>Resume project</button>}</div>}
      <div className="runway-results"><h3>Saved work</h3>{runway?.artifacts.length?runway.artifacts.map(artifact=>{const decision=runway.reviews?.find(item=>item.artifact_id===artifact.id);const canReview=canControl&&project.status==='needs_review'&&!decision&&['post_angles','revision_angles'].includes(artifact.kind);return <details className="runway-artifact" id={`runway-artifact-${artifact.id}`} key={artifact.id}><summary>{labels[artifact.kind]||artifact.kind} · saved {readableTime(artifact.created_at)}</summary><ArtifactBody artifact={artifact}/><small className="runway-provenance">Version {artifact.digest.slice(0,8)}</small>{decision&&<p className="runway-decision"><b>{decision.actor_name}:</b> {decision.decision.replaceAll('_',' ')}{decision.instruction?' · '+decision.instruction:''}</p>}{canReview&&<div className="runway-review"><p>Approving records your decision. Nothing is published.</p><div className="runway-actions"><button type="button" disabled={busy} onClick={()=>void reviewArtifact(artifact,'rejected')}>Reject</button><button type="button" disabled={busy||!(liveWorkEnabled||deferredRevisionEnabled)} onClick={()=>setRevision({artifactId:artifact.id,instruction:''})}>Request changes</button><button type="button" className="primary" disabled={busy} onClick={()=>void reviewArtifact(artifact,'approved')}>Approve</button></div>{revision?.artifactId===artifact.id&&<form onSubmit={event=>{event.preventDefault();void reviewArtifact(artifact,'revision_requested');}}><label>What should change?<textarea required maxLength={1000} value={revision.instruction} onChange={event=>setRevision({artifactId:artifact.id,instruction:event.target.value})} placeholder="Point to the angle, claim, or audience assumption to revise."/></label><button disabled={busy||!revision.instruction.trim()}>{busy?'Recording…':'Save revision request'}</button></form>}</div>}</details>;}):<p>{terminalFailure?'This run stopped before saving a verified result. Chat is available; this grant will not retry.':project.status==='unknown'?'No result was saved. Reconcile the held execution before more work.':'No saved results yet. Refresh when the first step finishes.'}</p>}</div>
      {campaignBriefEnabled&&runway&&<CampaignBriefPanel runway={runway} canControl={canControl} onSaved={async()=>{await onRefresh();}}/>}
      <div className="runway-inputs"><strong>Notes for Marketing</strong>
        {nativeSharedEnabled&&<p className="runway-reason">Use Campaign review → What changed to share this exact draft, connect its native conversation through HTTPS, and audit version-linked comments. This project note field remains owner-scoped.</p>}
        {shared?.suggestions.filter(item=>item.status!=='recorded').map(item=><p key={item.requestId}><b>{item.actorName}</b> · {readableTime(item.createdAt)} · {item.status.replaceAll('_',' ')}<br/>{item.content}{item.error&&<small> · {item.error}</small>}{canControl&&['gateway_recorded','ledger_conflict'].includes(item.status)&&<button type="button" disabled={sharedBusy} onClick={()=>void reconcileShared(item.requestId)}>Reconcile saved receipt</button>}</p>)}
        {runway?.inputs.map(item=><p key={item.id}><b>{item.actor_name}</b> · {readableTime(item.created_at)}{item.source_input_id&&<small> · linked source input {item.source_input_id.slice(0,12)}…</small>}<br/>{item.content}</p>)}
        {canContribute&&<form onSubmit={event=>{event.preventDefault();void addNote();}}><label>Add context for the next step<textarea value={note} maxLength={1000} rows={2} onChange={event=>setNote(event.target.value)} placeholder="Add a specific source limit or customer concern"/></label><button disabled={busy||sharedBusy||!note.trim()}>Add note</button><small>Notes guide the next step. They can’t approve spending or reopen finished work.</small></form>}
        {nativeSharedEnabled&&sharedError&&<p className="company-error" role="alert">{sharedError}</p>}
      </div>{expiredCheckpoint&&<section className="runway-grant" aria-label="Expired first request grant"><h3>First request grant expired</h3><p>{project.token_used.toLocaleString()} tokens were reported. No further requests can run under this grant, even if the first result failed validation. Review the saved response and start a new bounded assignment when ready.</p></section>}{usageCheckpoint&&<section className="runway-grant" aria-label="First request usage checkpoint"><h3>First request complete</h3><p>{project.token_used.toLocaleString()} tokens reported. The employee is stopped for this usage check.</p><p>You can release at most two more requests before {project.deadline_at?readableTime(project.deadline_at):"the original deadline"}. This does not extend the deadline or token allowance. Review your subscription’s remaining limits before continuing.</p>{canControl&&<><label><input type="checkbox" checked={usageReviewed} onChange={event=>setUsageReviewed(event.target.checked)}/>I reviewed this usage and my remaining subscription limits.</label><button type="button" disabled={busy||!liveWorkEnabled||!usageReviewed||!project.deadline_at||project.deadline_at*1000<=Date.now()} onClick={()=>void continuePilot()}>Release remaining pilot requests</button></>}</section>}<RevisionGrantReceipts runway={runway}/>{runway?.executions.length?<details className="runway-executions"><summary>Execution and usage receipts</summary><ul>{runway.executions.map(item=><li key={item.id}>{readableTime(item.started_at)} · {item.status} · {item.reported_tokens==null?`usage unavailable; ${item.reserved_tokens.toLocaleString()} reserved`:item.reported_tokens.toLocaleString()+' reported tokens'}{item.error?' · '+item.error:''}</li>)}</ul></details>:null}<WorkerResponseRecords runway={runway}/><ModelRequestReceipts runway={runway}/>
    </div>}
    </details>
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
        {archived.executions.length>0&&<details className="runway-executions"><summary>Execution and usage receipts</summary><ul>{archived.executions.map(item=><li key={item.id}>{readableTime(item.started_at)} · {item.status} · {item.reported_tokens==null?`usage unavailable; ${item.reserved_tokens.toLocaleString()} reserved`:item.reported_tokens.toLocaleString()+' reported tokens'}{item.error?' · '+item.error:''}</li>)}</ul></details>}<WorkerResponseRecords runway={archived}/><ModelRequestReceipts runway={archived}/>
      </div>}
    </div>}
  </section>;
}
