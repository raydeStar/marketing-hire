import {useState} from 'react';
import {Lightbulb,ArrowUpRight,Sparkles,Archive,Plus} from 'lucide-react';
import {api} from '../api';
import type {State,LibraryItem,Run} from '../types';
import {Collections} from './Collections';

const starters=[{title:'Make a little room in your day',category:'Everyday life',content:'Turn a busy day into a short, realistic plan with room to breathe.',prompt:'Help me make a realistic plan for my day. Ask what is on my plate and how much time I have.'},{title:'Build something just for you',category:'Small useful apps',content:'A tracker, a collection, a checklist. Something shaped around how you work.',prompt:'Help me design and build a small app that would make my day easier. Ask about my routines and what I want to track before building it.'},{title:'Learn something worth keeping',category:'Learning',content:'Choose a topic and turn curiosity into a small, practical lesson.',prompt:'Help me learn something useful. Ask what I am curious about and my current experience, then give me a small practical exercise.'}];
export function Ideas({data,online,onChanged,onStart,onDiscuss,onDetails,focusId}:{data:State;online:boolean;onChanged:()=>Promise<unknown>;onStart:(prompt:string)=>Promise<void>;onDiscuss:(text:string)=>void;onDetails:(id:string)=>void;focusId?:string}){
 const [pane,setPane]=useState(focusId?'saved':'discover'),[busy,setBusy]=useState(false),[error,setError]=useState('');
 const saved=data.library.filter(i=>i.kind==='idea'&&i.status==='open'),ideas=saved.filter(i=>i.prompt),categories=[...new Set(ideas.map(i=>i.category||'For you'))];
 const active=data.runs.find(r=>r.suggestIdeas&&['queued','running'].includes(r.state));
 const latest=active||data.runs.filter(r=>r.suggestIdeas).sort((a,b)=>Date.parse(b.created)-Date.parse(a.created))[0];
 const stopped=latest&&!['queued','running','succeeded'].includes(latest.state);
 async function act(work:()=>Promise<unknown>){setBusy(true);setError('');try{await work();await onChanged();}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 async function generate(){await act(async()=>{await api<Run>('/chat',{content:'Suggest a fresh set of useful ideas based on my recent conversations. Use broad categories when you do not yet know my interests.',suggestIdeas:true,budget:{modelCalls:1,toolCalls:1,maxOutputTokens:2048,seconds:600,repairs:0,maxTotalTokens:64000}});});}
 const card=(idea:{title:string;content:string;prompt?:string|null},id:string,item?:LibraryItem)=><article className="idea-card" key={id}><button className="idea-launch" disabled={!online||busy||!!active} onClick={()=>void act(()=>onStart(idea.prompt||idea.title))}><span className="idea-glyph"><Lightbulb size={24}/></span><span><h3>{idea.title}</h3><p>{idea.content}</p><small>Let’s explore <ArrowUpRight size={12}/></small></span></button>{item&&<button className="idea-dismiss" aria-label={'Dismiss '+item.title} disabled={!online||busy} onClick={()=>void act(()=>api('/library/'+item.id,{...item,status:'archived'},'PUT'))}><Archive size={14}/></button>}</article>;
 return <section className="ideas-workspace"><div className="page-heading"><div><p className="eyebrow">ROOM FOR WONDER</p><h1>Ideas</h1></div><button disabled={!online||busy||!!active||data.provider.kind!=='compatible'} onClick={()=>void generate()}><Sparkles size={15}/>{active?'Finding ideas…':'Fresh ideas'}</button></div>
  <nav className="collection-filters" aria-label="Idea sections"><button aria-pressed={pane==='discover'} onClick={()=>setPane('discover')}>For you</button><button aria-pressed={pane==='saved'} onClick={()=>setPane('saved')}><Plus size={14}/>Saved ideas</button></nav>
  {error&&<p role="alert" className="error">{error}</p>}
  {latest&&<div className="collection-notice" role={stopped?'alert':'status'}><span>{active?'Finding fresh ideas. You can keep using the study.':stopped?`${latest.state==='cancelled'?'Finding ideas was cancelled.':'Could not refresh ideas.'} ${latest.summary} Your saved ideas are unchanged.`:'Fresh ideas are ready.'}</span><button onClick={()=>onDetails(latest.id)}>View details</button>{active&&<button disabled={!online||busy} onClick={()=>void act(()=>api('/runs/'+active.id+'/cancel',{}))}>Cancel</button>}</div>}
  {pane==='saved'?<Collections kind="idea" focusId={focusId} items={data.library} online={online} onChanged={onChanged} onDiscuss={onDiscuss}/>:<>
   <p className="lead">A few possibilities. Fresh ideas use your recent conversation and the selected model; opening this page uses no tokens.</p>
   {!ideas.length?starters.map((idea,i)=><section className="idea-group" key={i}><h2>{idea.category}</h2>{card(idea,String(i))}</section>):categories.map(category=><section className="idea-group" key={category}><h2>{category}</h2>{ideas.filter(i=>(i.category||'For you')===category).map(i=>card(i,i.id,i))}</section>)}
   {data.provider.kind!=='compatible'&&<p className="muted">Connect a model in Settings for personalized suggestions. These starting points are ready to explore.</p>}
  </>}
 </section>;
}
