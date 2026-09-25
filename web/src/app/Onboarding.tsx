import {useState} from 'react';
import {ArrowLeft,Check,Globe,LoaderCircle,MessagesSquare,PencilLine,Sparkles,X} from 'lucide-react';
import {api} from '../api';
import {requestId,type MarketingProfile,type MarketingState} from '../components/MarketingPanels';
import {BriefEditor,briefKeys,type BriefFields} from './BriefEditor';
import {Conversation} from './ChatView';
import {useAttempt} from './shared';

type Step='welcome'|'import'|'talk'|'review'|'done';

const shape=`Reply with ONLY a JSON object in a \`\`\`json code block, using these keys (plain text values, empty string if unknown):
{"display_name": "a name for you, the marketing employee", "product_summary": "what we sell, 2-3 sentences", "audience": "who it is for; say if it is an assumption", "goals": "what matters in the next few weeks", "voice": "how we sound", "claims": "what we can truthfully claim, and what is unproven", "examples": "our best existing work and what to learn from it", "channels": "where our audience is and where we show up", "guardrails": "what we must never do or say", "ethos": "our beliefs and values in a short paragraph", "north_star": "the one number that shows marketing is working, with a target and date if known", "objectives": "2-3 outcomes for this quarter, one per line", "positioning": "who it is for, their problem, what they use instead, and why us, in one or two sentences", "proof_points": "facts we can back up, one per line", "competitors": "main alternatives, one per line", "non_goals": "what we are deliberately not doing now, one per line"}`;

export function importPrompt(links:string){
  return `Onboarding: please read our website and social profiles below and figure out who we are: offer, audience, voice and ethos. Only use what the pages actually say; mark guesses as guesses.\n\n${links.trim()}\n\n${shape}`;
}
const interviewPrompt=`Onboarding: let's get you up to speed on our business. Interview me one question at a time (what we sell, who it's for, our one north-star metric and target, this quarter's objectives, why customers pick us over alternatives and what proves it, how we sound, what we can claim, and what we're not doing or is off limits). Keep each question short. When you have enough, tell me to press "Draft my brief".`;
const summarizePrompt=`Thanks. Now turn our onboarding conversation into a brand brief. ${shape}`;

/** Pull the brief object out of a model reply, tolerating prose around it. */
export const goalKeys=['north_star','objectives','positioning','proof_points','competitors','non_goals'] as const;
export type GoalDraft=Partial<Record<typeof goalKeys[number],string>>;
export function parseBrief(reply:string):(Partial<BriefFields>&{ethos?:string}&GoalDraft)|null{
  const fenced=/```(?:json)?\s*([\s\S]*?)```/i.exec(reply)?.[1];
  const candidates=[fenced,reply.slice(reply.indexOf('{'),reply.lastIndexOf('}')+1)].filter((text):text is string=>!!text&&text.trim().startsWith('{'));
  for(const text of candidates){
    try{
      const data=JSON.parse(text) as Record<string,unknown>;
      const result:Record<string,string>={};
      for(const key of [...briefKeys,'ethos',...goalKeys]){const value=data[key];if(typeof value==='string'&&value.trim())result[key]=value.trim();else if(Array.isArray(value))result[key]=value.filter(item=>typeof item==='string').join('\n');}
      if(Object.keys(result).length)return result;
    }catch{}
  }
  return null;
}

