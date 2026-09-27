import {createContext,useCallback,useContext,useEffect,useState} from 'react';
import {ChevronRight,Megaphone,Pencil,Plus} from 'lucide-react';
import {api} from '../api';
import type {MarketingState} from '../components/MarketingPanels';
import type {Library} from './library';
import {CampaignPackage} from './CampaignPackage';

export type CampaignStatus='planned'|'active'|'paused'|'done';
export type Campaign={id:string;name:string;goal:string;starts:string|null;ends:string|null;channels:string[];status:CampaignStatus;moves:string|null;planWikiId:string|null;createdBy:string;createdAt:string;updatedAt:string};
export type CampaignLedger={version:number;campaigns:Campaign[];items:Record<string,string>};
export type CampaignFields={name:string;goal:string;starts:string;ends:string;channels:string[];status:CampaignStatus;moves:string};
export type CampaignBook={
  ledger:CampaignLedger|null;reload:()=>Promise<void>;
  of:(key:string)=>Campaign|undefined;keysOf:(id:string)=>string[];
  assign:(key:string,id:string|null)=>Promise<void>;save:(id:string|null,fields:CampaignFields)=>Promise<Campaign>;fromPlan:(wikiId:string)=>Promise<Campaign>;
};

/** The workspace's campaigns and which work belongs to each. Changes move files between Library folders, so the Library reloads after. */
export function useCampaignBook(enabled:boolean,onFilesMoved:()=>void):CampaignBook{
  const [ledger,setLedger]=useState<CampaignLedger|null>(null);
  const reload=useCallback(async()=>{if(enabled)setLedger(await api<CampaignLedger>('/campaigns'));},[enabled]);
  useEffect(()=>{void reload().catch(()=>{});const timer=setInterval(()=>{if(document.visibilityState==='visible')void reload().catch(()=>{});},60000);return()=>clearInterval(timer);},[reload]);
  const current=async()=>ledger??await api<CampaignLedger>('/campaigns');
  const of=(key:string)=>{const id=ledger?.items[key];return id?ledger?.campaigns.find(campaign=>campaign.id===id):undefined;};
  const keysOf=(id:string)=>Object.entries(ledger?.items||{}).filter(([,owner])=>owner===id).map(([key])=>key);
  async function assign(key:string,id:string|null){const next=await api<CampaignLedger>('/campaigns/assign',{expectedVersion:(await current()).version,key,campaignId:id});setLedger(next);onFilesMoved();}
  async function save(id:string|null,fields:CampaignFields){
    const body={expectedVersion:(await current()).version,...fields,starts:fields.starts||null,ends:fields.ends||null,moves:fields.moves||null};
    const result=await api<{campaign:Campaign;ledger:CampaignLedger}>(id?'/campaigns/'+id:'/campaigns',body,id?'PUT':'POST');
    setLedger(result.ledger);onFilesMoved();return result.campaign;
  }
  async function fromPlan(wikiId:string){const result=await api<{campaign:Campaign;ledger:CampaignLedger}>('/campaigns/from-plan',{expectedVersion:(await current()).version,wikiId});setLedger(result.ledger);onFilesMoved();return result.campaign;}
  return {ledger,reload,of,keysOf,assign,save,fromPlan};
}

const CampaignContext=createContext<CampaignBook|null>(null);
export const CampaignsProvider=CampaignContext.Provider;
export function useCampaigns(){return useContext(CampaignContext);}

