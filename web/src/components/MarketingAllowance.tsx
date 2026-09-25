import {useEffect,useState} from 'react';
import {api} from '../api';

type Window={key:string;label:string;usedPercent:number;windowMinutes:number|null;resetsAt:number|null};
type Sample={accountKey:string;accountLabel:string;plan:string|null;observedAt:number;resetCredits:number|null;windows:Window[]};
type History={configured:boolean;latest:Sample|null;samples:Sample[];lastAttemptAt:number|null;error:string|null;stale:boolean;pollSeconds:number};
const time=(seconds:number)=>new Date(seconds*1000).toLocaleString();
const remaining=(window:Window)=>Math.max(0,100-window.usedPercent);

function WindowHistory({samples,windowKey}:{samples:Sample[];windowKey:string}){
  const rows=samples.flatMap(sample=>{
    const window=sample.windows.find(w=>w.key===windowKey);
    return window?[{time:sample.observedAt,window}]:[];
  });
  const changes=rows.flatMap((row,index)=>{
    const previous=rows[index-1];
    if(previous&&previous.window.usedPercent===row.window.usedPercent&&previous.window.resetsAt===row.window.resetsAt&&previous.window.windowMinutes===row.window.windowMinutes)return [];
    const changedWindow=previous&&(previous.window.resetsAt!==row.window.resetsAt||previous.window.windowMinutes!==row.window.windowMinutes);
    const drop=previous?row.window.usedPercent-previous.window.usedPercent:0;
    const description=!previous?'First observation in this range':changedWindow?'Allowance window changed':drop<0?'Allowance increased / adjusted':`${drop.toLocaleString()} percentage points used since previous check`;
    return [{...row,description}];
  });
  return <ul className="marketing-usage-receipts">{[...changes].reverse().slice(0,60).map(row=><li key={row.time}>
    <strong>{remaining(row.window).toLocaleString()}% remaining</strong><small>{time(row.time)}</small><span>{row.description}</span>
  </li>)}{changes.length>60&&<li>Showing the latest 60 changes. Download history for every saved observation.</li>}</ul>;
}

export function MarketingAllowance(){
  const [history,setHistory]=useState<History|null>(null),[error,setError]=useState(''),[range,setRange]=useState(7);
  useEffect(()=>{
    let active=true;
    const refresh=async()=>{
      try{const value=await api<History>('/marketing/allowance');if(active){setHistory(value);setError('');}}
      catch{if(active)setError('Shared allowance is unavailable on this host.');}
    };
    void refresh();
    const timer=setInterval(()=>{if(document.visibilityState==='visible')void refresh();},30000);
    return()=>{active=false;clearInterval(timer);};
  },[]);
  const latest=history?.latest;
  const stale=history?.stale||!!error||(latest?Date.now()/1000-latest.observedAt>600:true);
  const samples=(history?.samples??[]).filter(sample=>sample.observedAt>=Date.now()/1000-range*86400);
  function download(){
    if(!history)return;
    const url=URL.createObjectURL(new Blob([JSON.stringify({source:'codex_app_server',scope:'Host Codex account; account-wide, not attributable to this employee',...history},null,2)],{type:'application/json'}));
    const link=document.createElement('a');link.href=url;link.download='shared-allowance-history.json';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  }
  return <section className="business-side-section marketing-allowance" aria-label="Shared account allowance">
    <div className="side-section-title"><h2>Shared allowance</h2><small>{stale?'Last saved':'Account-wide'}</small></div>
    {!latest?<p className="sidebar-muted">{error||history?.error||(history?.configured?'Waiting for the first account check…':'Account tracking is not connected on this host.')}</p>:<>
      <small>Host Codex account · {latest.accountLabel}{latest.plan?` · ${latest.plan}`:''}</small>
      <div className="allowance-windows">{latest.windows.map(window=><div className="allowance-window" key={window.key}>
        <div><span>{window.label}</span><strong>{remaining(window).toLocaleString()}% remaining{stale?' · saved':''}</strong></div>
        <meter min={0} max={100} value={remaining(window)} aria-label={`${window.label} remaining`}/>
        <small>{window.resetsAt?`Scheduled reset ${time(window.resetsAt)}`:'Reset time unreported'}</small>
        {!stale&&remaining(window)<=10&&<p role="status">Allowance is low. Review usage before starting more model work.</p>}
      </div>)}</div>
      <small>Checked {time(latest.observedAt)} · checks every 5 minutes while the host runs</small>
      {stale&&<p role="status">This is a saved observation, not a current balance. {history?.error||error||(!history?.configured?'Account tracking is disconnected.':'A fresh check is due.')}</p>}
      <p>Includes other Codex work using this account. Employee tokens below cannot explain the entire percentage change.</p>
      <details><summary>Allowance history</summary>
        <div className="usage-range" role="group" aria-label="Allowance history range">{[[1,'Day'],[7,'Week'],[30,'Month']].map(([days,label])=><button type="button" key={days} aria-pressed={range===days} onClick={()=>setRange(Number(days))}>{label}</button>)}</div>
        <p>Last {range===1?'24 hours':`${range} days`}. Observed changes only; no history is invented for gaps. A window change can be a scheduled renewal or a manual reset.</p>
        {latest.windows.map(window=><section key={window.key} aria-label={`${window.label} observations`}><h3>{window.label}</h3><WindowHistory samples={samples} windowKey={window.key}/></section>)}
        {samples.length===0&&<p>No observations in this range.</p>}
        <p>History starts when tracking is connected. Read-only checks make zero model calls. Changes are not attributed to a specific app or request.</p>
        <p>Reset credits available: {latest.resetCredits??'unreported'}. Resets require your separate explicit instruction; this tracker cannot redeem them.</p>
        <button type="button" onClick={download}>Download 30-day history</button>
      </details>
    </>}
  </section>;
}
