import {useEffect,useState} from 'react';
import {Check,Play,Rocket} from 'lucide-react';
import {api} from '../api';
import {requestId,type MarketingState} from '../components/MarketingPanels';
import type {ShiftView} from './shifts';

export type WorkspaceRoleName='owner'|'marketer'|'sales'|'affiliate';
export type WorkspaceRoleInfo={role:WorkspaceRoleName;person:string;offer:string;disclosure:string};

export const roleChoices:{role:WorkspaceRoleName;label:string;hint:string}[]=[
  {role:'owner',label:'My business',hint:'I run it'},
  {role:'marketer',label:'I market it',hint:'In-house or agency'},
  {role:'sales',label:'I sell it',hint:'Sales, business development'},
  {role:'affiliate',label:'I promote it',hint:'Affiliate or partner, on commission'}
];

export function useWorkspaceRole(enabled=true){
  const [info,setInfo]=useState<WorkspaceRoleInfo|null>(null);
  useEffect(()=>{if(enabled)void api<WorkspaceRoleInfo>('/workspace-role').then(setInfo).catch(()=>setInfo({role:'owner',person:'',offer:'',disclosure:''}));},[enabled]);
  async function save(next:Partial<WorkspaceRoleInfo>&{role:WorkspaceRoleName}){const saved=await api<WorkspaceRoleInfo>('/workspace-role',{person:'',offer:'',disclosure:'',...info,...next},'PUT');setInfo(saved);return saved;}
  return {info,save};
}

/** "Whose marketing is this?": four choices, one click. */
export function RolePicker({value,onChange,disabled=false}:{value:WorkspaceRoleName;onChange:(role:WorkspaceRoleName)=>void;disabled?:boolean}){
  return <fieldset className="fe-role-picker" disabled={disabled}><legend>Whose marketing is this?</legend>
    <div className="fe-role-options">{roleChoices.map(choice=><label key={choice.role} className={value===choice.role?'on':''}>
      <input type="radio" name="workspace-role" value={choice.role} checked={value===choice.role} onChange={()=>onChange(choice.role)}/>
      <strong>{choice.label}</strong><small>{choice.hint}</small></label>)}</div>
  </fieldset>;
}

export type PlaybookTask={title:string;next:string;summary?:string};
export type Playbook={id:string;name:string;hint:string;northStar:string;channels:string[];starters:PlaybookTask[]};

/** What kind of business this is: a product, a practice, a community or a local business. */
export function usePlaybook(enabled=true){
  const [current,setCurrent]=useState<string|null>(null),[all,setAll]=useState<Playbook[]>([]);
  useEffect(()=>{if(enabled)void api<{current:string|null;all:Playbook[]}>('/playbook').then(view=>{setCurrent(view.current);setAll(view.all);}).catch(()=>{});},[enabled]);
  async function save(id:string){const saved=await api<Playbook>('/playbook',{id},'PUT');setCurrent(saved.id);return saved;}
  return {current,all,playbook:all.find(item=>item.id===current)??null,save};
}

/** "What are you marketing?": it shapes the guidance, the first steps and the first shift. */
export function PlaybookPicker({value,options,onChange,disabled=false}:{value:string|null;options:Playbook[];onChange:(id:string)=>void;disabled?:boolean}){
  if(!options.length)return null;
  return <fieldset className="fe-role-picker" disabled={disabled}><legend>What are you marketing?</legend>
    <div className="fe-role-options">{options.map(choice=><label key={choice.id} className={value===choice.id?'on':''}>
      <input type="radio" name="workspace-playbook" value={choice.id} checked={value===choice.id} onChange={()=>onChange(choice.id)}/>
      <strong>{choice.name}</strong><small>{choice.hint}</small></label>)}</div>
  </fieldset>;
}

/** The import instructions, tuned to whose marketing it is, and told to fill gaps with sensible, marked assumptions. */
export function roleImportNote(role:WorkspaceRoleName,person:string){
  const about=person.trim()?` About me: ${person.trim()}`:'';
  const fill=' Where the pages say little, fill each field with what is typical for a business like this and mark it (assumed); leave nothing empty, so work can start today.';
  if(role==='sales')return `I'm a salesperson for this company, not the company itself.${about} The pages describe what I sell. In the brief: audience is who I sell to, voice is how I come across as a person (not the brand), goals are my sales goals for the next few weeks (booked meetings, if unknown), channels are where I reach prospects (LinkedIn, email, calls), and guardrails include never posing as the company's official accounts or promising pricing or terms it hasn't published.${fill}`;
  if(role==='affiliate')return `I promote this company's product as an independent affiliate and earn a commission.${about} The pages describe what I recommend. In the brief: audience is my audience, voice is mine, goals are my goals (clicks and sales through my link, if unknown), channels are where my audience follows me, and guardrails include disclosing the commission in every public post, never posing as the company, and never inventing discounts or claims.${fill}`;
  if(role==='marketer')return `I lead marketing for this company.${about}${fill}`;
  return fill.trim();
}