const statusLabel:Record<CampaignStatus,string>={planned:'Planned',active:'Active',paused:'Paused',done:'Done'};
const statusTone:Record<CampaignStatus,string>={planned:'',active:'live',paused:'warn',done:''};
const order:Record<CampaignStatus,number>={active:0,planned:1,paused:2,done:3};
const day=(value:string)=>new Date(value+'T12:00:00');
/** "Sep 26–30", "Sep 26 – Oct 3", "from Sep 26", "until Sep 30". */
export function campaignDates(campaign:Pick<Campaign,'starts'|'ends'>){
  const short=(value:string)=>day(value).toLocaleDateString(undefined,{month:'short',day:'numeric'});
  const {starts,ends}=campaign;
  if(starts&&ends){const a=day(starts),b=day(ends);return a.getMonth()===b.getMonth()?`${short(starts)}–${b.getDate()}`:`${short(starts)} – ${short(ends)}`;}
  return starts?'from '+short(starts):ends?'until '+short(ends):'No dates';
}
/** How far along it is: tasks and drafts done, and what waits on the owner. */
export function campaignProgress(keys:string[],state:MarketingState){
  const tasks=state.tasks.filter(task=>keys.includes('task:'+task.id));
  const drafts=state.drafts.filter(draft=>keys.includes('draft:'+draft.id));
  const done=tasks.filter(task=>task.status==='done').length+drafts.filter(draft=>draft.status==='posted'||draft.status==='approved').length;
  const waiting=tasks.filter(task=>task.status==='needs_you').length+drafts.filter(draft=>draft.status==='pending').length;
  return {tasks,drafts,done,waiting,total:tasks.length+drafts.length};
}
const progressLine=(progress:ReturnType<typeof campaignProgress>)=>progress.total?`${progress.done} of ${progress.total} done`+(progress.waiting?` · ${progress.waiting} waiting on you`:''):'Nothing filed yet';

/** Top of Work: the campaigns the employee is following right now. */
export function CampaignStrip({state,onOpen}:{state:MarketingState;onOpen:(key:string)=>void}){
  const book=useCampaigns();
  const active=(book?.ledger?.campaigns||[]).filter(campaign=>campaign.status==='active');
  if(!book||active.length===0)return null;
  return <div className="fe-following" aria-label="Following">{active.slice(0,3).map(campaign=>{
    const progress=campaignProgress(book.keysOf(campaign.id),state);
    return <button type="button" key={campaign.id} className="fe-following-row" onClick={()=>onOpen('campaign:'+campaign.id)}>
      <Megaphone size={15}/><span className="fe-following-label">Following</span><strong>{campaign.name}</strong>
      <span className="fe-muted">{campaignDates(campaign)} · {progressLine(progress)}</span><ChevronRight size={15}/>
    </button>;
  })}</div>;
}

/** Work → Campaigns: every named campaign, running ones first. */
export function CampaignRows({state,owner,onOpen}:{state:MarketingState;owner:boolean;onOpen:(key:string)=>void}){
  const book=useCampaigns();
  const campaigns=[...(book?.ledger?.campaigns||[])].sort((a,b)=>order[a.status]-order[b.status]||(a.starts||'').localeCompare(b.starts||''));
  return <>
    {campaigns.map(campaign=>{const progress=campaignProgress(book!.keysOf(campaign.id),state);return <button type="button" key={campaign.id} className="fe-list-row" onClick={()=>onOpen('campaign:'+campaign.id)}>
      <span className="fe-row-icon"><Megaphone size={16}/></span>
      <span className="fe-list-main"><strong>{campaign.name}</strong><small>{campaignDates(campaign)} · {progressLine(progress)}{campaign.goal?' · '+campaign.goal:''}</small></span>
      <span className={'fe-status-chip '+statusTone[campaign.status]}>{statusLabel[campaign.status]}</span><ChevronRight size={16}/>
    </button>;})}
  </>;
}

/** The last row of Work → Campaigns: start a named campaign. */
export function NewCampaignRow({owner,onOpen}:{owner:boolean;onOpen:(key:string)=>void}){
  const book=useCampaigns();
  if(!owner||!book)return null;
  return <button type="button" className="fe-list-row" onClick={()=>onOpen('campaign:new')}><span className="fe-row-icon"><Plus size={16}/></span>
    <span className="fe-list-main"><strong>New campaign</strong><small>A named push with a goal, dates and channels; everything made for it is kept together.</small></span><ChevronRight size={16}/></button>;
}

