import {useCallback,useEffect,useRef,useState} from 'react';
import {FlaskConical,Star,Upload} from 'lucide-react';
import {api} from '../api';
import {Dialog} from './shared';
import {DataConnectionsPanel} from './DataConnectionsView';

type Metric={key:string;name:string;unit:string;good:'up'|'down';primary:boolean;source:string};
type Point={date:string;value:number};
type Anomaly={metric:string;name:string;date:string;value:number;baseline:number;changePercent:number;zScore:number;severity:'high'|'medium';good:boolean};
type Experiment={id:string;title:string;hypothesis:string;metric:string;startDate:string;reviewDate:string;rule:{direction:'up'|'down';thresholdPercent:number};status:'proposed'|'running'|'decided'|'declined';outcome:string|null;outcomeNote:string|null};
type Measurement={baseline:number|null;during:number|null;changePercent:number|null;baselinePoints:number;duringPoints:number};
export type ScorecardData={version:number;metrics:Metric[];series:Record<string,Point[]>;anomalies:Anomaly[];experiments:{experiment:Experiment;measurement:Measurement}[];imports:{requestId:string;rows:number;metrics:number;at:string}[]};

const today=()=>new Date().toISOString().slice(0,10);
const shift=(days:number)=>new Date(Date.now()+days*86400000).toISOString().slice(0,10);
function format(value:number|null|undefined,unit=''){if(value===null||value===undefined)return '—';const abs=Math.abs(value);const text=abs>=1000?value.toLocaleString(undefined,{maximumFractionDigits:0}):value.toLocaleString(undefined,{maximumFractionDigits:2});return unit==='%'?text+'%':text;}

function Sparkline({points,flag}:{points:Point[];flag?:Anomaly}){
  const data=points.slice(-30);
  if(data.length<2)return <span className="fe-muted">—</span>;
  const values=data.map(point=>point.value),min=Math.min(...values),max=Math.max(...values),span=max-min||1;
  const path=data.map((point,index)=>`${(index/(data.length-1)*96+2).toFixed(1)},${(22-(point.value-min)/span*18).toFixed(1)}`).join(' ');
  // Colour marks only a flagged move; ordinary noise stays neutral.
  return <svg className={'fe-spark'+(flag?(flag.good?' good':' bad'):'')} viewBox="0 0 100 24" width="100" height="24" aria-hidden="true"><polyline points={path}/></svg>;
}

function ImportData({onClose,onImported}:{onClose:()=>void;onImported:(data:ScorecardData,summary:string)=>void}){
  const [mode,setMode]=useState<'paste'|'file'|'sheet'>('paste'),[csv,setCsv]=useState(''),[url,setUrl]=useState(''),[source,setSource]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const file=useRef<HTMLInputElement>(null);
  async function submit(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{
      const result=await api<{rows:number;metrics:number;scorecard:ScorecardData}>('/scorecard/import',{requestId:crypto.randomUUID(),...(mode==='sheet'?{url}:{csv}),source:source.trim()||undefined});
      onImported(result.scorecard,`Imported ${result.rows.toLocaleString()} value${result.rows===1?'':'s'} across ${result.metrics} metric${result.metrics===1?'':'s'}.`);
    }catch(cause){setError((cause as Error).message);setBusy(false);}
  }
  return <Dialog title="Import scorecard data" onClose={onClose}><form className="fe-form" onSubmit={event=>void submit(event)}>
    <nav className="fe-tabs" aria-label="Import from">{([['paste','Paste CSV'],['file','Upload a CSV'],['sheet','Google Sheet']] as const).map(([value,label])=><button type="button" key={value} aria-pressed={mode===value} onClick={()=>setMode(value)}>{label}</button>)}</nav>
    {mode==='sheet'?<label>Published sheet link<input type="url" required value={url} onChange={event=>setUrl(event.target.value)} placeholder="https://docs.google.com/spreadsheets/d/…/pub?output=csv"/><small>In Google Sheets: File → Share → Publish to web → CSV. Anyone with the link can read a published sheet.</small></label>
      :mode==='file'?<label>CSV file<input ref={file} type="file" accept=".csv,text/csv" required onChange={async event=>{const chosen=event.target.files?.[0];if(chosen){if(chosen.size>2_000_000){setError('A CSV import can be up to 2 MB.');return;}setCsv(await chosen.text());setSource(current=>current||chosen.name.replace(/\.csv$/i,''));}}}/>{csv&&<small>{csv.split(/\r?\n/).length-1} rows ready.</small>}</label>
      :<label>CSV<textarea className="fe-editor" rows={8} required value={csv} onChange={event=>setCsv(event.target.value)} placeholder={'date,Sessions,Signups,Cost per signup\n2026-09-01,1200,40,12.50\n2026-09-02,1100,38,13.10'}/></label>}
    <label>Source name (optional)<input maxLength={80} value={source} onChange={event=>setSource(event.target.value)} placeholder="e.g. GA4 export, Meta Ads, Stripe"/></label>
    <small>Dates go in the first column (or one named date). Each other column is a metric. A long layout with date, metric and value columns works too. Re-importing a date replaces its value.</small>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy||(mode==='sheet'?!url.trim():!csv.trim())}>{busy?'Importing…':'Import'}</button></footer>
  </form></Dialog>;
}

