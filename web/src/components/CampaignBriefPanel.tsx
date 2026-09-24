import {useEffect,useRef,useState,type FormEvent} from 'react';
import {api} from '../api';
import {readableTime,requestId,type RunwaySnapshot} from './MarketingPanels';
import {CampaignFixtureControls} from './CampaignFixtureControls';
import {CampaignManualObservation} from './CampaignManualObservation';
import {CampaignInternalDecision} from './CampaignInternalDecision';
import {CampaignLaunchReadiness} from './CampaignLaunchReadiness';

type Brief={audience:string;problem:string;hypothesis:string;priority_rationale:string;proposition:string;desired_behavior:string;channel:string;primary_metric:string;metric_definition:string;guardrail:string;review_timing:string;non_goals:string};
type Experiment={intervention:string;target_population:string;observation_window:string;metric_source:string;decision_rule:'learning_only'|'minimum_sample';minimum_sample:number};
type PriorLesson={campaign_id:string;action_id:string;created_at:number;lesson:{lesson:string;context:string;uncertainty:string;revisit_condition:string;next_action:string;evidence_type:string;causality:string};brief:{audience:string};decision:{decision:string;rationale:string};observations:{source_reference:string;value_type:string;attribution_limitations:string}[]};
type FixturePriorLesson={campaign_id:string;action_id:string;created_at:number;lesson:{lesson:string;context:string;uncertainty:string;revisit_condition:string;next_action:string};brief:{audience:string};decision:{decision:string;rationale:string;actual_sample:number;required_sample:number};observations:{action_id:string;source:string;captured_at:number;period_start:number;period_end:number;timezone:string;metric_definition:string;value_type:string;numerator:number;denominator:number;attribution_limitations:string}[]};
const emptyBrief:Brief={audience:'',problem:'',hypothesis:'',priority_rationale:'',proposition:'',desired_behavior:'',channel:'',primary_metric:'',metric_definition:'',guardrail:'',review_timing:'At owner review; no calendar date set',non_goals:'No new channels or unverified product claims'};
const emptyExperiment:Experiment={intervention:'',target_population:'',observation_window:'',metric_source:'',decision_rule:'learning_only',minimum_sample:0};
const briefLabels:Record<keyof Brief,string>={audience:'Audience',problem:'Customer problem',hypothesis:'Opportunity hypothesis',priority_rationale:'Why prioritize this opportunity',proposition:'Proposition to test',desired_behavior:'Desired customer behavior',channel:'Selected channel',primary_metric:'Primary outcome metric',metric_definition:'How the metric is counted',guardrail:'Claim or conduct guardrail',review_timing:'Next review timing',non_goals:'Additional campaign non-goals'};
const experimentLabels:Record<'intervention'|'target_population'|'observation_window'|'metric_source',string>={intervention:'Intervention',target_population:'Target population',observation_window:'Observation window and timezone',metric_source:'Source of observations'};

function parsed<T>(value:string|undefined,fallback:T):T{
  try{return value?{...fallback,...JSON.parse(value)}:fallback;}catch{return fallback;}
}

