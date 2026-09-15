import {useId} from 'react';
import type {Run} from '../types';

function usageTotals(runs:Run[]){
  const live=runs.filter(run=>run.goal.provider.kind==='compatible'&&run.modelCalls>0);
  const reported=live.reduce((total,run)=>total+(run.inputTokens??0)+(run.outputTokens??0),0);
  const reserved=live.reduce((total,run)=>total+(run.reservedTokens??0),0);
  const unknown=live.filter(run=>run.inputTokens==null||run.outputTokens==null||!!run.reservedTokens);
  const searchAttempts=runs.reduce((count,run)=>count+(run.capabilities??[]).filter(call=>call.name==='thaddeus_search_public_web').length,0);
  return {live,reported,reserved,unknown,searchAttempts};
}

export function ModelUsageButton({model,runs,expanded,onOpen}:{model:string;runs:Run[];expanded:boolean;onOpen:(trigger:HTMLButtonElement)=>void}){
  const tooltipId=useId();
  const {reported,reserved,unknown}=usageTotals(runs);
  return <button type="button" className="model-usage" aria-label={`${model}: token usage`} aria-describedby={tooltipId}
    aria-expanded={expanded} aria-controls="activity-log" onClick={event=>onOpen(event.currentTarget)}>
    <span className="model-name">{model}</span>
    <span className="model-usage-tooltip" id={tooltipId} role="tooltip">
      <strong>{reported.toLocaleString()} reported tokens</strong>
      <span>Retained study history · all models</span>
      {reserved>0&&<span>{reserved.toLocaleString()} reserved · not measured usage</span>}
      {unknown.length>0&&<span>Incomplete total · {unknown.length} task{unknown.length===1?'':'s'} with unreported usage</span>}
      <span>Open Log → Info for details</span>
    </span>
  </button>;
}

export function TokenUsage({runs,onRun,expanded,onExpandedChange}:{runs:Run[];onRun:(id:string)=>void;expanded:boolean;onExpandedChange:(expanded:boolean)=>void}){
  const {live,reported,reserved,unknown,searchAttempts}=usageTotals(runs);
  const format=(value:number)=>value.toLocaleString();
  return <details className="token-usage" aria-label="Token usage" open={expanded} onToggle={event=>onExpandedChange(event.currentTarget.open)}>
    <summary>Token usage · {format(reported)} reported{searchAttempts>0&&` · ${format(searchAttempts)} search attempt${searchAttempts===1?'':'s'}`}{reserved>0&&` · ${format(reserved)} reserved`}{unknown.length>0&&` · ${unknown.length} task${unknown.length===1?'':'s'} with unreported usage`}</summary>
    <p>Totals cover retained task history on this host, including conversation context sent again. Scripted demos are excluded. These are provider-reported counts, not a bill or your Codex account quota.</p>
    {unknown.length>0&&<p role="status">Some usage is unreported. The reported total is incomplete; reservations are held against task allowances and are not measured consumption.</p>}
    <p>Task allowances prevent further calls once exhausted. An uncertified provider can exceed a requested limit; strict token admission refuses those providers before dispatch. Dollar cost is not tracked.</p>
    {searchAttempts>0&&<p>{format(searchAttempts)} search attempt{searchAttempts===1?'':'s'} recorded in retained history, including failures and unknown outcomes. Search requests are separate from model tokens; see task receipts for details. No provider bill is inferred.</p>}
    <ul>{live.slice(0,8).map(run=><li key={run.id}><button className="text-button" onClick={()=>onRun(run.id)}>{run.goal.objective.slice(0,65)}{run.goal.objective.length>65?'…':''}</button><small>
      {' '}Input {run.inputTokens==null?'unreported':format(run.inputTokens)} · output {run.outputTokens==null?'unreported':format(run.outputTokens)} · allowance charged {format(run.chargedTokens??0)} / {format(run.goal.limits.maxTotalTokens)} · reserved {format(run.reservedTokens??0)} · remaining allowance {format(Math.max(0,run.goal.limits.maxTotalTokens-(run.chargedTokens??0)-(run.reservedTokens??0)))}
    </small></li>)}</ul>
    {!live.length&&<p>No live model dispatches in retained history.</p>}
  </details>;
}