function NewExperiment({metrics,onClose,onSaved}:{metrics:Metric[];onClose:()=>void;onSaved:()=>void}){
  const primary=metrics.find(metric=>metric.primary)||metrics[0];
  const [form,setForm]=useState({title:'',hypothesis:'',metric:primary?.key||'',startDate:today(),reviewDate:shift(14),direction:primary?.good||'up',thresholdPercent:'10'});
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  async function submit(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{await api('/scorecard/experiments',{requestId:crypto.randomUUID(),...form,thresholdPercent:Number(form.thresholdPercent)});onSaved();}
    catch(cause){setError((cause as Error).message);setBusy(false);}
  }
  return <Dialog title="New experiment" onClose={onClose}><form className="fe-form" onSubmit={event=>void submit(event)}>
    <label>Name<input required maxLength={160} value={form.title} onChange={event=>setForm({...form,title:event.target.value})} placeholder="e.g. Shorter signup form"/></label>
    <label>Hypothesis<textarea rows={2} required maxLength={1000} value={form.hypothesis} onChange={event=>setForm({...form,hypothesis:event.target.value})} placeholder="If we … then … because …"/></label>
    <div className="fe-form-row"><label>Primary metric<select value={form.metric} onChange={event=>setForm({...form,metric:event.target.value})}>{metrics.map(metric=><option key={metric.key} value={metric.key}>{metric.name}</option>)}</select></label>
      <label>Starts<input type="date" value={form.startDate} onChange={event=>setForm({...form,startDate:event.target.value})}/></label>
      <label>Review on<input type="date" value={form.reviewDate} onChange={event=>setForm({...form,reviewDate:event.target.value})}/></label></div>
    <fieldset className="fe-rule"><legend>Decision rule, set before the results</legend>Scale it if the metric goes <select aria-label="Direction" value={form.direction} onChange={event=>setForm({...form,direction:event.target.value as 'up'|'down'})}><option value="up">up</option><option value="down">down</option></select> by at least
      <input aria-label="Threshold percent" inputMode="decimal" value={form.thresholdPercent} onChange={event=>setForm({...form,thresholdPercent:event.target.value.replace(/[^\d.]/g,'')})}/>% against the same number of days before it started. Short of that: iterate. The wrong way: stop.</fieldset>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onClose}>Cancel</button><button className="primary" disabled={busy||!form.title.trim()||!form.hypothesis.trim()||!form.metric}>{busy?'Saving…':'Save experiment'}</button></footer>
  </form></Dialog>;
}

