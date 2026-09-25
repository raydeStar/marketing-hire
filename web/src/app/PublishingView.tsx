import {useCallback,useEffect,useState} from 'react';
import {CalendarClock,ChevronRight,ExternalLink,Heart,Mail,MessageCircle,MousePointerClick,Plus,Repeat2,Send,Unplug} from 'lucide-react';
import {api} from '../api';
import {readableTime,type MarketingDraft,type MarketingState} from '../components/MarketingPanels';
import {Dialog} from './shared';

type Kind='bluesky'|'mastodon'|'wordpress'|'linkedin'|'x'|'email';
type Connection={id:string;kind:Kind;status:string;account:string;address:string|null;createdAt:string;expiresAt:string|null;saveAsDraft:boolean};
export type PostResults={likes:number|null;reposts:number|null;replies:number|null;quotes:number|null;impressions:number|null;visits:number|null;checkedAt:string;note:string|null};
export type Publication={id:string;draftId:number;connectionId:string;createdAt:string;kind:Kind;excerpt?:string|null;channel?:string|null;results?:PostResults|null;status:'scheduled'|'publishing'|'published'|'failed'|'unknown'|'cancelled'|'missed'|'awaiting_link'|'due';scheduledFor:string|null;publishedAt:string|null;url:string|null;error:string|null};
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
  email:{fields:[],how:'Uses your Google app (Settings → Google app) with the Gmail API enabled in its Google Cloud project, and the redirect URL below added to it. Approved emails are saved to your Gmail drafts; you press Send in Gmail. A separate mailbox for marketing works well.'},
};
const oauth=(kind:Kind)=>kind==='linkedin'||kind==='x'||kind==='email';
const zone=(()=>{try{return Intl.DateTimeFormat().resolvedOptions().timeZone;}catch{return 'your time zone';}})();
/** Local "YYYY-MM-DDTHH:mm" for a datetime-local input. */
const local=(date:Date)=>`${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}T${String(date.getHours()).padStart(2,'0')}:${String(date.getMinutes()).padStart(2,'0')}`;
function at(daysAhead:number,hour:number,weekday?:number){const date=new Date();date.setSeconds(0,0);
  if(weekday!==undefined){const ahead=(weekday-date.getDay()+7)%7||7;date.setDate(date.getDate()+ahead);}else date.setDate(date.getDate()+daysAhead);
  date.setHours(hour,0,0,0);return local(date);}
const presets=[{label:'Tomorrow 7:00 AM',value:()=>at(1,7)},{label:'Tomorrow 9:00 AM',value:()=>at(1,9)},{label:'Monday 9:00 AM',value:()=>at(0,9,1)}];

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
      if(oauth(kind)){
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
        <span className="fe-row-icon">{value==='email'?<Mail size={15}/>:<Send size={15}/>}</span><span className="fe-list-main"><strong>{name(value)}</strong><small>{value==='email'?'Approved emails land in your Gmail drafts':value==='linkedin'||value==='x'?'Sign in with your own developer app':value==='wordpress'?'Your blog, with an application password':value==='bluesky'?'An app password from Bluesky settings':'An access token from your server'}</small></span></button>)}
    </div>:<form className="fe-form" onSubmit={event=>void submit(event)} aria-label={`Connect ${name(kind)}`}>
      <p className="fe-muted">{help[kind].how}</p>
      {oauth(kind)&&<p className="fe-notice">Redirect URL to register: <code>{data.redirectUri}</code>. Sign-in has to happen on this computer.</p>}
      {help[kind].fields.map(id=>id==='clientId'?field(id,'Client ID'):id==='clientSecret'?field(id,kind==='x'?'Client secret (confidential apps)':'Client secret',true):
        id==='secret'?field(id,help[kind].secret||'Secret',true):id==='address'?field(id,help[kind].address||'Address',false,kind==='mastodon'?'https://mastodon.social':'https://example.com'):field(id,help[kind].account||'Account',false,kind==='bluesky'?'you.bsky.social':''))}
      {kind==='bluesky'&&<details className="fe-help"><summary>Self-hosted server</summary>{field('address','Server (leave empty for bsky.social)',false,'https://bsky.social')}</details>}
      {kind==='wordpress'&&<label className="fe-check"><input type="checkbox" checked={draftMode} onChange={event=>setDraftMode(event.target.checked)}/>Save as a WordPress draft instead of publishing</label>}
      {waiting&&<p className="fe-muted" role="status">Finish signing in on the tab that opened. This closes on its own once the channel is connected.</p>}
      {error&&<p className="fe-alert" role="alert">{error}</p>}
      <footer><button type="button" className="fe-ghost" onClick={()=>{setKind(null);setWaiting(false);}}>Back</button><button className="primary" disabled={busy||waiting}>{busy?'Checking…':kind==='email'?'Sign in with Google':oauth(kind)?`Sign in with ${name(kind)}`:'Connect'}</button></footer>
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

