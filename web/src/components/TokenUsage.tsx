import {useEffect,useId,useMemo,useState} from 'react';
import type {Run} from '../types';

type UsageRange='day'|'week'|'month';
type UsageBucket={key:string;label:string;shortLabel:string;tokens:number};
export type UsagePoint={createdAt:number;totalTokens:number|null};

function isLiveRun(run:Run){return run.goal.provider.kind==='compatible'&&run.modelCalls>0;}
function reportedTokens(run:Run){return (run.inputTokens??0)+(run.outputTokens??0);}
function runTime(run:Run){
  const value=new Date(run.created);
  return Number.isNaN(value.getTime())?null:value;
}
function startOfDay(value:Date){return new Date(value.getFullYear(),value.getMonth(),value.getDate());}
function sameDay(left:Date,right:Date){return left.getFullYear()===right.getFullYear()&&left.getMonth()===right.getMonth()&&left.getDate()===right.getDate();}

export function useDailyClock(){
  const [now,setNow]=useState(()=>new Date());
  useEffect(()=>{
    const next=new Date(now.getFullYear(),now.getMonth(),now.getDate()+1,0,0,1);
    const timer=window.setTimeout(()=>setNow(new Date()),Math.max(1000,next.getTime()-Date.now()));
    return ()=>window.clearTimeout(timer);
  },[now]);
  return now;
}

function usageTotals(runs:Run[],now:Date){
  const live=runs.filter(isLiveRun);
  const today=live.filter(run=>{const time=runTime(run);return time!=null&&sameDay(time,now);});
  const reported=live.reduce((total,run)=>total+reportedTokens(run),0);
  const reportedToday=today.reduce((total,run)=>total+reportedTokens(run),0);
  const reserved=live.reduce((total,run)=>total+(run.reservedTokens??0),0);
  const unknown=live.filter(run=>run.inputTokens==null||run.outputTokens==null||!!run.reservedTokens);
  const unknownToday=today.filter(run=>run.inputTokens==null||run.outputTokens==null||!!run.reservedTokens);
  const searchAttempts=runs.reduce((count,run)=>count+(run.capabilities??[]).filter(call=>call.name==='thaddeus_search_public_web').length,0);
  return {live,reported,reportedToday,reserved,unknown,unknownToday,searchAttempts};
}

function usageSeries(points:UsagePoint[],range:UsageRange,now:Date):UsageBucket[]{
  const addTokens=(start:Date,end:Date)=>points.reduce((total,point)=>{
    const time=new Date(point.createdAt*1000);
    return time>=start&&time<end?total+(point.totalTokens??0):total;
  },0);
  if(range==='day'){
    const day=startOfDay(now);
    return Array.from({length:6},(_,index)=>{
      const start=new Date(day.getFullYear(),day.getMonth(),day.getDate(),index*4);
      const end=new Date(day.getFullYear(),day.getMonth(),day.getDate(),(index+1)*4);
      const hour=start.toLocaleTimeString([],{hour:'numeric'});
      return {key:start.toISOString(),label:`${hour} to ${end.toLocaleTimeString([],{hour:'numeric'})}`,shortLabel:hour,tokens:addTokens(start,end)};
    });
  }
  const days=range==='week'?7:30;
  const finalDay=startOfDay(now);
  return Array.from({length:days},(_,index)=>{
    const start=new Date(finalDay.getFullYear(),finalDay.getMonth(),finalDay.getDate()-(days-1-index));
    const end=new Date(start.getFullYear(),start.getMonth(),start.getDate()+1);
    return {key:start.toISOString(),label:start.toLocaleDateString([],{weekday:'short',month:'short',day:'numeric'}),shortLabel:range==='week'?start.toLocaleDateString([],{weekday:'narrow'}):String(start.getDate()),tokens:addTokens(start,end)};
  });
}

export function UsageChart({points,now}:{points:UsagePoint[];now:Date}){
  const [range,setRange]=useState<UsageRange>('week');
  const series=useMemo(()=>usageSeries(points,range,now),[points,range,now]);
  const max=Math.max(1,...series.map(bucket=>bucket.tokens));
  const total=series.reduce((sum,bucket)=>sum+bucket.tokens,0);
  const title=range==='day'?'Today':range==='week'?'Last 7 days':'Last 30 days';
  return <section className="usage-history" aria-label="Token usage history">
    <div className="usage-history-heading"><div><small>USAGE HISTORY</small><strong>{title} · {total.toLocaleString()} reported</strong></div><div className="usage-range" role="group" aria-label="Usage graph range">
      {(['day','week','month'] as UsageRange[]).map(value=><button type="button" key={value} aria-pressed={range===value} onClick={()=>setRange(value)}>{value[0].toUpperCase()+value.slice(1)}</button>)}
    </div></div>
    <div className={`usage-chart ${range}`} role="img" aria-label={`${title} token usage. ${series.map(bucket=>`${bucket.label}: ${bucket.tokens.toLocaleString()} reported tokens`).join('; ')}`}>
      {series.map((bucket,index)=><div className="usage-bar-column" key={bucket.key} title={`${bucket.label}: ${bucket.tokens.toLocaleString()} reported tokens`}>
        <span className="usage-bar-value">{bucket.tokens>0?bucket.tokens.toLocaleString():''}</span>
        <span className="usage-bar-track"><span className="usage-bar" style={{height:`${Math.max(bucket.tokens>0?5:0,(bucket.tokens/max)*100)}%`}}/></span>
        <span className="usage-bar-label">{range!=='month'||index%5===0||index===series.length-1?bucket.shortLabel:''}</span>
      </div>)}
    </div>
    <p className="usage-history-note">Reported model tokens are grouped by each task’s request date in your local time. Unreported usage is excluded from bar heights.</p>
  </section>;
}

