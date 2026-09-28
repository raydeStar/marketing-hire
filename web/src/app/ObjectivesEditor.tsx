import {useEffect,useState} from 'react';
import {Pencil,Plus,Target,X} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {actorLabel} from './library';
import {emptyObjectives,type ObjectivesContent,type ObjectivesView} from './objectives';

type Metric={key:string;name:string};
const blankPositioning={forWho:'',problem:'',alternatives:'',whyUs:'',proofPoints:[]};

function Lines({label,values,placeholder,onChange,max=10}:{label:string;values:string[];placeholder:string;onChange:(next:string[])=>void;max?:number}){
  return <div className="fe-lines"><span className="fe-lines-label">{label}</span>
    {values.map((value,index)=><div className="fe-line" key={index}><input aria-label={`${label} ${index+1}`} value={value} maxLength={300} placeholder={placeholder} onChange={event=>onChange(values.map((item,at)=>at===index?event.target.value:item))}/>
      <button type="button" className="fe-icon-button" aria-label={`Remove ${label.toLowerCase()} ${index+1}`} onClick={()=>onChange(values.filter((_,at)=>at!==index))}><X size={14}/></button></div>)}
    {values.length<max&&<button type="button" className="fe-ghost fe-add-line" onClick={()=>onChange([...values,''])}><Plus size={14}/> Add</button>}</div>;
}

function Summary({content,progress}:{content:ObjectivesContent;progress:ObjectivesView['progress']}){
  const star=content.northStar;
  return <div className="fe-objectives-read">
    <section><h3>North star</h3>{star?<><p className="fe-objectives-star"><Target size={16}/><strong>{star.name}</strong>{star.target!==null&&<span>{starLine(star)}</span>}</p>
      {progress&&progress.latest!==null&&<div className="fe-progress" role="img" aria-label={`${progress.percent}% of target`}><i style={{width:`${Math.min(100,progress.percent||0)}%`}}/><small>{progress.latest.toLocaleString()} {progress.window==='last 30 days'?'in the last 30 days':'latest'} · {progress.percent}% of target</small></div>}
      {star.why&&<p className="fe-muted">{star.why}</p>}</>:<p className="fe-muted">Not set.</p>}</section>
    <section><h3>This quarter</h3>{content.objectives.length?<ol className="fe-objective-list">{content.objectives.map(item=><li key={item.title}><strong>{item.title}</strong>{item.keyResults.length>0&&<ul>{item.keyResults.map(result=><li key={result.text}>{result.text}</li>)}</ul>}</li>)}</ol>:<p className="fe-muted">No objectives yet.</p>}</section>
    <section><h3>Positioning</h3>{content.positioning?<dl className="fe-objectives-dl">
      <div><dt>For</dt><dd>{content.positioning.forWho||'—'}</dd></div><div><dt>Problem</dt><dd>{content.positioning.problem||'—'}</dd></div>
      <div><dt>Instead of</dt><dd>{content.positioning.alternatives||'—'}</dd></div><div><dt>Why us</dt><dd>{content.positioning.whyUs||'—'}</dd></div>
      <div><dt>Proof points</dt><dd>{content.positioning.proofPoints.length?<ul>{content.positioning.proofPoints.map(point=><li key={point}>{point}</li>)}</ul>:'None yet. Without proof, claims are flagged in QA.'}</dd></div></dl>:<p className="fe-muted">Not set.</p>}</section>
    {content.competitors.length>0&&<section><h3>Competitors</h3><ul>{content.competitors.map(item=><li key={item.name}><strong>{item.name}</strong>{item.note&&` — ${item.note}`}</li>)}</ul></section>}
    <section><h3>Current focus</h3><p>{content.currentFocus||<span className="fe-muted">Not set.</span>}</p></section>
    <section><h3>Call to action</h3>{content.callToAction?<p>{content.callToAction.label} · <a href={content.callToAction.url} target="_blank" rel="noopener noreferrer">{content.callToAction.url}</a></p>:<p className="fe-muted">Not set. Posts, emails and pages end on it, so readers know the one thing to do next.</p>}</section>
    <section><h3>Not doing</h3>{content.nonGoals.length?<ul>{content.nonGoals.map(item=><li key={item}>{item}</li>)}</ul>:<p className="fe-muted">No non-goals listed.</p>}</section>
    <section><h3>Listening</h3>{content.watchTopics?.length||content.feeds?.length?<ul>{(content.watchTopics||[]).map(item=><li key={'t'+item}>Watching “{item}”</li>)}{(content.feeds||[]).map(item=><li key={'f'+item}>Following {item}</li>)}</ul>:<p className="fe-muted">Nothing yet. Add topics and feeds so the employee can tell you when something changes.</p>}</section>
    <section><h3>Your site</h3>{content.ownSite?<p>{content.ownSite}</p>:<p className="fe-muted">Not set. Add it so the site check and landing-page drafts know which site is yours.</p>}</section>
    {!!content.watchPages?.length&&<section><h3>Pages to watch</h3><ul>{content.watchPages.map(item=><li key={item}>{item}</li>)}</ul></section>}
    <section><h3>Research sites</h3>{content.researchSites?.length?<ul>{content.researchSites.map(item=><li key={item}>{item}</li>)}</ul>:<p className="fe-muted">None. The employee reads public discussions and headlines only.</p>}</section>
  </div>;
}