const statusLabel:Record<Publication['status'],string>={scheduled:'Scheduled',publishing:'Publishing',published:'Published',failed:'Failed',unknown:'Check the channel',cancelled:'Cancelled',missed:'Missed',awaiting_link:'Waiting for the link',due:'Time to post'};

// ---------- Assisted posting: the network's own composer, with the approved text ----------
function emailParts(text:string){
  const lines=text.replace(/\r\n/g,'\n').split('\n');let subject='',to='';
  while(lines.length&&/^(subject|to|cc)\s*:/i.test(lines[0])){const [key,...rest]=lines.shift()!.split(':');const value=rest.join(':').trim();if(/subject/i.test(key))subject=value;else if(/^to/i.test(key.trim()))to=value;}
  if(!subject){while(lines.length&&!lines[0].trim())lines.shift();subject=(lines.shift()||'').replace(/^#+\s*/,'').trim();}
  return {subject,to,body:lines.join('\n').trim()};
}
function mastodonServer(publishing:PublishingData|null){
  const connected=publishing?.connections.find(item=>item.kind==='mastodon'&&item.address)?.address;
  if(connected)return connected.replace(/\/$/,'');
  let saved='';try{saved=localStorage.getItem('fe-mastodon-server')||'';}catch{}
  if(!saved){saved=(window.prompt('Your Mastodon server, e.g. mastodon.social')||'').trim().replace(/^https?:\/\//,'').replace(/\/.*$/,'');if(saved)try{localStorage.setItem('fe-mastodon-server',saved);}catch{}}
  return saved?`https://${saved}`:'';
}
/** Where to post a draft by hand: the channel's composer, prefilled where the network supports it. */
export function composeUrl(draft:MarketingDraft,publishing:PublishingData|null):string{
  const text=encodeURIComponent(draft.content);
  switch(draft.channel.trim().toLowerCase()){
    case 'x':case 'twitter':case 'x (twitter)':return `https://x.com/intent/post?text=${text}`;
    case 'bluesky':case 'bsky':return `https://bsky.app/intent/compose?text=${text}`;
    case 'threads':return `https://www.threads.net/intent/post?text=${text}`;
    case 'linkedin':return `https://www.linkedin.com/feed/?shareActive=true&text=${text}`;
    case 'mastodon':{const server=mastodonServer(publishing);return server?`${server}/share?text=${text}`:draft.destination;}
    case 'email':case 'e-mail':case 'newsletter':{const mail=emailParts(draft.content);return `mailto:${encodeURIComponent(mail.to)}?subject=${encodeURIComponent(mail.subject)}&body=${encodeURIComponent(mail.body)}`;}
    default:return draft.destination;
  }
}
/** Copy the text (LinkedIn and others may ignore the prefill) and open the composer, both inside the click. */
export function openComposer(draft:MarketingDraft,publishing:PublishingData|null){
  void navigator.clipboard?.writeText(draft.content).catch(()=>{});
  const url=composeUrl(draft,publishing);
  if(url.startsWith('mailto:'))window.location.href=url;else window.open(url,'_blank','noopener');
}
export const assistedLabel=(channel:string)=>`Post it yourself on ${channel}`;

/** Work → Content calendar: what is scheduled, what needs a new time, and what went out in the last two weeks. */
/** A post's counts, as the channel reported them. */
export function Results({results}:{results:PostResults|null|undefined}){
  if(!results)return <small className="fe-muted">No results yet</small>;
  const parts:[number|null,React.ReactNode,string][]=[[results.likes,<Heart size={12}/>,'likes'],[results.reposts,<Repeat2 size={12}/>,'reposts'],[results.replies,<MessageCircle size={12}/>,'replies'],[results.visits,<MousePointerClick size={12}/>,'visits']];
  const shown=parts.filter(([value])=>value!==null&&value!==undefined);
  if(!shown.length)return <small className="fe-muted" title={results.note||''}>{results.note?'Visits only when Google Analytics is connected':'No counts yet'}</small>;
  return <span className="fe-results" aria-label={shown.map(([value,,label])=>`${value} ${label}`).join(', ')}>{shown.map(([value,icon,label])=><span key={label} title={label}>{icon}{value}</span>)}</span>;
}

export function ContentCalendar({state,owner,onOpen}:{state:MarketingState;owner:boolean;onOpen:(key:string)=>void}){
  const {data,load}=usePublishing();
  const [error,setError]=useState('');
  if(!data)return null;
  const since=Date.now()-14*86400000;
  const rows=data.publications.filter(item=>item.status==='scheduled'||item.status==='missed'||item.status==='unknown'||item.status==='awaiting_link'||item.status==='due'||(item.status==='published'&&item.publishedAt&&new Date(item.publishedAt).getTime()>=since))
    .map(item=>({item,time:new Date(item.scheduledFor&&item.status!=='published'?item.scheduledFor:item.publishedAt||item.scheduledFor||Date.now())}))
    .sort((a,b)=>(a.item.status==='published'?1:0)-(b.item.status==='published'?1:0)||(a.item.status==='published'?b.time.getTime()-a.time.getTime():a.time.getTime()-b.time.getTime()));
  const account=(id:string)=>id?data.connections.find(item=>item.id===id)?.account||'':'posted by you';
  const name=(kind:Kind)=>data.kinds.find(item=>item.kind===kind)?.name||kind;
  async function cancel(id:string){try{await api(`/publishing/publications/${id}/cancel`,{});await load();}catch(cause){setError((cause as Error).message);}}
  return <section className="fe-section" aria-label="Content calendar">
    <div className="fe-section-head"><div><h3>Content calendar</h3><small>Scheduled posts go out on time from this workspace ({zone}). Published posts from the last two weeks are listed below them.</small></div></div>
    {rows.length===0?<p className="fe-muted">Nothing scheduled. Approve a draft, then choose <strong>Schedule</strong> on it.</p>:
    <div className="fe-calendar">{rows.map(({item,time})=>{const draft=state.drafts.find(entry=>entry.id===item.draftId);
      return <div key={item.id} className={'fe-calendar-row '+item.status}>
        <span className="fe-calendar-when"><strong>{time.toLocaleDateString(undefined,{weekday:'short',month:'short',day:'numeric'})}</strong><small>{time.toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'})}</small></span>
        <span className="fe-list-main"><strong>{item.channel||name(item.kind)} · {account(item.connectionId)}</strong><small>{(draft?.content||`Draft #${item.draftId}`).replace(/\s+/g,' ').slice(0,110)}</small></span>
        {item.status==='published'&&item.kind!=='email'&&<Results results={item.results}/>}
        <span className={'fe-status-chip '+(item.status==='published'?'live':item.status==='scheduled'?'':'warn')}>{statusLabel[item.status]}</span>
        {item.url&&<a className="fe-icon-button" href={item.url} target="_blank" rel="noopener noreferrer" aria-label="Open the post" title="Open the post"><ExternalLink size={14}/></a>}
        {owner&&item.status==='scheduled'&&<button type="button" className="fe-inline-button" onClick={()=>void cancel(item.id)}>Cancel</button>}
        <button type="button" className="fe-icon-button" aria-label={`Open draft ${item.draftId}`} title="Open the draft" onClick={()=>onOpen('draft:'+item.draftId)}><ChevronRight size={15}/></button>
      </div>;})}</div>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}

/** On an approved draft: publish the exact approved text now or at a time, and see what happened. */
export function PublishBar({draft,owner,onRefresh}:{draft:MarketingDraft;owner:boolean;onRefresh:()=>Promise<void>}){
  const {data,load}=usePublishing();
  const [connection,setConnection]=useState(''),[when,setWhen]=useState(''),[scheduling,setScheduling]=useState(false),[link,setLink]=useState(''),[yourself,setYourself]=useState(false);
  const [busy,setBusy]=useState(false),[error,setError]=useState('');
  const [requestId]=useState(()=>crypto.randomUUID());
  if(!data||(draft.status!=='approved'&&draft.status!=='posted'))return null;
  const mine=data.publications.filter(item=>item.draftId===draft.id&&item.status!=='cancelled').sort((a,b)=>(b.publishedAt||b.scheduledFor||'').localeCompare(a.publishedAt||a.scheduledFor||''));
  const current=mine.find(item=>item.status!=='failed'&&item.status!=='missed')||mine[0];
  const retry=current&&(current.status==='failed'||current.status==='missed');
  const choices=data.connections.filter(item=>item.status==='ready'&&serves(data,item.kind,draft.channel));
  const chosen=connection||choices[0]?.id||'';
  const target=choices.find(item=>item.id===chosen);
  const name=(kind:Kind)=>data.kinds.find(item=>item.kind===kind)?.name||kind;
  async function publish(){
    if(!target||busy)return;
    const at=scheduling&&when&&target.kind!=='email'?new Date(when).toISOString():null;
    if(!window.confirm(target.kind==='email'?`Save this email to the Gmail drafts of ${target.account}? Nothing is sent; you send it from Gmail.`:at?`Schedule this exact text to ${name(target.kind)} as ${target.account} for ${new Date(when).toLocaleString()} (${zone})?`:`Publish this exact text to ${name(target.kind)} as ${target.account} now? It will be public.`))return;
    setBusy(true);setError('');
    try{const result=await api<Publication>(`/publishing/drafts/${draft.id}`,{requestId:requestId+(retry?':'+mine.length:''),connectionId:target.id,digest:draft.digest,at});
      if(result.status==='failed')setError(result.error||'The channel refused it.');await load();await onRefresh();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const assisted=choices.length===0||yourself;
  /** Open the composer now (the owner posts), or set a reminder for a time. */
  async function assist(){
    if(busy)return;
    const at=scheduling&&when?new Date(when).toISOString():null;
    if(!at)openComposer(draft,data);
    setBusy(true);setError('');
    try{await api(`/publishing/drafts/${draft.id}/assist`,{requestId:requestId+':assist'+(retry?':'+mine.length:''),digest:draft.digest,at});await load();await onRefresh();}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  async function act(path:string,body:object){setBusy(true);setError('');try{await api(path,body);await load();await onRefresh();}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}}
  const live=current?.status==='published'?current:null;
  return <div className="fe-publish" aria-label="Publishing">
    {live?<p className="fe-notice" role="status">{live.kind==='email'?'Saved to your Gmail drafts':`Published to ${name(live.kind)}`} {live.publishedAt?readableTime(seconds(live.publishedAt)):''}. {live.url&&<a href={live.url} target="_blank" rel="noopener noreferrer">{live.kind==='email'?'Open in Gmail to send':'View the post'} <ExternalLink size={12}/></a>}</p>
    :draft.status==='posted'?<p className="fe-notice">Marked posted.</p>
    :current?.status==='scheduled'?<p className="fe-notice" role="status"><CalendarClock size={14}/> {current.connectionId?`Scheduled for ${new Date(current.scheduledFor!).toLocaleString()} to ${name(current.kind)}.`:`Reminder set for ${new Date(current.scheduledFor!).toLocaleString()}: you’ll post it on ${draft.channel}.`} {owner&&<button type="button" className="fe-inline-button" disabled={busy} onClick={()=>void act(`/publishing/publications/${current.id}/cancel`,{})}>Cancel</button>}</p>
    :current&&(current.status==='awaiting_link'||current.status==='due')?<div className={current.status==='due'?'fe-alert':'fe-notice'} role="status"><p>{current.status==='due'?`It’s time to post this on ${draft.channel}.`:`Post it on ${draft.channel}, then paste the link here so its results can be tracked.`} The text is on your clipboard when you open the composer.</p>
      {owner&&<div className="fe-publish-resolve"><button type="button" onClick={()=>openComposer(draft,data)}><ExternalLink size={13}/> Open {draft.channel}</button>
        <input value={link} onChange={event=>setLink(event.target.value)} placeholder="Link to the live post" aria-label="Link to the live post"/>
        <button type="button" className="primary" disabled={busy||!link.startsWith('https://')} onClick={()=>void act(`/publishing/publications/${current.id}/link`,{url:link.trim()})}>It’s posted</button>
        <button type="button" className="fe-ghost" disabled={busy} onClick={()=>void act(`/publishing/publications/${current.id}/cancel`,{})}>Cancel</button></div>}</div>
    :current?.status==='unknown'?<div className="fe-alert" role="alert"><p>{current.error}</p>{owner&&<div className="fe-publish-resolve"><input value={link} onChange={event=>setLink(event.target.value)} placeholder="Link to the post, if it went out"/>
      <button type="button" disabled={busy||!link.startsWith('https://')} onClick={()=>void act(`/publishing/publications/${current.id}/resolve`,{outcome:'posted',url:link})}>It was posted</button>
      <button type="button" disabled={busy} onClick={()=>void act(`/publishing/publications/${current.id}/resolve`,{outcome:'not_posted'})}>It wasn’t posted</button></div>}</div>
    :owner&&<>
      {retry&&<p className="fe-alert">{current!.status==='missed'?'Missed':'Last attempt'}: {current!.error}</p>}
      {assisted?<div className="fe-publish-row">
        <label className="fe-check"><input type="checkbox" checked={scheduling} onChange={event=>{setScheduling(event.target.checked);if(event.target.checked&&!when)setWhen(presets[0].value());}}/>Remind me at a time</label>
        {scheduling&&<input type="datetime-local" aria-label="When" value={when} min={local(new Date())} onChange={event=>setWhen(event.target.value)}/>}
        <button type="button" className="primary" disabled={busy||(scheduling&&!when)} onClick={()=>void assist()}><ExternalLink size={14}/> {busy?'Working…':scheduling?'Set the reminder':assistedLabel(draft.channel)}</button>
        {choices.length>0&&<button type="button" className="fe-ghost" onClick={()=>setYourself(false)}>Publish from here instead</button>}
        <small className="fe-muted fe-block">{scheduling?'At that time the cockpit and chat remind you, with the composer one click away.':`Opens ${draft.channel}’s own composer with the text (also copied to your clipboard). Free, and nothing to connect.`}</small>
      </div>:
      <div className="fe-publish-row">
        {choices.length>1&&<select aria-label="Channel" value={chosen} onChange={event=>setConnection(event.target.value)}>{choices.map(item=><option key={item.id} value={item.id}>{name(item.kind)} · {item.account}</option>)}</select>}
        {target?.kind!=='email'&&<label className="fe-check"><input type="checkbox" checked={scheduling} onChange={event=>{setScheduling(event.target.checked);if(event.target.checked&&!when)setWhen(presets[0].value());}}/>Schedule</label>}
        {scheduling&&target?.kind!=='email'&&<input type="datetime-local" aria-label="When" value={when} min={local(new Date())} onChange={event=>setWhen(event.target.value)}/>}
        <button type="button" className="primary" disabled={busy||!target||(scheduling&&target?.kind!=='email'&&!when)} onClick={()=>void publish()}>{target?.kind==='email'?<Mail size={14}/>:<Send size={14}/>} {busy?'Working…':target?.kind==='email'?'Save to Gmail drafts':scheduling?'Schedule':`Publish to ${target?name(target.kind):''}`}</button>
        <button type="button" className="fe-ghost" onClick={()=>setYourself(true)}>Post it yourself instead</button>
      </div>}
      {scheduling&&(assisted||target&&target.kind!=='email')&&<div className="fe-presets" aria-label="Quick times">{presets.map(item=><button key={item.label} type="button" className="fe-ghost" onClick={()=>setWhen(item.value())}>{item.label}</button>)}
        <small className="fe-muted">Times are in {zone}. The workspace has to be running then; a post more than two hours late is held for you instead.</small></div>}
    </>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </div>;
}
