import {useEffect,useState} from 'react';
import {TrendingUp} from 'lucide-react';
import {api} from '../api';

export type RubricCategory={key:string;name:string;asks:string};
export type RubricEntry={at:string;title:string;type:string;channel:string;scores:Record<string,number>;passes:number;first:number;keys?:string[]|null;missing?:string[]};
type RubricData={categories:RubricCategory[];focus:string[];focusBar:number;entries:RubricEntry[]};

export const grade=(score:number)=>score>=4.5?'A':score>=3.5?'B':score>=2.5?'C':score>=1.5?'D':'F';
const tone=(score:number)=>score>=4.5?'ok':score>=3.5?'':score>=2.5?'attn':'bad';

// One copy for every card on the page; a change to the focus refreshes them all.
let cached:Promise<RubricData>|null=null;const listeners=new Set<(data:RubricData)=>void>();
function load(force=false){if(force||!cached)cached=api<RubricData>('/rubric');return cached;}
function useRubricData(){
  const [data,setData]=useState<RubricData|null>(null);
  useEffect(()=>{let live=true;void load().then(value=>{if(live)setData(value);}).catch(()=>{});const listen=(value:RubricData)=>setData(value);listeners.add(listen);return()=>{live=false;listeners.delete(listen);};},[]);
  return data;
}
async function refresh(){const value=await load(true);listeners.forEach(listen=>listen(value));return value;}

function overall(scores:Record<string,number>,focus:string[]){
  let total=0,weight=0;for(const [key,value] of Object.entries(scores)){const w=focus.includes(key)?2:1;total+=value*w;weight+=w;}
  return weight?total/weight:0;
}

/** What a piece still lacks of what was asked, in plain words, from its latest review; empty when it has it all. */
export function useMissing(itemKey:string){
  const data=useRubricData();
  return data?.entries.filter(item=>item.keys?.includes(itemKey)).at(-1)?.missing||[];
}
/** "the event's date, the sign-up link and 3 more". */
export const missingLine=(items:string[])=>items.length<=3?items.join(', ').replace(/, ([^,]*)$/,' and $1'):`${items[0]}, ${items[1]} and ${items.length-2} more`;

/** Unfinished work says so where it's decided, with the one-click way to have it finished. */
export function Unfinished({items,onSendBack,busy}:{items:string[];onSendBack?:(note:string)=>void;busy?:boolean}){
  if(!items.length)return null;
  const note=`Please finish this. It still needs: ${items.join('; ')}.`.slice(0,600);
  return <div className="fe-unfinished" role="note"><span><strong>Not finished yet.</strong> It still needs {missingLine(items)}{/[….]$/.test(missingLine(items))?'':'.'} Send it back and Chip finishes it at its next shift.</span>
    {onSendBack&&<button type="button" className="primary" disabled={busy} onClick={()=>onSendBack(note)}>{busy?'Sending…':'Send it back to finish'}</button>}</div>;
}

/** A piece of work's grades on the marketing rubric, from its self-review. */
export function RubricGrades({itemKey}:{itemKey:string}){
  const data=useRubricData();
  const entry=data?.entries.filter(item=>item.keys?.includes(itemKey)).at(-1);
  if(!data||!entry)return null;
  const final=overall(entry.scores,data.focus);
  return <section className="fe-rubric" aria-label="Marketing rubric">
    <div className="fe-rubric-head"><strong>Marketing rubric: {grade(final)}</strong>
      {entry.passes>1&&grade(entry.first)!==grade(final)&&<small className="fe-muted">first draft {grade(entry.first)}, {entry.passes} passes</small>}</div>
    <ul>{data.categories.filter(category=>entry.scores[category.key]).map(category=>{const score=entry.scores[category.key];return <li key={category.key} className={tone(score)+(data.focus.includes(category.key)?' focus':'')} title={`${category.asks}${data.focus.includes(category.key)?' · being raised':''}`}>
      <span>{category.name}</span><b>{grade(score)}</b></li>;})}</ul>
  </section>;
}

/** Team → the employee → Usage: how its work grades on each category, and the ones the owner is raising. */
export function RubricPanel({canEdit}:{canEdit:boolean}){
  const data=useRubricData();
  const [busy,setBusy]=useState(''),[error,setError]=useState('');
  if(!data)return <section aria-label="Marketing rubric"><h3>Marketing rubric</h3><p className="fe-muted">Loading…</p></section>;
  const recent=data.entries.slice(-20);
  const average=(key:string)=>{const values=recent.map(entry=>entry.scores[key]).filter((value):value is number=>typeof value==='number');return values.length?values.reduce((a,b)=>a+b,0)/values.length:0;};
  const finals=recent.map(entry=>overall(entry.scores,data.focus));
  const overallNow=finals.length?finals.reduce((a,b)=>a+b,0)/finals.length:0;
  const firstNow=recent.length?recent.reduce((total,entry)=>total+entry.first,0)/recent.length:0;
  async function toggle(key:string){
    const next=data!.focus.includes(key)?data!.focus.filter(item=>item!==key):[...data!.focus,key];
    setBusy(key);setError('');
    try{await api('/rubric',{focus:next},'PUT');await refresh();}catch(cause){setError((cause as Error).message);}finally{setBusy('');}
  }
  return <section aria-label="Marketing rubric" className="fe-rubric-panel"><h3>Marketing rubric</h3>
    <dl className="fe-stats">
      <div><dt>Overall</dt><dd>{recent.length?grade(overallNow):'–'}</dd></div>
      <div><dt>First drafts</dt><dd>{recent.length?grade(firstNow):'–'}</dd></div>
      <div><dt>Graded</dt><dd>{data.entries.length}</dd></div>
    </dl>
    <small className="fe-muted">{recent.length?`The last ${recent.length} pieces, graded by the employee’s own review before you saw them.`:'Nothing graded yet: each piece of work is graded on these eight categories before you see it.'} Raise up to three categories: they count double, are held to a higher standard, and a piece isn’t done until they reach a B. Your verdicts count more than these grades.</small>
    <ul className="fe-rubric-rows">{data.categories.map(category=>{const score=average(category.key);const raised=data.focus.includes(category.key);return <li key={category.key} className={raised?'focus':''}>
      <div><strong>{category.name}</strong><small>{category.asks}</small></div>
      <span className="fe-bar"><i style={{width:`${Math.round(score/5*100)}%`}}/></span>
      <b className={'fe-grade '+(score?tone(score):'')}>{score?grade(score):'–'}</b>
      {canEdit&&<button type="button" aria-pressed={raised} disabled={!!busy||(!raised&&data.focus.length>=3)} title={raised?'Stop raising this':'Raise this category'} onClick={()=>void toggle(category.key)}><TrendingUp size={14}/> {raised?'Raising':'Raise'}</button>}
    </li>;})}</ul>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {recent.length>0&&<h4>Recent pieces</h4>}
    <ul className="fe-usage-stages">{recent.slice(-10).reverse().map(entry=>{const final=overall(entry.scores,data.focus);return <li key={entry.at+entry.title}><span title={entry.title}>{entry.title.length>44?entry.title.slice(0,44)+'…':entry.title}</span>
      <span className="fe-bar"><i style={{width:`${Math.round(final/5*100)}%`}}/></span><strong>{grade(entry.first)!==grade(final)?`${grade(entry.first)} → `:''}{grade(final)}</strong></li>;})}</ul>
  </section>;
}
