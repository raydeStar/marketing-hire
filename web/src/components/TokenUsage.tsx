import type {Run} from '../types';

export function TokenUsage({runs,onRun}:{runs:Run[];onRun:(id:string)=>void}){
  const live=runs.filter(run=>run.goal.provider.kind==='compatible'&&run.modelCalls>0);
  const reported=live.reduce((total,run)=>total+(run.inputTokens??0)+(run.outputTokens??0),0);
  const reserved=live.reduce((total,run)=>total+(run.reservedTokens??0),0);
  const unknown=live.filter(run=>run.inputTokens==null||run.outputTokens==null||!!run.reservedTokens);
  const format=(value:number)=>value.toLocaleString();
  return <details className="token-usage" aria-label="Token usage">
    <summary>Token usage · {format(reported)} reported{reserved>0&&` · ${format(reserved)} reserved`}{unknown.length>0&&` · ${unknown.length} task${unknown.length===1?'':'s'} with unreported usage`}</summary>
    <p>Totals cover retained task history on this host, including conversation context sent again. Scripted demos are excluded. These are provider-reported counts, not a bill or your Codex account quota.</p>
    {unknown.length>0&&<p role="status">Some usage is unreported. The reported total is incomplete; reservations are held against task allowances and are not measured consumption.</p>}
    <p>Task allowances prevent further calls once exhausted. An uncertified provider can exceed a requested limit; strict token admission refuses those providers before dispatch. Dollar cost is not tracked.</p>
    <ul>{live.slice(0,8).map(run=><li key={run.id}><button className="text-button" onClick={()=>onRun(run.id)}>{run.goal.objective.slice(0,65)}{run.goal.objective.length>65?'…':''}</button><small>
      {' '}Input {run.inputTokens==null?'unreported':format(run.inputTokens)} · output {run.outputTokens==null?'unreported':format(run.outputTokens)} · allowance charged {format(run.chargedTokens??0)} / {format(run.goal.limits.maxTotalTokens)} · reserved {format(run.reservedTokens??0)} · remaining allowance {format(Math.max(0,run.goal.limits.maxTotalTokens-(run.chargedTokens??0)-(run.reservedTokens??0)))}
    </small></li>)}</ul>
    {!live.length&&<p>No live model dispatches in retained history.</p>}
  </details>;
}
