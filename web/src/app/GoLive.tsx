import {useEffect,useState} from 'react';
import {CheckCircle2,Circle,CircleDot} from 'lucide-react';
import {api} from '../api';

type Item={id:string;title:string;detail:string;state:'done'|'todo'|'optional';action?:{label:string;target:string}};

/** Settings → Go-live checklist: what's set up for real work, what's missing, and one click to each. */
export function GoLiveChecklist({onNavigate}:{onNavigate:(target:string)=>void}){
  const [items,setItems]=useState<Item[]|null>(null);
  useEffect(()=>{void (async()=>{
    const get=async<T,>(path:string):Promise<T|null>=>{try{return await api<T>(path);}catch{return null;}};
    const [goals,data,publishing,schedule,weekly,shifts]=await Promise.all([
      get<{revision:{content:any}}>('/objectives'),get<{googleReady:boolean;connections:{kind:string;status:string}[]}>('/data-connections'),
      get<{connections:{kind:string;status:string;account:string}[]}>('/publishing'),get<{schedule:{enabled:boolean;tokenBudget:number|null}|null}>('/shifts/schedule'),
      get<{settings:{enabled:boolean}}>('/weekly'),get<{live:boolean;runtime:string}>('/shifts')]);
    const content=goals?.revision.content||{};
    const channels=(publishing?.connections||[]).filter(item=>item.status==='ready');
    const analytics=(data?.connections||[]).filter(item=>item.status==='ready');
    setItems([
      {id:'goals',title:'Objectives and positioning',detail:content.northStar?.name?`North star: ${content.northStar.name}.`:'Set the north star, objectives, proof points and non-goals. Everything is ranked against them.',state:content.northStar?.name?'done':'todo',action:{label:content.northStar?.name?'Review':'Set them',target:'brief:objectives'}},
      {id:'live',title:'Live model',detail:shifts?.live?'Shifts run on the OpenClaw employee, metered per turn.':'The scripted stand-in is running. Start the workspace with start-marketing.ps1 -LiveShifts to use the employee.',state:shifts?.live?'done':'todo'},
      {id:'hours',title:'Working hours',detail:schedule?.schedule?.enabled?`On${schedule.schedule.tokenBudget?`, up to ${schedule.schedule.tokenBudget.toLocaleString()} tokens a day`:''}.`:'Let it start its own shift on the days you choose.',state:schedule?.schedule?.enabled?'done':'todo',action:{label:schedule?.schedule?.enabled?'Change':'Set hours',target:'section:shifts'}},
      {id:'google',title:'Google app',detail:data?.googleReady?'Saved; Google Analytics, Search Console and Gmail drafts can sign in.':'One-time setup: import your Google Desktop app. Needed for Analytics, Search Console and Gmail drafts.',state:data?.googleReady?'done':'optional',action:{label:'Open',target:'view:settings'}},
      {id:'analytics',title:'Analytics',detail:analytics.length?`${analytics.length} source(s) feeding the scorecard.`:'Connect Google Analytics, Search Console or Plausible so the scorecard keeps itself current and posts get visits.',state:analytics.length?'done':'optional',action:{label:analytics.length?'Scorecard':'Connect',target:'section:scorecard'}},
      {id:'channels',title:'Publishing channels',detail:channels.length?`${channels.map(item=>item.kind).join(', ')} connected.`:'Optional: approved posts can always be posted through each network’s own composer. Connect Bluesky or Mastodon to publish and schedule from here.',state:channels.length?'done':'optional',action:{label:channels.length?'Manage':'Connect',target:'view:settings'}},
      {id:'email',title:'Email (Gmail drafts)',detail:channels.some(item=>item.kind==='email')?'Approved emails and the weekly update land in Gmail drafts.':'Connect a mailbox (ideally a separate one for marketing) for email drafts.',state:channels.some(item=>item.kind==='email')?'done':'optional',action:{label:'Connect',target:'view:settings'}},
      {id:'weekly',title:'Weekly rhythm',detail:weekly?.settings.enabled?'The Monday plan and Friday update are automatic.':'Turn on the Monday plan and Friday update.',state:weekly?.settings.enabled?'done':'todo',action:{label:weekly?.settings.enabled?'Open':'Turn on',target:'section:weekly'}},
      {id:'listening',title:'Listening',detail:(content.watchTopics||[]).length?`Watching ${(content.watchTopics||[]).length} topic(s), ${(content.feeds||[]).length} feed(s).`:'Add your product, category and competitors as watch topics.',state:(content.watchTopics||[]).length?'done':'todo',action:{label:'What to watch',target:'brief:objectives'}},
      {id:'sites',title:'Research sites',detail:(content.researchSites||[]).length?`${(content.researchSites||[]).length} site(s) the employee may read.`:'Your own site and competitors’, so research can read them.',state:(content.researchSites||[]).length?'done':'optional',action:{label:'Add sites',target:'brief:objectives'}},
    ]);
  })();},[]);
  if(!items)return <p className="fe-muted">Checking…</p>;
  const ready=items.filter(item=>item.state==='done').length;
  return <div className="fe-golive" aria-label="Go-live checklist">
    <p className="fe-muted">{ready} of {items.length} set up. Required items first; the optional ones make the employee more useful.</p>
    {[...items].sort((a,b)=>(a.state==='done'?2:a.state==='todo'?0:1)-(b.state==='done'?2:b.state==='todo'?0:1)).map(item=><div key={item.id} className={'fe-data-row '+item.state}>
      <span className="fe-row-icon">{item.state==='done'?<CheckCircle2 size={16}/>:item.state==='todo'?<CircleDot size={16}/>:<Circle size={16}/>}</span>
      <span className="fe-list-main"><strong>{item.title}{item.state==='optional'&&<span className="fe-muted"> · optional</span>}</strong><small>{item.detail}</small></span>
      {item.action&&<button type="button" className="fe-ghost" onClick={()=>onNavigate(item.action!.target)}>{item.action.label}</button>}
    </div>)}
  </div>;
}
