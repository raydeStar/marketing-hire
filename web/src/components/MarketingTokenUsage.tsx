import {useCallback,useEffect,useState} from 'react';
import {RefreshCw} from 'lucide-react';
import {api} from '../api';
import {UsageChart,useDailyClock,type UsagePoint} from './TokenUsage';
import {MarketingAllowance} from './MarketingAllowance';

type UsageEvent=UsagePoint&{id:string;kind:'chat'|'autonomous';source:string;inputTokens:number|null;outputTokens:number|null;status:string};
type UsageHistory={chat:UsageEvent[];autonomous:{events:UsageEvent[];reservedTokens:number}|null;autonomousAvailable:boolean;fixture:boolean;updatedAt:string};

export function MarketingTokenUsage(){
  const [history,setHistory]=useState<UsageHistory|null>(null),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  const now=useDailyClock();
  const refresh=useCallback(async()=>{
    setBusy(true);
    try{setHistory(await api<UsageHistory>('/marketing/usage'));setError('');}
    catch(e){setError((e as Error).message);}
    finally{setBusy(false);}
  },[]);
  useEffect(()=>{
    void refresh();
    const timer=setInterval(()=>{if(document.visibilityState==='visible')void refresh();},30000);
    return()=>clearInterval(timer);
  },[refresh]);
  const events=[...(history?.chat??[]),...(history?.autonomous?.events??[])];
  const today=new Date(now.getFullYear(),now.getMonth(),now.getDate()).getTime()/1000;
  const total=(start:number)=>events.filter(e=>e.createdAt>=start).reduce((sum,e)=>sum+(e.totalTokens??0),0);
  const week=new Date(now.getFullYear(),now.getMonth(),now.getDate()-6).getTime()/1000;
  const month=new Date(now.getFullYear(),now.getMonth(),now.getDate()-29).getTime()/1000;
  const unknown=events.filter(e=>e.createdAt>=month&&e.totalTokens===null).length;
  return <><MarketingAllowance/><section className="business-side-section marketing-token-usage" aria-label="Employee token usage">
    <div className="side-section-title"><h2>Receipts: chat and campaign work</h2><button type="button" aria-label="Refresh token usage" disabled={busy} onClick={()=>void refresh()}><RefreshCw size={14}/></button></div>
    {!history?<p className="sidebar-muted">{error?'Usage could not be loaded.':'Loading reported usage…'}</p>:<>
      <small>The receipts behind chat and campaign work, as each turn reported them. The totals above include shifts.</small>
      {unknown>0&&<p role="status">Last 30 days incomplete · {unknown} {unknown===1?'entry':'entries'} with unreported usage.</p>}
      {!history.autonomousAvailable&&<p role="status">Autonomous usage is unavailable. Totals currently include saved Chat receipts only.</p>}
      {(history.autonomous?.reservedTokens??0)>0&&<p>{history.autonomous!.reservedTokens.toLocaleString()} reserved against work allowances · not measured consumption.</p>}
      <details><summary>Day / week / month history</summary>
        <UsageChart points={events} now={now}/>
        <p>Today resets at local midnight; week and month show the last 7 and 30 days. Repeated conversation context counts each time it is sent.</p>
        <p>Autonomous work uses individual request receipts where available. Chat uses reported turn totals; older Chat entries may be unreported. These counts are not your subscription’s remaining quota or a bill.</p>
        {history.fixture&&<p>Disposable test workspace. No live usage is represented.</p>}
        <ul className="marketing-usage-receipts">{[...events].sort((a,b)=>b.createdAt-a.createdAt).slice(0,8).map(e=><li key={e.id}><strong>{e.kind==='chat'?'Chat':'Autonomous work'} · {e.totalTokens===null?'Unreported':e.totalTokens.toLocaleString()+' tokens'}</strong><small>{new Date(e.createdAt*1000).toLocaleString()} · {e.source==='provider_receipt'?'Request receipt':e.source==='historical_unknown'?'Historical entry':'Turn total'}</small></li>)}</ul>
        <small>Updated {new Date(history.updatedAt).toLocaleTimeString()}</small>
      </details>
    </>}
    {error&&<p role="alert">{history?'Refresh failed; showing the last saved view. ':''}{error}</p>}
  </section></>;
}
