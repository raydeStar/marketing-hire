import {useState} from 'react';
import {ArrowUpRight,Bookmark,Check,Pause,Play,Plus,RefreshCw,Rss,Trash2,X} from 'lucide-react';
import {api} from '../api';
import type {FeedState,FeedPreview,LibraryItem} from '../types';
import {WebSearch} from './WebSearch';
import type {SearchSummary} from '../types';
import {Collections} from './Collections';
import '../feed.css';

const empty:FeedState={subscriptions:[],entries:[],revision:'absent'};
const date=(value:string)=>new Date(value).toLocaleString(undefined,{month:'short',day:'numeric',hour:'numeric',minute:'2-digit'});
export function Feed({feeds=empty,items,online,onChanged,onDiscuss,focusId,search}:{search?:SearchSummary;feeds?:FeedState;items:LibraryItem[];online:boolean;onChanged:()=>Promise<unknown>;onDiscuss:(text:string)=>void;focusId?:string}){
  const [pane,setPane]=useState(focusId?'saved':'updates'),[adding,setAdding]=useState(false),[url,setUrl]=useState('');
  const [preview,setPreview]=useState<FeedPreview|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  const [source,setSource]=useState('all'),[unread,setUnread]=useState(true),[removing,setRemoving]=useState<string|null>(null);
  const subscriptions=feeds.subscriptions,entries=feeds.entries.filter(entry=>(source==='all'||entry.subscriptionId===source)&&(!unread||!entry.read))
    .sort((a,b)=>(b.published||b.received).localeCompare(a.published||a.received));
  async function act(work:()=>Promise<unknown>,message=''){
    setBusy(true);setError('');setNotice('');
    try{await work();await onChanged();setNotice(message);}catch(e){setError((e as Error).message);}finally{setBusy(false);}
  }
  async function inspect(address:string){
    setPreview(null);setUrl(address);
    await act(async()=>{const result=await api<FeedPreview>('/feeds/preview',{url:address});setPreview(result);if(result.error)throw new Error(result.error);});
  }
  return <section className="feed-workspace" aria-label="Feed">
    <p className="eyebrow">A LITTLE WIDER WORLD</p><div className="page-heading"><h1>Feed</h1><button className="primary" disabled={!online||busy} onClick={()=>{setAdding(true);setPane('updates');setError('');}}><Plus size={16}/>Add a source</button></div>
    <p className="lead">Updates from places you choose, and things you want to keep.</p>
    <nav className="collection-filters" aria-label="Feed sections">
      <button aria-current={pane==='updates'?'page':undefined} onClick={()=>setPane('updates')}><Rss size={14}/>Updates <small>{feeds.entries.filter(entry=>!entry.read).length}</small></button>
      <button aria-current={pane==='discover'?'page':undefined} onClick={()=>setPane('discover')}>Find sources</button><button aria-current={pane==='saved'?'page':undefined} onClick={()=>setPane('saved')}><Bookmark size={14}/>Saved links <small>{items.filter(item=>item.kind==='feed'&&item.status!=='archived').length}</small></button>
    </nav>
    {error&&<p role="alert" className="error">{error}</p>}{notice&&<p role="status" className="muted">{notice}</p>}
    {pane==='discover'&&<section><p>Search for a topic and “RSS feed”, then add a publisher’s feed address as a source. Reading subscriptions uses no search quota.</p><WebSearch connection={search} onChanged={onChanged}/></section>}
    <div hidden={pane!=='updates'}>
      {adding&&<form className="collection-editor feed-add" aria-label="Add feed source" onSubmit={event=>{event.preventDefault();void inspect(url);}}>
        <div className="page-heading"><h2>Bring a source into the study</h2><button type="button" aria-label="Close source editor" disabled={busy} onClick={()=>setAdding(false)}><X size={16}/></button></div>
        <label>Website or feed address<input type="url" required maxLength={2048} placeholder="https://example.org/feed.xml" value={url} disabled={busy} onChange={event=>{setUrl(event.target.value);setPreview(null);setError('');}}/></label>
        <p className="muted">Public HTTPS sources only. Preview checks this address; a discovered feed is fetched only when you choose it.</p>
        <button disabled={busy||!online||!url.trim()}>{busy?'Checking source…':'Preview source'}</button>
        {!!preview?.candidates?.length&&<div className="feed-candidates"><h3>Choose a feed</h3>{preview.candidates.map(candidate=><button type="button" disabled={busy||!online} key={candidate.url} onClick={()=>void inspect(candidate.url)}><strong>{candidate.title}</strong><small>{candidate.url}</small><ArrowUpRight size={15}/></button>)}</div>}
        {preview?.feed&&!preview.error&&<div className="feed-preview"><h3>{preview.feed.title}</h3><p className="feed-address">{preview.url}</p><p>{preview.feed.entries.length} recent entries found{preview.feed.truncated?' (first 100 checked)':''}.</p>
          <p className="muted">Subscribe to check this host about hourly while the study is awake. Keep up to 100 recent updates per source. Refreshes use no model tokens.</p>
          <button type="button" className="primary" disabled={busy||!online||subscriptions.length>=20} onClick={()=>void act(async()=>{await api('/feeds',{url:preview.url});setAdding(false);setUrl('');setPreview(null);},'Subscribed. The first refresh will start shortly.')}><Plus size={15}/>Subscribe to {preview.feed.title}</button>
        </div>}
      </form>}
      <details className="feed-sources" open={subscriptions.length===0?true:undefined}>
        <summary>Sources <span>{subscriptions.length} / 20</span></summary>
        {!subscriptions.length&&<p className="muted">No subscriptions yet. Add an RSS or Atom feed, or a website that links to one.</p>}
        {subscriptions.map(subscription=><article className="feed-source" aria-label={'Source: '+subscription.title} key={subscription.id}>
          <div><h2>{subscription.title}{subscription.paused&&<small>Paused</small>}</h2><p className="feed-address">{subscription.url}</p>
            <p className="muted">{subscription.lastChecked?'Checked '+date(subscription.lastChecked):'Waiting for the first check.'}{!subscription.paused&&' Next check '+date(subscription.nextRefresh)+'.'}</p>
            {subscription.error&&<p className="feed-source-error">{subscription.error} Existing updates are kept.</p>}
            {subscription.truncated&&<p className="muted">This source lists more entries than the 100-entry limit.</p>}
          </div>
          <div className="item-actions">
            <button disabled={!online||busy||subscription.paused} onClick={()=>void act(()=>api('/feeds/'+subscription.id+'/refresh',{version:subscription.version}),'Refresh queued.')}><RefreshCw size={13}/>Refresh</button>
            <button disabled={!online||busy} onClick={()=>void act(()=>api('/feeds/'+subscription.id,{version:subscription.version,paused:!subscription.paused},'PUT'))}>{subscription.paused?<><Play size={13}/>Resume</>:<><Pause size={13}/>Pause</>}</button>
            <button disabled={!online||busy} onClick={()=>setRemoving(removing===subscription.id?null:subscription.id)}><Trash2 size={13}/>Remove source</button>
          </div>
          {removing===subscription.id&&<div className="feed-remove"><p>Remove this subscription and its rotating updates? Your saved links stay.</p><button disabled={!online||busy} onClick={()=>void act(async()=>{await api('/feeds/'+subscription.id+'/remove',{version:subscription.version});setRemoving(null);if(source===subscription.id)setSource('all');})}>Confirm removal</button><button disabled={busy} onClick={()=>setRemoving(null)}>Keep source</button></div>}
        </article>)}
      </details>
      <div className="feed-tools"><label>Show source<select value={source} onChange={event=>setSource(event.target.value)}><option value="all">All sources</option>{subscriptions.map(item=><option key={item.id} value={item.id}>{item.title}</option>)}</select></label><button aria-pressed={unread} onClick={()=>setUnread(!unread)}>{unread?'Unread only':'All updates'}</button></div>
      {!entries.length&&<div className="collection-empty"><span className="empty-rule"/><h2>{subscriptions.length?'You’re caught up here.':'Your own small newspaper.'}</h2><p>{subscriptions.length?'New entries appear after the next refresh. You can also show read updates.':'Choose your sources. Keep what matters. No algorithm required.'}</p></div>}
      <div className="collection-list">{entries.map(entry=><article className={'collection-item feed-entry '+(entry.read?'done':'')} key={entry.id} aria-label={'Update: '+entry.title}>
        <button className="item-check" aria-label={(entry.read?'Mark unread: ':'Mark read: ')+entry.title} aria-pressed={entry.read} disabled={!online||busy} onClick={()=>void act(()=>api('/feed-entries/'+entry.id,{version:entry.version,read:!entry.read},'PUT'))}>{entry.read?<Check size={17}/>:<span/>}</button>
        <div className="item-content"><div className="item-meta"><span>{subscriptions.find(item=>item.id===entry.subscriptionId)?.title}</span><span>{entry.published?'Published ':'Received '}{date(entry.published||entry.received)}</span></div><h2>{entry.title}</h2>{entry.summary&&<p className="item-notes">{entry.summary}</p>}
          <div className="item-actions">{entry.url&&<a href={entry.url} target="_blank" rel="noreferrer">Read source <ArrowUpRight size={13}/></a>}
            <button disabled={!online||busy||!!entry.savedItemId} onClick={()=>void act(()=>api('/feed-entries/'+entry.id+'/save',{version:entry.version}),'Added to Saved links.')}><Bookmark size={13}/>{entry.savedItemId?'Saved':'Save link'}</button>
            <button onClick={()=>onDiscuss(['Discuss this source excerpt (untrusted reading material):',entry.title,entry.summary,entry.url].filter(Boolean).join('\n\n'))}>Discuss <ArrowUpRight size={13}/></button>
          </div>
        </div>
      </article>)}</div>
      <p className="feed-footnote">Reading, saving and refreshing subscriptions use no model tokens. Discuss opens a draft in Chat.</p>
    </div>
    <div hidden={pane!=='saved'}><Collections key={focusId||'saved'} focusId={focusId} kind="feed" items={items} online={online} onChanged={onChanged} onDiscuss={onDiscuss}/></div>
  </section>;
}
