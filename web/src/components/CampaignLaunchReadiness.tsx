import {useRef,useState,type FormEvent} from 'react';
import {api} from '../api';
import {readableTime,requestId,type RunwaySnapshot} from './MarketingPanels';

function fields(value:string):Record<string,unknown>{
  try{return JSON.parse(value) as Record<string,unknown>;}catch{return {};}
}

export function CampaignLaunchReadiness({runway,onSaved}:{runway:RunwaySnapshot;onSaved:(saved:RunwaySnapshot)=>Promise<void>}){
  const campaign=runway.campaign;
  const [blockedTask,setBlockedTask]=useState('Publish the selected campaign draft to a named channel');
  const [requiredScope,setRequiredScope]=useState(''),[expectedBenefit,setExpectedBenefit]=useState('');
  const [costStatus,setCostStatus]=useState<'unknown'|'zero'|'estimated'>('unknown');
  const [costNote,setCostNote]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const attempt=useRef<{signature:string;id:string}|null>(null);
  if(!campaign||campaign.mode!=='internal')return null;
  const activeCampaign=campaign;
  const localApproval=runway.reviews.some(review=>review.artifact_id===campaign.asset_artifact_id&&
    review.artifact_digest===campaign.asset_artifact_digest&&review.decision==='approved'&&
    review.created_at>=campaign.updated_at);
  const adoptedApproval=(runway.campaign_actions||[]).some(item=>item.action==='adopt_revision'&&
    item.owner_verified&&fields(item.payload_json).revision_artifact_id===campaign.asset_artifact_id&&
    fields(item.payload_json).brief_revision===runway.campaign_revisions?.at(-1)?.version);
  const requests=(runway.campaign_actions||[]).filter(item=>item.action==='capability_request');
  async function save(event:FormEvent){
    event.preventDefault();
    if(busy||!activeCampaign.owner_verified)return;
    const payload={blockedTask:blockedTask.trim(),requiredScope:requiredScope.trim(),
      expectedBenefit:expectedBenefit.trim(),costStatus,costNote:costNote.trim()};
    const body={projectVersion:runway.project.version,version:activeCampaign.version,
      action:'capability_request',payload};
    const signature=runway.project.id+JSON.stringify(body);
    const id=attempt.current?.signature===signature?attempt.current.id:requestId();
    attempt.current={signature,id};setBusy(true);setError('');
    try{
      const saved=await api<RunwaySnapshot>(`/marketing/runway/${runway.project.id}/campaign-internal-action`,
        {requestId:id,...body});
      attempt.current=null;setRequiredScope('');setExpectedBenefit('');setCostNote('');
      await onSaved(saved);
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <details className="runway-proposal" aria-label="Launch readiness">
    <summary>Launch readiness · blocked</summary>
    <p>No live publisher is connected to this campaign. Creative approval and internal decisions cannot authorize publication.</p>
    <ul>
      <li><b>Brief and metric:</b> {campaign.owner_verified?'host verified':'owner receipt missing'}</li>
      <li><b>Asset:</b> {campaign.asset_artifact_id?`selected version ${campaign.asset_artifact_digest?.slice(0,12)}…`:'no selected asset'}</li>
      <li><b>Creative review:</b> {localApproval||adoptedApproval?'internal approval recorded':'exact approval missing'}</li>
      <li><b>Link and tracking:</b> not configured for an external destination.</li>
      <li><b>Destination and rollback:</b> no authorized destination or rollback procedure.</li>
      <li><b>Publishing capability:</b> unavailable. No launch checklist can pass in this pilot.</li>
    </ul>
    {requests.map(item=>{const value=fields(item.payload_json);return <article key={item.id}>
      <p><b>Capability request:</b> {String(value.blocked_task||'')} · {readableTime(item.created_at)}</p>
      <small>{item.owner_verified?'Verified owner receipt':'Owner receipt unverified'} · scope {String(value.required_scope||'')} · expected benefit {String(value.expected_benefit||'')} · cost {String(value.cost_status||'unknown')}: {String(value.cost_note||'')} · no capability granted</small>
    </article>;})}
    {campaign.owner_verified?<form onSubmit={event=>void save(event)}>
      <p>Describe a missing capability for later review. Saving this request cannot connect a channel, spend, or publish.</p>
      <label>Blocked task<input required maxLength={1000} value={blockedTask} onChange={event=>setBlockedTask(event.target.value)}/></label>
      <label>Required action and destination scope<input required maxLength={1000} value={requiredScope} onChange={event=>setRequiredScope(event.target.value)} placeholder="Name the channel, account, and exact action needed"/></label>
      <label>Expected benefit<input required maxLength={1000} value={expectedBenefit} onChange={event=>setExpectedBenefit(event.target.value)}/></label>
      <label>Cost status<select value={costStatus} onChange={event=>setCostStatus(event.target.value as typeof costStatus)}><option value="unknown">Unknown</option><option value="zero">Known zero</option><option value="estimated">Estimate only</option></select></label>
      <label>Cost source or uncertainty<input required maxLength={1000} value={costNote} onChange={event=>setCostNote(event.target.value)}/></label>
      <button disabled={busy||!blockedTask.trim()||!requiredScope.trim()||!expectedBenefit.trim()||!costNote.trim()}>{busy?'Saving…':'Record capability request'}</button>
    </form>:<p>Save the brief through the owner Work route before requesting a capability.</p>}
    {error&&<p className="company-error" role="alert">{error} Refresh before retrying a version conflict.</p>}
  </details>;
}
