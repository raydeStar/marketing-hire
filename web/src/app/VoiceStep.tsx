import {useState} from 'react';
import {Download,LoaderCircle,Mic} from 'lucide-react';
import {api} from '../api';

/** Posts written one per block: kept apart by --- lines, or by blank lines when there are none. */
export function splitPosts(text:string){
  const blocks=/\n\s*---\s*\n/.test(text)?text.split(/\n\s*---\s*\n/):text.split(/\n\s*\n/);
  return blocks.map(block=>block.trim()).filter(block=>block.length>=20);
}

/** Sounds like you: past posts and three true stories become the Voice and Stories pages the employee writes from. */
export function VoiceStep({name,onDone}:{name:string;onDone:(saved:string|null)=>void}){
  const [handle,setHandle]=useState(''),[posts,setPosts]=useState(''),[started,setStarted]=useState(''),[customer,setCustomer]=useState(''),[opinion,setOpinion]=useState('');
  const [pulling,setPulling]=useState(false),[saving,setSaving]=useState(false),[error,setError]=useState('');
  const count=splitPosts(posts).length;
  async function pull(){
    if(!handle.trim()||pulling)return;setPulling(true);setError('');
    try{const found=await api<{posts:string[]}>('/voice/import',{handle:handle.trim()});
      if(!found.posts.length)setError('No posts of your own were found there. Paste a few instead.');
      else setPosts(current=>[current.trim(),...found.posts].filter(Boolean).join('\n\n---\n\n'));}
    catch(cause){setError((cause as Error).message);}finally{setPulling(false);}
  }
  async function save(event:React.FormEvent){
    event.preventDefault();if(saving)return;setSaving(true);setError('');
    try{const saved=await api<{posts:number;stories:number}>('/voice',{posts:splitPosts(posts),started,customer,opinion});
      onDone([saved.posts?`${saved.posts} of your posts as the Voice page`:'',saved.stories?`${saved.stories} true ${saved.stories===1?'story':'stories'}`:''].filter(Boolean).join(' and '));}
    catch(cause){setError((cause as Error).message);}finally{setSaving(false);}
  }
  const ready=count>0||[started,customer,opinion].some(text=>text.trim().length>=10);
  return <form className="fe-onboarding-center wide fe-form fe-voice-step" onSubmit={event=>void save(event)} aria-label="Sounds like you">
    <h1>Make {name} sound like you.</h1>
    <p className="fe-lead">Ten to twenty of your own posts, and three stories only you can tell. {name} writes each piece from the closest of them and is graded against them.</p>
    <div className="fe-voice-pull">
      <label>Your Bluesky or Mastodon<input value={handle} onChange={event=>setHandle(event.target.value)} placeholder="you.bsky.social or @you@mastodon.social" disabled={pulling}/></label>
      <button type="button" className="fe-ghost" disabled={!handle.trim()||pulling} onClick={()=>void pull()}>{pulling?<><LoaderCircle size={15} className="fe-spin"/> Reading…</>:<><Download size={15}/> Pull my posts</>}</button>
    </div>
    <label>Your past posts <span className="fe-muted">({count} {count===1?'post':'posts'}; one per block, with --- or a blank line between)</span>
      <textarea rows={9} value={posts} onChange={event=>setPosts(event.target.value)} placeholder={'Paste a post you wrote…\n\n---\n\nAnd another…'}/></label>
    <div className="fe-voice-stories">
      <label>How did you start? <span className="fe-muted">(what happened, not the mission statement)</span><textarea rows={3} maxLength={1500} value={started} onChange={event=>setStarted(event.target.value)} placeholder="e.g. We lost a launch because nobody had time to write about it."/></label>
      <label>A customer moment you remember<textarea rows={3} maxLength={1500} value={customer} onChange={event=>setCustomer(event.target.value)} placeholder="e.g. A founder approved three drafts from her phone between meetings."/></label>
      <label>Something you believe that most in your field don’t act on<textarea rows={3} maxLength={1500} value={opinion} onChange={event=>setOpinion(event.target.value)} placeholder="e.g. An AI employee should ask first. Autopilot is the wrong goal for anything in your name."/></label>
    </div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={()=>onDone(null)}>Skip for now</button><button className="primary" disabled={saving||!ready}>{saving?<><LoaderCircle size={16} className="fe-spin"/> Saving…</>:<><Mic size={16}/> Save my voice</>}</button></footer>
  </form>;
}
