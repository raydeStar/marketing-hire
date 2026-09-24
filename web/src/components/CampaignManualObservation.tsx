import {useRef,useState,type FormEvent} from 'react';
import {api} from '../api';
import {publicLink,requestId,type RunwaySnapshot} from './MarketingPanels';

function data(value:string):Record<string,unknown>{
  try{return JSON.parse(value) as Record<string,unknown>;}catch{return {};}
}

export function CampaignManualObservation({runway,onSaved}:{runway:RunwaySnapshot;onSaved:(saved:RunwaySnapshot)=>Promise<void>}){
  const campaign=runway.campaign;
  const [sourceReference,setSourceReference]=useState(''),[interpretation,setInterpretation]=useState('');
  const [periodStart,setPeriodStart]=useState(''),[periodEnd,setPeriodEnd]=useState('');
  const [numerator,setNumerator]=useState(0),[denominator,setDenominator]=useState(1);
  const [valueType,setValueType]=useState<'actual'|'estimated'>('actual');
  const [limitations,setLimitations]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const attempt=useRef<{signature:string;id:string;body:{projectVersion:number;version:number;observation:Record<string,unknown>}}|null>(null),observationId=useRef(requestId());
  if(!campaign||campaign.mode!=='internal')return null;
  const activeCampaign=campaign;
  const brief=data(activeCampaign.brief_json),experiment=data(activeCampaign.experiment_json);
  const timezone=Intl.DateTimeFormat().resolvedOptions().timeZone||'UTC';
  const observations=(runway.campaign_actions||[]).filter(item=>item.action==='manual_observation');
  async function save(event:FormEvent){
    event.preventDefault();
    if(busy||!activeCampaign.owner_verified)return;
    const start=Date.parse(periodStart)/1000,end=Date.parse(periodEnd)/1000;
    if(!Number.isFinite(start)||!Number.isFinite(end)||start>=end||end>Date.now()/1000){
      setError('Enter a finished measurement period in your browser’s local timezone.');return;
    }
    const observation={observation_id:observationId.current,source:String(experiment.metric_source||''),
      source_reference:sourceReference.trim(),interpretation:interpretation.trim(),
      captured_at:Date.now()/1000,period_start:start,period_end:end,timezone,
      metric_definition:String(brief.metric_definition||''),attribution_limitations:limitations.trim(),
      numerator,denominator,value_type:valueType};
    const signature=runway.project.id+JSON.stringify({projectVersion:runway.project.version,
      version:activeCampaign.version,...observation,captured_at:null});
    const prior=attempt.current?.signature===signature?attempt.current:null;
    const body=prior?.body||{projectVersion:runway.project.version,version:activeCampaign.version,observation};
    const id=prior?.id||requestId();
    attempt.current={signature,id,body};setBusy(true);setError('');
    try{
      const saved=await api<RunwaySnapshot>(`/marketing/runway/${runway.project.id}/campaign-observation`,{requestId:id,...body});
      attempt.current=null;observationId.current=requestId();setSourceReference('');setInterpretation('');setLimitations('');
      await onSaved(saved);
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <details className="runway-proposal"><summary>Owner-reported observations · {observations.length}</summary>
    <p>Record a source you inspected. This is context for the internal brief; no campaign launch is recorded, and attribution to this draft is unknown.</p>
    {observations.map(item=>{const value=data(item.payload_json),reference=String(value.source_reference||'unknown');return <article key={item.id}>
      <p><b>{String(value.value_type||'unknown')} observation:</b> {String(value.numerator??'?')}/{String(value.denominator??'?')} · {String(value.metric_definition||'unknown metric')}</p>
      <small>{item.owner_verified?'Verified owner receipt':'Owner receipt unverified'} · source {publicLink(reference)?<a href={publicLink(reference)!} target="_blank" rel="noopener noreferrer">{reference}</a>:reference} · period {new Date(Number(value.period_start)*1000).toLocaleString()} to {new Date(Number(value.period_end)*1000).toLocaleString()} ({String(value.timezone||'unknown')}) · captured {new Date(Number(value.captured_at)*1000).toLocaleString()}</small>
      <p><b>Interpretation:</b> {String(value.interpretation||'')}<br/><b>Attribution limits:</b> {String(value.attribution_limitations||'')}</p>
    </article>;})}
    {campaign.owner_verified?<form onSubmit={event=>void save(event)}>
      <label>Source record or URL<input required maxLength={500} value={sourceReference} onChange={event=>setSourceReference(event.target.value)} placeholder="Report name, record ID, or HTTPS URL"/></label>
      <label>Measurement period starts<input type="datetime-local" required value={periodStart} onChange={event=>setPeriodStart(event.target.value)}/></label>
      <label>Measurement period ends<input type="datetime-local" required value={periodEnd} onChange={event=>setPeriodEnd(event.target.value)}/></label>
      <small>Times use this browser’s {timezone} timezone. Capture time is recorded when you save.</small>
      <label>Numerator<input type="number" min={0} max={100000000} required value={numerator} onChange={event=>setNumerator(Number(event.target.value))}/></label>
      <label>Denominator<input type="number" min={0} max={100000000} required value={denominator} onChange={event=>setDenominator(Number(event.target.value))}/></label>
      <label>Value type<select value={valueType} onChange={event=>setValueType(event.target.value as 'actual'|'estimated')}><option value="actual">Actual count</option><option value="estimated">Estimate</option></select></label>
      <label>What you observed and how you interpret it<input required maxLength={1000} value={interpretation} onChange={event=>setInterpretation(event.target.value)}/></label>
      <label>Attribution limits<input required maxLength={500} value={limitations} onChange={event=>setLimitations(event.target.value)} placeholder="Why this cannot establish campaign causality"/></label>
      <button disabled={busy||numerator>denominator}>{busy?'Recording…':'Record owner observation'}</button>
    </form>:<p>This brief has no matching private owner receipt. Save it through Work before recording observations.</p>}
    {error&&<p className="company-error" role="alert">{error} Refresh before retrying a version conflict.</p>}
  </details>;
}
