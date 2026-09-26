import {useCallback,useEffect,useState} from 'react';
import {api} from '../api';
import {useDailyClock,type UsagePoint} from '../components/TokenUsage';
import {RubricPanel} from './Rubric';

/** The employee's spend: shift turns from its own shift records, chat turns from the chat receipts. Today resets at local midnight. */
type ShiftPoint=UsagePoint&{stage:string;shift:string};
type ShiftRow={id:string;status:string;startedAt:string;endedAt:string|null;turnsUsed:number;turnBudget:number;tokensUsed:number;tokenBudget:number|null;cycles:number;created:number};
type QualityItem={item:string;average:number};
type Quality={summary:{reviewed:number;average?:number;firstDraft?:number;weakest?:QualityItem[];strongest?:QualityItem[]};entries:{at:string;title:string;type:string;score:number;first:number;passes:number}[]};
type EmployeeUsageData={live:boolean;runtime:string;points:ShiftPoint[];byStage:Record<string,number>;shifts:ShiftRow[];quality?:Quality};
type ChatUsage={chat:(UsagePoint&{id:string})[]};

export type UsageSummary={points:UsagePoint[];today:number;week:number;month:number;shiftsToday:number;turnsToday:number;data:EmployeeUsageData;chat:UsagePoint[]};

export function useEmployeeUsage(enabled:boolean){
  const [summary,setSummary]=useState<UsageSummary|null>(null);
  const now=useDailyClock();
  const load=useCallback(async()=>{
    if(!enabled)return;
    try{
      const [data,chat]=await Promise.all([api<EmployeeUsageData>('/employee/usage'),api<ChatUsage>('/marketing/usage').catch(()=>({chat:[]}))]);
      const points=[...data.points,...chat.chat];
      const since=(days:number)=>new Date(now.getFullYear(),now.getMonth(),now.getDate()-days).getTime()/1000;
      const sum=(start:number)=>points.filter(point=>point.createdAt>=start).reduce((total,point)=>total+(point.totalTokens??0),0);
      const midnight=since(0);
      const todayShifts=data.shifts.filter(shift=>new Date(shift.startedAt).getTime()/1000>=midnight);
      setSummary({points,today:sum(midnight),week:sum(since(6)),month:sum(since(29)),shiftsToday:todayShifts.length,turnsToday:todayShifts.reduce((total,shift)=>total+shift.turnsUsed,0),data,chat:chat.chat});
    }catch{/* usage is informational; the chip simply shows no numbers */}
  },[enabled,now]);
  useEffect(()=>{
    void load();
    const timer=setInterval(()=>{if(document.visibilityState==='visible')void load();},60000);
    return()=>clearInterval(timer);
  },[load]);
  return summary;
}

const stageNames:Record<string,string>={prioritize:'Planning',create:'Writing and self-review',institutionalize:'Learning',report:'Shift reports',sense:'Listening',measure:'Measuring',decide:'Deciding',launch:'Launch checks',align:'Routing'};
const tokens=(value:number)=>value.toLocaleString();
const when=(value:string)=>new Date(value).toLocaleString(undefined,{month:'short',day:'numeric',hour:'numeric',minute:'2-digit'});

/** Team → the employee → Usage: today, the week and the month, a chart, where the tokens went, and the recent shifts. */
export function EmployeeUsage({summary}:{summary:UsageSummary|null}){
  const now=useDailyClock();
  if(!summary)return <p className="fe-muted">Loading usage…</p>;
  const stages=Object.entries(summary.data.byStage).sort((a,b)=>b[1]-a[1]);
  const stageTotal=Math.max(1,stages.reduce((total,[,value])=>total+value,0));
  const chatTotal=summary.chat.reduce((total,point)=>total+(point.totalTokens??0),0);
  return <div className="fe-usage-page">
    <dl className="fe-stats">
      <div><dt>Today</dt><dd>{tokens(summary.today)}</dd></div>
      <div><dt>Last 7 days</dt><dd>{tokens(summary.week)}</dd></div>
      <div><dt>Last 30 days</dt><dd>{tokens(summary.month)}</dd></div>
      <div><dt>Turns today</dt><dd>{summary.turnsToday}</dd></div>
    </dl>
    <small className="fe-muted">Metered tokens, reported by the provider for each turn. Today resets at local midnight. {summary.data.live?'Live model: '+summary.data.runtime+'.':'Scripted stand-in: no model spend.'}</small>
    <DailyBars points={summary.points} now={now}/>
    <section aria-label="Where the tokens went"><h3>Where the tokens went</h3>
      {stages.length===0&&chatTotal===0?<p className="fe-muted">No turns yet.</p>:<ul className="fe-usage-stages">
        {stages.map(([stage,value])=><li key={stage}><span>{stageNames[stage]||stage}</span><span className="fe-bar"><i style={{width:`${Math.round(value/stageTotal*100)}%`}}/></span><strong>{tokens(value)}</strong></li>)}
        {chatTotal>0&&<li><span>Chat</span><span className="fe-bar"/><strong>{tokens(chatTotal)}</strong></li>}
      </ul>}
    </section>
    <RubricPanel canEdit/>
    <section aria-label="Recent shifts"><h3>Recent shifts</h3>
      {summary.data.shifts.length===0?<p className="fe-muted">No shifts yet.</p>:<table className="fe-table"><thead><tr><th>Started</th><th>Status</th><th>Turns</th><th>Tokens</th><th>Made</th></tr></thead><tbody>
        {summary.data.shifts.map(shift=><tr key={shift.id}><td>{when(shift.startedAt)}</td><td>{shift.status}</td><td className="fe-num">{shift.turnsUsed}/{shift.turnBudget}</td>
          <td className="fe-num">{tokens(shift.tokensUsed)}{shift.tokenBudget?` / ${tokens(shift.tokenBudget)}`:''}</td><td className="fe-num">{shift.created}</td></tr>)}
      </tbody></table>}
    </section>
  </div>;
}

