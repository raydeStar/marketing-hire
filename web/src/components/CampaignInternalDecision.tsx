import {useRef,useState,type FormEvent} from 'react';
import {api} from '../api';
import {requestId,type RunwaySnapshot} from './MarketingPanels';

function fields(value:string):Record<string,unknown>{
  try{return JSON.parse(value) as Record<string,unknown>;}catch{return {};}
}

export function CampaignInternalDecision({runway,onSaved}:{runway:RunwaySnapshot;onSaved:(saved:RunwaySnapshot)=>Promise<void>}){
  const campaign=runway.campaign;
  const [decision,setDecision]=useState('pause'),[rationale,setRationale]=useState('');
  const [lesson,setLesson]=useState(''),[context,setContext]=useState(''),[uncertainty,setUncertainty]=useState('');
  const [revisit,setRevisit]=useState(''),[nextAction,setNextAction]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const attempt=useRef<{signature:string;id:string}|null>(null);
  if(!campaign||campaign.mode!=='internal')return null;
  const activeCampaign=campaign;
  const briefVersion=runway.campaign_revisions?.at(-1)?.version;
  const experiment=fields(campaign.experiment_json);
  const actions=runway.campaign_actions||[];
  const observations=actions.filter(item=>{
    if(item.action!=='manual_observation'||!item.owner_verified)return false;
    const value=fields(item.payload_json);
    return value.brief_revision===briefVersion&&value.asset_id===campaign.asset_artifact_id&&
      value.asset_digest===campaign.asset_artifact_digest;
  });
  const actualSample=observations.reduce((sum,item)=>{
    const value=fields(item.payload_json);
    return sum+(value.value_type==='actual'?Number(value.denominator)||0:0);
  },0);
  const requiredSample=Number(experiment.minimum_sample)||0;
  const insufficient=experiment.decision_rule==='minimum_sample'&&actualSample<requiredSample;
  const currentDecision=[...actions].reverse().find(item=>item.action==='internal_decision'&&item.owner_verified&&
    fields(item.payload_json).brief_revision===briefVersion);
  const waitingForEvidence=currentDecision&&fields(currentDecision.payload_json).decision==='collect_evidence'&&
    !observations.some(item=>item.version>currentDecision.version);
  const last=[...actions].reverse().find(item=>item.action!=='capability_request');
  const lessonDecision=campaign.stage==='learn'&&last?.action==='internal_decision'&&last.owner_verified?last:null;
  async function save(action:'internal_decision'|'internal_lesson',payload:Record<string,unknown>){
    if(busy||!activeCampaign.owner_verified)return;
    const body={projectVersion:runway.project.version,version:activeCampaign.version,action,payload};
    const signature=runway.project.id+JSON.stringify(body);
    const id=attempt.current?.signature===signature?attempt.current.id:requestId();
    attempt.current={signature,id};setBusy(true);setError('');
    try{
      const saved=await api<RunwaySnapshot>(`/marketing/runway/${runway.project.id}/campaign-internal-action`,
        {requestId:id,...body});
      attempt.current=null;await onSaved(saved);
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  function recordDecision(event:FormEvent){
    event.preventDefault();
    void save('internal_decision',{decision:insufficient?'collect_evidence':decision,rationale});
  }
  function recordLesson(event:FormEvent){
    event.preventDefault();
    if(!lessonDecision)return;
    void save('internal_lesson',{decisionId:lessonDecision.id,lesson,context,uncertainty,
      revisitCondition:revisit,nextAction});
  }
  return <div className="runway-proposal" aria-label="Internal campaign decision">
    <h4>Decision and learning</h4>
    <p>Owner-reported context only. No launch receipt or causal result exists for this internal campaign.</p>
    <p><b>Evidence for this brief and asset:</b> {observations.length} verified observation{observations.length===1?'':'s'} · {actualSample} actual counted denominator{requiredSample>0?` / ${requiredSample} required by the saved rule`:''}.</p>
    {!campaign.owner_verified&&<p>Save the internal brief through Work before deciding.</p>}
    {campaign.stage==='align'&&campaign.owner_verified&&(
      observations.length===0?<p>Record a sourced owner observation before making an outcome decision.</p>:
      waitingForEvidence?<p>Waiting for a new observation after the collect-evidence decision.</p>:
      <form onSubmit={recordDecision}>
        {insufficient?<p>The recorded minimum sample is unmet. Collect evidence is the only eligible decision.</p>:
          <label>Decision<select value={decision} onChange={event=>setDecision(event.target.value)}>
            <option value="pause">Pause</option><option value="revise">Revise</option><option value="stop">Stop</option>
            <option value="collect_evidence">Collect evidence</option>
            {experiment.decision_rule==='minimum_sample'&&<option value="continue">Continue internal planning</option>}
          </select></label>}
        <label>Reason for this decision<input required maxLength={1000} value={rationale}
          onChange={event=>setRationale(event.target.value)} placeholder="Refer to the observation and its limits"/></label>
        <small>This records a decision only. It cannot release work, purchase, or publish.</small>
        <button disabled={busy||!rationale.trim()}>{busy?'Recording…':'Record internal decision'}</button>
      </form>)}
    {campaign.stage==='learn'&&lessonDecision&&<form onSubmit={recordLesson}>
      <p>Decision: {String(fields(lessonDecision.payload_json).decision||'unknown')} · {String(fields(lessonDecision.payload_json).rationale||'')}</p>
      <label>Proposed lesson<input required maxLength={1000} value={lesson} onChange={event=>setLesson(event.target.value)}/></label>
      <label>Context<input required maxLength={1000} value={context} onChange={event=>setContext(event.target.value)}/></label>
      <label>Uncertainty<input required maxLength={1000} value={uncertainty} onChange={event=>setUncertainty(event.target.value)}/></label>
      <label>Revisit when<input required maxLength={1000} value={revisit} onChange={event=>setRevisit(event.target.value)}/></label>
      <label>Proposed next action<input required maxLength={1000} value={nextAction} onChange={event=>setNextAction(event.target.value)}/></label>
      <small>A proposed lesson does not rewrite agent instructions or authorize work.</small>
      <button disabled={busy}>{busy?'Saving…':'Save proposed lesson'}</button>
    </form>}
    {campaign.stage==='learn'&&!lessonDecision&&<p>The current decision lacks a verified owner receipt. Reopen the assignment before saving a lesson.</p>}
    {campaign.stage==='complete'&&<p>The proposed lesson is saved with its decision and observation IDs. New evidence can reopen review without erasing history.</p>}
    {error&&<p className="company-error" role="alert">{error} Refresh before retrying a version conflict.</p>}
  </div>;
}