const blank:CampaignFields={name:'',goal:'',starts:'',ends:'',channels:[],status:'active',moves:''};
/** Create or edit a campaign. */
export function CampaignForm({campaign,onSaved,onCancel}:{campaign?:Campaign;onSaved:(campaign:Campaign)=>void;onCancel:()=>void}){
  const book=useCampaigns();
  const [fields,setFields]=useState<CampaignFields>(campaign?{name:campaign.name,goal:campaign.goal,starts:campaign.starts||'',ends:campaign.ends||'',channels:campaign.channels,status:campaign.status,moves:campaign.moves||''}:blank);
  const [channels,setChannels]=useState((campaign?.channels||[]).join(', '));
  const [working,setWorking]=useState(false),[error,setError]=useState('');
  const set=(change:Partial<CampaignFields>)=>setFields(current=>({...current,...change}));
  async function submit(event:React.FormEvent){
    event.preventDefault();if(!book||working)return;setWorking(true);setError('');
    try{onSaved(await book.save(campaign?.id??null,{...fields,channels:channels.split(',').map(item=>item.trim()).filter(Boolean)}));}
    catch(cause){setError((cause as Error).message);}finally{setWorking(false);}
  }
  return <form className="fe-form fe-campaign-form" onSubmit={event=>void submit(event)}>
    <label>Name<input autoFocus required maxLength={60} value={fields.name} onChange={event=>set({name:event.target.value})} placeholder="e.g. Launch week"/></label>
    <label>Goal<textarea rows={2} maxLength={400} value={fields.goal} onChange={event=>set({goal:event.target.value})} placeholder="What it should achieve, in a sentence"/></label>
    <div className="fe-form-row">
      <label>Starts<input type="date" value={fields.starts} onChange={event=>set({starts:event.target.value})}/></label>
      <label>Ends<input type="date" value={fields.ends} onChange={event=>set({ends:event.target.value})}/></label>
      <label>Status<select value={fields.status} onChange={event=>set({status:event.target.value as CampaignStatus})}>{(Object.keys(statusLabel) as CampaignStatus[]).map(status=><option key={status} value={status}>{statusLabel[status]}</option>)}</select></label>
    </div>
    <label>Channels <span className="fe-muted">(comma separated)</span><input maxLength={500} value={channels} onChange={event=>setChannels(event.target.value)} placeholder="LinkedIn, X, email, blog"/></label>
    <label>What it moves <span className="fe-muted">(optional)</span><input maxLength={120} value={fields.moves} onChange={event=>set({moves:event.target.value})} placeholder="e.g. Qualified conversations"/></label>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onCancel}>Cancel</button><button className="primary" disabled={working||fields.name.trim().length<2}>{working?'Saving…':campaign?'Save campaign':'Create campaign'}</button></footer>
  </form>;
}

const taskStatus:Record<string,string>={needs_you:'Needs you',ready:'Assigned',working:'In progress',done:'Done',paused:'Paused',blocked:'Blocked'};
const draftStatus:Record<string,string>={pending:'Waiting for approval',approved:'Approved',posted:'Posted',rejected:'Rejected',withdrawn:'Withdrawn'};

/** One campaign: what it's for, how far along it is, and everything filed with it. */
export function CampaignPage({campaign,state,library,owner,onOpen,onRefresh,onChat}:{campaign:Campaign;state:MarketingState;library:Library;owner:boolean;onOpen:(key:string)=>void;onRefresh:()=>Promise<void>;onChat?:(text:string)=>void}){
  const book=useCampaigns()!;
  const [editing,setEditing]=useState(false);
  const keys=book.keysOf(campaign.id);
  const progress=campaignProgress(keys,state);
  if(editing)return <CampaignForm campaign={campaign} onSaved={()=>setEditing(false)} onCancel={()=>setEditing(false)}/>;
  const percent=progress.total?Math.round(progress.done/progress.total*100):0;
  return <article className="fe-doc fe-campaign">
    <dl className="fe-facts">
      <div><dt>Status</dt><dd><span className={'fe-status-chip '+statusTone[campaign.status]}>{statusLabel[campaign.status]}</span></dd></div>
      <div><dt>Dates</dt><dd>{campaignDates(campaign)}</dd></div>
      <div><dt>Channels</dt><dd>{campaign.channels.length?campaign.channels.join(', '):'Any'}</dd></div>
      {campaign.moves&&<div><dt>Moves</dt><dd>{campaign.moves}</dd></div>}
    </dl>
    {campaign.goal&&<p className="fe-campaign-goal">{campaign.goal}</p>}
    <div className="fe-campaign-progress" aria-label="Assignments and drafts progress"><span className="fe-bar"><i style={{width:percent+'%'}}/></span><small>Assignments and drafts · {progressLine(progress)}</small></div>
    <div className="fe-actions">
      {owner&&<button type="button" onClick={()=>setEditing(true)}><Pencil size={14}/> Edit</button>}
      {campaign.planWikiId&&<button type="button" className="fe-ghost" onClick={()=>onOpen('wiki:'+campaign.planWikiId)}>Open the plan</button>}
    </div>
    <CampaignPackage campaign={campaign} keys={keys} state={state} library={library} owner={owner} onOpen={onOpen} onRefresh={onRefresh} onChat={onChat}/>
    <section aria-label="Campaign hypothesis"><h3>How we will judge this</h3><p>{campaign.moves||'Choose a metric and a review condition before running a test.'}</p><p className="fe-outcome-note">Prepared or approved work does not establish a campaign result.</p><button type="button" className="fe-link" onClick={()=>onOpen('section:scorecard')}>Review measured results →</button></section>
  </article>;
}

