import {useCallback,useEffect,useState} from 'react';
import {ExternalLink,MessageCircle,Radio,RefreshCw,Settings2} from 'lucide-react';
import {api} from '../api';
import {publicLink,readableTime} from '../components/MarketingPanels';

type Mention={id:string;topic:string;source:string;title:string;snippet:string;url:string;publishedAt:string;sentiment:'positive'|'negative'|'neutral'};
type TopicStats={topic:string;last24h:number;perDayPriorWeek:number;negativeShare:number;daily:number[];flag:null|'mention_spike'|'sentiment_drop'};
type Watched={url:string;title:string|null;checkedAt:string|null;prices:string[];error:string|null;lastChange:{at:string;kind:'prices'|'copy';summary:string}|null};
export type ListeningData={topics:string[];feeds:string[];lastScanAt:string|null;errors:string[];stats:TopicStats[];mentions:Mention[];watch?:Watched[]};

function Bars({values,label}:{values:number[];label:string}){
  const max=Math.max(1,...values);
  return <svg className="fe-bars" viewBox={`0 0 ${values.length*6} 20`} role="img" aria-label={label} preserveAspectRatio="none">
    {values.map((value,index)=><rect key={index} x={index*6} y={20-Math.max(1,value/max*20)} width={4} height={Math.max(1,value/max*20)} className={index===values.length-1?'last':''}/>)}</svg>;
}

const flagLabel:Record<string,string>={mention_spike:'Spike',sentiment_drop:'Negative turn'};
const whenOf=(value:string)=>new Date(value).getTime()/1000;
/** Mentions that can be answered where they were said. */
const replyable=(item:Mention)=>/^https:\/\/(bsky\.app\/profile\/[^/]+\/post\/|news\.ycombinator\.com\/item\?id=)/.test(item.url);
/** The next shift drafts the reply; the owner approves it and posts it from the post itself. */
export function replyTask(item:Mention){
  const network=item.url.includes('bsky.app')?'Bluesky':'Hacker News';
  const quoted=(item.snippet||item.title).replace(/\s+/g,' ').trim().slice(0,400);
  return {requestId:crypto.randomUUID(),title:`Reply on ${network}: ${item.title.slice(0,110)}`,status:'ready',priority:'normal',action_state:'agent_ready',
    next_action:`Draft a reply as a ${network} draft whose destination is exactly ${item.url} . What they said: “${quoted}”. Keep it short and useful to that person; no pitch unless they asked for one. If replying would not help, say so instead of drafting.`};
}

