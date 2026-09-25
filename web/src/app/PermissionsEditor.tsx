import {useEffect,useState} from 'react';
import {Ban,Check,Plus,ShieldCheck,X} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {useAttempt,type Member} from './shared';

// Pre-approved guardrails, from the owner's research: decide in advance what ships autonomously,
// what needs the owner, and what is never allowed. Stored as the member's PERMISSIONS.md.
type Lists={own:string[];ask:string[];never:string[]};
const file='PERMISSIONS.md';
const headings:{key:keyof Lists;title:string;hint:string;icon:typeof Check}[]=[
  {key:'own',title:'Does on its own',hint:'Safe, reversible work',icon:Check},
  {key:'ask',title:'Asks you first',hint:'Anything public, costly, or hard to undo',icon:ShieldCheck},
  {key:'never',title:'Never',hint:'Off limits, whatever the reason',icon:Ban}
];
export const defaultPermissions:Lists={
  own:['Research public sources and cite them','Draft posts, pages, emails and briefs','Summarize results and suggest next steps','Update its own task notes and decision log'],
  ask:['Publish or post anything','Email, message or reply to anyone','Spend money or change a budget','Change settings in any connected account','Mention a customer or partner by name'],
  never:['Write fake reviews or testimonials','Make claims we cannot back up','Collect private or personal data','Pretend to be a person']
};

export function toMarkdown(name:string,lists:Lists){
  return `# ${name}: permissions\n\n`+headings.map(({key,title})=>`## ${title}\n${lists[key].map(item=>`- ${item}`).join('\n')||'- (none)'}`).join('\n\n')+'\n';
}
export function fromMarkdown(text:string):Lists{
  const lists:Lists={own:[],ask:[],never:[]};let current:keyof Lists|null=null;
  for(const line of text.split(/\r?\n/)){
    const heading=/^##\s+(.+)$/.exec(line);
    if(heading){current=headings.find(item=>item.title.toLowerCase()===heading[1].trim().toLowerCase())?.key??null;continue;}
    const bullet=/^\s*[-*]\s+(.+)$/.exec(line);
    if(current&&bullet&&bullet[1].trim()!=='(none)')lists[current].push(bullet[1].trim());
  }
  return lists;
}

export function PermissionsEditor({member,canEdit}:{member:Member;canEdit:boolean}){
  const [saved,setSaved]=useState<{version:number;updatedAt:string;lists:Lists}|null>(null),[lists,setLists]=useState<Lists>(defaultPermissions);
  const [adding,setAdding]=useState<Record<keyof Lists,string>>({own:'',ask:'',never:''}),[busy,setBusy]=useState(false),[error,setError]=useState(''),[loaded,setLoaded]=useState(false);
  const attempt=useAttempt();
  useEffect(()=>{void api<{name:string;version:number;content:string;updatedAt:string}[]>(`/organization/agents/${member.id}/files`).then(files=>{
    const current=files.find(item=>item.name.toLowerCase()===file.toLowerCase());
    if(current){const parsed=fromMarkdown(current.content);setSaved({version:current.version,updatedAt:current.updatedAt,lists:parsed});setLists(parsed);}
    setLoaded(true);}).catch(cause=>{setError((cause as Error).message);setLoaded(true);});},[member.id]);
  const dirty=JSON.stringify(lists)!==JSON.stringify(saved?.lists??null);
  async function save(){
    setBusy(true);setError('');
    const change={name:file,version:saved?.version??0,content:toMarkdown(member.name,lists)};
    try{const result=await api<{version:number;updatedAt:string}>(`/organization/agents/${member.id}/files`,{...change,requestId:attempt.id(member.id+JSON.stringify(change))},'PUT');attempt.done();setSaved({version:result.version,updatedAt:result.updatedAt,lists});}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  function add(key:keyof Lists){const value=adding[key].trim();if(!value)return;setLists(current=>({...current,[key]:[...current[key],value]}));setAdding(current=>({...current,[key]:''}));}
  if(!loaded)return <p className="fe-muted">Loading permissions…</p>;
  return <div className="fe-permissions">
    <div className="fe-notice"><ShieldCheck size={17}/><span><strong>Decide once, not every time</strong>{saved?`Saved ${readableTime(saved.updatedAt)} as ${file} · version ${saved.version}.`:`These are suggested defaults. Save them to make them ${member.name}’s rules.`} Agent setup turns them into what the employee may do.</span></div>
    <div className="fe-permission-grid">{headings.map(({key,title,hint,icon:Icon})=><section key={key} className={'fe-card fe-permission '+key} aria-label={title}>
      <header><span className="fe-row-icon"><Icon size={16}/></span><div><h3>{title}</h3><small>{hint}</small></div></header>
      <ul>{lists[key].map((item,index)=><li key={item+index}><span>{item}</span>{canEdit&&<button type="button" className="fe-icon-button" aria-label={`Remove “${item}”`} onClick={()=>setLists(current=>({...current,[key]:current[key].filter((_,position)=>position!==index)}))}><X size={14}/></button>}</li>)}
        {!lists[key].length&&<li className="fe-muted">Nothing yet</li>}</ul>
      {canEdit&&<form onSubmit={event=>{event.preventDefault();add(key);}}><input aria-label={`Add to ${title}`} value={adding[key]} maxLength={200} onChange={event=>setAdding(current=>({...current,[key]:event.target.value}))} placeholder="Add a rule"/><button type="submit" className="fe-icon-button" aria-label={`Add rule to ${title}`} disabled={!adding[key].trim()}><Plus size={16}/></button></form>}
    </section>)}</div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {canEdit&&<div className="fe-decision-bar"><small/>{dirty&&saved&&<button type="button" className="fe-ghost" onClick={()=>setLists(saved.lists)}>Discard</button>}<button type="button" className="primary" disabled={busy||(!dirty&&!!saved)} onClick={()=>void save()}>{busy?'Saving…':saved?'Save permissions':'Save these permissions'}</button></div>}
  </div>;
}
