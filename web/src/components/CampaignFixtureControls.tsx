import {useRef,useState,type FormEvent} from 'react';
import {api} from '../api';
import {requestId,type RunwaySnapshot} from './MarketingPanels';

function fields(value:string):Record<string,unknown>{try{return JSON.parse(value) as Record<string,unknown>;}catch{return {};}}

export function CampaignFixtureControls({runway,onSaved}:{runway:RunwaySnapshot;onSaved:(saved:RunwaySnapshot)=>Promise<void>}){
  const campaign=runway.campaign;
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const [numerator,setNumerator]=useState(0),[denominator,setDenominator]=useState(1);
  const [valueType,setValueType]=useState<'actual'|'estimated'>('actual');
  const [timezone,setTimezone]=useState('America/Denver');
  const [limitations,setLimitations]=useState('Synthetic observation; no causal inference');
  const [decision,setDecision]=useState('pause'),[rationale,setRationale]=useState('Small fixture sample; review before continuing');
  const [lesson,setLesson]=useState(''),[context,setContext]=useState(''),[uncertainty,setUncertainty]=useState('');
  const [revisit,setRevisit]=useState(''),[nextAction,setNextAction]=useState('');
  const attempt=useRef<{signature:string;id:string}|null>(null);
  const observationId=useRef(requestId());
  if(!campaign||campaign.mode!=='fixture')return null;
  const campaignVersion=campaign.version;
  const brief=fields(campaign.brief_json),experiment=fields(campaign.experiment_json);
  const asset=runway.artifacts.find(item=>item.id===campaign.asset_artifact_id);
  const approval=runway.reviews.find(item=>item.artifact_id===asset?.id&&item.decision==='approved');
  const actions=runway.campaign_actions||[];
  const briefVersion=runway.campaign_revisions?.at(-1)?.version;
  const observations=actions.filter(item=>item.action==='measure'&&fields(item.payload_json).brief_revision===briefVersion);
  const actualSample=observations.reduce((total,item)=>{const value=fields(item.payload_json);return total+(value.value_type==='actual'?Number(value.denominator)||0:0);},0);
  const insufficient=experiment.decision_rule==='minimum_sample'&&actualSample<Number(experiment.minimum_sample);
  async function act(action:string,payload:Record<string,unknown>){
    if(busy)return;
    const body={projectVersion:runway.project.version,version:campaignVersion,action,payload};
    const signature=runway.project.id+JSON.stringify(body);
    const id=attempt.current?.signature===signature?attempt.current.id:requestId();
    attempt.current={signature,id};setBusy(true);setError('');
    try{
      const saved=await api<RunwaySnapshot>(`/marketing/runway/${runway.project.id}/campaign-action`,{requestId:id,...body});
      attempt.current=null;if(action==='measure')observationId.current=requestId();await onSaved(saved);
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function measure(event:FormEvent){
    event.preventDefault();
    const now=Math.floor(Date.now()/1000);
    await act('measure',{observation_id:observationId.current,source:experiment.metric_source,
      captured_at:now,period_start:now-3600,period_end:now-30,timezone,
      metric_definition:brief.metric_definition,attribution_limitations:limitations,
      numerator,denominator,value_type:valueType});
  }
  return <div className="runway-proposal" aria-label="Simulated campaign controls">
    <h4>Simulated campaign controls</h4>
    <p>Disposable fixture only. These controls cannot publish or spend.</p>
    {campaign.stage==='align'&&<><p>{approval?'An exact draft approval is saved.':'Approve the exact draft below before alignment.'}</p>
      <button disabled={busy||!approval||!asset} onClick={()=>void act('align',{review_id:approval?.id,asset_id:asset?.id,asset_digest:asset?.digest})}>Align approved fixture draft</button></>}
    {campaign.stage==='launch'&&<button disabled={busy} onClick={()=>void act('launch',{destination:'fixture://publisher',checklist:{asset:'checked',link:'not_applicable',tracking:'fixture_only',destination:'fixture_only',rollback:'fixture_reset'}})}>Record fake launch</button>}
    {campaign.stage==='measure'&&<><form onSubmit={event=>void measure(event)}>
      <p><b>Metric:</b> {String(brief.metric_definition||'unknown')} · source {String(experiment.metric_source||'unknown')} · actual sample {actualSample}/{String(experiment.minimum_sample??0)}</p>
      <label>Numerator<input type="number" min={0} max={100000000} required value={numerator} onChange={event=>setNumerator(Number(event.target.value))}/></label>
      <label>Denominator<input type="number" min={0} max={100000000} required value={denominator} onChange={event=>setDenominator(Number(event.target.value))}/></label>
      <label>Value type<select value={valueType} onChange={event=>setValueType(event.target.value as 'actual'|'estimated')}><option value="actual">Actual fixture observation</option><option value="estimated">Estimate</option></select></label>
      <label>Timezone<input required maxLength={500} value={timezone} onChange={event=>setTimezone(event.target.value)}/></label>
      <label>Attribution limits<input required maxLength={500} value={limitations} onChange={event=>setLimitations(event.target.value)}/></label>
      <small>Measurement window: the preceding hour; capture time is recorded on save. No missing history is inferred.</small>
      <button disabled={busy||numerator>denominator}>Record fixture observation</button>
    </form>
    {observations.length>0&&<form onSubmit={event=>{event.preventDefault();void act('decide',{decision:insufficient?'collect_evidence':decision,rationale});}}>
      {insufficient?<p>Insufficient actual sample. The only available decision is collect evidence.</p>:<label>Decision<select value={decision} onChange={event=>setDecision(event.target.value)}>{['pause','revise','stop','collect_evidence',...(experiment.decision_rule==='minimum_sample'?['continue']:[])].map(item=><option key={item} value={item}>{item.replaceAll('_',' ')}</option>)}</select></label>}
      <label>Rationale<input required maxLength={1000} value={rationale} onChange={event=>setRationale(event.target.value)}/></label>
      <button disabled={busy}>Record fixture decision</button>
    </form>}</>}
    {campaign.stage==='learn'&&<form onSubmit={event=>{event.preventDefault();void act('learn',{lesson,context,uncertainty,revisit_condition:revisit,next_action:nextAction});}}>
      <label>Proposed lesson<input required maxLength={1000} value={lesson} onChange={event=>setLesson(event.target.value)}/></label>
      <label>Context<input required maxLength={1000} value={context} onChange={event=>setContext(event.target.value)}/></label>
      <label>Uncertainty<input required maxLength={1000} value={uncertainty} onChange={event=>setUncertainty(event.target.value)}/></label>
      <label>Revisit when<input required maxLength={1000} value={revisit} onChange={event=>setRevisit(event.target.value)}/></label>
      <label>Next action<input required maxLength={1000} value={nextAction} onChange={event=>setNextAction(event.target.value)}/></label>
      <button disabled={busy}>Save proposed lesson</button>
    </form>}
    {campaign.stage==='complete'&&<p>Fixture loop complete. The lesson remains a proposal with evidence and uncertainty attached.</p>}
    {error&&<p className="company-error" role="alert">{error} Refresh before retrying a version conflict.</p>}
  </div>;
}
