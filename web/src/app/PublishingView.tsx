import {useCallback,useEffect,useState} from 'react';
import {CalendarClock,ExternalLink,Plus,Send,Unplug} from 'lucide-react';
import {api} from '../api';
import {readableTime,type MarketingDraft} from '../components/MarketingPanels';
import {Dialog} from './shared';

type Kind='bluesky'|'mastodon'|'wordpress'|'linkedin'|'x';
type Connection={id:string;kind:Kind;status:string;account:string;address:string|null;createdAt:string;expiresAt:string|null;saveAsDraft:boolean};
type Publication={id:string;draftId:number;connectionId:string;kind:Kind;status:'scheduled'|'publishing'|'published'|'failed'|'unknown'|'cancelled';scheduledFor:string|null;publishedAt:string|null;url:string|null;error:string|null};
export type PublishingData={redirectUri:string;kinds:{kind:Kind;name:string;channels:string[];limit:number|null}[];connections:Connection[];publications:Publication[]};

const seconds=(value:string)=>new Date(value).getTime()/1000;
export function usePublishing(){
  const [data,setData]=useState<PublishingData|null>(null),[error,setError]=useState('');
  const load=useCallback(async()=>{try{setData(await api<PublishingData>('/publishing'));setError('');}catch(cause){setError((cause as Error).message);}},[]);
  useEffect(()=>{void load();},[load]);
  return {data,error,load,setData};
}
const serves=(data:PublishingData,kind:Kind,channel:string)=>data.kinds.find(item=>item.kind===kind)?.channels.includes(channel.trim().toLowerCase())??false;

const help:Record<Kind,{fields:('address'|'account'|'secret'|'clientId'|'clientSecret')[];how:string;secret?:string;account?:string;address?:string}>={
  bluesky:{fields:['account','secret'],account:'Handle',secret:'App password',how:'In Bluesky: Settings → Privacy and security → App passwords → Add. Use an app password, never your account password.'},
  mastodon:{fields:['address','secret'],address:'Server',secret:'Access token',how:'On your server: Preferences → Development → New application with the write:statuses and read:accounts scopes, then copy “Your access token”.'},
  wordpress:{fields:['address','account','secret'],address:'Site address',account:'Username',secret:'Application password',how:'In WordPress: Users → Profile → Application Passwords → Add New. Works with any self-hosted WordPress 5.6 or later.'},
  linkedin:{fields:['clientId','clientSecret'],how:'Create an app at linkedin.com/developers, add the “Share on LinkedIn” and “Sign In with LinkedIn using OpenID Connect” products, and add the redirect URL below. Posts go to your personal profile. Access lasts about 60 days.'},
  x:{fields:['clientId','clientSecret'],how:'Create a project and app at developer.x.com with OAuth 2.0 (read and write) and the redirect URL below. X charges for API access under its own terms. The client secret is needed for confidential apps only.'},
};

