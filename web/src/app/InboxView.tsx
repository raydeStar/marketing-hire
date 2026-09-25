import {useState} from 'react';
import {ExternalLink} from 'lucide-react';
import {api} from '../api';
import {needsDecision} from '../components/WorkBoard';
import {publicLink,type MarketingDraft,type MarketingState} from '../components/MarketingPanels';
import {plain,useAttempt} from './shared';
import {PublishBar} from './PublishingView';

export type InboxItem={id:string;kind:'review'|'draft'|'task'|'brief';title:string;detail:string};

/** Everything that is waiting on the owner, in one list. */
export function inboxItems(state:MarketingState|null):InboxItem[]{
  // Decisions are the owner's; managers see the same list as what the owner still has to do.
  if(!state||(state.access?!['owner','manager'].includes(state.access):state.canConfigure===false))return [];
  const runway=state.runway;
  const items:InboxItem[]=[];
  if(!state.profile.product_summary.trim()||!state.profile.goals.trim())
    items.push({id:'brief',kind:'brief',title:'Finish your business brief',detail:'Marketing needs your offer and goals before it can plan useful work.'});
  if(runway?.project.status==='needs_review')for(const artifact of runway.artifacts.filter(item=>['post_angles','revision_angles'].includes(item.kind)&&!runway.reviews.some(review=>review.artifact_id===item.id)))
    items.push({id:'review:'+artifact.id,kind:'review',title:artifact.kind==='revision_angles'?'Review revised post angles':'Review draft post angles',detail:'Approve, reject or ask for changes. Approving never publishes.'});
  for(const draft of state.drafts.filter(item=>item.status==='pending'))
    items.push({id:'draft:'+draft.id,kind:'draft',title:`Draft for ${draft.channel}`,detail:plain(draft.content).slice(0,120)});
  for(const task of state.tasks.filter(needsDecision))
    items.push({id:'task:'+task.id,kind:'task',title:task.title,detail:plain(task.blocker||task.next_action)||'Needs your decision.'});
  return items;
}

export function DraftCard({draft,canDecide,onRefresh}:{draft:MarketingDraft;canDecide:boolean;onRefresh:()=>Promise<void>}){
  const [working,setWorking]=useState<string|null>(null),[error,setError]=useState(''),[why,setWhy]=useState('');
  const attempt=useAttempt();
  const link=publicLink(draft.destination);
  // A draft opened in the work window stays open after the decision; it can't be decided twice.
  const decided=draft.status!=='pending';
  async function decide(decision:'approved'|'rejected'){
    if(!canDecide||working||decided)return;setWorking(decision);setError('');
    try{
      await api(`/marketing/drafts/${draft.id}/decision`,{requestId:attempt.id(`${draft.id}:${draft.revision}:${draft.digest}:${decision}`),decision,revision:draft.revision,digest:draft.digest});
      attempt.done();
      // The verdict and the reason go to the employee's memory; the decision itself is already recorded.
      await api('/feedback',{key:`draft:${draft.id}`,title:`${draft.channel} draft #${draft.id}`,verdict:decision,note:why.trim()}).catch(()=>{});
      setWhy('');await onRefresh();
    }catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}
    finally{setWorking(null);}
  }
  return <article className="fe-draft" aria-label={`Draft ${draft.id}`}>
    <div className="fe-card-head"><div><span className="fe-pill accent">{draft.channel}</span></div>{link?<a href={link} target="_blank" rel="noopener noreferrer">Where it would go <ExternalLink size={13}/></a>:<small>{draft.destination}</small>}</div>
    <div className="fe-draft-text">{draft.content}</div>
    <p className="fe-draft-why"><strong>Why this draft:</strong> {draft.rationale}</p>
    {canDecide&&!decided&&<label className="fe-draft-feedback">Your reason <span className="fe-muted">(optional; the employee learns from it)</span>
      <input maxLength={600} value={why} onChange={event=>setWhy(event.target.value)} placeholder="e.g. Too salesy for LinkedIn; lead with the customer story"/></label>}
    <div className="fe-decision-bar">
      <small>{decided?`Decision recorded: ${draft.status}.`:'Approving records your decision. It doesn’t post or contact anyone.'}</small>
      <button type="button" disabled={!canDecide||!!working||decided} onClick={()=>void decide('rejected')}>{working==='rejected'?'Saving…':'Reject'}</button>
      <button type="button" className="primary" disabled={!canDecide||!!working||decided} onClick={()=>void decide('approved')}>{working==='approved'?'Saving…':'Approve'}</button>
    </div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <PublishBar draft={draft} owner={canDecide} onRefresh={onRefresh}/>
  </article>;
}