export function nextCampaignAction(runway:RunwaySnapshot):string{
  const campaign=runway.campaign;
  if(runway.project.accounting_mode==='post_response'&&runway.project.request_allowance===1&&runway.project.status==='needs_review'&&runway.model_requests?.some(r=>r.status==='reported'))return 'Review the first request’s observed usage before releasing up to two more requests within the original deadline.';
  if(runway.terminal_receipts?.length)return 'Review the failed run and its retained usage reservation. Chat is available; no retry is scheduled.';
  if(runway.project.status==='unknown')return 'Reconcile the unresolved worker result before any new work.';
  if(runway.project.status==='paused'||runway.project.status==='budget_exhausted')return 'Owner review is required; this worker grant cannot advance.';
  if(!campaign)return 'Record a versioned brief and experiment rule against the checked audience note.';
  if(campaign.stage==='align'){
    const briefVersion=runway.campaign_revisions?.at(-1)?.version;
    const observations=(runway.campaign_actions||[]).filter(item=>{
      if(item.action!=='manual_observation'||!item.owner_verified)return false;
      const detail=parsed<Record<string,unknown>>(item.payload_json,{});
      return detail.brief_revision===briefVersion&&detail.asset_id===campaign.asset_artifact_id;
    });
    const latestDecision=(runway.campaign_actions||[]).filter(item=>item.action==='internal_decision'&&item.owner_verified&&
      parsed<Record<string,unknown>>(item.payload_json,{}).brief_revision===briefVersion).at(-1);
    if(latestDecision&&parsed<Record<string,unknown>>(latestDecision.payload_json,{}).decision==='collect_evidence'&&
      !observations.some(item=>item.version>latestDecision.version))return 'Wait for a new sourced observation before deciding again.';
    if(observations.length)return 'Review owner-reported evidence and record an internal decision; no launch attribution is established.';
    const adopted=(runway.campaign_actions||[]).filter(item=>item.action==='adopt_revision'&&item.owner_verified)
      .slice().reverse().find(item=>{
        const detail=parsed<Record<string,unknown>>(item.payload_json,{});
        return detail.revision_artifact_id===campaign.asset_artifact_id&&
          detail.brief_revision===runway.campaign_revisions?.at(-1)?.version;
      });
    if(adopted)return 'Approved revision selected for this internal brief. Live launch remains blocked.';
    const latest=runway.reviews.filter(item=>item.artifact_id===campaign.asset_artifact_id).at(-1);
    if(!latest)return 'Review the exact draft against this brief; approval remains internal.';
    if(latest.decision==='revision_requested'||latest.created_at<campaign.updated_at)return campaign.mode==='fixture'?'Create a simulated asset revision, then review the new exact version.':'The draft needs a new asset and fresh review.';
    return campaign.mode==='fixture'?'Align the approved simulated asset.':'Internal review is saved; live launch remains blocked.';
  }
  if(campaign.stage==='launch')return campaign.mode==='fixture'?'Record a fake publisher receipt.':'Live launch is blocked; request a scoped capability before any external action.';
  if(campaign.stage==='measure')return 'Record a sourced observation or collect more evidence under the saved rule.';
  if(campaign.stage==='learn')return 'Record the contextual proposed lesson and revisit condition.';
  return 'Review the decision and proposed lesson before choosing any new assignment.';
}