function ConnectChannel({data,onClose,onChanged}:{data:PublishingData;onClose:()=>void;onChanged:()=>Promise<void>}){
  const [kind,setKind]=useState<Kind|null>(null),[form,setForm]=useState<Record<string,string>>({}),[draftMode,setDraftMode]=useState(false);
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[waiting,setWaiting]=useState(false);
  const name=(value:Kind)=>data.kinds.find(item=>item.kind===value)?.name||value;
  const before=data.connections.length;
  useEffect(()=>{if(!waiting)return;const timer=setInterval(()=>void onChanged(),2500);return()=>clearInterval(timer);},[waiting]);
  useEffect(()=>{if(waiting&&data.connections.length>before){setWaiting(false);onClose();}},[data.connections.length]);
  async function submit(event:React.FormEvent){
    event.preventDefault();if(!kind||busy)return;setBusy(true);setError('');
    try{
      if(kind==='linkedin'||kind==='x'){
        const tab=window.open('about:blank','_blank');
        try{const started=await api<{authorizationUrl:string}>(`/publishing/oauth/${kind}`,{clientId:form.clientId||'',clientSecret:form.clientSecret||null});if(tab){tab.opener=null;tab.location.href=started.authorizationUrl;}else window.location.assign(started.authorizationUrl);setWaiting(true);}
        catch(cause){tab?.close();throw cause;}
      }else{await api(`/publishing/connect/${kind}`,{address:form.address||null,account:form.account||null,secret:form.secret||'',saveAsDraft:draftMode});setForm({});await onChanged();onClose();}
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const field=(id:'address'|'account'|'secret'|'clientId'|'clientSecret',label:string,secret=false,placeholder='')=>
    <label key={id}>{label}<input required={id!=='clientSecret'&&!(id==='address'&&kind==='bluesky')} type={secret?'password':'text'} autoComplete="off" value={form[id]||''} placeholder={placeholder} onChange={event=>setForm({...form,[id]:event.target.value})}/></label>;
  return <Dialog title="Connect a channel" onClose={onClose}>
    {!kind?<div className="fe-connect-options">
      <p className="fe-muted">Connect only the channels you post to. Nothing is ever posted without you: you approve a draft, then publish or schedule it yourself.</p>
      {(Object.keys(help) as Kind[]).map(value=><button key={value} type="button" className="fe-list-row" onClick={()=>{setKind(value);setError('');}}>
        <span className="fe-row-icon"><Send size={15}/></span><span className="fe-list-main"><strong>{name(value)}</strong><small>{value==='linkedin'||value==='x'?'Sign in with your own developer app':value==='wordpress'?'Your blog, with an application password':value==='bluesky'?'An app password from Bluesky settings':'An access token from your server'}</small></span></button>)}
    </div>:<form className="fe-form" onSubmit={event=>void submit(event)} aria-label={`Connect ${name(kind)}`}>
      <p className="fe-muted">{help[kind].how}</p>
      {(kind==='linkedin'||kind==='x')&&<p className="fe-notice">Redirect URL to register: <code>{data.redirectUri}</code>. Sign-in has to happen on this computer.</p>}
      {help[kind].fields.map(id=>id==='clientId'?field(id,'Client ID'):id==='clientSecret'?field(id,kind==='x'?'Client secret (confidential apps)':'Client secret',true):
        id==='secret'?field(id,help[kind].secret||'Secret',true):id==='address'?field(id,help[kind].address||'Address',false,kind==='mastodon'?'https://mastodon.social':'https://example.com'):field(id,help[kind].account||'Account',false,kind==='bluesky'?'you.bsky.social':''))}
      {kind==='bluesky'&&<details className="fe-help"><summary>Self-hosted server</summary>{field('address','Server (leave empty for bsky.social)',false,'https://bsky.social')}</details>}
      {kind==='wordpress'&&<label className="fe-check"><input type="checkbox" checked={draftMode} onChange={event=>setDraftMode(event.target.checked)}/>Save as a WordPress draft instead of publishing</label>}
      {waiting&&<p className="fe-muted" role="status">Finish signing in on the tab that opened. This closes on its own once the channel is connected.</p>}
      {error&&<p className="fe-alert" role="alert">{error}</p>}
      <footer><button type="button" className="fe-ghost" onClick={()=>{setKind(null);setWaiting(false);}}>Back</button><button className="primary" disabled={busy||waiting}>{busy?'Checking…':kind==='linkedin'||kind==='x'?`Sign in with ${name(kind)}`:'Connect'}</button></footer>
    </form>}
  </Dialog>;
}

/** Settings → Publishing channels: the accounts approved drafts can be published to. */
export function PublishingSettings(){
  const {data,error,load,setData}=usePublishing();
  const [open,setOpen]=useState(false),[failure,setFailure]=useState('');
  async function disconnect(connection:Connection){
    if(!window.confirm(`Disconnect ${connection.account}? The saved access is deleted; published posts stay where they are.`))return;
    try{setData(await api<PublishingData>(`/publishing/connections/${connection.id}`,{},'DELETE'));setFailure('');}catch(cause){setFailure((cause as Error).message);}
  }
  if(!data)return error?<p className="fe-alert">{error}</p>:null;
  const name=(kind:Kind)=>data.kinds.find(item=>item.kind===kind)?.name||kind;
  return <div className="fe-data-connections" aria-label="Publishing channels">
    <p className="fe-muted">Where approved drafts can go. You publish or schedule each one yourself; approving a draft never posts it.</p>
    {data.connections.filter(item=>item.status==='ready').map(connection=><div key={connection.id} className="fe-data-row">
      <span className="fe-row-icon"><Send size={15}/></span>
      <span className="fe-list-main"><strong>{name(connection.kind)} · {connection.account}</strong><small>{connection.address||''}{connection.saveAsDraft?' · saves as WordPress drafts':''}{connection.expiresAt?` · access until ${new Date(connection.expiresAt).toLocaleDateString()}`:''}</small></span>
      <button type="button" className="fe-icon-button" aria-label={`Disconnect ${name(connection.kind)}`} title="Disconnect" onClick={()=>void disconnect(connection)}><Unplug size={15}/></button></div>)}
    <button type="button" className="fe-ghost fe-add-line" onClick={()=>setOpen(true)}><Plus size={14}/> Connect a channel</button>
    {failure&&<p className="fe-alert" role="alert">{failure}</p>}
    {open&&<ConnectChannel data={data} onClose={()=>setOpen(false)} onChanged={load}/>}
  </div>;
}

/** On an approved draft: publish the exact approved text now or at a time, and see what happened. */
export function PublishBar({draft,owner,onRefresh}:{draft:MarketingDraft;owner:boolean;onRefresh:()=>Promise<void>}){
  const {data,load}=usePublishing();
  const [connection,setConnection]=useState(''),[when,setWhen]=useState(''),[scheduling,setScheduling]=useState(false),[link,setLink]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const [requestId]=useState(()=>crypto.randomUUID());
  if(!data||(draft.status!=='approved'&&draft.status!=='posted'))return null;
  const mine=data.publications.filter(item=>item.draftId===draft.id&&item.status!=='cancelled').sort((a,b)=>(b.publishedAt||b.scheduledFor||'').localeCompare(a.publishedAt||a.scheduledFor||''));
  const current=mine.find(item=>item.status!=='failed')||mine[0];
  const choices=data.connections.filter(item=>item.status==='ready'&&serves(data,item.kind,draft.channel));
  const chosen=connection||choices[0]?.id||'';
  const target=choices.find(item=>item.id===chosen);
  const name=(kind:Kind)=>data.kinds.find(item=>item.kind===kind)?.name||kind;
  async function publish(){
    if(!target||busy)return;
    const at=scheduling&&when?new Date(when).toISOString():null;
    if(!window.confirm(at?`Schedule this exact text to ${name(target.kind)} as ${target.account} for ${new Date(when).toLocaleString()}?`:`Publish this exact text to ${name(target.kind)} as ${target.account} now? It will be public.`))return;
    setBusy(true);setError('');
    try{const result=await api<Publication>(`/publishing/drafts/${draft.id}`,{requestId:requestId+(current?.status==='failed'?':'+mine.length:''),connectionId:target.id,digest:draft.digest,at});
      if(result.status==='failed')setError(result.error||'The channel refused it.');await load();await onRefresh();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function act(path:string,body:object){setBusy(true);setError('');try{await api(path,body);await load();await onRefresh();}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}}
  const live=current?.status==='published'?current:null;
  return <div className="fe-publish" aria-label="Publishing">
    {live?<p className="fe-notice" role="status">Published to {name(live.kind)} {live.publishedAt?readableTime(seconds(live.publishedAt)):''}. {live.url&&<a href={live.url} target="_blank" rel="noopener noreferrer">View the post <ExternalLink size={12}/></a>}</p>
    :draft.status==='posted'?<p className="fe-notice">Marked posted.</p>
    :current?.status==='scheduled'?<p className="fe-notice" role="status"><CalendarClock size={14}/> Scheduled for {new Date(current.scheduledFor!).toLocaleString()} to {name(current.kind)}. {owner&&<button type="button" className="fe-inline-button" disabled={busy} onClick={()=>void act(`/publishing/publications/${current.id}/cancel`,{})}>Cancel</button>}</p>
    :current?.status==='unknown'?<div className="fe-alert" role="alert"><p>{current.error}</p>{owner&&<div className="fe-publish-resolve"><input value={link} onChange={event=>setLink(event.target.value)} placeholder="Link to the post, if it went out"/>
      <button type="button" disabled={busy||!link.startsWith('https://')} onClick={()=>void act(`/publishing/publications/${current.id}/resolve`,{outcome:'posted',url:link})}>It was posted</button>
      <button type="button" disabled={busy} onClick={()=>void act(`/publishing/publications/${current.id}/resolve`,{outcome:'not_posted'})}>It wasn’t posted</button></div>}</div>
    :owner&&<>
      {current?.status==='failed'&&<p className="fe-alert">Last attempt: {current.error}</p>}
      {choices.length===0?<p className="fe-muted">To publish from here, connect {draft.channel} in Settings → Publishing channels. Or post it yourself and it’s done.</p>:
      <div className="fe-publish-row">
        {choices.length>1&&<select aria-label="Channel" value={chosen} onChange={event=>setConnection(event.target.value)}>{choices.map(item=><option key={item.id} value={item.id}>{name(item.kind)} · {item.account}</option>)}</select>}
        <label className="fe-check"><input type="checkbox" checked={scheduling} onChange={event=>setScheduling(event.target.checked)}/>Schedule</label>
        {scheduling&&<input type="datetime-local" aria-label="When" value={when} onChange={event=>setWhen(event.target.value)}/>}
        <button type="button" className="primary" disabled={busy||!target||(scheduling&&!when)} onClick={()=>void publish()}><Send size={14}/> {busy?'Publishing…':scheduling?'Schedule':`Publish to ${target?name(target.kind):''}`}</button>
      </div>}
    </>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </div>;
}