/** An item's campaign, changeable by anyone who works on tasks: a compact select in the window header. */
export function CampaignPicker({itemKey,canChange}:{itemKey:string;canChange:boolean}){
  const book=useCampaigns();
  const [working,setWorking]=useState(false),[error,setError]=useState('');
  if(!book?.ledger||!/^(task|draft|wiki|media|pagecopy|exp):/.test(itemKey)||(book.ledger.campaigns.length===0))return null;
  const current=book.of(itemKey);
  if(!canChange)return current?<span className="fe-pill" title="Campaign">{current.name}</span>:null;
  return <label className="fe-campaign-picker" title={error||'Campaign'}><Megaphone size={14}/>
    <select aria-label="Campaign" disabled={working} value={current?.id||''} onChange={event=>{setWorking(true);setError('');void book.assign(itemKey,event.target.value||null).catch(cause=>setError((cause as Error).message)).finally(()=>setWorking(false));}}>
      <option value="">Always-on</option>
      {book.ledger.campaigns.filter(campaign=>campaign.status!=='done'||campaign.id===current?.id).map(campaign=><option key={campaign.id} value={campaign.id}>{campaign.name}</option>)}
    </select></label>;
}

/** A small label naming an item's campaign, which opens it. */
export function CampaignPill({itemKey,onOpen}:{itemKey:string;onOpen?:(key:string)=>void}){
  const campaign=useCampaigns()?.of(itemKey);
  if(!campaign)return null;
  return onOpen?<button type="button" className="fe-pill fe-campaign-pill" onClick={()=>onOpen('campaign:'+campaign.id)}><Megaphone size={12}/> {campaign.name}</button>
    :<span className="fe-pill fe-campaign-pill"><Megaphone size={12}/> {campaign.name}</span>;
}

/** A plan document can become a campaign in one click; its dates, goal and channels come from the text, to edit after. */
export function PlanToCampaign({wikiId,title,onOpen}:{wikiId:string;title:string;onOpen:(key:string)=>void}){
  const book=useCampaigns();
  const [working,setWorking]=useState(false),[error,setError]=useState('');
  if(!book?.ledger||!/\bplan\b|\bcampaign\b/i.test(title)||book.ledger.campaigns.some(campaign=>campaign.planWikiId===wikiId))return null;
  return <div className="fe-notice fe-plan-campaign"><Megaphone size={16}/><span>This reads like a campaign plan. Make it a campaign to keep everything made for it together and see what the employee is following.</span>
    <button type="button" disabled={working} onClick={()=>{setWorking(true);setError('');void book.fromPlan(wikiId).then(made=>onOpen('campaign:'+made.id)).catch(cause=>setError((cause as Error).message)).finally(()=>setWorking(false));}}>{working?'Making…':'Make it a campaign'}</button>
    {error&&<small className="fe-alert" role="alert">{error}</small>}</div>;
}