export function Onboarding({state,canWrite,onClose,onRefresh}:{state:MarketingState;canWrite:boolean;onClose:()=>void;onRefresh:()=>Promise<void>}){
  const [step,setStep]=useState<Step>('welcome'),[links,setLinks]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const [draft,setDraft]=useState<(Partial<BriefFields>&{ethos?:string}&GoalDraft)|undefined>(),[saveGoals,setSaveGoals]=useState(true),[saveEthos,setSaveEthos]=useState(true),[writeSoul,setWriteSoul]=useState(true),[packaged,setPackaged]=useState<string[]>([]);
  const [kickoff,setKickoff]=useState<string|undefined>(),[from,setFrom]=useState<Step>('welcome');
  const wikiAttempt=useAttempt();
  const name=state.employee.name||'Marketing';
  async function ask(content:string){
    setBusy(true);setError('');
    try{
      const result=await api<{status:string;reply?:string|null}>('/marketing/chat',{requestId:requestId(),content});
      await onRefresh().catch(()=>{});
      const parsed=result.reply?parseBrief(result.reply):null;
      if(!parsed)throw new Error(`${name} replied, but not with a brief I could read. Try again, or fill it in yourself.`);
      setDraft(parsed);setFrom(step);setStep('review');
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function saved(profile:MarketingProfile){
    // Package what was learned: the brief is saved; the ethos becomes a wiki page and the employee's SOUL.md.
    const ethos=draft?.ethos?.trim()||'';
    const section=(title:string,text?:string)=>text?.trim()?`## ${title}\n${text.trim()}`:'';
    const done=['your business brief'];
    if(saveEthos&&ethos){
      const body=[ethos,section('How we sound',profile.voice),section('What we will never do',profile.guardrails),section('What we can claim',profile.claims)].filter(Boolean).join('\n\n');
      const fields={id:null,version:0,scope:'company',scopeId:'company',title:'Company ethos',body,kind:'policy',status:'active'};
      try{await api('/company-wiki',{...fields,requestId:wikiAttempt.id(JSON.stringify(fields))},'PUT');wikiAttempt.done();done.push('the “Company ethos” wiki page');}catch{/* The brief is saved; the page can be added from the wiki. */}
    }
    if(writeSoul&&(ethos||profile.voice.trim())){
      try{
        const organization=await api<{directory:{agents:{id:string;runtimeKey:string|null}[]}}>('/organization');
        const member=organization.directory.agents.find(agent=>agent.runtimeKey==='marketing');
        const content=[`# ${profile.display_name}: soul`,section('Ethos',ethos),section('Voice',profile.voice),section('Boundaries',profile.guardrails),section('What we can truthfully claim',profile.claims)].filter(Boolean).join('\n\n')+'\n';
        if(member){await api(`/organization/agents/${member.id}/files`,{requestId:requestId(),name:'SOUL.md',version:0,content},'PUT');done.push(`${profile.display_name}’s SOUL.md`);}
      }catch{/* An existing SOUL.md is left untouched. */}
    }
    // Goals and positioning become the Objectives record, unless the owner already set one.
    const lines=(text?:string)=>(text||'').split(/\r?\n|;/).map(line=>line.replace(/^[-*\d.)\s]+/,'').trim()).filter(Boolean);
    if(saveGoals&&draft&&goalKeys.some(key=>draft[key]?.trim())){
      try{
        const current=await api<{revision:{version:number}}>('/objectives');
        if(current.revision.version===0){
          const content={northStar:draft.north_star?.trim()?{name:draft.north_star.trim().slice(0,120),metric:null,target:null,unit:'',by:null,why:''}:null,
            objectives:lines(draft.objectives).slice(0,5).map(title=>({title:title.slice(0,200),keyResults:[]})),
            positioning:{forWho:profile.audience.slice(0,400),problem:'',alternatives:lines(draft.competitors).join(', ').slice(0,600),whyUs:(draft.positioning||'').trim().slice(0,600),proofPoints:lines(draft.proof_points).slice(0,10).map(point=>point.slice(0,300))},
            competitors:lines(draft.competitors).slice(0,10).map(name=>({name:name.slice(0,80),note:''})),currentFocus:profile.goals.slice(0,1000),nonGoals:lines(draft.non_goals).slice(0,12).map(item=>item.slice(0,200))};
          await api('/objectives',{expectedVersion:0,content},'PUT');done.push('your objectives and positioning');
        }
      }catch{/* The brief is saved; objectives can be set from the Library. */}
    }
    setPackaged(done);await onRefresh();setStep('done');
  }
  return <div className="fe-onboarding" role="dialog" aria-modal="true" aria-label="Onboarding">
    <header className="fe-onboarding-head">
      {step!=='welcome'&&step!=='done'?<button type="button" className="fe-ghost" onClick={()=>{setError('');setStep(step==='review'?from:'welcome');}}><ArrowLeft size={16}/> Back</button>:<span/>}
      <ol className="fe-steps" aria-label="Progress">{['Choose','Share','Review','Done'].map((label,index)=>{const at=({welcome:0,import:1,talk:1,review:2,done:3} as const)[step];return <li key={label} className={index<at?'done':index===at?'current':''}>{index<at?<Check size={12}/>:index+1}<span>{label}</span></li>;})}</ol>
      <button type="button" className="fe-icon-button" aria-label="Close onboarding" onClick={onClose}><X size={19}/></button>
    </header>
    <div className="fe-onboarding-body">
      {step==='welcome'&&<div className="fe-onboarding-center">
        <h1>Let’s get <span className="fe-gradient">{name}</span> up to speed.</h1>
        <p className="fe-lead">A good employee starts by learning who you are. Pick whichever is easiest. You’ll review and edit everything before it’s saved.</p>
        <div className="fe-choice-grid">
          <button type="button" className="fe-choice" disabled={!canWrite} onClick={()=>setStep('import')}><Globe size={24}/><strong>Learn from my website & socials</strong><small>Paste your links. {name} reads them and drafts your brand brief.</small></button>
          <button type="button" className="fe-choice" disabled={!canWrite} onClick={()=>{setKickoff(interviewPrompt);setStep('talk');}}><MessagesSquare size={24}/><strong>Talk it through</strong><small>{name} interviews you, one question at a time.</small></button>
          <button type="button" className="fe-choice" onClick={()=>{setDraft({});setFrom('welcome');setStep('review');}}><PencilLine size={24}/><strong>Fill it in myself</strong><small>A short form with examples. About two minutes.</small></button>
        </div>
        {!canWrite&&<p className="fe-muted">{name} is offline, so only the form is available right now.</p>}
      </div>}
      {step==='import'&&<form className="fe-onboarding-center fe-form" onSubmit={event=>{event.preventDefault();if(links.trim())void ask(importPrompt(links));}}>
        <h1>Where can {name} learn about you?</h1>
        <p className="fe-lead">Your website, LinkedIn, X, Instagram, YouTube, a recent launch post: anything public that sounds like you.</p>
        <label className="marketing-sr-only" htmlFor="onboarding-links">Links</label>
        <textarea id="onboarding-links" rows={6} value={links} onChange={event=>setLinks(event.target.value)} placeholder={'https://yourcompany.com\nhttps://linkedin.com/company/yourcompany\nhttps://x.com/yourhandle'} disabled={busy}/>
        {error&&<p className="fe-alert" role="alert">{error}</p>}
        <footer><button type="button" className="fe-ghost" onClick={()=>{setDraft({});setFrom('import');setStep('review');}}>Skip to the form</button><button className="primary" disabled={busy||!links.trim()}>{busy?<><LoaderCircle size={16} className="fe-spin"/> Reading your pages…</>:<><Sparkles size={16}/> Draft my brief</>}</button></footer>
        {busy&&<p className="fe-muted">This can take a minute while {name} reads each page.</p>}
      </form>}
      {step==='talk'&&<div className="fe-onboarding-talk">
        <div className="fe-onboarding-chat"><Conversation state={state} canWrite={canWrite} prefill={kickoff} autoSend onPrefillUsed={()=>setKickoff(undefined)} onRefresh={onRefresh} compact/></div>
        {error&&<p className="fe-alert" role="alert">{error}</p>}
        <footer className="fe-onboarding-foot"><small>Answer as much as you like. You can edit the result.</small><button type="button" className="primary" disabled={busy||!canWrite} onClick={()=>void ask(summarizePrompt)}>{busy?<><LoaderCircle size={16} className="fe-spin"/> Drafting…</>:<><Sparkles size={16}/> Draft my brief</>}</button></footer>
      </div>}
      {step==='review'&&<div className="fe-onboarding-center wide">
        <h1>{draft&&Object.keys(draft).length?`Here’s what ${name} learned.`:'Tell us about your business.'}</h1>
        <p className="fe-lead">{draft&&Object.keys(draft).length?'Edit anything that’s off. This brief is what your employee reads before every piece of work.':'Short answers are fine. You can refine this any time from Library → Company.'}</p>
        {draft?.ethos&&<div className="fe-card fe-ethos"><h3>Your ethos</h3><textarea rows={4} aria-label="Ethos" value={draft.ethos} onChange={event=>setDraft({...draft,ethos:event.target.value})}/><label className="fe-check"><input type="checkbox" checked={saveEthos} onChange={event=>setSaveEthos(event.target.checked)}/> Also publish it as the “Company ethos” wiki page</label><label className="fe-check"><input type="checkbox" checked={writeSoul} onChange={event=>setWriteSoul(event.target.checked)}/> Write {name}’s SOUL.md from it (skipped if one exists)</label></div>}
        {draft&&goalKeys.some(key=>draft[key]?.trim())&&<div className="fe-card fe-ethos"><h3>Goals & positioning</h3>
          {([['north_star','North star'],['objectives','This quarter’s objectives'],['positioning','Positioning'],['proof_points','Proof points'],['competitors','Competitors'],['non_goals','Not doing']] as const).map(([key,label])=><label key={key}>{label}<textarea rows={key==='north_star'?1:2} value={draft[key]||''} onChange={event=>setDraft({...draft,[key]:event.target.value})}/></label>)}
          <label className="fe-check"><input type="checkbox" checked={saveGoals} onChange={event=>setSaveGoals(event.target.checked)}/> Save as Objectives & positioning (skipped if already set)</label></div>}
        <BriefEditor profile={state.profile} evidenceEnabled={state.businessBriefEvidenceEnabled===true} canEdit initial={draft} startEditing onSaved={saved} onCancel={()=>setStep('welcome')}/>
      </div>}
      {step==='done'&&<div className="fe-onboarding-center">
        <span className="fe-done-mark"><Check size={30}/></span>
        <h1>{name} is ready to work.</h1>
        <p className="fe-lead">Saved {packaged.join(', ')}. Start with a morning meeting and {name} will propose today’s priorities.</p>
        <footer><button type="button" className="primary" onClick={onClose}>Go to chat</button></footer>
      </div>}
    </div>
  </div>;
}