function CampaignHistory({runway}:{runway:RunwaySnapshot}){
  const actions=runway.campaign_actions||[];
  const currentBriefVersion=runway.campaign_revisions?.at(-1)?.version;
  if(!actions.length)return <p className="runway-reason">Next: review the exact draft against this brief. Launch remains blocked in this pilot.</p>;
  return <details><summary>Campaign decisions and receipts · {actions.length}</summary>
    {actions.map(item=>{
      const detail=parsed<Record<string,unknown>>(item.payload_json,{});
      const label=item.action==='capability_request'?'Blocked capability request':item.action==='internal_decision'?'Owner internal decision':item.action==='internal_lesson'?'Proposed internal lesson':item.action==='adopt_revision'?'Approved linked revision selected':item.action==='manual_observation'?'Owner-reported observation':item.action==='revise_asset'?'SIMULATED asset revision':item.action==='launch'?'SIMULATED fixture launch':item.action==='measure'?'Fixture observation':item.action==='decide'?'Outcome decision':item.action==='learn'?'Proposed lesson':'Alignment';
      return <article key={item.id} className="runway-proposal"><h4>{label} · version {item.version}{detail.brief_revision!==currentBriefVersion?' · historical brief':''}</h4>
        <small>{readableTime(item.created_at)} · {item.status} · recorded actor {item.actor_id.slice(0,12)}…</small>
        {item.action==='launch'&&<p>Fake publisher receipt: {String(detail.receipt||'unknown')}. No external publication.</p>}
        {item.action==='measure'&&<p>{String(detail.value_type||'unknown')} · {String(detail.numerator??'?')}/{String(detail.denominator??'?')} · {String(detail.source||'unknown source')} · {String(detail.attribution_limitations||'')}</p>}
        {item.action==='decide'&&<p>{String(detail.decision||'unknown')} · {String(detail.rationale||'')} {detail.inconclusive?'· insufficient actual sample':''}</p>}
        {item.action==='learn'&&<p>{String(detail.lesson||'')} · Uncertainty: {String(detail.uncertainty||'')} · Revisit: {String(detail.revisit_condition||'')}</p>}
        {item.action==='manual_observation'&&<p>{item.owner_verified?'Verified owner receipt':'Owner receipt unverified'} · {String(detail.value_type||'unknown')} {String(detail.numerator??'?')}/{String(detail.denominator??'?')} · no launch attribution</p>}
        {item.action==='internal_decision'&&<p>{item.owner_verified?'Verified owner receipt':'Owner receipt unverified'} · {String(detail.decision||'unknown')} · {String(detail.rationale||'')} · actual counted denominator {String(detail.actual_sample??'?')}/{String(detail.required_sample??'?')} · {detail.inconclusive?'inconclusive':'owner assessment'} · no work released</p>}
        {item.action==='internal_lesson'&&<p>{item.owner_verified?'Verified owner receipt':'Owner receipt unverified'} · {String(detail.lesson||'')} · Uncertainty: {String(detail.uncertainty||'')} · Revisit: {String(detail.revisit_condition||'')} · decision {String(detail.decision_id||'').slice(0,12)}…</p>}
        {item.action==='capability_request'&&<p>{item.owner_verified?'Verified owner receipt':'Owner receipt unverified'} · {String(detail.blocked_task||'')} · scope: {String(detail.required_scope||'')} · benefit: {String(detail.expected_benefit||'')} · cost {String(detail.cost_status||'unknown')} · no capability granted.</p>}
        {item.action==='adopt_revision'&&<p>{item.owner_verified?'Verified owner receipt':'Owner receipt unverified'} · predecessor {String(detail.predecessor_id||'unknown').slice(0,12)}… · revised asset {String(detail.revision_artifact_id||'unknown').slice(0,12)}… · internal selection only; no launch authorized.</p>}
        {item.action==='align'&&<p>Exact asset and approval linked for this brief version.</p>}
        {item.action==='revise_asset'&&<p>Predecessor {String(detail.predecessor_id||'unknown').slice(0,12)}… · revised asset {String(detail.asset_id||'unknown').slice(0,12)}… · {String(detail.revision_note||'Revision requested')} · owner review still required.</p>}
      </article>;
    })}</details>;
}

function PriorCampaignLessons({audience,campaignId}:{audience:string;campaignId:string}){
  const [lessons,setLessons]=useState<PriorLesson[]>([]),[status,setStatus]=useState<'loading'|'ready'|'unavailable'>('loading');
  useEffect(()=>{
    let current=true;
    setStatus('loading');
    void api<{lessons:PriorLesson[]}>(`/marketing/campaign-lessons?audience=${encodeURIComponent(audience)}&excludeCampaignId=${encodeURIComponent(campaignId)}`)
      .then(result=>{if(current){setLessons(result.lessons);setStatus('ready');}})
      .catch(()=>{if(current){setLessons([]);setStatus('unavailable');}});
    return ()=>{current=false;};
  },[audience,campaignId]);
  return <details><summary>Relevant prior proposed learning · {status==='ready'?lessons.length:status==='loading'?'loading':'unavailable'}</summary>
    <p>Historical owner-reported context for this audience. It does not change this brief, grant work, or establish causality.</p>
    {status==='unavailable'&&<p role="status">The lesson lookup is unavailable. Open the source campaign in Work to inspect its saved receipts.</p>}
    {status==='ready'&&!lessons.length&&<p>No earlier owner-verified lesson matches this audience.</p>}
    {lessons.map(item=><article className="runway-proposal" key={item.action_id}>
      <h4>{item.lesson.lesson}</h4><small>{readableTime(item.created_at)} · campaign {item.campaign_id.slice(0,12)}… · owner-reported {item.lesson.evidence_type||'evidence'} · causality {item.lesson.causality||'not established'}</small>
      <p><b>Original audience:</b> {item.brief.audience}<br/><b>Context:</b> {item.lesson.context}<br/><b>Decision:</b> {item.decision.decision} · {item.decision.rationale}<br/><b>Uncertainty:</b> {item.lesson.uncertainty}<br/><b>Revisit when:</b> {item.lesson.revisit_condition}<br/><b>Suggested next action:</b> {item.lesson.next_action}</p>
      <p><b>Sources:</b> {item.observations.map(observation=>`${observation.source_reference} (${observation.value_type}; ${observation.attribution_limitations})`).join('; ')}</p>
    </article>)}
  </details>;
}

