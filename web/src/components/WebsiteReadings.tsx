import type {Run} from '../types';

export function WebsiteReadings({run}:{run?:Run}){
 const reads=(run?.capabilities??[]).filter(c=>c.name==='thaddeus_fetch_public_page');
 if(!reads.length)return null;
 const successful=reads.filter(c=>!c.isError&&c.result.source).length;
 return <details className="website-readings"><summary>{successful ? `Read ${successful} source${successful===1?'':'s'}` : 'Website could not be read'}{successful<reads.length&&successful>0?' · some reads failed':''}</summary>
  {reads.map(read=>{const source=read.result.source;return <div key={read.operationId}>{source&&!read.isError?<><a href={source.url.startsWith('https://')?source.url:undefined} target="_blank" rel="noreferrer">{source.title||source.url}</a><small>Read {new Date(source.retrieved).toLocaleString()}{source.truncated?' · excerpt only':''}</small></>:<p role="status">{read.result.error||'Reading was interrupted; no page contents were confirmed.'}</p>}</div>;})}
  <small>Public page reading · no search credits used</small>
 </details>;
}
