import {useState} from 'react';
import Markdown from 'react-markdown';
import {ExternalLink} from 'lucide-react';
import {api} from '../api';
import {needsDecision} from '../components/WorkBoard';
import {publicLink,type MarketingDraft,type MarketingState} from '../components/MarketingPanels';
import {Dialog,plain,useAttempt} from './shared';
import {PublishBar} from './PublishingView';
import {SocialImageDialog} from './SocialImage';
import {draftText,keepLineBreaks} from './draftText';
import {CampaignPill} from './campaigns';

export type InboxItem={id:string;kind:'review'|'draft'|'task'|'brief';title:string;detail:string};

/** Everything that is waiting on the owner, in one list. */
/** Blog posts, newsletters and emails are written in Markdown; short posts are plain text. */
const longForm=(channel:string)=>/blog|newsletter|email|buttondown|article|hirezero/i.test(channel);
function reasonHint(channel:string){
  const name=channel.toLowerCase();
  if(name.includes('linkedin'))return 'e.g. Too salesy for LinkedIn; lead with the customer story';
  if(name==='x'||name.includes('twitter')||name.includes('bluesky')||name.includes('threads'))return 'e.g. Cut it to one sharp line; drop the hashtags';
  if(name.includes('reddit')||name.includes('hacker'))return 'e.g. Reads like an ad; ask the community a real question';
  if(longForm(name))return 'e.g. Open with the problem, and make the call to action one link';
  return 'e.g. Shorter, and say who it’s for in the first line';
}

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
    items.push({id:'draft:'+draft.id,kind:'draft',title:`Draft for ${draft.channel}`,detail:plain(draftText(draft)).slice(0,120)});
  for(const task of state.tasks.filter(needsDecision))
    items.push({id:'task:'+task.id,kind:'task',title:task.title,detail:plain(task.blocker||task.next_action)||'Needs your decision.'});
  return items;
}

const channelChoices=['LinkedIn','X','Bluesky','Mastodon','Threads','Email','Blog'];

/** One idea, every channel: the employee writes a separate draft per channel, each needing approval. */
function Versions({draft,onAsk,onClose}:{draft:MarketingDraft;onAsk:(text:string)=>void;onClose:()=>void}){
  const others=channelChoices.filter(item=>item.toLowerCase()!==draft.channel.trim().toLowerCase());
  const [chosen,setChosen]=useState<string[]>(others.slice(0,2)),[note,setNote]=useState('');
  function ask(){
    const text=`Please adapt draft #${draft.id} (${draft.channel}) into new drafts for ${chosen.join(', ')}. Keep the core idea and the facts; make each native to its channel and within its limit; set the tracking link's utm_source to the channel. `+
      `Add each with hire draft add and a rationale starting "Adapted from draft #${draft.id}", then give me an open button for each.${note.trim()?` Also: ${note.trim()}`:''}`;
    onAsk(text);onClose();
  }
  return <Dialog title="Versions for other channels" onClose={onClose}><div className="fe-form">
    <p className="fe-muted">The employee writes a separate draft for each channel, native to it and within its limits, and marks it “adapted from #{draft.id}”. Each version needs your approval; nothing is posted.</p>
    <fieldset className="fe-day-picker"><legend>Channels</legend>{others.map(item=><label key={item} className={chosen.includes(item)?'active':''}>
      <input type="checkbox" checked={chosen.includes(item)} onChange={event=>setChosen(event.target.checked?[...chosen,item]:chosen.filter(entry=>entry!==item))}/>{item}</label>)}</fieldset>
    <label>Anything to change? <span className="fe-muted">(optional)</span><input value={note} maxLength={300} onChange={event=>setNote(event.target.value)} placeholder="e.g. shorter and more casual for X"/></label>
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button type="button" className="primary" disabled={chosen.length===0} onClick={ask}>Ask for {chosen.length} version{chosen.length===1?'':'s'}</button></footer>
  </div></Dialog>;
}

export function DraftCard({draft,canDecide,onRefresh,onAsk,onOpen}:{draft:MarketingDraft;canDecide:boolean;onRefresh:()=>Promise<void>;onAsk?:(text:string)=>void;onOpen?:(key:string)=>void}){
  const [working,setWorking]=useState<string|null>(null),[error,setError]=useState(''),[why,setWhy]=useState(''),[versions,setVersions]=useState(false),[image,setImage]=useState(false);
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
    <div className="fe-card-head"><div className="fe-draft-labels"><span className="fe-pill accent">{draft.channel}</span><CampaignPill itemKey={'draft:'+draft.id} onOpen={onOpen}/></div>{link?<a href={link} target="_blank" rel="noopener noreferrer">Where it would go <ExternalLink size={13}/></a>:<small>{draft.destination}</small>}</div>
    {longForm(draft.channel)?<div className="fe-draft-text md fe-prose"><Markdown components={{img:()=>null}}>{keepLineBreaks(draftText(draft))}</Markdown></div>:<div className="fe-draft-text">{draftText(draft)}</div>}
    <p className="fe-draft-why"><strong>Why this draft:</strong> {draft.rationale}</p>
    {canDecide&&!decided&&<label className="fe-draft-feedback">Your reason <span className="fe-muted">(optional; the employee learns from it)</span>
      <input maxLength={600} value={why} onChange={event=>setWhy(event.target.value)} placeholder={reasonHint(draft.channel)}/></label>}
    <div className="fe-decision-bar">
      <small>{decided?`Decision recorded: ${draft.status}.`:'Approving records your decision. It doesn’t post or contact anyone.'}</small>
      <button type="button" disabled={!canDecide||!!working||decided} onClick={()=>void decide('rejected')}>{working==='rejected'?'Saving…':'Reject'}</button>
      <button type="button" className="primary" disabled={!canDecide||!!working||decided} onClick={()=>void decide('approved')}>{working==='approved'?'Saving…':'Approve'}</button>
    </div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <PublishBar draft={draft} owner={canDecide} onRefresh={onRefresh}/>
    {onAsk&&canDecide&&draft.status!=='rejected'&&draft.status!=='withdrawn'&&<button type="button" className="fe-ghost fe-versions" onClick={()=>setVersions(true)}>Versions for other channels…</button>}
    {versions&&onAsk&&<Versions draft={draft} onAsk={onAsk} onClose={()=>setVersions(false)}/>}
    {canDecide&&draft.status!=='rejected'&&draft.status!=='withdrawn'&&draft.channel.toLowerCase()!=='email'&&<button type="button" className="fe-ghost fe-versions" onClick={()=>setImage(true)}>Make an image…</button>}
    {image&&<SocialImageDialog content={draft.content} channel={draft.channel} onClose={()=>setImage(false)}/>}
  </article>;
}
