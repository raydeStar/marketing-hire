import {useCallback,useEffect,useState} from 'react';
import {BarChart3,Handshake,Link2,Megaphone,RefreshCw,Search,Unplug} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {Dialog} from './shared';

export type DataKind='google-analytics'|'search-console'|'plausible'|'hubspot'|'meta-ads';
type Kind=DataKind;
type Connection={id:string;kind:Kind;status:'authorizing'|'choose'|'ready'|'error';account:string|null;resource:string|null;resourceName:string|null;metrics:string[];lastSyncAt:string|null;lastError:string|null;lastRows:number|null};
type KindInfo={kind:Kind;name:string;label:string;metrics:{id:string;name:string;standard:boolean}[]};
export type DataConnectionsData={googleReady:boolean;kinds:KindInfo[];connections:Connection[]};
type Resource={id:string;name:string};

const icon=(kind:Kind)=>kind==='search-console'?<Search size={16}/>:kind==='hubspot'?<Handshake size={16}/>:kind==='meta-ads'?<Megaphone size={16}/>:<BarChart3 size={16}/>;
const seconds=(value:string)=>new Date(value).getTime()/1000;

function Metrics({info,value,onChange}:{info:KindInfo;value:string[];onChange:(next:string[])=>void}){
  return <fieldset className="fe-checks"><legend>Metrics to track</legend>{info.metrics.map(metric=><label key={metric.id}><input type="checkbox" checked={value.includes(metric.id)} onChange={event=>onChange(event.target.checked?[...value,metric.id]:value.filter(item=>item!==metric.id))}/>{metric.name}</label>)}</fieldset>;
}

