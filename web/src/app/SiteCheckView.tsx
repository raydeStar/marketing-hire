import {useCallback,useEffect,useState} from 'react';
import {FileText,Gauge,RefreshCw,Settings2} from 'lucide-react';
import {api} from '../api';
import {readableTime} from '../components/MarketingPanels';
import {SiteConnection} from './PublishingView';

type Issue={severity:'error'|'warning'|'notice';check:string;url:string;detail:string};
type Result={site:string;at:string;pages:number;robots:boolean;sitemap:boolean;issues:Issue[];reportWikiId:string|null};
type SiteCheckData={sites:string[];ownSite?:string|null;latest:Result[]};

const seconds=(value:string)=>Date.parse(value)/1000;

/** The next shift proposes new copy for one page of your site, fixing what the check found there. */
export function copyTask(url:string,issues:Issue[]){
  const found=issues.filter(item=>item.url===url).slice(0,4).map(item=>`${item.check}: ${item.detail}`).join(' ');
  return {requestId:crypto.randomUUID(),title:`New copy for ${url.replace(/^https:\/\/(www\.)?/,'').replace(/\/$/,'')||url}`.slice(0,160),status:'ready',priority:'normal',action_state:'agent_ready',
    next_action:`Propose new copy for ${url} as a page deliverable (page: ${url}). Fix what the site check found: ${found} Keep what already works, use only the brief's proof points, and say what changed and why.`.slice(0,990)};
}

/** Work → Site check: a technical SEO read of your own site, with the report in the Library. */
export function SiteCheckSection({owner,onOpen}:{owner:boolean;onOpen:(key:string)=>void}){
  const [data,setData]=useState<SiteCheckData|null>(null),[site,setSite]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState(''),[asked,setAsked]=useState<Record<string,boolean>>({}),[notice,setNotice]=useState('');
  async function askCopy(url:string){try{await api('/marketing/tasks',copyTask(url,result?.issues||[]));setAsked(current=>({...current,[url]:true}));setNotice('On it: the fix is written at the next check-in, with the page as it reads now beside it. You approve it before anything changes, and it’s logged with the page.');}catch(cause){setError((cause as Error).message);}}
  const load=useCallback(async()=>{try{const value=await api<SiteCheckData>('/site-audit');setData(value);setSite(current=>current||value.latest[0]?.site||value.sites[0]||'');setError('');}catch(cause){setError((cause as Error).message);}},[]);
  useEffect(()=>{void load();},[load]);
  async function run(){
    if(busy||!site)return;setBusy(true);setError('');
    try{await api<Result>('/site-audit',{site});await load();}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const result=data?.latest.find(item=>item.site===site);
  const mine=owner&&!!data?.ownSite&&site===data.ownSite;
  const count=(severity:Issue['severity'])=>result?.issues.filter(item=>item.severity===severity).length||0;
  return <section className="fe-section" aria-label="Site check">
    <div className="fe-section-head"><div><h2>Site check</h2><small>A technical SEO read of your own site: titles, descriptions, headings, alt text, thin pages, duplicates and broken links. Up to 25 pages, read politely; nothing on the site changes.</small></div>
      {owner&&data&&data.sites.length>0&&<>
        {data.sites.length>1&&<select aria-label="Site to check" value={site} onChange={event=>setSite(event.target.value)}>{data.sites.map(item=><option key={item} value={item}>{item}</option>)}</select>}
        <button type="button" disabled={busy||!site} onClick={()=>void run()}><RefreshCw size={15}/> {busy?'Checking…':result?'Check again':'Check the site'}</button></>}
      {owner&&<button type="button" onClick={()=>onOpen('brief:objectives')}><Settings2 size={15}/> Sites</button>}</div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {notice&&<p className="fe-notice" role="status">{notice}</p>}
    {data&&data.sites.length===0&&<div className="fe-empty-state"><Gauge size={20}/><strong>No site to check yet</strong><p>Add your site to the research sites in Objectives &amp; positioning. The check only reads sites you list.</p></div>}
    {busy&&<p className="fe-muted" role="status">Reading {site} one page at a time; this takes a minute.</p>}
    {result&&<>
      <dl className="fe-stats compact" aria-label={`Site check of ${result.site}`}>
        <div className={count('error')?'attn':'ok'}><dt>To fix</dt><dd>{count('error')}</dd></div>
        <div><dt>Worth improving</dt><dd>{count('warning')}</dd></div>
        <div><dt>To note</dt><dd>{count('notice')}</dd></div>
        <div><dt>Pages read</dt><dd>{result.pages}</dd></div>
      </dl>
      {mine&&result.issues.length>0&&<SiteConnection onOpen={onOpen}/>}
      {result.issues.length>0&&<div className="fe-table-wrap"><table className="fe-table"><thead><tr><th>Check</th><th>Page</th><th>What we found</th>{mine&&<th aria-label="Action"/>}</tr></thead><tbody>
        {[...result.issues].sort((a,b)=>({error:0,warning:1,notice:2}[a.severity]-{error:0,warning:1,notice:2}[b.severity])).slice(0,8).map((item,index)=><tr key={index} className={item.severity==='error'?'bad':''}>
          <td><span className={'fe-pill '+(item.severity==='error'?'attn':item.severity==='warning'?'':'ok')}>{item.check}</span></td>
          <td className="fe-url">{item.url.replace(/^https:\/\//,'')}</td><td>{item.detail}</td>
          {mine&&<td>{item.check!=='Broken link'&&!/sitemap|robots/i.test(item.check)&&<button type="button" className="fe-ghost" disabled={asked[item.url]} onClick={()=>void askCopy(item.url)} aria-label={`Fix ${item.url} for me`}>{asked[item.url]?'On it':'Fix for me'}</button>}</td>}</tr>)}
      </tbody></table></div>}
      <small className="fe-muted fe-block">Checked {readableTime(seconds(result.at))} · robots.txt {result.robots?'present':'missing'} · sitemap {result.sitemap?'present':'missing'}.
        {result.reportWikiId&&<> <button type="button" className="fe-link" onClick={()=>onOpen('wiki:'+result.reportWikiId)}><FileText size={12}/> Full report</button></>}</small>
    </>}
  </section>;
}