/** Tokens per day for the last two weeks, today last. */
export function DailyBars({points,now,days=14}:{points:UsagePoint[];now:Date;days?:number}){
  const buckets=Array.from({length:days},(_,index)=>{
    const start=new Date(now.getFullYear(),now.getMonth(),now.getDate()-(days-1-index));
    const from=start.getTime()/1000,to=from+86400;
    return {start,total:points.filter(point=>point.createdAt>=from&&point.createdAt<to).reduce((sum,point)=>sum+(point.totalTokens??0),0)};
  });
  const top=Math.max(1,...buckets.map(bucket=>bucket.total));
  return <figure className="fe-daily" aria-label={`Tokens per day, last ${days} days`}>
    <div className="fe-daily-bars">{buckets.map((bucket,index)=><div key={index} className={'fe-daily-day'+(index===days-1?' today':'')} title={`${bucket.start.toLocaleDateString(undefined,{weekday:'short',month:'short',day:'numeric'})}: ${tokens(bucket.total)} tokens`}>
      <span className="fe-daily-value">{bucket.total?compact(bucket.total):''}</span>
      <span className="fe-daily-track"><i style={{height:`${Math.max(bucket.total?3:0,Math.round(bucket.total/top*100))}%`}}/></span>
      <small>{index===days-1?'Today':bucket.start.toLocaleDateString(undefined,{weekday:'narrow'})+bucket.start.getDate()}</small>
    </div>)}</div>
    <figcaption className="fe-muted">Tokens per day, last {days} days</figcaption>
  </figure>;
}
const compact=(value:number)=>value>=1_000_000?(value/1_000_000).toFixed(1)+'M':value>=1000?Math.round(value/1000)+'k':String(value);

/** Settings → Usage: the same numbers as the employee's Usage tab, with the way there. */
export function UsageOverview({summary,onOpen}:{summary:UsageSummary|null;onOpen:()=>void}){
  const now=useDailyClock();
  if(!summary)return <p className="fe-muted">Loading usage…</p>;
  return <div className="fe-usage-page">
    <dl className="fe-stats">
      <div><dt>Today</dt><dd>{tokens(summary.today)}</dd></div>
      <div><dt>Last 7 days</dt><dd>{tokens(summary.week)}</dd></div>
      <div><dt>Last 30 days</dt><dd>{tokens(summary.month)}</dd></div>
    </dl>
    <DailyBars points={summary.points} now={now}/>
    <small className="fe-muted">Shift and chat turns, as reported by the provider. Today resets at local midnight. These are not your plan’s remaining quota or a bill.</small>
    <div><button type="button" onClick={onOpen}>Usage by stage, shifts and quality →</button></div>
  </div>;
}

/** The top bar's employee chip: point at it (or focus it) for today's spend; click for the details. */
export function UsageHoverCard({summary,onOpen}:{summary:UsageSummary|null;onOpen:()=>void}){
  if(!summary)return null;
  return <div className="fe-usage-hover" role="tooltip">
    <strong>{tokens(summary.today)} tokens today</strong>
    <small>{summary.turnsToday} turn{summary.turnsToday===1?'':'s'} across {summary.shiftsToday} shift{summary.shiftsToday===1?'':'s'} · resets at midnight</small>
    <small>7 days: {tokens(summary.week)} · 30 days: {tokens(summary.month)}</small>
    <button type="button" className="fe-link" onClick={onOpen}>Usage details →</button>
  </div>;
}