/** Choose the GA4 property or Search Console site a signed-in Google account can read. */
function Choose({connection,info,onDone}:{connection:Connection;info:KindInfo;onDone:(message:string)=>void}){
  const [resources,setResources]=useState<Resource[]|null>(null),[resource,setResource]=useState(''),[metrics,setMetrics]=useState(info.metrics.filter(item=>item.standard).map(item=>item.id));
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  useEffect(()=>{void api<Resource[]>(`/data-connections/${connection.id}/resources`).then(list=>{setResources(list);setResource(list[0]?.id||'');}).catch(cause=>setError((cause as Error).message));},[connection.id]);
  async function save(){
    if(!resource||busy)return;setBusy(true);setError('');
    try{const saved=await api<Connection>(`/data-connections/${connection.id}`,{resource,resourceName:resources?.find(item=>item.id===resource)?.name,metrics},'PUT');
      if(saved.status==='error')setError(saved.lastError||'The first sync failed.');else onDone(`${info.name} connected: ${saved.lastRows??0} values in the scorecard.`);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <div className="fe-form">
    <p className="fe-muted">Signed in{connection.account?` as ${connection.account}`:''}. Choose what to read.</p>
    {resources===null&&!error&&<p className="fe-muted">Loading what this account can read…</p>}
    {resources?.length===0&&<p className="fe-alert">This account can’t read any {info.label}s. Sign in with the account that owns it, or ask for read access.</p>}
    {resources&&resources.length>0&&<label>{info.label}<select value={resource} onChange={event=>setResource(event.target.value)}>{resources.map(item=><option key={item.id} value={item.id}>{item.name}</option>)}</select></label>}
    <Metrics info={info} value={metrics} onChange={setMetrics}/>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="primary" disabled={busy||!resource||metrics.length===0} onClick={()=>void save()}>{busy?'Reading…':'Connect and sync'}</button></footer>
  </div>;
}

export function ConnectData({data,onClose,onChanged,initial=null}:{data:DataConnectionsData;onClose:()=>void;onChanged:(message?:string)=>Promise<void>;initial?:Kind|null}){
  const [pending,setPending]=useState<{id:string;kind:Kind}|null>(null),[plausible,setPlausible]=useState(initial==='plausible'),[token,setToken]=useState<'hubspot'|'meta-ads'|null>(initial==='hubspot'||initial==='meta-ads'?initial:null),[error,setError]=useState('');
  const [address,setAddress]=useState('https://plausible.io'),[site,setSite]=useState(''),[key,setKey]=useState(''),[busy,setBusy]=useState(false);
  const info=(kind:Kind)=>data.kinds.find(item=>item.kind===kind)!;
  const [metrics,setMetrics]=useState(info('plausible').metrics.filter(item=>item.standard).map(item=>item.id));
  const current=pending?data.connections.find(item=>item.id===pending.id):null;
  // While Google's consent tab is open, check back until the host has the answer.
  useEffect(()=>{if(!pending||current?.status!=='authorizing')return;const timer=setInterval(()=>void onChanged(),2000);return()=>clearInterval(timer);},[pending,current?.status]);
  useEffect(()=>{if((initial==='google-analytics'||initial==='search-console')&&data.googleReady)void google(initial);},[]);
  async function google(kind:Kind){
    setError('');
    // Opened during the click so it isn't blocked as a pop-up; it goes to Google once the host has prepared the sign-in.
    const tab=window.open('about:blank','_blank');
    try{const started=await api<{id:string;authorizationUrl:string}>('/data-connections/google',{kind});setPending({id:started.id,kind});
      if(tab){tab.opener=null;tab.location.href=started.authorizationUrl;}else window.location.assign(started.authorizationUrl);await onChanged();}
    catch(cause){tab?.close();setError((cause as Error).message);}
  }
  async function connectPlausible(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{const saved=await api<Connection>('/data-connections/plausible',{baseUrl:address,siteId:site,apiKey:key,metrics});setKey('');await onChanged(`Plausible connected: ${saved.lastRows??0} values in the scorecard.`);onClose();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <Dialog title="Connect data" onClose={onClose}>
    {token?<TokenForm kind={token} info={info(token)} onBack={()=>{setToken(null);setError('');}} onDone={message=>{void onChanged(message);onClose();}}/>:pending&&current?<div className="fe-connect-step">
      <h3>{info(pending.kind).name}</h3>
      {current.status==='authorizing'&&<p className="fe-muted">Finish signing in on the Google tab that opened. This updates on its own when you’re done.</p>}
      {current.status==='error'&&<p className="fe-alert" role="alert">{current.lastError}</p>}
      {current.status==='choose'&&<Choose connection={current} info={info(pending.kind)} onDone={message=>{void onChanged(message);onClose();}}/>}
    </div>:plausible?<form className="fe-form" onSubmit={event=>void connectPlausible(event)} aria-label="Connect Plausible">
      <p className="fe-muted">Plausible is open-source analytics. Create an API key in Plausible under <strong>Account settings → API keys</strong> (Stats API). It is stored in your system’s credential store, never in the workspace.</p>
      <label>Plausible address<input value={address} onChange={event=>setAddress(event.target.value)} placeholder="https://plausible.io"/></label>
      <label>Site domain<input required value={site} onChange={event=>setSite(event.target.value)} placeholder="example.com"/></label>
      <label>API key<input required type="password" autoComplete="off" value={key} onChange={event=>setKey(event.target.value)}/></label>
      <Metrics info={info('plausible')} value={metrics} onChange={setMetrics}/>
      {error&&<p className="fe-alert" role="alert">{error}</p>}
      <footer><button type="button" className="fe-ghost" onClick={()=>setPlausible(false)}>Back</button><button className="primary" disabled={busy||metrics.length===0}>{busy?'Connecting…':'Connect Plausible'}</button></footer>
    </form>:<div className="fe-connect-options">
      <p className="fe-muted">Read-only: the employee reads daily numbers into the scorecard and can never change anything in these tools. Numbers sync every six hours and at the start of each shift.</p>
      {(['google-analytics','search-console'] as Kind[]).map(kind=><button key={kind} type="button" className="fe-list-row" disabled={!data.googleReady} onClick={()=>void google(kind)}>
        <span className="fe-row-icon">{icon(kind)}</span><span className="fe-list-main"><strong>{info(kind).name}</strong><small>{kind==='google-analytics'?'Sessions, users, new users and key events by day':'Clicks, impressions, CTR and average position by day'}</small></span><span className="fe-status-chip">Sign in with Google</span></button>)}
      {!data.googleReady&&<p className="fe-notice">Google needs a one-time app setup first: <strong>Settings → Google app</strong>.</p>}
      <h4 className="fe-connect-group">Pipeline and ad spend</h4>
      <button type="button" className="fe-list-row" onClick={()=>setToken('hubspot')}><span className="fe-row-icon"><Handshake size={16}/></span><span className="fe-list-main"><strong>HubSpot</strong><small>New contacts, deals, deals won and open pipeline, and where leads came from</small></span><span className="fe-status-chip">Read-only token</span></button>
      <button type="button" className="fe-list-row" onClick={()=>setToken('meta-ads')}><span className="fe-row-icon"><Megaphone size={16}/></span><span className="fe-list-main"><strong>Meta Ads</strong><small>Spend, clicks and leads by day, and each campaign’s last seven days</small></span><span className="fe-status-chip">Read-only token</span></button>
      <details className="fe-help"><summary>Google Ads, LinkedIn Ads or another CRM</summary><p>Export a daily report as CSV and use <strong>Import data</strong> on the scorecard. Google Ads: Reports → a report with <em>Day</em>, <em>Cost</em>, <em>Clicks</em> and <em>Conversions</em> → Download → CSV, or schedule it to a Google Sheet and link the sheet. LinkedIn Campaign Manager: Analyze → Export → CSV with time breakdown <em>Daily</em>. Title lines and totals in the export are skipped.</p></details>
      <h4 className="fe-connect-group">Site analytics</h4>
      <button type="button" className="fe-list-row" onClick={()=>setPlausible(true)}><span className="fe-row-icon"><BarChart3 size={16}/></span><span className="fe-list-main"><strong>Plausible</strong><small>Open-source analytics, cloud or self-hosted, with an API key</small></span><span className="fe-status-chip">API key</span></button>
      <details className="fe-help"><summary>Google setup notes</summary><p>In the Google Cloud project behind your Google app, enable the <strong>Google Analytics Data API</strong>, <strong>Google Analytics Admin API</strong> and <strong>Google Search Console API</strong>. While the app is in testing, add your account as a test user. Sign-in has to happen on this computer, because Google returns to it directly.</p></details>
      {error&&<p className="fe-alert" role="alert">{error}</p>}
    </div>}
  </Dialog>;
}

/** A read-only token for the CRM or an ad account: where to create it, which scopes, and what the employee will read. */
function TokenForm({kind,info,onBack,onDone}:{kind:'hubspot'|'meta-ads';info:KindInfo;onBack:()=>void;onDone:(message:string)=>void}){
  const [token,setToken]=useState(''),[account,setAccount]=useState(''),[metrics,setMetrics]=useState(info.metrics.filter(item=>item.standard).map(item=>item.id));
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  async function connect(event:React.FormEvent){
    event.preventDefault();if(busy)return;setBusy(true);setError('');
    try{const saved=await api<Connection>('/data-connections/'+kind,{token,accountId:account,metrics});setToken('');onDone(`${info.name} connected: ${saved.lastRows??0} values in the scorecard.`);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <form className="fe-form" onSubmit={event=>void connect(event)} aria-label={'Connect '+info.name}>
    {kind==='hubspot'
      ?<p className="fe-muted">In HubSpot: <strong>Settings → Integrations → Private apps → Create a private app</strong>. On Scopes, tick only <strong>crm.objects.contacts.read</strong> and <strong>crm.objects.deals.read</strong>, create it, and copy its access token. Read-only: the employee can’t change a contact or a deal.</p>
      :<p className="fe-muted">In Meta Business Settings: <strong>Users → System users</strong>, add one with access to the ad account, then <strong>Generate token</strong> with only <strong>ads_read</strong>. A system user’s token doesn’t expire. Read-only: the employee can’t change a budget, an ad or an audience.</p>}
    <a className="fe-button" href={kind==='hubspot'?'https://app.hubspot.com/private-apps/':'https://business.facebook.com/settings/system-users'} target="_blank" rel="noopener noreferrer">Open {kind==='hubspot'?'HubSpot private apps':'Meta Business Settings'} ↗</a>
    {kind==='meta-ads'&&<label>Ad account ID<input required value={account} onChange={event=>setAccount(event.target.value)} placeholder="act_1234567890 (shown in Ads Manager)"/></label>}
    <label>Access token<input required type="password" autoComplete="off" value={token} onChange={event=>setToken(event.target.value)}/></label>
    <small className="fe-muted">It is stored in your system’s credential store, never in the workspace or the backup.</small>
    <Metrics info={info} value={metrics} onChange={setMetrics}/>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    <footer><button type="button" className="fe-ghost" onClick={onBack}>Back</button><button className="primary" disabled={busy||metrics.length===0||!token.trim()}>{busy?'Connecting…':'Connect '+info.name}</button></footer>
  </form>;
}

/** The analytics sources feeding the scorecard, with sync and disconnect for the owner. */
export function DataConnectionsPanel({owner,onSynced}:{owner:boolean;onSynced:()=>Promise<void>}){
  const [data,setData]=useState<DataConnectionsData|null>(null),[open,setOpen]=useState(false),[busy,setBusy]=useState(''),[notice,setNotice]=useState(''),[error,setError]=useState('');
  const load=useCallback(async(message?:string)=>{try{setData(await api<DataConnectionsData>('/data-connections'));if(message){setNotice(message);await onSynced();}}catch(cause){setError((cause as Error).message);}},[onSynced]);
  useEffect(()=>{void load();},[load]);
  async function act(connection:Connection,action:'sync'|'remove'){
    if(busy)return;setBusy(connection.id);setError('');setNotice('');
    try{
      if(action==='sync'){const saved=await api<Connection>(`/data-connections/${connection.id}/sync`,{});saved.status==='error'?setError(saved.lastError||'Sync failed.'):setNotice(`Synced ${saved.lastRows??0} values.`);await load();await onSynced();}
      else{if(!window.confirm(`Disconnect ${connection.resourceName||'this source'}? Its numbers stay in the scorecard; the saved access is deleted.`))return;setData(await api<DataConnectionsData>(`/data-connections/${connection.id}`,{},'DELETE'));}
    }catch(cause){setError((cause as Error).message);}finally{setBusy('');}
  }
  if(!data)return null;
  const name=(kind:Kind)=>data.kinds.find(item=>item.kind===kind)?.name||kind;
  const shown=data.connections.filter(item=>item.status!=='authorizing');
  return <div className="fe-data-connections" aria-label="Data connections">
    {shown.map(connection=><div key={connection.id} className="fe-data-row">
      <span className="fe-row-icon">{icon(connection.kind)}</span>
      <span className="fe-list-main"><strong>{name(connection.kind)}{connection.resourceName?` · ${connection.resourceName}`:''}</strong>
        <small>{connection.status==='choose'?'Signed in; choose what to read':connection.status==='error'?(connection.lastError||'Needs attention'):connection.lastSyncAt?`Synced ${readableTime(seconds(connection.lastSyncAt))} · ${connection.lastRows??0} values`:'Not synced yet'}</small></span>
      <span className={'fe-status-chip '+(connection.status==='ready'?'live':connection.status==='error'?'warn':'')}>{connection.status==='ready'?'Connected':connection.status==='error'?'Error':'Setup'}</span>
      {owner&&connection.resource&&<button type="button" className="fe-icon-button" aria-label={`Sync ${name(connection.kind)} now`} title="Sync now" disabled={!!busy} onClick={()=>void act(connection,'sync')}><RefreshCw size={15}/></button>}
      {owner&&<button type="button" className="fe-icon-button" aria-label={`Disconnect ${name(connection.kind)}`} title="Disconnect" disabled={!!busy} onClick={()=>void act(connection,'remove')}><Unplug size={15}/></button>}
    </div>)}
    {owner&&<button type="button" className="fe-ghost fe-add-line" onClick={()=>setOpen(true)}><Link2 size={14}/> Connect data</button>}
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {open&&<ConnectData data={data} onClose={()=>setOpen(false)} onChanged={load}/>}
  </div>;
}
