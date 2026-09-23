import {Check,ExternalLink,Globe,Hand,Pause,Play,X} from 'lucide-react';
import type {Run} from '../types';
import type {Limits} from './BudgetFields';

export const defaultBrowserLimits:Limits={modelCalls:8,toolCalls:12,maxOutputTokens:4096,seconds:600,repairs:0,maxTotalTokens:64000,requireCertifiedTokenBound:false};
export function BrowserAllowance({value,onChange}:{value:Limits;onChange:(value:Limits)=>void}){
  return <details><summary>Chrome task allowance</summary><p>A browser task has its own allowance, reviewed before Chrome opens. Proposal usage counts toward it. Lower reply token and time limits still apply.</p><div className="budget-grid">{([['modelCalls','Browser model calls',1,8],['toolCalls','Browser actions',1,12],['seconds','Browser active seconds',1,600],['maxTotalTokens','Browser token allowance',1,64000]] as const).map(([key,label,min,max])=><label key={key}>{label}<input type="number" min={min} max={max} value={value[key]} onChange={event=>onChange({...value,[key]:Number(event.target.value)})}/></label>)}</div></details>;
}
type Command='pause'|'takeover'|'resume'|'close';
export function BrowserTaskCard({run,owner,disabled,onDecision,onControl,onDetails}:{run:Run;owner:boolean;disabled:boolean;onDecision:(allow:boolean)=>void;onControl?:(command:Command)=>void;onDetails:()=>void}){
  const task=run.browser;if(!task)return null;
  const initial=task.phase==='review',review=run.state==='awaitingApproval'&&run.approval?.decision==='pending';
  const action=task.pendingAction,limits=task.scope.limits;
  const stopped=['paused','takeover'].includes(task.phase),closed=['closed','interrupted'].includes(task.phase);
  const unknown=task.receipts.some(receipt=>receipt.state==='outcome-unknown');
  const active=['opening','working','resuming','action-approved','review-action'].includes(task.phase);
  const tokenAllowanceSpent=(run.chargedTokens??0)>=limits.maxTotalTokens;
  const interruptedModel=run.modelStages?.some(stage=>stage.status==='interrupted')??false;
  return <section className="approval-card chat-approval-review browser-task-card" aria-label="Chrome task">
    <div className="card-heading"><Globe/><div><h2>{initial?'Open Chrome for this task':'Chrome task'}</h2><p role="status">{run.summary}</p></div></div>
    <h3>{task.scope.objective}</h3>
    <p className="browser-task-address"><ExternalLink size={14}/>{task.page?.url||task.scope.startUrl}</p>
    {initial&&<><dl><dt>Websites</dt><dd>{task.scope.hosts.join(', ')}</dd><dt>Allowance</dt><dd>Up to {limits.modelCalls} model calls, {limits.toolCalls} browser actions, {limits.maxTotalTokens.toLocaleString()} tokens and {limits.seconds} active seconds</dd><dt>Permission</dt><dd>Read and navigate these websites for this task. Every click and form action gets a separate exact review.</dd><dt>Ends</dt><dd>Two hours after approval, or when closed. Waiting for you does not spend active time.</dd></dl><p>Chrome opens in a separate saved profile. Use Take over to sign in or handle a CAPTCHA; credentials belong in Chrome.</p></>}
    {review&&!initial&&action&&<div className="delegation-review"><h3>{action.description||action.kind}</h3><dl><dt>Action</dt><dd>{action.kind}</dd><dt>Target</dt><dd>{action.target}</dd>{action.text!==null&&action.text!==undefined&&<><dt>Exact text</dt><dd className="browser-exact-text">{action.text||'(empty)'}</dd></>}{action.values&&<><dt>Selected values</dt><dd>{action.values.join(', ')}</dd></>}{action.key&&<><dt>Key</dt><dd>{action.key}</dd></>}</dl><p>This approval is for this page and target once. Changing the page or taking over invalidates it.</p></div>}
    {unknown&&<p className="error" role="alert">An action may have reached the website, but its result is unknown. Inspect Chrome and the receipt before starting anything new. It will not be retried.</p>}
    {!initial&&<p className="muted">{run.modelCalls}/{limits.modelCalls} model calls · {run.toolCalls}/{limits.toolCalls} browser actions · {Math.ceil(task.activeSeconds)} active seconds{task.authorizedUntil&&<> · permission ends {new Date(task.authorizedUntil).toLocaleTimeString()}</>}</p>}
    {review&&<div className="approval-actions chat-approval-actions"><span>Review expires {new Date(run.approval!.expires).toLocaleTimeString()}</span><button disabled={disabled||!owner} onClick={()=>onDecision(false)}>Deny</button><button className="primary" disabled={disabled||!owner} onClick={()=>onDecision(true)}><Check size={16}/>{initial?'Open Chrome & begin':'Approve this action'}</button></div>}
    {onControl&&!initial&&!closed&&<div className="browser-task-controls">{active&&<><button disabled={disabled||!owner} onClick={()=>onControl('pause')}><Pause size={15}/>Pause</button><button disabled={disabled||!owner} onClick={()=>onControl('takeover')}><Hand size={15}/>Take over</button></>}{stopped&&!unknown&&!tokenAllowanceSpent&&<button disabled={disabled||!owner} onClick={()=>onControl('resume')}><Play size={15}/>Resume AI</button>}<button disabled={disabled||!owner} onClick={()=>onControl('close')}><X size={15}/>Close Chrome</button></div>}
    {stopped&&tokenAllowanceSpent&&!unknown?<p role="status">This Chrome task's token allowance is spent.{interruptedModel?' An interrupted model call had unknown usage, so its reserved allowance was retained.':''} Close Chrome and ask again to review a new task.</p>:stopped&&<p>AI is stopped. You can use Chrome yourself. Resume reads the page again and asks before another action.</p>}
    <button className="text-button" onClick={onDetails}>View task receipts</button>
  </section>;
}