type Starter={title:string;next:string;summary?:string};
const starters:Record<WorkspaceRoleName,Starter[]>={
  owner:[
    {title:'Positioning one-pager from our website',next:'Read our site and the brief, then write a one-page positioning document: who it is for, their problem, what they use instead, why us, and the proof. Mark every assumption.'},
    {title:'Three LinkedIn posts for this week',next:'Draft three LinkedIn posts from the brief, each from a different angle: the customer’s problem, a proof point, and how it works. One clear next step each.'},
    {title:'Our three closest competitors, compared',next:'Find our three closest alternatives and write a battlecard: what each costs, who it is for, and where we win or lose. Cite their pages.'},
    {title:'Site check: the five fixes that matter',next:'Check our own site for clarity and SEO problems, and list the five fixes that would matter most, in order.'},
    {title:'A two-week launch campaign',next:'Plan a two-week campaign: a goal, the channels, and a day-by-day list of the posts and emails to make, starting with what we can ship first.'}
  ],
  marketer:[],
  sales:[
    {title:'Ideal customer profile and 20 target accounts',next:'From the company’s site and my brief, describe the ideal customer (industry, size, role, trigger) and list 20 kinds of accounts or named public companies that fit, with a reason each.'},
    {title:'A three-email outreach sequence',next:'Write a three-email sequence to that customer in my voice: a short first touch, a useful follow-up, and a polite last note. One ask throughout: a 15-minute call.'},
    {title:'Three LinkedIn posts from my profile',next:'Draft three LinkedIn posts I can publish from my own profile that show I understand my buyers’ problem. No company-announcement tone.'},
    {title:'Objection handling and a competitor battlecard',next:'List the ten objections my buyers are likely to raise with a short, honest answer to each, and a battlecard against the two most common alternatives.'},
    {title:'Discovery call prep sheet',next:'Write a one-page call prep sheet: ten discovery questions, what a good answer sounds like, and how to move to a next step.'}
  ],
  affiliate:[
    {title:'An honest review of the product',next:'Write an honest review for my audience: what it does, who it suits and who it doesn’t, the price, and my verdict. Disclose the commission at the top and use my link.'},
    {title:'Comparison: this vs two alternatives',next:'Write a fair comparison of the product against two alternatives my audience would consider, with prices from their pages and who should pick which.'},
    {title:'Three social posts with my link',next:'Draft three short social posts for my channels, each with a different hook, my link, and a plain disclosure.'},
    {title:'An email to my list',next:'Write an email to my list recommending the product: why I use or recommend it, one example, my link, and the disclosure.'},
    {title:'Two weeks of content, planned',next:'Plan two weeks of posts and one video for my channels around the product, with a hook for each and the day to publish.'}
  ]
};
starters.marketer=starters.owner;

/** One-click first steps: each becomes a task the employee starts on at its next cycle, or right away with a short shift. */
export function FirstSteps({state,owner,onRefresh,heading=true,title='First steps',hint='Pick any; each becomes a task the employee starts on. You review everything before it goes out.'}:{state:MarketingState;owner:boolean;onRefresh:()=>Promise<void>;heading?:boolean;title?:string;hint?:string}){
  const {info}=useWorkspaceRole();
  const {playbook}=usePlaybook();
  const [busy,setBusy]=useState(''),[error,setError]=useState(''),[shift,setShift]=useState<ShiftView|null>(null),[started,setStarted]=useState(false);
  useEffect(()=>{if(owner)void api<ShiftView>('/shifts').then(setShift).catch(()=>{});},[owner,started]);
  if(!info)return null;
  // A business's first steps follow its playbook (a practice starts with a seminar kit, a group with its welcome post).
  const list=(info.role==='owner'||info.role==='marketer')&&playbook?playbook.starters:starters[info.role]||starters.owner;
  const queued=(title:string)=>state.tasks.some(task=>task.title===title);
  async function queue(items:Starter[]){
    setError('');
    for(const [index,item] of items.entries()){
      if(queued(item.title))continue;
      setBusy(item.title);
      try{await api('/marketing/tasks',{requestId:requestId(),title:item.title,status:'ready',priority:index===0?'high':'normal',next_action:item.next,action_state:'agent_ready'});}
      catch(cause){setError((cause as Error).message);break;}
    }
    setBusy('');await onRefresh().catch(()=>{});
  }
  async function startShift(){
    setBusy('shift');setError('');
    try{await api('/shifts',{requestId:requestId(),hours:1,cycleMinutes:30});setStarted(true);}
    catch(cause){setError((cause as Error).message);}finally{setBusy('');}
  }
  const onShift=started||!!shift?.current&&['running','paused','finishing'].includes(shift.current.status);
  const any=list.some(item=>queued(item.title));
  return <section className="fe-first-steps" aria-label="First steps">
    {heading&&<div className="fe-first-head"><Rocket size={16}/><div><h3>{title}</h3><small>{hint}</small></div></div>}
    <ul>{list.map(item=>{const done=queued(item.title);return <li key={item.title} className={done?'done':''}>
      <div><strong>{item.title}</strong><small>{item.summary||item.next}</small></div>
      <button type="button" disabled={done||!!busy} onClick={()=>void queue([item])}>{done?<><Check size={14}/> Queued</>:busy===item.title?'Adding…':'Do this'}</button>
    </li>;})}</ul>
    <div className="fe-first-foot">
      {!list.every(item=>queued(item.title))&&<button type="button" disabled={!!busy} onClick={()=>void queue(list)}>Do all five</button>}
      {owner&&any&&!onShift&&<button type="button" className="primary" disabled={!!busy} onClick={()=>void startShift()}><Play size={14}/> {busy==='shift'?'Starting…':'Start a 1-hour shift now'}</button>}
      {onShift&&any&&<small className="fe-muted">On shift: the first results arrive for your review within the hour.</small>}
    </div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}
