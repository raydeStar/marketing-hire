import {useState,type ReactNode} from 'react';
import Markdown from 'react-markdown';
import {CheckCircle2,Dot,Play,Info} from 'lucide-react';
import {Modal} from './Modal';
import {WebsiteReadings} from './WebsiteReadings';
import {names} from './Raven';
import type {Run} from '../types';

const labels:Record<string,string>={'goal.created':'Request created','conversation.accepted':'Message received','conversation.retry.accepted':'New reply attempt','run.started':'Started','model.reserved':'Asked the model','model.completed':'Model replied','tool.request':'Prepared an action','tool.result':'Action result','public.retrieval.intent':'Page read prepared','public.retrieval.started':'Reading website','capability.result':'Tool result','conversation.web.read':'Website result ready','conversation.background':'Continued in the background','conversation.completed':'Reply delivered','delegation.reminder.review':'Reminder proposed','delegation.reminder.scheduled':'Reminder scheduled','delegation.occurrence.succeeded':'Reminder delivered','delegation.occurrence.failed':'Reminder failed','delegation.occurrence.unknown':'Reminder outcome needs review','delegation.occurrence.missed':'Reminder missed','delegation.occurrence.cancelled':'Reminder cancelled','delegation.job.cancelled':'Scheduled work cancelled','artifact.created':'App created','artifact.updated':'App updated','artifact.completed':'App saved','ideas.saved':'Ideas saved','run.completed':'Completed','run.failed':'Stopped with an error','run.cancelled':'Cancelled','approval.requested':'Asked for permission','approval.decided':'Permission recorded'};
function readable(type:string){return labels[type]||type.split(/[._-]/).map((word,i)=>i?word:word[0]?.toUpperCase()+word.slice(1)).join(' ');}
function eventFields(event:any):[string,string][]{
 const data=event.data||{},fields=textFields(data).filter(([label,value])=>label!=='Name'&&value.trim());
 if(data.goal?.objective)fields.unshift(['Request',data.goal.objective]);
 if(data.provider?.model)fields.push(['Model',data.provider.model],['Output allowance',Number(data.maxOutputTokens||data.budget?.maxOutputTokens||0).toLocaleString()+' tokens']);
 if(data.action?.name)fields.push(['Requested action',readable(data.action.name)]);
 if(data.name)fields.push(['Action',readable(data.name)]);
 if(data.inputTokens!=null||data.outputTokens!=null)fields.push(['Reported usage',`${data.inputTokens??'Unreported'} input tokens · ${data.outputTokens??'unreported'} output tokens`]);
 if(data.result?.source)fields.push(['Source',data.result.source.title||data.result.source.url],['URL',data.result.source.url],['Read at',new Date(data.result.source.retrieved).toLocaleString()],['Coverage',data.result.source.truncated?'Bounded excerpt':'Extracted page text']);
 if(data.result?.error)fields.push(['Could not read',data.result.error]);
 if(event.type==='ideas.saved')fields.push(['Result',`${data.count} ideas saved; suggested work has not started.`]);
 return fields;
}
function textFields(data:unknown):[string,string][]{if(!data||typeof data!=='object')return [];return Object.entries(data).filter(([key,value])=>typeof value==='string'&&!/id|hash|digest|credential|token|path|content/i.test(key)).map(([key,value]):[string,string]=>[readable(key),value as string]).slice(0,8);}
export function ActivityDialog({run,trace,onClose,children}:{run:Run;trace:any[];onClose:()=>void;children:ReactNode}){
 const [step,setStep]=useState<string|null>(null),events=trace.filter(e=>e.type!=='model.delta'),selected=events.find(e=>e.eventId===step);
 return <Modal title={run.goal.objective} onClose={onClose} className="activity-dialog"><div className="activity-dialog-status"><span className={'badge '+run.state}>{names[run.state]}</span><time>{new Date(run.created).toLocaleString()}</time></div><div className="activity-dialog-grid">
  <nav className="activity-steps" aria-label="Activity steps"><button aria-current={!selected?'step':undefined} onClick={()=>setStep(null)}><Info size={16}/><span>Summary</span></button>{events.map((event,i)=><button aria-current={step===event.eventId?'step':undefined} key={event.eventId} onClick={()=>setStep(event.eventId)}>{i===0?<Play size={14}/>:i===events.length-1&&run.state==='succeeded'?<CheckCircle2 size={16}/>:<Dot size={18}/>}<span>{readable(event.type)}<small>{new Date(event.timestamp).toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'})}</small></span></button>)}</nav>
  <section className="activity-reading" aria-live="polite">{selected?<><h3>{readable(selected.type)}</h3>{eventFields(selected).map(([label,value])=><div className="activity-field" key={label}><h4>{label}</h4><p>{value}</p></div>)}{selected.data?.path&&<p>File: {selected.data.path}</p>}{!eventFields(selected).length&&<p>This step was recorded at {new Date(selected.timestamp).toLocaleString()}.</p>}<details><summary>Technical details</summary><pre>{JSON.stringify(selected.data,null,2)}</pre></details></>:<><h3>Summary</h3><p className="activity-summary">{run.summary}</p>{run.draftText&&<div className="activity-answer"><Markdown>{run.draftText}</Markdown></div>}<WebsiteReadings run={run}/><dl className="activity-facts"><dt>Model</dt><dd>{run.goal.provider.model}</dd><dt>Reported tokens</dt><dd>{run.inputTokens==null&&run.outputTokens==null?'Not reported':((run.inputTokens||0)+(run.outputTokens||0)).toLocaleString()}</dd><dt>Model calls</dt><dd>{run.modelCalls}</dd><dt>Actions</dt><dd>{run.toolCalls}</dd></dl>{['awaitingApproval','awaitingInput','needsAttention','paused','running','queued'].includes(run.state)?<div className="activity-controls">{children}</div>:<details className="activity-full"><summary>Checks, sources & full receipts</summary>{children}</details>}</>}
  </section>
 </div></Modal>;
}
