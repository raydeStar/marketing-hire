import {useEffect,useRef,useState,type FormEvent} from 'react';
import {api} from '../api';
import {readableTime,requestId,type RunwaySnapshot} from './MarketingPanels';
import {CampaignFixtureControls} from './CampaignFixtureControls';

type Brief={audience:string;problem:string;hypothesis:string;proposition:string;desired_behavior:string;channel:string;primary_metric:string;metric_definition:string;guardrail:string};
type Experiment={intervention:string;target_population:string;observation_window:string;metric_source:string;decision_rule:'learning_only'|'minimum_sample';minimum_sample:number};
const emptyBrief:Brief={audience:'',problem:'',hypothesis:'',proposition:'',desired_behavior:'',channel:'',primary_metric:'',metric_definition:'',guardrail:''};
const emptyExperiment:Experiment={intervention:'',target_population:'',observation_window:'',metric_source:'',decision_rule:'learning_only',minimum_sample:0};
const briefLabels:Record<keyof Brief,string>={audience:'Audience',problem:'Customer problem',hypothesis:'Opportunity hypothesis',proposition:'Proposition to test',desired_behavior:'Desired customer behavior',channel:'Selected channel',primary_metric:'Primary outcome metric',metric_definition:'How the metric is counted',guardrail:'Claim or conduct guardrail'};
const experimentLabels:Record<'intervention'|'target_population'|'observation_window'|'metric_source',string>={intervention:'Intervention',target_population:'Target population',observation_window:'Observation window and timezone',metric_source:'Source of observations'};

function parsed<T>(value:string|undefined,fallback:T):T{
  try{return value?{...fallback,...JSON.parse(value)}:fallback;}catch{return fallback;}
}

function CampaignHistory({runway}:{runway:RunwaySnapshot}){
  const actions=runway.campaign_actions||[];
  const currentBriefVersion=runway.campaign_revisions?.at(-1)?.version;
  if(!actions.length)return <p className="runway-reason">Next: review the exact draft against this brief. Launch remains blocked in this pilot.</p>;
  return <details><summary>Campaign decisions and receipts · {actions.length}</summary>
    {actions.map(item=>{
      const detail=parsed<Record<string,unknown>>(item.payload_json,{});
      const label=item.action==='launch'?'SIMULATED fixture launch':item.action==='measure'?'Observation':item.action==='decide'?'Outcome decision':item.action==='learn'?'Proposed lesson':'Alignment';
      return <article key={item.id} className="runway-proposal"><h4>{label} · version {item.version}{detail.brief_revision!==currentBriefVersion?' · historical brief':''}</h4>
        <small>{readableTime(item.created_at)} · {item.status} · recorded actor {item.actor_id.slice(0,12)}…</small>
        {item.action==='launch'&&<p>Fake publisher receipt: {String(detail.receipt||'unknown')}. No external publication.</p>}
        {item.action==='measure'&&<p>{String(detail.value_type||'unknown')} · {String(detail.numerator??'?')}/{String(detail.denominator??'?')} · {String(detail.source||'unknown source')} · {String(detail.attribution_limitations||'')}</p>}
        {item.action==='decide'&&<p>{String(detail.decision||'unknown')} · {String(detail.rationale||'')} {detail.inconclusive?'· insufficient actual sample':''}</p>}
        {item.action==='learn'&&<p>{String(detail.lesson||'')} · Uncertainty: {String(detail.uncertainty||'')} · Revisit: {String(detail.revisit_condition||'')}</p>}
        {item.action==='align'&&<p>Exact asset and approval linked for this brief version.</p>}
      </article>;
    })}</details>;
}

export function CampaignBriefPanel({runway,canControl,onSaved}:{runway:RunwaySnapshot;canControl:boolean;onSaved:(saved:RunwaySnapshot)=>Promise<void>}){
  const source=runway.artifacts.find(item=>item.kind==='audience_note');
  const campaign=runway.campaign;
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
    <details><summary>Source provenance · {runway.source_metadata?.length||0}</summary>
      {runway.source_metadata?.map(item=><p key={item.url}>{item.url.startsWith('fixture://')?<span>SIMULATED source · {item.url}</span>:<a href={item.url} target="_blank" rel="noopener noreferrer">{item.url}</a>}<br/><small>Captured {item.captured_at?readableTime(item.captured_at):'unknown (legacy source)'} · publication date unknown · saved digest {item.digest.slice(0,12)}…</small></p>)}
    </details>
    {campaign?<><p><b>Outcome metric:</b> {brief.primary_metric} · {brief.metric_definition}</p><p><b>Decision rule:</b> {experiment.decision_rule==='learning_only'?'Learn and collect evidence; no continuation threshold yet.':`At least ${experiment.minimum_sample} actual observations before an outcome decision.`}</p><p><b>Authority:</b> {campaign.mode==='fixture'?'isolated fixture · simulated only':'internal research and drafts only'} · spend limit $0 · no live publishing</p><p><b>Owner receipt:</b> {campaign.mode==='fixture'?'fixture identity only':campaign.owner_verified?'verified by the local host':'unverified; do not use as action authority'}</p><small>Campaign version {campaign.version} · last change {readableTime(campaign.updated_at)} · source artifact {campaign.source_artifact_id.slice(0,12)}…</small><CampaignHistory runway={runway}/><details><summary>Brief, experiment, and edit history</summary><p><b>Audience:</b> {brief.audience}</p><p><b>Problem:</b> {brief.problem}</p><p><b>Hypothesis:</b> {brief.hypothesis}</p><p><b>Proposition:</b> {brief.proposition}</p><p><b>Desired behavior:</b> {brief.desired_behavior}</p><p><b>Channel:</b> {brief.channel}</p><p><b>Guardrail:</b> {brief.guardrail}</p><p><b>Intervention:</b> {experiment.intervention}</p><p><b>Target population:</b> {experiment.target_population}</p><p><b>Window:</b> {experiment.observation_window}</p><p><b>Metric source:</b> {experiment.metric_source}</p>{runway.campaign_revisions?.map(item=><p key={item.id}>Version {item.version} · {readableTime(item.created_at)} · recorded actor {item.actor_id.slice(0,12)}… · source {item.source_artifact_digest.slice(0,12)}…</p>)}</details></>:<p>Prioritize this opportunity by recording a brief and a measurement rule before treating a draft as a campaign asset.</p>}
    {canControl&&campaign?.mode==='fixture'&&<CampaignFixtureControls runway={runway} onSaved={onSaved}/>}
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
