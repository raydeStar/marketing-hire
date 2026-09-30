import {useEffect,useState} from 'react';
import {Pencil} from 'lucide-react';
import {api} from '../api';
import {readableTime,type MarketingProfile} from '../components/MarketingPanels';
import {useAttempt} from './shared';

export type BriefFields=Pick<MarketingProfile,'display_name'|'product_summary'|'audience'|'goals'|'voice'|'channels'|'guardrails'>&{claims?:string;examples?:string};
export const briefKeys=['display_name','product_summary','audience','goals','voice','channels','guardrails','claims','examples'] as const;

/** The three answers it needs to start; everything else is optional and waits under "More about your business". */
const essential=new Set<string>(['product_summary','audience','goals']);
const fields:{key:typeof briefKeys[number];label:string;hint:string;rows:number;max:number;evidence?:boolean}[]=[
  {key:'display_name',label:'Employee name',hint:'What should your marketing employee be called?',rows:1,max:80},
  {key:'product_summary',label:'What you sell',hint:'A sentence or two: what it is and why people buy it.',rows:3,max:1200},
  {key:'audience',label:'Who it’s for',hint:'The customers you most want more of. A guess is fine.',rows:3,max:1200},
  {key:'goals',label:'What you want right now',hint:'For example: more bookings this month, or 50 people at the open day.',rows:3,max:1200},
  {key:'voice',label:'Voice',hint:'How you sound, e.g. warm, plain-spoken, a little funny.',rows:2,max:1000},
  {key:'claims',label:'What we can truthfully claim',hint:'Proof points, and anything that is not proven yet.',rows:3,max:1600,evidence:true},
  {key:'examples',label:'Examples to learn from',hint:'Your best posts, pages or links, and what to take from each.',rows:3,max:1600,evidence:true},
  {key:'channels',label:'Where to listen and show up',hint:'Communities, sites and channels that matter.',rows:2,max:1000},
  {key:'guardrails',label:'Boundaries',hint:'Anything your employee must never do or say.',rows:3,max:1000}
];

export function briefComplete(profile:MarketingProfile){return !!profile.product_summary.trim()&&!!profile.goals.trim();}

export async function saveBrief(profile:MarketingProfile,next:BriefFields,evidenceEnabled:boolean,requestId:string){
  const changes=Object.fromEntries(briefKeys.filter(key=>evidenceEnabled||!['claims','examples'].includes(key)).map(key=>[key,String(next[key]??'').trim()]));
  return api<MarketingProfile>('/marketing/profile',{...changes,version:profile.version,requestId},'PUT');
}

/** The business brief: what Marketing reads before every turn. */
export function BriefEditor({profile,evidenceEnabled,canEdit,initial,startEditing=false,onSaved,onCancel,extra,requireEssentials=false}:{
  profile:MarketingProfile;evidenceEnabled:boolean;canEdit:boolean;initial?:Partial<BriefFields>;startEditing?:boolean;extra?:React.ReactNode;requireEssentials?:boolean;
  onSaved:(saved:MarketingProfile)=>Promise<void>|void;onCancel?:()=>void;
}){
  const [editing,setEditing]=useState(startEditing||!!initial);
  // Just saved: show what was saved while the workspace reloads, not the brief as it was before.
  const [justSaved,setJustSaved]=useState<MarketingProfile|null>(null);
  const shown=justSaved&&justSaved.version>=profile.version?justSaved:profile;
  const [draft,setDraft]=useState<BriefFields>(()=>({...profile,...initial}));
  const [saving,setSaving]=useState(false),[error,setError]=useState('');
  const attempt=useAttempt();
  useEffect(()=>{if(initial){setDraft({...profile,...initial});setEditing(true);}},[initial]);
  const visible=fields.filter(field=>evidenceEnabled||!field.evidence);
  const input=(field:typeof fields[number])=><label key={field.key}>{field.label}<small>{field.hint}</small>
    {field.rows===1?<input value={String(draft[field.key]??'')} maxLength={field.max} required={field.key==='display_name'} onChange={event=>setDraft(current=>({...current,[field.key]:event.target.value}))}/>
    :<textarea rows={field.rows} value={String(draft[field.key]??'')} maxLength={field.max} onChange={event=>setDraft(current=>({...current,[field.key]:event.target.value}))}/>}
  </label>;
  async function save(event:React.FormEvent){
    event.preventDefault();if(saving||!canEdit)return;
    // The three answers everything else is written from: a brief without them leaves the employee guessing.
    const missing=([['product_summary','what you sell'],['audience','who it’s for'],['goals','what you want right now']] as const).filter(([key])=>!String(draft[key]??'').trim()).map(([,label])=>label);
    if(requireEssentials&&missing.length){setError(`Add ${missing.join(', ').replace(/, ([^,]*)$/,' and $1')} first. A sentence each is enough.`);return;}
    setSaving(true);setError('');
    try{const saved=await saveBrief(profile,draft,evidenceEnabled,attempt.id(JSON.stringify({draft,version:profile.version})));attempt.done();setJustSaved(saved);setEditing(false);await onSaved(saved);}
    catch(cause){setError((cause as Error).message);}finally{setSaving(false);}
  }
  if(!editing)return <section className="fe-card fe-brief" aria-label="Business brief">
    <div className="fe-card-head"><div><h3>Business brief</h3><small>Your employee reads this before everything it writes · updated {readableTime(shown.updated_at)}</small></div>
      {canEdit&&<button type="button" onClick={()=>{setDraft({...profile});setEditing(true);}}><Pencil size={14}/> Edit</button>}</div>
    <dl className="fe-brief-list">{visible.map(field=><div key={field.key}><dt>{field.label}</dt><dd>{String(shown[field.key]??'').trim()||<span className="fe-muted">Not set yet</span>}</dd></div>)}</dl>
  </section>;
  return <form className="fe-card fe-form fe-brief" aria-label="Edit business brief" onSubmit={event=>void save(event)}>
    <div className="fe-card-head"><div><h3>Business brief</h3><small>Three answers are enough to start. You can change any of this later.</small></div></div>
    {visible.filter(field=>essential.has(field.key)).map(input)}
    {extra}
    {/* Open when there's something in it already, so editing a full brief shows it all. */}
    <details className="fe-brief-more" open={visible.some(field=>!essential.has(field.key)&&field.key!=='display_name'&&String(profile[field.key]??'').trim())||undefined}>
      <summary>More about your business (optional)</summary>
      {visible.filter(field=>!essential.has(field.key)).map(input)}
    </details>
    {!evidenceEnabled&&<small>Claims and examples become editable after the workspace server update.</small>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" disabled={saving} onClick={()=>{setEditing(false);onCancel?.();}}>Cancel</button><button className="primary" disabled={saving||!draft.display_name.trim()}>{saving?'Saving…':'Save brief'}</button></footer>
  </form>;
}