function PriorFixtureLessons({audience,campaignId}:{audience:string;campaignId:string}){
  const [lessons,setLessons]=useState<FixturePriorLesson[]>([]),[status,setStatus]=useState<'loading'|'ready'|'unavailable'>('loading');
  useEffect(()=>{
    let current=true;
    setStatus('loading');
    void api<{lessons:FixturePriorLesson[]}>('/marketing/runway/fixture/lessons',{audience,excludeCampaignId:campaignId})
      .then(result=>{if(current){setLessons(result.lessons);setStatus('ready');}})
      .catch(()=>{if(current){setLessons([]);setStatus('unavailable');}});
    return ()=>{current=false;};
  },[audience,campaignId]);
  return <details><summary>Relevant prior simulated learning · {status==='ready'?lessons.length:status==='loading'?'loading':'unavailable'}</summary>
    <p>Earlier fixture lessons retain their original decision and observations. They do not change this brief or authorize work.</p>
    {status==='unavailable'&&<p role="status">Fixture lesson lookup is unavailable. Open the source campaign to inspect its receipts.</p>}
    {status==='ready'&&!lessons.length&&<p>No earlier simulated lesson matches this audience.</p>}
    {lessons.map(item=><article className="runway-proposal" key={item.action_id}>
      <h4>SIMULATED ONLY · {item.lesson.lesson}</h4>
      <small>{readableTime(item.created_at)} · fixture campaign {item.campaign_id.slice(0,12)}… · audience {item.brief.audience}</small>
      <p><b>Context:</b> {item.lesson.context}<br/><b>Decision:</b> {item.decision.decision} · {item.decision.rationale} · actual fixture sample {item.decision.actual_sample}/{item.decision.required_sample}<br/><b>Uncertainty:</b> {item.lesson.uncertainty}<br/><b>Revisit when:</b> {item.lesson.revisit_condition}<br/><b>Suggested next action:</b> {item.lesson.next_action}</p>
      <p><b>Original observations:</b></p>
      {item.observations.map(observation=><p key={observation.action_id}>{observation.value_type} {observation.numerator}/{observation.denominator} · {observation.metric_definition} · {observation.source} · captured {readableTime(observation.captured_at)} · period {readableTime(observation.period_start)} to {readableTime(observation.period_end)} ({observation.timezone}) · {observation.attribution_limitations}</p>)}
    </article>)}
  </details>;
}