export function ModelUsageButton({model,runs,online,expanded,onOpen}:{model:string;runs:Run[];online:boolean;expanded:boolean;onOpen:(trigger:HTMLButtonElement)=>void}){
  const tooltipId=useId();
  const now=useDailyClock();
  const {reported,reportedToday,reserved,unknownToday}=usageTotals(runs,now);
  return <button type="button" className="model-usage" aria-label={`${model}: token usage`} aria-describedby={tooltipId}
    aria-expanded={expanded} aria-controls="activity-log" onClick={event=>onOpen(event.currentTarget)}>
    <span className={'connection-dot'+(online?' connected':'')} aria-hidden="true"/>
    <span className="model-name">{model}</span>
    <span className="model-usage-tooltip" id={tooltipId} role="tooltip">
      <span>{online?'Host connected':'Host disconnected'}</span>
      <strong>{reportedToday.toLocaleString()} reported tokens today</strong>
      <span>Resets at local midnight · retained total {reported.toLocaleString()}</span>
      {reserved>0&&<span>{reserved.toLocaleString()} reserved · not measured usage</span>}
      {unknownToday.length>0&&<span>Today is incomplete · {unknownToday.length} task{unknownToday.length===1?'':'s'} with unreported usage</span>}
      <span>Open Log → Info for history</span>
    </span>
  </button>;
}

export function TokenUsage({runs,onRun,expanded,onExpandedChange}:{runs:Run[];onRun:(id:string)=>void;expanded:boolean;onExpandedChange:(expanded:boolean)=>void}){
  const now=useDailyClock();
  const {live,reported,reportedToday,reserved,unknown,unknownToday,searchAttempts}=usageTotals(runs,now);
  const format=(value:number)=>value.toLocaleString();
  return <details className="token-usage" aria-label="Token usage" open={expanded} onToggle={event=>onExpandedChange(event.currentTarget.open)}>
    <summary>Today · {format(reportedToday)} reported{unknownToday.length>0&&` · ${unknownToday.length} incomplete`} · retained total {format(reported)}</summary>
    <UsageChart points={live.flatMap(run=>{const date=runTime(run);return date?[{createdAt:date.getTime()/1000,totalTokens:reportedTokens(run)}]:[];})} now={now}/>
    <p>Today resets at local midnight. The retained total covers task history on this host, including conversation context sent again. Scripted demos are excluded. These are provider-reported counts, not a bill or your Codex account quota.</p>
    {unknown.length>0&&<p role="status">Some usage is unreported. The reported total is incomplete; reservations are held against task allowances and are not measured consumption.</p>}
    <p>Task allowances prevent further calls once exhausted. An uncertified provider can exceed a requested limit; strict token admission refuses those providers before dispatch. Dollar cost is not tracked.</p>
    {searchAttempts>0&&<p>{format(searchAttempts)} search attempt{searchAttempts===1?'':'s'} recorded in retained history, including failures and unknown outcomes. Search requests are separate from model tokens; see task receipts for details. No provider bill is inferred.</p>}
    <ul>{live.slice(0,8).map(run=><li key={run.id}><button className="text-button" onClick={()=>onRun(run.id)}>{run.goal.objective.slice(0,65)}{run.goal.objective.length>65?'…':''}</button><small>
      {' '}Input {run.inputTokens==null?'unreported':format(run.inputTokens)} · output {run.outputTokens==null?'unreported':format(run.outputTokens)} · allowance charged {format(run.chargedTokens??0)} / {format(run.goal.limits.maxTotalTokens)} · reserved {format(run.reservedTokens??0)} · remaining allowance {format(Math.max(0,run.goal.limits.maxTotalTokens-(run.chargedTokens??0)-(run.reservedTokens??0)))}
    </small></li>)}</ul>
    {!live.length&&<p>No live model dispatches in retained history.</p>}
  </details>;
}
