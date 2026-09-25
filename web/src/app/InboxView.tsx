import {useState} from 'react';
import {ChevronRight,CircleCheckBig,ExternalLink,FileText,Megaphone,NotebookPen,ShieldCheck} from 'lucide-react';
import {api} from '../api';
import {needsDecision} from '../components/WorkBoard';
import {publicLink,type MarketingDraft,type MarketingState} from '../components/MarketingPanels';
import {PageHead,plain,useAttempt} from './shared';

export type InboxItem={id:string;kind:'review'|'draft'|'task'|'brief';title:string;detail:string};

/** Everything that is waiting on the owner, in one list. */
export function inboxItems(state:MarketingState|null):InboxItem[]{
  if(!state||state.canConfigure===false)return [];
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

function DraftCard({draft,canDecide,onRefresh}:{draft:MarketingDraft;canDecide:boolean;onRefresh:()=>Promise<void>}){
  const [working,setWorking]=useState<string|null>(null),[error,setError]=useState('');
  const attempt=useAttempt();
  const link=publicLink(draft.destination);
  async function decide(decision:'approved'|'rejected'){
    if(!canDecide||working)return;setWorking(decision);setError('');
    try{
      await api(`/marketing/drafts/${draft.id}/decision`,{requestId:attempt.id(`${draft.id}:${draft.revision}:${draft.digest}:${decision}`),decision,revision:draft.revision,digest:draft.digest});
      attempt.done();await onRefresh();
    }catch(cause){setError((cause as Error).message);await onRefresh().catch(()=>{});}
    finally{setWorking(null);}
  }
  return <article className="fe-draft" aria-label={`Draft ${draft.id}`}>
    <div className="fe-card-head"><div><span className="fe-pill accent">{draft.channel}</span></div>{link?<a href={link} target="_blank" rel="noopener noreferrer">Where it would go <ExternalLink size={13}/></a>:<small>{draft.destination}</small>}</div>
    <div className="fe-draft-text">{draft.content}</div>
    <p className="fe-draft-why"><strong>Why this draft:</strong> {draft.rationale}</p>
    <div className="fe-decision-bar">
      <small>Approving records your decision. It doesn’t post or contact anyone.</small>
      <button type="button" disabled={!canDecide||!!working} onClick={()=>void decide('rejected')}>{working==='rejected'?'Saving…':'Reject'}</button>
      <button type="button" className="primary" disabled={!canDecide||!!working} onClick={()=>void decide('approved')}>{working==='approved'?'Saving…':'Approve'}</button>
    </div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </article>;
}

export function InboxView({state,canWrite,onOpenTask,onOpenReview,onOpenBrief,onRefresh}:{state:MarketingState;canWrite:boolean;onOpenTask:(id:string)=>void;onOpenReview:(artifactId:string)=>void;onOpenBrief:()=>void;onRefresh:()=>Promise<void>}){
  const items=inboxItems(state);
  const drafts=state.drafts.filter(item=>item.status==='pending');
  const others=items.filter(item=>item.kind!=='draft');
  const name=state.employee.name||'Marketing';
  const icon={review:Megaphone,task:ShieldCheck,brief:NotebookPen,draft:FileText};
  return <div className="fe-page"><div className="fe-page-inner narrow">
    <PageHead title="Inbox" subtitle={items.length?`${items.length} thing${items.length===1?'':'s'} waiting for your call. ${name} keeps working on everything else.`:`Decisions from ${name} land here.`}/>
    {!items.length&&<div className="fe-empty fe-caught-up"><CircleCheckBig size={40}/><h3>You’re all caught up</h3><p>When {name} needs a decision, like approving a draft or unblocking a task, it shows up here.</p></div>}
    {others.length>0&&<section className="fe-inbox-group" aria-label="Needs your decision"><h3>Needs your decision</h3><div className="fe-row-list">{others.map(item=>{const Icon=icon[item.kind];return <button type="button" className="fe-row" key={item.id}
      onClick={()=>item.kind==='task'?onOpenTask(item.id.slice(5)):item.kind==='review'?onOpenReview(item.id.slice(7)):onOpenBrief()}>
      <span className={'fe-row-icon '+(item.kind==='review'?'accent':'attn')}><Icon size={19}/></span><span className="fe-row-body"><strong>{item.title}</strong><small>{item.detail}</small></span><ChevronRight size={17}/></button>;})}</div></section>}
    {drafts.length>0&&<section className="fe-inbox-group" aria-label="Draft approvals"><h3>Drafts to approve</h3>{drafts.map(draft=><DraftCard key={draft.id} draft={draft} canDecide={canWrite} onRefresh={onRefresh}/>)}</section>}
  </div></div>;
}