export function CampaignBriefPanel({runway,canControl,onSaved}:{runway:RunwaySnapshot;canControl:boolean;onSaved:(saved:RunwaySnapshot)=>Promise<void>}){
  const source=runway.artifacts.find(item=>item.kind==='audience_note');
  const campaign=runway.campaign;
  const latestChange=campaign?Math.max(campaign.updated_at,...(runway.campaign_actions||[]).map(item=>item.created_at)):0;
  const [brief,setBrief]=useState<Brief>(emptyBrief),[experiment,setExperiment]=useState<Experiment>(emptyExperiment);
  const [editing,setEditing]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const attempt=useRef<{signature:string;id:string}|null>(null);
  useEffect(()=>{
    const note=parsed<{audience?:string;problem?:string}>(source?.content,{});
    setBrief({...emptyBrief,audience:note.audience||'',problem:note.problem||'',...parsed<Partial<Brief>>(campaign?.brief_json,{})});
    setExperiment({...emptyExperiment,...parsed<Partial<Experiment>>(campaign?.experiment_json,{})});
    setEditing(false);setError('');attempt.current=null;
  },[runway.project.id,campaign?.version,source?.digest]);
  if(!source)return <section className="runway-results" aria-label="Campaign workflow"><h3>Campaign workflow</h3><p>Sense is waiting for a checked audience and problem note. No campaign claim or launch is available.</p></section>;
  async function save(event:FormEvent){
    event.preventDefault();
    if(!source||!canControl||busy)return;
    const payload={projectVersion:runway.project.version,version:campaign?.version||0,
      sourceArtifactId:source.id,sourceArtifactDigest:source.digest,brief,
      experiment:{...experiment,minimum_sample:experiment.decision_rule==='learning_only'?0:Number(experiment.minimum_sample)}};
    const signature=runway.project.id+JSON.stringify(payload);
    const id=attempt.current?.signature===signature?attempt.current.id:requestId();
    attempt.current={signature,id};setBusy(true);setError('');
    try{
      const saved=await api<RunwaySnapshot>(`/marketing/runway/${runway.project.id}/campaign-brief`,{requestId:id,...payload});
      attempt.current=null;await onSaved(saved);
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <section className="runway-results" aria-label="Campaign workflow">
    <h3>Campaign workflow · {campaign?.stage||'sense → prioritize'}</h3>
    <p>The source note was saved {readableTime(source.created_at)}. Its audience is a hypothesis; the cited comments are observations, not demand evidence.</p>
    <p><b>Next action:</b> {nextCampaignAction(runway)}<br/><b>Worker status:</b> {runway.project.status.replaceAll('_',' ')}{runway.project.wait_reason?' · '+runway.project.wait_reason:''}<br/><b>Next review:</b> {campaign?(brief.review_timing||'Not specified'):'After the owner records a brief; no date scheduled'}</p>
    <details><summary>Source provenance · {runway.source_metadata?.length||0}</summary>
      {runway.source_metadata?.map(item=><p key={item.url}>{item.url.startsWith('fixture://')?<span>SIMULATED source · {item.url}</span>:<a href={item.url} target="_blank" rel="noopener noreferrer">{item.url}</a>}<br/><small>Captured {item.captured_at?readableTime(item.captured_at):'unknown (legacy source)'} · publication date unknown · saved digest {item.digest.slice(0,12)}…</small></p>)}
    </details>
    {campaign&&<p><b>Priority rationale:</b> {brief.priority_rationale||'Not recorded in this earlier brief'}</p>}
    {campaign?<><p><b>Outcome metric:</b> {brief.primary_metric} · {brief.metric_definition}</p><p><b>Decision rule:</b> {experiment.decision_rule==='learning_only'?'Learn and collect evidence; no continuation threshold yet.':`At least ${experiment.minimum_sample} actual observations before an outcome decision.`}</p><p><b>Authority:</b> {campaign.mode==='fixture'?'isolated fixture · simulated only':(runway.project.scope?.replaceAll('_',' ')||'internal research and drafts')} · spend limit $0 · no live publishing · recorded worker limit {runway.project.max_runs} runs/{runway.project.token_limit.toLocaleString()} admission tokens</p><p><b>Owner receipt:</b> {campaign.mode==='fixture'?'fixture identity only':campaign.owner_verified?'verified by the local host':'unverified; do not use as action authority'}</p><small>Campaign version {campaign.version} · last change {readableTime(latestChange)} · source artifact {campaign.source_artifact_id.slice(0,12)}…</small><CampaignHistory runway={runway}/>{canControl&&campaign.mode==='internal'&&brief.audience&&<PriorCampaignLessons audience={brief.audience} campaignId={campaign.runway_id}/>}{canControl&&campaign.mode==='fixture'&&brief.audience&&<PriorFixtureLessons audience={brief.audience} campaignId={campaign.runway_id}/>}<details><summary>Brief, experiment, and edit history</summary><p><b>Audience:</b> {brief.audience}</p><p><b>Problem:</b> {brief.problem}</p><p><b>Hypothesis:</b> {brief.hypothesis}</p><p><b>Proposition:</b> {brief.proposition}</p><p><b>Desired behavior:</b> {brief.desired_behavior}</p><p><b>Channel:</b> {brief.channel}</p><p><b>Guardrail:</b> {brief.guardrail}</p><p><b>Review timing:</b> {brief.review_timing||'Not specified in this older brief'}</p><p><b>Additional non-goals:</b> {brief.non_goals||'Not specified in this older brief'}</p><p><b>Intervention:</b> {experiment.intervention}</p><p><b>Target population:</b> {experiment.target_population}</p><p><b>Window:</b> {experiment.observation_window}</p><p><b>Metric source:</b> {experiment.metric_source}</p>{runway.campaign_revisions?.map(item=><p key={item.id}>Version {item.version} · {readableTime(item.created_at)} · recorded actor {item.actor_id.slice(0,12)}… · source {item.source_artifact_digest.slice(0,12)}…</p>)}</details></>:<p>Prioritize this opportunity by recording a brief and a measurement rule before treating a draft as a campaign asset.</p>}
    {canControl&&campaign?.mode==='fixture'&&<CampaignFixtureControls runway={runway} onSaved={onSaved}/>}
    {canControl&&campaign?.mode==='internal'&&<CampaignManualObservation runway={runway} onSaved={onSaved}/>}
    {canControl&&campaign?.mode==='internal'&&<CampaignInternalDecision runway={runway} onSaved={onSaved}/>}
    {canControl&&campaign?.mode==='internal'&&<CampaignLaunchReadiness runway={runway} onSaved={onSaved}/>}
    {canControl&&campaign?.mode!=='fixture'&&<button type="button" disabled={busy} onClick={()=>setEditing(value=>!value)}>{editing?'Close brief':'Edit campaign brief'}</button>}
    {editing&&canControl&&<form onSubmit={event=>void save(event)}>
      {(Object.keys(briefLabels) as (keyof Brief)[]).map(key=><label key={key}>{briefLabels[key]}<input required maxLength={600} value={brief[key]} onChange={event=>setBrief(current=>({...current,[key]:event.target.value}))}/></label>)}
      {(Object.keys(experimentLabels) as (keyof typeof experimentLabels)[]).map(key=><label key={key}>{experimentLabels[key]}<input required maxLength={600} value={experiment[key]} onChange={event=>setExperiment(current=>({...current,[key]:event.target.value}))}/></label>)}
      <label>Decision rule<select value={experiment.decision_rule} onChange={event=>setExperiment(current=>({...current,decision_rule:event.target.value as Experiment['decision_rule'],minimum_sample:event.target.value==='learning_only'?0:1}))}><option value="learning_only">Learning only · no threshold</option><option value="minimum_sample">Minimum sample before deciding</option></select></label>
      {experiment.decision_rule==='minimum_sample'&&<label>Minimum observations<input type="number" min={1} max={1000000} required value={experiment.minimum_sample} onChange={event=>setExperiment(current=>({...current,minimum_sample:Number(event.target.value)}))}/></label>}
      <p className="runway-reason">Saving records a versioned internal brief. It does not grant a model turn, approve a draft, or enable launch.</p>
      <button className="primary" disabled={busy}>{busy?'Saving…':'Save campaign brief'}</button>
    </form>}
    {error&&<p className="company-error" role="alert">{error} Refresh the assignment before retrying a version conflict.</p>}
  </section>;
}
