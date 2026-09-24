import {useEffect,useRef,useState} from 'react';
import {api} from '../api';
import {requestId,type RunwaySnapshot,type RunwayRevisionGrant} from './MarketingPanels';

export function RevisionRunGrant({runway,enabled,onReleased}:{runway:RunwaySnapshot|null|undefined;enabled:boolean;onReleased:()=>Promise<void>}){
  const [accepted,setAccepted]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const [prepared,setPrepared]=useState<RunwayRevisionGrant|null>(null);
  const attempt=useRef<{requestId:string;deadlineAt:number}|null>(null);
  const review=runway?.reviews.at(-1);
  useEffect(()=>{setAccepted(false);setPrepared(null);setError('');attempt.current=null;},[runway?.project.id,runway?.project.version]);
  if(!runway||runway.project.status!=='needs_review'||review?.decision!=='revision_requested'||review.step_id)return null;
  const grants=runway.revision_grants?.filter(g=>g.source_review_id===review.id)??[];
  if(grants.some(g=>g.status==='released'))return null;
  const current=prepared??grants.find(g=>g.status==='held_for_metering'&&g.accounting_mode==='post_response'&&g.deadline_at*1000>Date.now());
  async function run(){
    if(!runway||!review?.owner_verified||!enabled||!accepted||busy)return;
    setBusy(true);setError('');
    try{
      let grant=current;
      if(!grant||grant.deadline_at*1000<=Date.now()){
        if(!attempt.current||attempt.current.deadlineAt*1000<=Date.now())attempt.current={requestId:requestId(),deadlineAt:Date.now()/1000+600};
        const saved=await api<RunwaySnapshot>(`/marketing/runway/${runway.project.id}/revision-grants`,{
          ...attempt.current,version:runway.project.version,reviewId:review.id,artifactId:review.artifact_id,digest:review.artifact_digest,
          budgetMode:'fresh_pilot',maxRuns:1,maxModelRequests:1,tokenLimit:25000,maxActiveSeconds:300,acceptPostResponseAccounting:true,
        });
        grant=saved.revision_grants?.find(g=>g.source_review_id===review.id&&g.status==='held_for_metering'&&g.accounting_mode==='post_response');
        if(!grant)throw new Error('The exact revision grant was not confirmed. Refresh the assignment.');
        setPrepared(grant);
      }
      await api(`/marketing/revision-grants/${grant.id}/release`,{});
      await onReleased();
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <section className="runway-grant" aria-label="Run saved revision">
    <h3>Run this saved change request</h3><blockquote>{review.instruction}</blockquote>
    <p>One Luna request, one revised draft, up to five active minutes. The new grant expires ten minutes after you authorize it.</p>
    <p>25,000 tokens reserved. Actual usage is measured after the response and may exceed that amount. This grant cannot release a second request.</p>
    {!review.owner_verified?<p role="status">This saved feedback needs a matching owner receipt before a revision can run.</p>:<>
      <label><input type="checkbox" checked={accepted} onChange={e=>setAccepted(e.target.checked)}/>I authorize one new revision request with measured usage.</label>
      <button type="button" disabled={!enabled||!accepted||busy} onClick={()=>void run()}>{busy?'Preparing revision…':current?'Release the saved revision grant':'Authorize and run one revision'}</button>
      {!enabled&&<p>The live worker must be enabled before this revision can start.</p>}
    </>}
    {error&&<p role="alert">{error}</p>}
  </section>;
}
