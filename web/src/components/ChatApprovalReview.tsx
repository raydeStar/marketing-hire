import {Check,ShieldCheck,Settings2} from 'lucide-react';
import type {DelegationJob,Run} from '../types';

type Props={run:Run;jobs:DelegationJob[];owner:boolean;online:boolean;busy:boolean;onDecision:(allow:boolean,remember?:'allow'|'deny')=>void;onSettings:()=>void;onDetails:()=>void};
type Values=Record<string,unknown>;

const parse=(content?:string):Values=>{try{return JSON.parse(content||'{}') as Values;}catch{return {};}};
const text=(value:unknown,fallback='Unknown')=>typeof value==='string'&&value.trim()?value:fallback;
const record=(value:unknown):Values=>value&&typeof value==='object'&&!Array.isArray(value)?value as Values:{};
const date=(value:unknown)=>typeof value==='string'&&!Number.isNaN(Date.parse(value))?new Date(value).toLocaleString():'Unknown';

export function ChatApprovalReview({run,jobs,owner,online,busy,onDecision,onSettings,onDetails}:Props){
  const approval=run.approval;if(!approval)return null;
  const action=approval.action,body=parse(action.content),name=action.name;
  const reminder=name==='delegation_schedule_reminder';
  const email=name.startsWith('delegation_email_');
  const brief=name.startsWith('delegation_brief_');
  const inbox=name.startsWith('delegation_inbox_watch_');
  const todos=name==='todo_batch_create';
  const soul=name==='soul_edit'&&action.path==='SOUL.md';
  const user=name==='user_edit'&&action.path==='USER.md';
  const management=['delegation_cancel','delegation_reschedule_reminder','delegation_edit_email','delegation_pause_brief','delegation_resume_brief','delegation_edit_brief'].includes(name);
  const connected=run.connectedTools?.find(tool=>tool.modelName===name&&tool.connectorId===action.path);
  const scheduledEmail=record(body.email),scheduledBrief=record(body.brief),scheduledWatch=record(body.watch);
  const watchEmail=record(scheduledWatch.email),briefEmail=record(scheduledBrief.email),briefCalendar=record(scheduledBrief.calendar);
  const watchTool=record(watchEmail.tool),briefEmailTool=record(briefEmail.tool),briefCalendarTool=record(briefCalendar.tool);
  const managed=management?jobs.find(job=>job.id===(text(body.jobId,'')||action.path)):undefined;
  const items=Array.isArray(body.items)?body.items.map(record):[];
  const title=soul?'Soul change review':user?'User profile review':inbox?'Inbox watch review':brief?'Recurring brief review':email?'Scheduled email review':reminder?'Reminder review':todos?'To-do batch review':management?'Delegated-work change':connected?'Connected action review':'Exact action review';
  const approve=soul?'Update Soul':user?'Update User':inbox?'Enable inbox watch':brief?'Schedule this brief':email?'Schedule this email':reminder?'Schedule reminder':todos?'Create these To-dos':management?'Apply this change':connected?'Approve exact action':'Approve exact write';
  const deny=soul?'Keep current Soul':user?'Keep current profile':(inbox||brief||email||reminder)?'Do not schedule':todos?'Create nothing':management?'Keep unchanged':connected?'Deny action':'Deny write';
  const ready=run.state==='awaitingApproval'&&approval.decision==='pending';
  return <section className="approval-card chat-approval-review" aria-label={title}>
    <div className="card-heading"><ShieldCheck/><div><h2>{title}</h2><p>Review the exact action here before Thaddeus proceeds.</p></div></div>
    {reminder&&<div className="delegation-review"><h3>{text(body.title,'Reminder')}</h3><p>{text(body.message,'No message')}</p><dl><dt>When</dt><dd>{body.immediate===true?'Immediately after approval':date(body.dueUtc)}</dd>{body.immediate!==true&&<><dt>Timezone</dt><dd>{text(body.timeZone)}</dd></>}<dt>Authority</dt><dd>Deliver this exact reminder once</dd></dl></div>}
    {email&&<div className="delegation-review"><h3>{text(scheduledEmail.subject,'(No subject)')}</h3><p>{text(scheduledEmail.body,'No body')}</p><dl><dt>Sender</dt><dd>{text(scheduledEmail.senderConnection)}</dd><dt>Recipient</dt><dd>{text(scheduledEmail.recipient)}</dd><dt>Send time</dt><dd>{date(body.dueUtc)}</dd><dt>Timezone</dt><dd>{text(body.timeZone)}</dd><dt>Authority</dt><dd>Send this exact email once</dd></dl></div>}
    {brief&&<div className="delegation-review"><h3>Weekday morning brief</h3><dl><dt>When</dt><dd>{text(scheduledBrief.localTime)} · {text(scheduledBrief.timeZone)}</dd><dt>Calendar</dt><dd>{text(briefCalendarTool.connectorName)}</dd><dt>Email</dt><dd>{text(briefEmailTool.connectorName)}</dd><dt>Email selection</dt><dd>{text(scheduledBrief.emailSelectionRule)}</dd><dt>Authority</dt><dd>Read the bounded sources and compose one brief each scheduled day</dd></dl></div>}
    {inbox&&<div className="delegation-review"><h3>Important inbox watch</h3><dl><dt>Mail account</dt><dd>{text(watchTool.connectorName)}</dd><dt>Interval</dt><dd>Every five minutes while this host is running and awake</dd><dt>Importance</dt><dd>{text(scheduledWatch.importanceInstruction)}</dd><dt>Authority</dt><dd>Read and assess bounded new mail; never change messages</dd></dl></div>}
    {todos&&<div className="delegation-review"><p>Create {items.length} editable item{items.length===1?'':'s'} from {text(body.sourceReference,'the supplied material')}.</p>{items.map((item,index)=><article className="receipt-block" key={index}><h3>{text(item.title,`To-do ${index+1}`)}</h3>{typeof item.notes==='string'&&item.notes&&<p>{item.notes}</p>}<dl><dt>Due</dt><dd>{text(item.due,'Unresolved')}</dd>{typeof item.ambiguity==='string'&&item.ambiguity&&<><dt>Needs review</dt><dd>{item.ambiguity}</dd></>}</dl></article>)}</div>}
    {management&&<div className="delegation-review"><h3>{managed?.title||'Delegated work'}</h3><dl><dt>Change</dt><dd>{name.replaceAll('_',' ')}</dd><dt>Current state</dt><dd>{managed?.state||'Changed or unavailable'}</dd><dt>Current time</dt><dd>{managed?.nextRunUtc?new Date(managed.nextRunUtc).toLocaleString():'No future run'}</dd>{typeof body.dueUtc==='string'&&<><dt>New time</dt><dd>{date(body.dueUtc)}</dd></>}</dl></div>}
    {(soul||user)&&<div className="delegation-review"><p>This replaces the current {soul?'Soul':'User profile'} with the exact text below. It does not grant tools or permissions.</p><pre>{action.content}</pre></div>}
    {!reminder&&!email&&!brief&&!inbox&&!todos&&!management&&!soul&&!user&&<div className="delegation-review"><p>{connected?`Use ${connected.connectorName} · ${connected.remoteName}. Effect: ${connected.effect}.`:`${action.name} · ${action.path}`}</p><pre>{action.content}</pre></div>}
    {run.approvalPolicy&&<p className="approval-scope">A remembered choice applies only to <strong>{run.approvalPolicy.label}</strong>. Changing the connector or action scope makes Thaddeus ask again.</p>}
    <details><summary>Exact arguments & approval binding</summary><pre>{JSON.stringify(approval,null,2)}</pre></details>
    <div className="approval-actions chat-approval-actions"><span>Expires {new Date(approval.expires).toLocaleTimeString()}</span><button disabled={!ready||busy||!online} onClick={()=>onDecision(false)}>{deny}</button>{owner&&run.approvalPolicy&&<button disabled={!ready||busy||!online} onClick={()=>onDecision(false,'deny')}>Always deny this type</button>}<button className="primary" disabled={!ready||busy||!online} onClick={()=>onDecision(true)}><Check size={16}/>{approve}</button>{owner&&run.approvalPolicy&&<button className="primary" disabled={!ready||busy||!online} onClick={()=>onDecision(true,'allow')}><Check size={16}/>Always allow this type</button>}</div>
    <div className="approval-links"><button className="text-button" type="button" onClick={onSettings}><Settings2 size={15}/>Approval settings</button><button className="text-button" type="button" onClick={onDetails}>View task receipts</button></div>
  </section>;
}
