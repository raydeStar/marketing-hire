import type {Run} from '../types';

export function PublicResearchActivity({run}:{run:Run}){
 const calls=(run.capabilities??[]).filter(call=>call.name==='thaddeus_search_public_web');
 if(!calls.length)return null;
 return <section className="receipt-block" aria-label="Public search receipts"><h2>Public search</h2>
  <p>{calls.length} search attempt{calls.length===1?'':'s'} used{run.goal.web?.search&&` / ${run.goal.web.search.maxQueries} allowed`}. Requests are separate from model tokens. Dollar cost is not measured.</p>
  {calls.map(call=><details key={call.operationId}><summary>{call.result.query||'Search attempt'} · {call.isError?'needs attention':'result recorded'}</summary>
   <p>{call.result.provider||'Search provider'} · {new Date(call.recorded).toLocaleString()}</p>
   {call.result.error&&<p role="status">{call.result.error}</p>}{call.result.status==='search-outcome-unknown'&&<p>The provider outcome is unknown. This request is not retried automatically.</p>}
   <p>These links and snippets are discovery hints. Page quotation checks require a separate recorded fetch.</p>
   {call.result.results?.map(hit=><article key={hit.url}><a href={hit.url.startsWith('https://')?hit.url:undefined} target="_blank" rel="noreferrer">{hit.title||hit.url}</a><p>{hit.description}</p></article>)}
   <small>Receipt {call.operationId} · {call.authority}{call.result.httpStatus!=null&&` · HTTP ${call.result.httpStatus}`}</small>
  </details>)}
 </section>;
}