/** The scorecard the employee works from: one primary KPI, leading indicators, material moves and experiments. */
export function ScorecardSection({canEdit,owner}:{canEdit:boolean;owner:boolean}){
  const [data,setData]=useState<ScorecardData|null>(null),[dialog,setDialog]=useState<null|'import'|'experiment'>(null),[notice,setNotice]=useState(''),[error,setError]=useState('');
  const load=useCallback(async()=>{try{setData(await api<ScorecardData>('/scorecard'));setError('');}catch(cause){setError((cause as Error).message);}},[]);
  useEffect(()=>{void load();},[load]);
  async function decide(id:string,outcome:string){try{await api(`/scorecard/experiments/${id}/decision`,{outcome});setNotice(`Recorded: ${outcome}.`);await load();}catch(cause){setError((cause as Error).message);}}
  async function propose(id:string,action:'start'|'decline'){const note=action==='decline'?(prompt('Why not? The employee learns from your reason.')??''):'';try{await api(`/scorecard/experiments/${id}/${action}`,action==='decline'?{note}:{});setNotice(action==='start'?'Started. It is measured on its review date.':'Declined.');await load();}catch(cause){setError((cause as Error).message);}}
  async function makePrimary(key:string){try{const result=await api<{scorecard:ScorecardData}>(`/scorecard/metrics/${key}`,{primary:true},'PUT');setData(result.scorecard);}catch(cause){setError((cause as Error).message);}}
  const anomalies=new Map(data?.anomalies.map(item=>[item.metric,item]));
  return <section className="fe-section" aria-label="Scorecard">
    <div className="fe-section-head"><div><h2>Scorecard</h2><small>Your numbers, which it checks each time it starts work. It tells you only when one moves a lot, not about everyday ups and downs.</small></div>
      {canEdit&&data&&data.metrics.length>0&&<button type="button" onClick={()=>setDialog('experiment')}><FlaskConical size={15}/> New experiment</button>}
      {canEdit&&<button type="button" onClick={()=>setDialog('import')}><Upload size={15}/> Import data</button>}</div>
    <DataConnectionsPanel owner={owner} onSynced={load}/>
    <BusinessSnapshot/>
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {data&&data.metrics.length===0&&<div className="fe-empty-row"><Upload size={16}/><span><strong>No metrics yet</strong><small>Connect Google Analytics, Search Console or Plausible above, or import a CSV from your ads, store or billing tool. The employee watches it every cycle and brings you only what moved.</small></span></div>}
    {data&&data.metrics.length>0&&<div className="fe-table-wrap"><table className="fe-table fe-scorecard"><thead><tr><th>Metric</th><th>Latest</th><th>vs. 14-day</th><th>Trend</th><th>Source</th></tr></thead><tbody>
      {data.metrics.map(metric=>{const series=data.series[metric.key]||[];const latest=series[series.length-1];const window=series.slice(-15,-1);const mean=window.length?window.reduce((sum,point)=>sum+point.value,0)/window.length:null;
        const change=latest&&mean?(latest.value-mean)/Math.abs(mean)*100:null;const flag=anomalies.get(metric.key);
        return <tr key={metric.key} className={flag?(flag.good?'good':'bad'):''}>
          <td><span className="fe-cell-metric">{metric.primary?<Star size={13} aria-label="Primary KPI"/>:canEdit?<button type="button" className="fe-inline-button" aria-label={'Make '+metric.name+' the primary KPI'} title="Make primary KPI" onClick={()=>void makePrimary(metric.key)}><Star size={13}/></button>:<span/>}<strong>{metric.name}</strong>{flag&&<span className={'fe-pill '+(flag.good?'ok':'attn')}>{flag.severity==='high'?'Major move':'Moved'}</span>}</span></td>
          <td className="fe-num">{format(latest?.value,metric.unit)}<small>{latest?.date||''}</small></td>
          <td className={'fe-num '+(change===null||!flag?'':((change>=0)===(metric.good==='up')?'good':'bad'))}>{change===null?'—':`${change>=0?'+':''}${change.toFixed(1)}%`}</td>
          <td><Sparkline points={series} flag={flag}/></td><td className="fe-cell-muted">{metric.source}</td></tr>;})}
    </tbody></table></div>}
    {data&&data.experiments.length>0&&<div className="fe-table-wrap fe-experiments"><table className="fe-table"><thead><tr><th>Experiment</th><th>Rule</th><th>Review</th><th>Measured</th><th>Status</th></tr></thead><tbody>
      {data.experiments.map(({experiment,measurement})=>{const due=experiment.status==='running'&&experiment.reviewDate<=today();const metric=data.metrics.find(item=>item.key===experiment.metric);
        return <tr key={experiment.id}><td><strong>{experiment.title}</strong><small className="fe-block">{experiment.hypothesis}</small></td>
          <td className="fe-cell-muted">{metric?.name||experiment.metric} {experiment.rule.direction==='up'?'≥ +':'≤ −'}{experiment.rule.thresholdPercent}%</td>
          <td className="fe-cell-muted">{experiment.reviewDate}</td>
          <td className="fe-num">{measurement.changePercent===null?'—':`${measurement.changePercent>=0?'+':''}${measurement.changePercent}%`}</td>
          <td>{experiment.status==='proposed'?(owner?<span className="fe-decide"><button type="button" onClick={()=>void propose(experiment.id,'start')}>Start</button><button type="button" onClick={()=>void propose(experiment.id,'decline')}>Decline</button></span>:<span className="fe-pill attn">Proposed</span>):experiment.status==='declined'?<span className="fe-pill">Declined</span>:experiment.status==='decided'?<span className="fe-pill">{experiment.outcome}</span>:due?(owner?<span className="fe-decide">{(['scale','iterate','stop'] as const).map(outcome=><button type="button" key={outcome} onClick={()=>void decide(experiment.id,outcome)}>{outcome[0].toUpperCase()+outcome.slice(1)}</button>)}</span>:<span className="fe-pill attn">Decision due</span>):<span className="fe-pill">Running</span>}</td></tr>;})}
    </tbody></table></div>}
    {dialog==='import'&&<ImportData onClose={()=>setDialog(null)} onImported={(next,summary)=>{setData(next);setNotice(summary);setDialog(null);}}/>}
    {dialog==='experiment'&&data&&<NewExperiment metrics={data.metrics} onClose={()=>setDialog(null)} onSaved={()=>{setDialog(null);setNotice('Experiment saved. The employee measures it on its review date and applies the rule.');void load();}}/>}
  </section>;
}

type Business={crm:unknown|null;ads:{campaigns:{name:string;spend:number;leads:number}[]}|null;pipeline:string[];paid:string[]};
/** The CRM and ad spend, when connected: leads by source, the pipeline, and each campaign's week. */
function BusinessSnapshot(){
  const [data,setData]=useState<Business|null>(null);
  useEffect(()=>{void api<Business>('/data-connections/business').then(setData).catch(()=>setData(null));},[]);
  if(!data||(!data.crm&&!data.ads))return null;
  return <div className="fe-business">
    {data.pipeline.length>0&&<section aria-label="Pipeline"><h4>Pipeline</h4><ul>{data.pipeline.map(line=><li key={line}>{line}</li>)}</ul></section>}
    {data.paid.length>0&&<section aria-label="Paid"><h4>Paid, last seven days</h4><ul>{data.paid.map(line=><li key={line} className={/no leads$/.test(line)?'bad':''}>{line}</li>)}</ul></section>}
  </div>;
}