/** What the employee hears between shifts: mentions of the watch topics and new posts on followed feeds, with spikes and negative turns flagged. */
export function ListeningSection({owner,onOpen}:{owner:boolean;onOpen:(key:string)=>void}){
  const [data,setData]=useState<ListeningData|null>(null),[all,setAll]=useState(false),[busy,setBusy]=useState(false),[notice,setNotice]=useState(''),[error,setError]=useState('');
  const [asked,setAsked]=useState<Record<string,boolean>>({});
  async function askReply(item:Mention){
    setError('');
    try{await api('/marketing/tasks',replyTask(item));setAsked(current=>({...current,[item.id]:true}));setNotice('Assigned: the employee drafts the reply on its next shift, for you to approve.');}
    catch(cause){setError((cause as Error).message);}
  }
  const load=useCallback(async()=>{try{setData(await api<ListeningData>('/listening'));setError('');}catch(cause){setError((cause as Error).message);}},[]);
  useEffect(()=>{void load();},[load]);
  async function listen(){
    if(busy)return;setBusy(true);setNotice('');setError('');
    try{const result=await api<{scan:{new:number;errors:string[]};view:ListeningData}>('/listening/scan',{});setData(result.view);setNotice(`${result.scan.new} new mention${result.scan.new===1?'':'s'}.`);}
    catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const configured=!!data&&(data.topics.length>0||data.feeds.length>0||!!data.watch?.length);
  return <section className="fe-section" aria-label="Listening">
    <div className="fe-section-head"><div><h3>Listening</h3><small>Public mentions of your topics (Hacker News, Google News, Bluesky) and new posts on the feeds you follow. Checked every hour at no model cost; only spikes and negative turns reach a shift.</small></div>
      {owner&&configured&&<button type="button" disabled={busy} onClick={()=>void listen()}><RefreshCw size={15}/> {busy?'Listening…':'Listen now'}</button>}
      {owner&&<button type="button" onClick={()=>onOpen('brief:objectives')}><Settings2 size={15}/> What to watch</button>}</div>
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {data&&!configured&&<div className="fe-empty-state"><Radio size={20}/><strong>Nothing to listen to yet</strong><p>Add a few topics to watch (your product, your category, competitors’ names) and the blogs or newsletters you follow. The employee builds a baseline, then tells you only when something changes.</p>{owner&&<button type="button" onClick={()=>onOpen('brief:objectives')}>Choose what to watch</button>}</div>}
    {data&&configured&&<>
      {!!data.watch?.length&&<div className="fe-table-wrap"><table className="fe-table" aria-label="Watched pages"><thead><tr><th>Watched page</th><th>Prices now</th><th>Last read</th><th>Last change</th></tr></thead><tbody>
        {data.watch.map(item=>{const link=publicLink(item.url);const recent=item.lastChange&&Date.now()-Date.parse(item.lastChange.at)<3*86400000;
          return <tr key={item.url} className={recent&&item.lastChange!.kind==='prices'?'bad':''}>
            <td>{link?<a href={link} target="_blank" rel="noopener noreferrer">{item.title||item.url} <ExternalLink size={12}/></a>:item.url}</td>
            <td>{item.prices.length?item.prices.slice(0,6).join(', ')+(item.prices.length>6?' …':''):<span className="fe-muted">{item.checkedAt?'None shown':'—'}</span>}</td>
            <td>{item.error?<span className="fe-pill attn" title={item.error}>Couldn’t read</span>:item.checkedAt?readableTime(whenOf(item.checkedAt)):<span className="fe-muted">Next pass</span>}</td>
            <td>{item.lastChange?<span>{item.lastChange.kind==='prices'&&<span className="fe-pill attn">Price change</span>} <small>{readableTime(whenOf(item.lastChange.at))}: {item.lastChange.summary}</small></span>:<span className="fe-muted">{item.checkedAt?'No change since the first read':'—'}</span>}</td></tr>;})}
      </tbody></table></div>}
      {data.stats.length>0&&<div className="fe-table-wrap"><table className="fe-table fe-listening"><thead><tr><th>Topic</th><th>Last 24h</th><th>Per day, prior week</th><th>Negative</th><th>14 days</th></tr></thead><tbody>
        {data.stats.map(item=><tr key={item.topic} className={item.flag?'bad':''}>
          <td><span className="fe-cell-metric"><strong>{item.topic}</strong>{item.flag&&<span className="fe-pill attn">{flagLabel[item.flag]}</span>}</span></td>
          <td className="fe-num">{item.last24h}</td><td className="fe-num">{item.perDayPriorWeek}</td>
          <td className="fe-num">{item.last24h?`${Math.round(item.negativeShare*100)}%`:'—'}</td>
          <td><Bars values={item.daily} label={`Mentions per day for ${item.topic}, last 14 days`}/></td></tr>)}
      </tbody></table></div>}
      <small className="fe-muted fe-block">{data.lastScanAt?`Last checked ${readableTime(whenOf(data.lastScanAt))}.`:'Not checked yet.'} Sentiment is a word-list estimate: a reason to read, not a verdict.{data.errors.length>0?` ${data.errors.join(' ')}`:''}</small>
      {data.mentions.length>0&&<ul className="fe-mentions" aria-label="Recent mentions">{data.mentions.slice(0,all?40:5).map(item=>{const link=publicLink(item.url);
        return <li key={item.id}><div>{link?<a href={link} target="_blank" rel="noopener noreferrer">{item.title} <ExternalLink size={12}/></a>:<strong>{item.title}</strong>}
          <small>{item.source} · {item.topic.startsWith('feed:')?'feed':item.topic} · {readableTime(whenOf(item.publishedAt))}</small></div>
          {item.sentiment!=='neutral'&&<span className={'fe-pill '+(item.sentiment==='negative'?'attn':'ok')}>{item.sentiment==='negative'?'Negative':'Positive'}</span>}
          {owner&&replyable(item)&&<button type="button" className="fe-ghost" disabled={asked[item.id]} onClick={()=>void askReply(item)} aria-label={`Ask for a reply to ${item.title}`}><MessageCircle size={13}/> {asked[item.id]?'Reply asked':'Ask for a reply'}</button>}</li>;})}</ul>}
      {data.mentions.length>5&&<button type="button" className="fe-ghost fe-more" onClick={()=>setAll(!all)}>{all?'Show fewer':`Show ${Math.min(40,data.mentions.length)-5} more`}</button>}
    </>}
  </section>;
}
