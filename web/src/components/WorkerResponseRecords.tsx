import {readableTime,type RunwaySnapshot} from './MarketingPanels';

export function WorkerResponseRecords({runway}:{runway?:RunwaySnapshot|null}){
  if(!runway?.worker_responses?.length)return null;
  return <section aria-label="Employee response records">
    <details className="runway-executions">
      <summary>Employee response records · {runway.worker_responses.length}</summary>
      <p>Original replies saved before validation. These records do not establish token usage or approve a draft. Reviewable results appear under Saved work and review.</p>
      {runway.worker_responses.map(reply=><details className="runway-artifact" key={reply.execution_id}>
        <summary>{reply.kind.replaceAll('_',' ')} · received {readableTime(reply.received_at)}</summary>
        {reply.truncated&&<p role="status">Preview shortened to 12,000 characters. The original response had {reply.original_characters.toLocaleString()} characters; its full text is unavailable here.</p>}
        <pre>{reply.content}</pre>
        <small>Execution {reply.execution_id} · configured model {reply.configured_model}<br/>Original reply SHA-256: {reply.digest}</small>
      </details>)}
    </details>
  </section>;
}