/** North star, quarterly objectives, positioning, competitors, focus and non-goals: the frame every shift works within. */
export function ObjectivesEditor({view,canEdit,onSaved,startEditing=false}:{view:ObjectivesView|null;canEdit:boolean;onSaved:(next:ObjectivesView)=>void;startEditing?:boolean}){
  const revision=view?.revision;
  const [form,setForm]=useState<ObjectivesContent|null>(null),[metrics,setMetrics]=useState<Metric[]>([]);
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  useEffect(()=>{void api<{metrics:Metric[]}>('/scorecard').then(data=>setMetrics(data.metrics)).catch(()=>{});},[]);
  useEffect(()=>{if(startEditing&&revision&&canEdit&&!form)setForm(structuredClone(revision.content));},[startEditing,!!revision]);
  if(!view||!revision)return <p className="fe-muted">Loading…</p>;
  async function save(event:React.FormEvent){
    event.preventDefault();if(!form||busy)return;setBusy(true);setError('');setNotice('');
    try{const next=await api<ObjectivesView>('/objectives',{expectedVersion:revision!.version,content:form},'PUT');onSaved(next);setForm(null);setNotice('Saved. Its next check-in works from these.');}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  if(!form)return <div className="fe-doc fe-objectives">
    <div className="fe-doc-meta">{revision.version?<small>Version {revision.version} · {readableTime(revision.updatedAt)} · {actorLabel(revision.updatedBy)}</small>:<small>Not set yet. The employee ranks its work by these, so start with the north star.</small>}
      {canEdit&&<button type="button" className="fe-doc-edit" onClick={()=>setForm(structuredClone(revision.content.northStar||revision.content.objectives.length?revision.content:{...emptyObjectives,northStar:{name:'',metric:null,target:null,unit:'per month',by:null,why:''},objectives:[{title:'',keyResults:[{text:'',metric:null,target:null}]}]}))}><Pencil size={14}/> {revision.version?'Edit':'Set objectives'}</button>}</div>
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    <Summary content={revision.content} progress={view.progress}/>
  </div>;
  const star=form.northStar||{name:'',metric:null,target:null,unit:'',by:null,why:''};
  const positioning=form.positioning||blankPositioning;
  const setStar=(change:Partial<typeof star>)=>setForm({...form,northStar:{...star,...change}});
  const setPlace=(change:Partial<typeof positioning>)=>setForm({...form,positioning:{...positioning,...change}});
  return <form className="fe-doc fe-form fe-objectives" onSubmit={event=>void save(event)} aria-label="Edit objectives">
    <fieldset><legend>North star</legend><p className="fe-muted">The one number that says marketing is working. Tie it to a scorecard metric to track progress automatically.</p>
      <div className="fe-form-row"><label>Name<input value={star.name} maxLength={120} onChange={event=>setStar({name:event.target.value})} placeholder="e.g. Trial starts"/></label>
        <label>Scorecard metric<select value={star.metric||''} onChange={event=>setStar({metric:event.target.value||null})}><option value="">Not linked</option>{metrics.map(metric=><option key={metric.key} value={metric.key}>{metric.name}</option>)}</select></label></div>
      <div className="fe-form-row"><label>Target<input inputMode="decimal" value={star.target??''} onChange={event=>setStar({target:event.target.value===''?null:Number(event.target.value.replace(/[^\d.]/g,''))})} placeholder="250"/></label>
        <label>Unit<input value={star.unit||''} maxLength={20} onChange={event=>setStar({unit:event.target.value})} placeholder="per month"/></label>
        <label>By<input type="date" value={star.by||''} onChange={event=>setStar({by:event.target.value||null})}/></label></div>
      <label>Why it matters<input value={star.why} maxLength={600} onChange={event=>setStar({why:event.target.value})} placeholder="e.g. Trials convert to paid at a steady rate"/></label></fieldset>
    <fieldset><legend>This quarter’s objectives</legend><p className="fe-muted">Two or three outcomes, each with measurable key results.</p>
      {form.objectives.map((objective,index)=><div className="fe-objective" key={index}>
        <div className="fe-line"><input aria-label={`Objective ${index+1}`} value={objective.title} maxLength={200} placeholder="e.g. Recover signup conversion" onChange={event=>setForm({...form,objectives:form.objectives.map((item,at)=>at===index?{...item,title:event.target.value}:item)})}/>
          <button type="button" className="fe-icon-button" aria-label={`Remove objective ${index+1}`} onClick={()=>setForm({...form,objectives:form.objectives.filter((_,at)=>at!==index)})}><X size={14}/></button></div>
        <Lines label="Key results" values={objective.keyResults.map(result=>result.text)} placeholder="e.g. Signup conversion back above 5%" max={5}
          onChange={next=>setForm({...form,objectives:form.objectives.map((item,at)=>at===index?{...item,keyResults:next.map((text,k)=>({...(item.keyResults[k]||{metric:null,target:null}),text}))}:item)})}/>
      </div>)}
      {form.objectives.length<5&&<button type="button" className="fe-ghost fe-add-line" onClick={()=>setForm({...form,objectives:[...form.objectives,{title:'',keyResults:[{text:'',metric:null,target:null}]}]})}><Plus size={14}/> Add objective</button>}</fieldset>
    <fieldset><legend>Positioning</legend>
      <label>Who it’s for<input value={positioning.forWho} maxLength={400} onChange={event=>setPlace({forWho:event.target.value})} placeholder="e.g. Founders of 5–40 person B2B software companies"/></label>
      <label>Their problem<input value={positioning.problem} maxLength={600} onChange={event=>setPlace({problem:event.target.value})}/></label>
      <label>What they use instead<input value={positioning.alternatives} maxLength={600} onChange={event=>setPlace({alternatives:event.target.value})} placeholder="e.g. Agencies, freelancers, doing it themselves"/></label>
      <label>Why you<input value={positioning.whyUs} maxLength={600} onChange={event=>setPlace({whyUs:event.target.value})}/></label>
      <Lines label="Proof points" values={positioning.proofPoints} placeholder="A fact you can back up, e.g. Every draft needs owner approval" onChange={next=>setPlace({proofPoints:next})}/></fieldset>
    <fieldset><legend>Competitors</legend>
      {form.competitors.map((competitor,index)=><div className="fe-line" key={index}><input aria-label={`Competitor ${index+1}`} value={competitor.name} maxLength={80} placeholder="Name" onChange={event=>setForm({...form,competitors:form.competitors.map((item,at)=>at===index?{...item,name:event.target.value}:item)})}/>
        <input aria-label={`Competitor ${index+1} note`} value={competitor.note} maxLength={400} placeholder="How you differ" onChange={event=>setForm({...form,competitors:form.competitors.map((item,at)=>at===index?{...item,note:event.target.value}:item)})}/>
        <button type="button" className="fe-icon-button" aria-label={`Remove competitor ${index+1}`} onClick={()=>setForm({...form,competitors:form.competitors.filter((_,at)=>at!==index)})}><X size={14}/></button></div>)}
      {form.competitors.length<10&&<button type="button" className="fe-ghost fe-add-line" onClick={()=>setForm({...form,competitors:[...form.competitors,{name:'',note:''}]})}><Plus size={14}/> Add competitor</button>}</fieldset>
    <fieldset><legend>Focus and limits</legend>
      <label>Current focus<textarea rows={2} maxLength={1000} value={form.currentFocus} onChange={event=>setForm({...form,currentFocus:event.target.value})} placeholder="What matters most right now, in a sentence or two"/></label>
      <Lines label="Not doing" values={form.nonGoals} placeholder="e.g. Paid ads this quarter" max={12} onChange={next=>setForm({...form,nonGoals:next})}/>
      <div className="fe-form-row">
        <label>Call to action<input maxLength={80} value={form.callToAction?.label||''} onChange={event=>setForm({...form,callToAction:{label:event.target.value,url:form.callToAction?.url||''}})} placeholder="e.g. Try the free starter brief"/></label>
        <label>Its link<input maxLength={500} value={form.callToAction?.url||''} onChange={event=>setForm({...form,callToAction:{label:form.callToAction?.label||'',url:event.target.value}})} placeholder="https://yourcompany.com/start"/></label>
      </div>
      <small className="fe-muted">The one thing you want a reader to do next. Posts, emails and pages end on it.</small></fieldset>
    <fieldset><legend>Listening</legend><p className="fe-muted">Topics the employee watches in public discussions (Hacker News, Google News, Bluesky), and RSS or Atom feeds it follows: competitors’ blogs, newsletters, news alerts. Checked hourly without model cost.</p>
      <Lines label="Topic to watch" values={form.watchTopics||[]} placeholder="e.g. your product name, your category, a competitor" max={10} onChange={next=>setForm({...form,watchTopics:next})}/>
      <Lines label="Feed" values={form.feeds||[]} placeholder="https://competitor.com/blog/feed.xml" max={20} onChange={next=>setForm({...form,feeds:next})}/></fieldset>
    <fieldset><legend>Research sites</legend><p className="fe-muted">Websites the employee may read during a shift: yours and your competitors’. Subdomains are included. Nothing else is fetched.</p>
      <label>Your site<input value={form.ownSite||''} onChange={event=>setForm({...form,ownSite:event.target.value})} placeholder="e.g. yourcompany.com" maxLength={120}/></label>
      <Lines label="Research site" values={form.researchSites||[]} placeholder="e.g. competitor.com" max={10} onChange={next=>setForm({...form,researchSites:next})}/>
      <p className="fe-muted">Pages to watch: competitors’ pricing or plan pages, re-read once a day. A changed price reaches the next shift; other copy changes are noted in Listening.</p>
      <Lines label="Page to watch" values={form.watchPages||[]} placeholder="https://competitor.com/pricing" max={10} onChange={next=>setForm({...form,watchPages:next})}/></fieldset>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={()=>setForm(null)}>Cancel</button><button className="primary" disabled={busy}>{busy?'Saving…':'Save objectives'}</button></footer>
  </form>;
}

/** "20 signups by Oct 31, 2026": the target, its unit (without a repeated deadline) and the date, once. */
function starLine(star:{target:number|null;unit?:string|null;by?:string|null}){
  const unit=(star.unit||'').replace(/\s*\bby\b.*$/i,'').trim();
  const by=star.by?new Date(star.by+'T12:00:00').toLocaleDateString(undefined,{month:'short',day:'numeric',year:'numeric'}):'';
  return [star.target?.toLocaleString(),unit,by&&'by '+by].filter(Boolean).join(' ');
}

/** The cockpit's north star: the one number, its progress, and a way in for the owner to set it. */
export function NorthStarCard({view,owner,onOpen}:{view:ObjectivesView|null;owner:boolean;onOpen:()=>void}){
  if(!view)return null;
  const star=view.revision.content.northStar,progress=view.progress;
  if(!star)return owner?<button type="button" className="fe-north-star empty" onClick={onOpen}><Target size={15}/><span><strong>Set a north star</strong><small>The employee ranks its work against your goals.</small></span></button>:null;
  return <button type="button" className="fe-north-star" onClick={onOpen} aria-label={`North star: ${star.name}`}>
    <span className="fe-north-star-head"><Target size={14}/><strong>{star.name}</strong>{star.target!==null&&<small>{starLine(star)}</small>}</span>
    {progress&&progress.latest!==null?<><span className="fe-progress"><i style={{width:`${Math.min(100,progress.percent||0)}%`}}/></span><small>{progress.latest.toLocaleString()} {progress.window==='last 30 days'?'last 30 days':'latest'} · {progress.percent}%</small></>
      :<small>{star.metric?'No scorecard data for this metric yet.':'Link it to a scorecard metric to track progress.'}</small>}
  </button>;
}
