import {useEffect,useState} from 'react';
import {ArrowUpRight,BookOpen,CalendarClock,Check,CircleAlert,ExternalLink,Eye,FileText,Play,Plug,Radio,Rss,Send,Plus,Settings2,ThumbsDown,ThumbsUp,Wrench,X} from 'lucide-react';
import {api} from '../api';
import {readableTime,type MarketingDraft,type MarketingState} from '../components/MarketingPanels';
import {isChannelKind,openComposer,openConnect,usePublishing,type ChannelKind,type PublishingData} from './PublishingView';
import type {ShiftView} from './shifts';
import {useCurrentActivity} from './ShiftFeed';
import {useWeekly,type WeeklyDoc} from './WeeklyView';
import {plain} from './shared';
import {draftText} from './draftText';
import {useMissingByKey} from './Rubric';

/** “an email”, “a blog post”, “a LinkedIn post”: what a draft is, by its channel. */
function draftNoun(channel:string){
  const name=channel.trim();
  if(/email|newsletter/i.test(name))return 'an email';
  if(/blog|article/i.test(name))return 'a blog post';
  return `${/^[aeiou]/i.test(name)?'an':'a'} ${name} post`;
}

/** What the employee may offer as a button. The host checks everything again when it runs. */
export type ChatAction=
  |{type:'open';target:string;label?:string}
  |{type:'approve';draftId:number}
  |{type:'reject';draftId:number;note?:string}
  |{type:'schedule';draftId:number;at:string}
  |{type:'publish';draftId:number}
  |{type:'assist';draftId:number;at?:string}
  |{type:'shift';minutes:number;tokenBudget?:number}
  |{type:'watch';topic:string}
  |{type:'feed';url:string}
  |{type:'document';title:string;folder?:string}
  // Settings and connections: the card says what changes, and nothing does until the owner confirms (a connection's own popup
  // is its confirmation).
  |{type:'hours';enabled:boolean;days:number[];start:string;end:string}
  |{type:'weekly';enabled:boolean}
  |{type:'cta';label:string;url:string}
  |{type:'ownSite';url:string}
  |{type:'connect';kind:ChannelKind}
  |{type:'fix';url:string;what:string}
  |{type:'finish';key:string;missing:string[]}
  |{type:'task';title:string;note?:string;priority:'high'|'normal'};

const clock=/^([01]\d|2[0-3]):[0-5]\d$/;
const dayNames=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
/** "weekdays", "Mon, Wed", "every day": the days a schedule runs, in words. */
function daysLine(days:number[]){
  const sorted=[...days].sort().join();
  return sorted==='1,2,3,4,5'?'weekdays':sorted==='0,1,2,3,4,5,6'?'every day':sorted==='0,6'?'weekends':[...days].sort().map(day=>dayNames[day]).join(', ');
}
const hour=(value:string)=>{const [h,m]=value.split(':').map(Number);return new Date(2000,0,1,h,m).toLocaleTimeString(undefined,{hour:'numeric',minute:m?'2-digit':undefined});};
const pageName=(url:string)=>url.replace(/^https?:\/\/(www\.)?/,'').replace(/\/$/,'')||url;
const browserZone=(()=>{try{return Intl.DateTimeFormat().resolvedOptions().timeZone;}catch{return 'UTC';}})();

const targets=/^(draft:\d+|task:[A-Za-z0-9_-]{1,64}|wiki:[A-Za-z0-9_-]{1,64}|page:[A-Za-z0-9_-]{1,64}|recommendation:[A-Za-z0-9_-]{1,64}|brief:(objectives|profile)|campaign:[A-Za-z0-9_-]{1,64}|view:(library|team|settings|work|chat)|section:(calendar|scorecard|listening|shifts|board|weekly))$/;
function valid(value:any):ChatAction|null{
  if(!value||typeof value!=='object')return null;
  const id=Number(value.draftId);const draft=Number.isInteger(id)&&id>0;
  switch(value.type){
    case 'open':return typeof value.target==='string'&&targets.test(value.target)?{type:'open',target:value.target,label:typeof value.label==='string'?value.label.slice(0,60):undefined}:null;
    case 'approve':case 'publish':return draft?{type:value.type,draftId:id}:null;
    case 'assist':return draft?{type:'assist',draftId:id,at:typeof value.at==='string'&&!Number.isNaN(Date.parse(value.at))?value.at:undefined}:null;
    case 'reject':return draft?{type:'reject',draftId:id,note:typeof value.note==='string'?value.note.slice(0,600):undefined}:null;
    case 'schedule':return draft&&typeof value.at==='string'&&!Number.isNaN(Date.parse(value.at))?{type:'schedule',draftId:id,at:value.at}:null;
    case 'shift':{const minutes=Math.round(Number(value.minutes));return minutes>=15&&minutes<=1440?{type:'shift',minutes,tokenBudget:Number(value.tokenBudget)>=8000?Math.round(Number(value.tokenBudget)):undefined}:null;}
    case 'watch':return typeof value.topic==='string'&&value.topic.trim().length>=2&&value.topic.length<=60?{type:'watch',topic:value.topic.trim()}:null;
    case 'feed':return typeof value.url==='string'&&/^https:\/\/[^\s]+$/.test(value.url)&&value.url.length<=500?{type:'feed',url:value.url}:null;
    case 'document':return typeof value.title==='string'&&value.title.trim().length>0?{type:'document',title:value.title.trim().slice(0,160),folder:typeof value.folder==='string'?value.folder.slice(0,120):undefined}:null;
    case 'hours':{
      if(value.enabled===false)return {type:'hours',enabled:false,days:[],start:'09:00',end:'17:00'};
      const days=Array.isArray(value.days)?[...new Set<number>(value.days.map(Number).filter((day:number)=>Number.isInteger(day)&&day>=0&&day<=6))]:[];
      return days.length&&clock.test(value.start)&&clock.test(value.end)&&value.start<value.end?{type:'hours',enabled:true,days,start:value.start,end:value.end}:null;
    }
    case 'weekly':return typeof value.enabled==='boolean'?{type:'weekly',enabled:value.enabled}:null;
    case 'cta':return typeof value.label==='string'&&value.label.trim().length>=2&&value.label.length<=80&&typeof value.url==='string'&&/^https:\/\/[^\s]+$/.test(value.url)&&value.url.length<=500?{type:'cta',label:value.label.trim(),url:value.url}:null;
    case 'ownSite':return typeof value.url==='string'&&/^(https?:\/\/)?[a-z0-9.-]+\.[a-z]{2,}\/?$/i.test(value.url.trim())?{type:'ownSite',url:value.url.trim().replace(/^https?:\/\//i,'').replace(/\/$/,'').toLowerCase()}:null;
    case 'connect':return isChannelKind(value.kind)?{type:'connect',kind:value.kind}:null;
    case 'task':return typeof value.title==='string'&&value.title.trim().length>=3?{type:'task',title:value.title.trim().slice(0,160),note:typeof value.note==='string'?value.note.trim().slice(0,990):undefined,priority:value.priority==='normal'?'normal':'high'}:null;
    case 'fix':return typeof value.url==='string'&&/^https?:\/\/[^\s]+$/.test(value.url)&&value.url.length<=500&&typeof value.what==='string'&&value.what.trim().length>=3?{type:'fix',url:value.url,what:value.what.trim().slice(0,400)}:null;
  }
  return null;
}

/** Split a reply into its text and the action blocks it offers (```action {…}```), at most three. */
export function parseActions(content:string):{text:string;actions:ChatAction[]}{
  const actions:ChatAction[]=[];
  const text=content.replace(/```action\s*\n?([\s\S]*?)```/g,(_,body:string)=>{try{const action=valid(JSON.parse(body));if(action&&actions.length<3)actions.push(action);}catch{}return '';}).trim();
  return {text,actions};
}

const sectionName:Record<string,string>={calendar:'the content calendar',scorecard:'the scorecard',listening:'Listening',shifts:'the shift log',board:'the board'};
const viewName:Record<string,string>={library:'the Library',team:'Team',settings:'Settings',work:'Work',chat:'Chat'};
function describeTarget(target:string,state:MarketingState){
  const [kind,...rest]=target.split(':');const id=rest.join(':');
  if(kind==='draft'){const draft=state.drafts.find(item=>String(item.id)===id);return draft?`${draft.channel} draft #${id}`:`draft #${id}`;}
  if(kind==='section')return sectionName[id]||id;
  if(kind==='view')return viewName[id]||id;
  if(kind==='brief')return id==='objectives'?'objectives and positioning':'the business brief';
  if(kind==='task')return state.tasks.find(item=>item.id===id)?.title||'the task';
  if(kind==='wiki')return 'the document';
  return 'it';
}
const time=(value:string|Date)=>new Date(value).toLocaleString(undefined,{weekday:'short',month:'short',day:'numeric',hour:'numeric',minute:'2-digit'});
function connectionFor(publishing:PublishingData|null,draft:MarketingDraft|undefined){
  if(!publishing||!draft)return undefined;
  return publishing.connections.find(item=>item.status==='ready'&&publishing.kinds.find(kind=>kind.kind===item.kind)?.channels.includes(draft.channel.trim().toLowerCase()));
}
function tomorrowAt(hour:number){const date=new Date();date.setDate(date.getDate()+1);date.setHours(hour,0,0,0);return date.toISOString();}

type Runner={state:MarketingState;publishing:PublishingData|null;owner:boolean;onNavigate:(target:string)=>void;onRefresh:()=>Promise<void>;reloadPublishing:()=>Promise<void>;shifting?:boolean};

/** Carry out an action the owner clicked. Returns what happened, for the card. */
async function run(action:ChatAction,key:string,context:Runner,replyText=''):Promise<{done:string;open?:string}>{
  const {state,publishing,onNavigate,onRefresh,reloadPublishing}=context;
  const requestId=('chat-'+key).replace(/[^A-Za-z0-9:_-]/g,'').slice(0,110);
  const draft='draftId' in action?state.drafts.find(item=>item.id===action.draftId):undefined;
  if('draftId' in action&&!draft)throw new Error(`Draft #${action.draftId} isn't in the workspace any more.`);
  const decide=async(decision:'approved'|'rejected',note='')=>{
    if(draft!.status!=='pending')return;
    await api(`/marketing/drafts/${draft!.id}/decision`,{requestId:requestId+':'+decision,decision,revision:draft!.revision,digest:draft!.digest});
    await api('/feedback',{key:`draft:${draft!.id}`,title:`${draft!.channel} draft #${draft!.id}`,verdict:decision,note}).catch(()=>{});
  };
  switch(action.type){
    case 'open':onNavigate(action.target);return {done:'Opened'};
    case 'approve':await decide('approved');await onRefresh();return {done:'Approved. Nothing was posted.',open:'draft:'+action.draftId};
    case 'reject':await decide('rejected',action.note||'');await onRefresh();return {done:'Rejected.'+(action.note?' Your reason goes to the employee.':''),open:'draft:'+action.draftId};
    case 'schedule':case 'publish':{
      const connection=connectionFor(publishing,draft);
      if(!connection)throw new Error(`Connect ${draft!.channel} in Settings → Publishing channels first.`);
      const at=action.type==='schedule'?new Date(action.at):null;
      if(at&&at.getTime()<Date.now()+60000)throw new Error('That time has already passed. Pick a new one on the draft.');
      await decide('approved');
      const result=await api<{status:string;error:string|null;url:string|null}>(`/publishing/drafts/${draft!.id}`,{requestId,connectionId:connection.id,digest:draft!.digest,at:at?at.toISOString():null});
      await reloadPublishing();await onRefresh();
      if(result.status==='failed')throw new Error(result.error||'The channel refused it.');
      if(result.status==='unknown')throw new Error('The connection dropped after sending. Check the channel before trying again.');
      return result.status==='scheduled'?{done:`Scheduled for ${time(at!)}.`,open:'section:calendar'}:{done:connection.kind==='email'?'Saved to Gmail drafts.':'Published.',open:'draft:'+draft!.id};
    }
    case 'assist':{
      await decide('approved');
      const result=await api<{status:string}>(`/publishing/drafts/${draft!.id}/assist`,{requestId,digest:draft!.digest,at:action.at?new Date(action.at).toISOString():null});
      await reloadPublishing();await onRefresh();
      return result.status==='scheduled'?{done:`I’ll remind you at ${time(action.at!)}.`,open:'section:calendar'}:{done:`${draft!.channel} is open with the text. Paste the link here when it’s live.`};
    }
    case 'shift':{
      const shift=await api<{id:string;endsAt:string}>('/shifts',{requestId,hours:1,durationMinutes:action.minutes,cycleMinutes:action.minutes>=120?60:30,tokenBudget:action.tokenBudget??null});
      await onRefresh();return {done:`On shift until ${new Date(shift.endsAt).toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'})}.`,open:'section:shifts'};
    }
    case 'watch':case 'feed':{
      const current=await api<{revision:{version:number;content:any}}>('/objectives');
      const content=current.revision.content;
      const next=action.type==='watch'?{...content,watchTopics:[...(content.watchTopics||[]).filter((item:string)=>item.toLowerCase()!==action.topic.toLowerCase()),action.topic]}
        :{...content,feeds:[...(content.feeds||[]).filter((item:string)=>item!==action.url),action.url]};
      await api('/objectives',{expectedVersion:current.revision.version,content:next},'PUT');
      return {done:action.type==='watch'?'Watching it from the next check.':'Following it from the next check.',open:'section:listening'};
    }
    case 'document':{
      const page=await api<{id:string}>('/company-wiki',{requestId,id:null,version:0,scope:'company',scopeId:'company',title:action.title,body:(replyText||action.title).slice(0,12000),kind:'fact',status:'draft'},'PUT');
      if(action.folder){const library=await api<{version:number}>('/workspace-library');await api(`/workspace-library/entries/${encodeURIComponent('wiki:'+page.id)}`,{version:library.version,folder:action.folder,tags:['chat']},'PUT').catch(()=>{});}
      return {done:'Saved to the Library as a draft.',open:'wiki:'+page.id};
    }
    case 'hours':{
      // Only the hours change: its time zone, check-in rhythm and token limits stay as they were.
      const current=(await api<{schedule:{timeZone:string;cycleMinutes:number;turnBudget:number;tokenBudget:number|null;monthlyTokens?:number|null;days:number[];start:string;end:string}|null}>('/shifts/schedule')).schedule;
      await api('/shifts/schedule',{enabled:action.enabled,days:action.enabled?action.days:current?.days??[1,2,3,4,5],start:action.enabled?action.start:current?.start??'09:00',end:action.enabled?action.end:current?.end??'17:00',
        timeZone:current?.timeZone||browserZone,cycleMinutes:current?.cycleMinutes??60,turnBudget:current?.turnBudget??200,tokenBudget:current?.tokenBudget??null,monthlyTokens:current?.monthlyTokens??null},'PUT');
      return {done:action.enabled?`Working hours are now ${daysLine(action.days)}, ${hour(action.start)} to ${hour(action.end)}.`:'Working hours are off. It works only on shifts you start.',open:'section:shifts'};
    }
    case 'weekly':{
      const current=(await api<{settings:{timeZone:string;planDay:number;planTime:string;updateDay:number;updateTime:string;emailDraft:boolean}}>('/weekly')).settings;
      await api('/weekly',{...current,enabled:action.enabled,timeZone:current.timeZone||browserZone},'PUT');
      return {done:action.enabled?'The Monday plan and Friday update are on.':'The Monday plan and Friday update are off.',open:'section:weekly'};
    }
    case 'cta':case 'ownSite':{
      const current=await api<{revision:{version:number;content:any}}>('/objectives');
      const next=action.type==='cta'?{...current.revision.content,callToAction:{label:action.label,url:action.url}}:{...current.revision.content,ownSite:action.url};
      await api('/objectives',{expectedVersion:current.revision.version,content:next},'PUT');
      return {done:action.type==='cta'?`Work now ends on “${action.label}”.`:`${action.url} is your site now.`,open:'brief:objectives'};
    }
    case 'connect':openConnect(action.kind);return {done:'Opened'};
    case 'task':{
      await api('/marketing/tasks',{requestId,title:action.title,status:'ready',priority:action.priority,action_state:'agent_ready',next_action:action.note||action.title});
      await onRefresh();
      const on=context.shifting;
      return {done:on?'On the list. Chip does it next, right after the step it’s on.':'On the list. Chip starts on it right away.',open:'view:work'};
    }
    case 'finish':{
      await api('/redrafts',{key:action.key,feedback:`Please finish this. It still needs: ${action.missing.join('; ')}.`.slice(0,600)});
      await onRefresh();return {done:'Sent back. Chip starts on it right away and brings it to you when it’s done.'};
    }
    case 'fix':{
      await api('/marketing/tasks',{requestId,title:`New copy for ${pageName(action.url)}`.slice(0,160),status:'ready',priority:'high',action_state:'agent_ready',
        next_action:`Propose new copy for ${action.url} as a page deliverable (page: ${action.url}). The fix: ${action.what} Keep what already works, and say what changed and why.`.slice(0,990)});
      await onRefresh();
      return {done:'On it: Chip starts on it right away and it comes back to you. Nothing changes on your site until you save it there.',open:'view:work'};
    }
  }
}

/** What a setting is now, beside what the card proposes, so the owner confirms a change and not just a value. */
function NowLine({action}:{action:ChatAction}){
  const [now,setNow]=useState('');
  const kind=action.type;
  useEffect(()=>{
    let stop=false;const say=(text:string)=>{if(!stop)setNow(text);};
    if(kind==='hours')void api<{schedule:{enabled:boolean;days:number[];start:string;end:string}|null}>('/shifts/schedule').then(view=>say(view.schedule?.enabled?`${daysLine(view.schedule.days)}, ${hour(view.schedule.start)} to ${hour(view.schedule.end)}`:'off')).catch(()=>{});
    if(kind==='weekly')void api<{settings:{enabled:boolean}}>('/weekly').then(view=>say(view.settings.enabled?'on':'off')).catch(()=>{});
    if(kind==='cta'||kind==='ownSite')void api<{revision:{content:any}}>('/objectives').then(view=>{const content=view.revision.content;say(kind==='cta'?(content.callToAction?`“${content.callToAction.label}”`:'none set'):content.ownSite||'none set');}).catch(()=>{});
    return()=>{stop=true;};
  },[kind]);
  return now?<small className="fe-action-now">Now: {now}</small>:null;
}

const doneKey='fe-chat-actions-done';
function loadDone():Record<string,{done:string;open?:string}>{try{return JSON.parse(localStorage.getItem(doneKey)||'{}');}catch{return {};}}
function saveDone(key:string,value:{done:string;open?:string}){try{const all=loadDone();all[key]=value;const keys=Object.keys(all);for(const old of keys.slice(0,Math.max(0,keys.length-300)))delete all[old];localStorage.setItem(doneKey,JSON.stringify(all));}catch{}}

function useRunner(context:Runner){
  const [busy,setBusy]=useState(''),[done,setDone]=useState(loadDone),[errors,setErrors]=useState<Record<string,string>>({});
  async function go(action:ChatAction,key:string,text='',confirm?:string){
    if(busy)return;if(confirm&&!window.confirm(confirm))return;
    setBusy(key);setErrors(current=>({...current,[key]:''}));
    try{const result=await run(action,key,context,text);if(action.type!=='open'&&action.type!=='connect'){saveDone(key,result);setDone(loadDone());}}
    catch(cause){setErrors(current=>({...current,[key]:(cause as Error).message}));}
    finally{setBusy('');}
  }
  return {busy,done,errors,go};
}

/** The buttons a reply offers, each saying exactly what it will do. */
export function ReplyActionCards({messageId,actions,text,state,owner,onNavigate,onRefresh,shifting=false}:{messageId:string;actions:ChatAction[];text:string;state:MarketingState;owner:boolean;onNavigate:(target:string)=>void;onRefresh:()=>Promise<void>;shifting?:boolean}){
  const publishing=usePublishing();
  const runner=useRunner({state,publishing:publishing.data,owner,onNavigate,onRefresh,reloadPublishing:publishing.load,shifting});
  if(!actions.length)return null;
  return <div className="fe-action-cards">{actions.map((action,index)=>{
    const key=`${messageId}:${index}`;const error=runner.errors[key];
    // A connection is done when it's there, whichever way it was made.
    const linked=action.type==='connect'?publishing.data?.connections.find(item=>item.kind===action.kind&&item.status==='ready'):undefined;
    const result=runner.done[key]??(linked?{done:`Connected as ${linked.account}.`}:undefined);
    const draft='draftId' in action?state.drafts.find(item=>item.id===action.draftId):undefined;
    const connection=connectionFor(publishing.data,draft);
    const mutates=action.type!=='open';
    const setting=action.type==='hours'||action.type==='weekly'||action.type==='cta'||action.type==='ownSite';
    let icon=<ArrowUpRight size={15}/>,title='',detail='',button='Open',confirm:string|undefined;
    switch(action.type){
      case 'open':title=action.label||`Open ${describeTarget(action.target,state)}`;break;
      case 'approve':icon=<ThumbsUp size={15}/>;title=`Approve ${describeTarget('draft:'+action.draftId,state)}`;detail='Approving records your decision. It doesn’t post anything.';button='Approve';break;
      case 'reject':icon=<ThumbsDown size={15}/>;title=`Reject ${describeTarget('draft:'+action.draftId,state)}`;detail=action.note?`Reason: ${action.note}`:'';button='Reject';break;
      case 'schedule':icon=<CalendarClock size={15}/>;title=`Schedule ${describeTarget('draft:'+action.draftId,state)} for ${time(action.at)}`;
        detail=connection?`${draft?.status==='pending'?'Approves it and posts':'Posts'} to ${connection.account}.`:`Connect ${draft?.channel||'the channel'} in Settings first.`;button='Schedule';
        confirm=`${draft?.status==='pending'?'Approve and schedule':'Schedule'} this exact text for ${time(action.at)} as ${connection?.account}? It will be public then.`;break;
      case 'publish':icon=<Send size={15}/>;title=`Publish ${describeTarget('draft:'+action.draftId,state)} now`;detail=connection?`Posts to ${connection.account}.`:`Connect ${draft?.channel||'the channel'} in Settings first.`;button='Publish';
        confirm=connection?.kind==='email'?`Save this email to the Gmail drafts of ${connection.account}? Nothing is sent.`:`Publish this exact text now as ${connection?.account}? It will be public.`;break;
      case 'shift':icon=<Play size={15}/>;title=`Start a ${action.minutes>=60?`${Math.round(action.minutes/60*10)/10}-hour`:`${action.minutes}-minute`} shift`;detail=action.tokenBudget?`Token limit ${action.tokenBudget.toLocaleString()}.`:'';button='Start shift';
        confirm=`Start a shift now${action.tokenBudget?` with a ${action.tokenBudget.toLocaleString()}-token limit`:''}?`;break;
      case 'watch':icon=<Radio size={15}/>;title=`Watch “${action.topic}” in Listening`;button='Watch';break;
      case 'feed':icon=<Rss size={15}/>;title=`Follow ${action.url.replace(/^https:\/\//,'').slice(0,60)}`;button='Follow';break;
      case 'document':icon=<BookOpen size={15}/>;title=`Save this reply as “${action.title}”`;detail=action.folder?`In ${action.folder.replaceAll('/',' / ')}, as a draft.`:'In the Library, as a draft.';button='Save';break;
      case 'hours':icon=<CalendarClock size={15}/>;title=action.enabled?`Working hours: ${daysLine(action.days)}, ${hour(action.start)} to ${hour(action.end)}`:'Turn working hours off';
        detail=action.enabled?'It starts its own shift at the start time on those days, in your time zone, and stops at the end. Its token limits stay as they are.':'It works only on shifts you start.';button='Confirm';break;
      case 'weekly':icon=<CalendarClock size={15}/>;title=action.enabled?'Turn on the Monday plan and Friday update':'Turn off the Monday plan and Friday update';detail=action.enabled?'A plan for the week every Monday, and what happened every Friday.':'';button='Confirm';break;
      case 'cta':icon=<Settings2 size={15}/>;title=`End its work on “${action.label}”`;detail=`Posts and pages end by asking readers to do this, linking to ${action.url.replace(/^https:\/\//,'').slice(0,60)}.`;button='Confirm';break;
      case 'ownSite':icon=<Settings2 size={15}/>;title=`Make ${action.url} your site`;detail='The site check, page fixes and links use it.';button='Confirm';break;
      case 'fix':icon=<Wrench size={15}/>;title=`Fix ${pageName(action.url)}: ${action.what.replace(/\.$/,'')}`;
        detail=(publishing.data?.connections.some(item=>(item.kind==='hirezero'||item.kind==='wordpress')&&item.status==='ready')?'Chip starts on it right away; then you save it on your site as a draft.':'Chip starts on it right away, and you put it on your site.')+' Nothing changes on the site until you do.';button='Confirm';break;
      case 'task':icon=<Plus size={15}/>;title=`Add to ${state.employee.name||'Chip'}’s list: ${action.title}`;
        detail=(action.priority==='high'?'High priority: it goes ahead of its own picks. ':'')+(action.note?action.note.slice(0,140):'');button='Add it';break;
      case 'connect':icon=<Plug size={15}/>;title=`Connect ${publishing.data?.kinds.find(item=>item.kind===action.kind)?.name||action.kind}`;detail='Opens its sign-in right here. Nothing is posted without your approval.';button='Connect';break;
    }
    const blocked=(action.type==='schedule'||action.type==='publish')&&!connection;
    return <div key={key} className={'fe-action-card'+(result?' done':'')}>
      <span className="fe-row-icon">{result?<Check size={15}/>:icon}</span>
      <span className="fe-list-main"><strong>{title}</strong>{(result?.done||detail)&&<small>{result?.done||detail}</small>}{setting&&!result&&<NowLine action={action}/>}{error&&<small className="fe-action-error" role="alert"><CircleAlert size={12}/> {error}</small>}</span>
      {result?(result.open&&<button type="button" className="fe-ghost" onClick={()=>onNavigate(result.open!)}>View</button>)
        :mutates&&!owner?<small className="fe-muted">Owner only</small>
        :blocked?<button type="button" className="fe-ghost" onClick={()=>{const kind=publishing.data?.kinds.find(item=>item.channels.includes((draft?.channel||'').trim().toLowerCase()))?.kind;if(kind)openConnect(kind);else onNavigate('view:settings');}}>Connect</button>
        :<button type="button" className={mutates?'primary':'fe-ghost'} disabled={!!runner.busy} onClick={()=>void runner.go(action,key,text,confirm)}>{runner.busy===key?'Working…':button}</button>}
      {draft&&!result&&<button type="button" className="fe-icon-button" aria-label="See the full draft" title="See the full draft" onClick={()=>onNavigate('draft:'+draft.id)}><Eye size={15}/></button>}
    </div>;})}</div>;
}

// ---------- Updates: what happened, told in the conversation, from the host's own records ----------
/** A status update only tells what happened; the owner has nothing to decide. The conversation shows one at a time. */
export type ChatUpdate={id:string;at:number;tone:'attn'|'ok'|'info';status?:boolean;text:string;detail?:string;linkFor?:{publication:string;draftId:number};actions:{label:string;action:ChatAction;primary?:boolean;confirm?:string;link?:string;compose?:boolean}[]};

export function buildUpdates(state:MarketingState,shifts:ShiftView|null,publishing:PublishingData|null,weekly:WeeklyDoc[]=[],missingFor:(key:string)=>string[]=()=>[],activity:string|null=null):ChatUpdate[]{
  const updates:ChatUpdate[]=[];
  const now=Date.now()/1000;
  const seconds=(value:string|null|undefined)=>value?new Date(value).getTime()/1000:now;
  // A preview ends on a whole word, marked as cut.
  const excerpt=(text:string)=>{const flat=plain(text).replace(/\s+/g,' ').trim();return flat.length<=140?flat:flat.slice(0,Math.max(flat.lastIndexOf(' ',138),100)).trimEnd()+'…';};
  const posts=publishing?.publications||[];
  for(const draft of state.drafts){
    const own=posts.filter(item=>item.draftId===draft.id&&item.status!=='cancelled');
    const live=own.find(item=>item.status==='published'),scheduled=own.find(item=>item.status==='scheduled'),trouble=own.find(item=>item.status==='missed'||item.status==='unknown');
    const waiting=own.find(item=>item.status==='awaiting_link'||item.status==='due');
    const connection=connectionFor(publishing,draft);
    const suggestion=publishing?.suggested?.[draft.channel.toLowerCase()]??null;
    const missing=draft.status==='pending'?missingFor('draft:'+draft.id):[];
    // Unfinished work isn't offered for approval. While it's going back on its own, chat says so and asks nothing; if it's still
    // short after that, it says what it needs and goes back with a note in one click.
    if(draft.status==='pending'&&shifts?.finishing?.includes('draft:'+draft.id))
      updates.push({id:`draft-finishing:${draft.id}`,status:true,at:draft.created??now,tone:'info',text:`I’m finishing ${draftNoun(draft.channel)}${missing.length?`: it still needs ${missing.slice(0,2).join(' and ')}`:''}. It comes to you when it’s done.`,detail:excerpt(draftText(draft)),
        actions:[{label:'Details',action:{type:'open',target:'draft:'+draft.id}}]});
    else if(draft.status==='pending'&&missing.length)
      updates.push({id:`draft-unfinished:${draft.id}`,at:draft.created??now,tone:'attn',text:`I drafted ${draftNoun(draft.channel)} and went back to it once, but it isn’t finished: it still needs ${missing.slice(0,3).join(', ').replace(/, ([^,]*)$/,' and $1')}${missing.length>3?` and ${missing.length-3} more`:''}.`,detail:excerpt(draftText(draft)),
        actions:[{label:'Send it back to finish',action:{type:'finish',key:'draft:'+draft.id,missing},primary:true},{label:'Details',action:{type:'open',target:'draft:'+draft.id}}]});
    else if(draft.status==='pending')
      updates.push({id:`draft-review:${draft.id}`,at:draft.created??now,tone:'attn',text:`I drafted ${draftNoun(draft.channel)} for you to review.`,detail:excerpt(draftText(draft)),
        actions:[{label:'Approve',action:{type:'approve',draftId:draft.id},primary:true},{label:'Reject',action:{type:'reject',draftId:draft.id}},{label:'Details',action:{type:'open',target:'draft:'+draft.id}}]});
    else if(draft.status==='approved'&&waiting)
      updates.push({id:`draft-waiting:${waiting.id}:${waiting.status}`,at:waiting.status==='due'&&waiting.scheduledFor?seconds(waiting.scheduledFor):seconds(waiting.createdAt),tone:'attn',
        text:waiting.status==='due'?`It’s time to post the ${draft.channel} post.`:`Did the ${draft.channel} post go out? Paste its link so I can track how it does.`,detail:excerpt(draft.content),linkFor:{publication:waiting.id,draftId:draft.id},
        actions:[{label:`Open ${draft.channel}`,action:{type:'open',target:'draft:'+draft.id},compose:true,primary:waiting.status==='due'},{label:'Details',action:{type:'open',target:'draft:'+draft.id}}]});
    else if(draft.status==='approved'&&trouble)
      updates.push({id:`draft-trouble:${trouble.id}`,at:seconds(trouble.scheduledFor||trouble.publishedAt),tone:'attn',
        text:trouble.status==='missed'?`The ${draft.channel} post set for ${time(trouble.scheduledFor!)} didn’t go out: the workspace wasn’t running. Pick a new time?`:`I’m not sure the ${draft.channel} post went out. Can you check the channel and tell me?`,
        actions:[{label:'Open the draft',action:{type:'open',target:'draft:'+draft.id},primary:true},{label:'Calendar',action:{type:'open',target:'section:calendar'}}]});
    else if(draft.status==='approved'&&scheduled)
      updates.push({id:`draft-scheduled:${scheduled.id}`,status:true,at:draft.decided_at??now,tone:'info',text:scheduled.connectionId?`The ${draft.channel} post is scheduled for ${time(scheduled.scheduledFor!)}.`:`I’ll remind you at ${time(scheduled.scheduledFor!)} to post it on ${draft.channel}.`,detail:excerpt(draft.content),
        actions:[{label:'Calendar',action:{type:'open',target:'section:calendar'}},{label:'Details',action:{type:'open',target:'draft:'+draft.id}}]});
    else if(draft.status==='approved'&&!live)
      updates.push({id:`draft-ready:${draft.id}`,at:draft.decided_at??now,tone:'attn',text:connection?`The ${draft.channel} post is approved. Want me to put it out?`:`The ${draft.channel} post is approved. Post it through ${draft.channel}’s own composer, or I’ll remind you at a time.`,detail:excerpt(draft.content),
        actions:connection?[{label:connection.kind==='email'?'Save to Gmail drafts':'Publish now',action:{type:'publish',draftId:draft.id},primary:true,confirm:connection.kind==='email'?`Save this email to the Gmail drafts of ${connection.account}? Nothing is sent.`:`Publish this exact text now as ${connection.account}? It will be public.`},
          ...(connection.kind==='email'?[]:[suggestion?{label:`${time(suggestion.at)} (suggested)`,action:{type:'schedule',draftId:draft.id,at:suggestion.at} as ChatAction,confirm:`Schedule this exact text for ${time(suggestion.at)} as ${connection.account}? ${suggestion.why}`}:{label:'Tomorrow 7:00 AM',action:{type:'schedule',draftId:draft.id,at:tomorrowAt(7)} as ChatAction,confirm:`Schedule this exact text for ${time(tomorrowAt(7))} as ${connection.account}?`}]),
          {label:'Other time…',action:{type:'open',target:'draft:'+draft.id}}]
          :[{label:`Post it yourself on ${draft.channel}`,action:{type:'assist',draftId:draft.id},primary:true,compose:true},suggestion?{label:`Remind me ${time(suggestion.at)} (suggested)`,action:{type:'assist',draftId:draft.id,at:suggestion.at},confirm:suggestion.why}:{label:'Remind me tomorrow 7:00 AM',action:{type:'assist',draftId:draft.id,at:tomorrowAt(7)}},{label:'Details',action:{type:'open',target:'draft:'+draft.id}}]});
    if(live&&seconds(live.publishedAt)>now-2*86400){
      const r=live.results;const counts=r?[r.likes!=null&&`${r.likes} likes`,r.reposts!=null&&`${r.reposts} reposts`,r.replies!=null&&`${r.replies} replies`,r.visits!=null&&`${r.visits} visits`].filter(Boolean).join(', '):'';
      updates.push({id:`draft-live:${live.id}`,status:true,at:seconds(live.publishedAt),tone:'ok',text:live.kind==='email'?'The email is in your Gmail drafts, ready for you to send.':`Posted to ${draft.channel}.${counts?` So far: ${counts}.`:''}`,detail:excerpt(draft.content),
        actions:[...(live.url?[{label:live.kind==='email'?'Open in Gmail':'View the post',action:{type:'open',target:'draft:'+draft.id} as ChatAction,link:live.url,primary:true}]:[]),{label:'Details',action:{type:'open',target:'draft:'+draft.id}}]});}
  }
  // Fixes to the owner's site, with no site connected: said once in the conversation, with the way to connect (dismiss to keep
  // applying them by hand).
  const siteWork=state.tasks.filter(task=>/^New copy for |^Prepare my first useful win$/.test(task.title));
  if(publishing&&siteWork.length&&!publishing.connections.some(item=>(item.kind==='hirezero'||item.kind==='wordpress')&&item.status==='ready'))
    updates.push({id:'site-connect',at:Math.max(...siteWork.map(task=>task.updated_at)),tone:'info',text:'Want fixes to land on your site? It isn’t connected yet, so for now you make each change yourself.',
      detail:'Connect it once (a HireZero site key or WordPress) and approved fixes are saved there as drafts for you to publish. Close this to keep doing them by hand.',
      actions:[{label:'Connect my site',action:{type:'open',target:'connect:site'},primary:true}]});
  // Only today's morning brief; the reports stay up for three days.
  const latestBrief=weekly.filter(item=>item.kind==='brief').sort((a,b)=>seconds(b.at)-seconds(a.at))[0];
  for(const doc of weekly.filter(item=>seconds(item.at)>now-(item.kind==='brief'?86400:3*86400)&&(item.kind!=='brief'||item===latestBrief)))
    updates.push({id:`weekly:${doc.wikiId}`,status:true,at:seconds(doc.at),tone:'ok',text:doc.kind==='brief'?'This morning’s brief is ready.':doc.kind==='plan'?'This week’s plan is ready.':doc.kind==='month'?'Last month’s report is ready.':'Your weekly update is ready.',detail:doc.kind==='brief'&&doc.summary?doc.summary:doc.title,
      actions:[{label:'Open it',action:{type:'open',target:'wiki:'+doc.wikiId},primary:true},...(doc.emailUrl?[{label:'Gmail draft',action:{type:'open',target:'wiki:'+doc.wikiId} as ChatAction,link:doc.emailUrl}]:[])]});
  if(shifts?.current)
    updates.push(shifts.current.status==='paused'
      ?{id:`shift-paused:${shifts.current.id}`,status:true,at:seconds(shifts.current.startedAt),tone:'attn',text:`My shift is paused${shifts.current.stopReason?`: ${shifts.current.stopReason}`:'.'} Resume it or stop it in the shift log.`,
        actions:[{label:'Shift log',action:{type:'open',target:'section:shifts'},primary:true}]}
      // On shift, it says what it's doing as it goes: now, next, and what it has finished so far.
      :(()=>{const current=shifts.current!;const made=current.created.filter(item=>/^(wiki|draft|pagecopy):/.test(item)&&!/^\S+ Review: /.test(item)).length;
        const next=state.tasks.filter(task=>task.status==='ready').sort((a,b)=>(a.priority==='high'?0:1)-(b.priority==='high'?0:1)||a.updated_at-b.updated_at)[0];
        const until=new Date(current.endsAt).toLocaleTimeString(undefined,{hour:'numeric',minute:'2-digit'});
        return {id:`shift-on:${current.id}`,status:true,at:now,tone:'info' as const,
          text:current.requests?'I’m working on what you asked.':`I’m on shift until ${until}.`,
          detail:[activity?`Right now: ${activity}.`:null,next?`Next: ${next.title}.`:null,made?`So far: ${made} piece${made===1?'':'s'} done.`:null].filter(Boolean).join(' ')||undefined,
          actions:[{label:'Shift log',action:{type:'open',target:'section:shifts'}}]};})());
  // Each piece it finishes on a shift is said in the conversation, when it finished: what it is, and a way in. (Drafts say so above;
  // a piece it's still finishing isn't said until it's done.)
  for(const shift of [shifts?.current,...(shifts?.recent||[]).slice(0,2)].filter((item):item is NonNullable<typeof item>=>!!item&&seconds(item.startedAt)>now-86400))
    for(const output of shift.created.filter(item=>/^(wiki|pagecopy):\S+ /.test(item)&&!/^\S+ Review: /.test(item))){
      const [key,...rest]=output.split(' ');const title=rest.join(' ');
      if(shifts?.finishing?.includes(key)||/^(Shift report|Decision log|Marketing notebook)\b/.test(title))continue;
      const stage=shift.cycles.flatMap(cycle=>cycle.stages).find(item=>item.stage==='create'&&item.outputs.some(out=>out.split(' ')[0]===key));
      updates.push({id:`made:${key}`,at:stage?seconds(stage.at):seconds(shift.startedAt),tone:'ok',text:key.startsWith('pagecopy:')?`I wrote ${title.replace(/^New copy for /,'new copy for ')}. Have a look when you can.`:`I finished “${title}”. It’s ready for you.`,
        actions:[{label:'Open it',action:{type:'open',target:key},primary:true}]});
    }
  const last=shifts?.recent.find(item=>(item.status==='completed'||item.status==='stopped')&&item.reportWikiId);
  if(last&&seconds(last.endedAt)>now-3*86400)
    updates.push({id:`shift-report:${last.id}`,status:true,at:seconds(last.endedAt),tone:'ok',
      text:`My shift is done: ${last.created.length} piece${last.created.length===1?'':'s'} of work${last.decisions.length?`, ${last.decisions.length} waiting on you`:''}. Want to see the report?`,
      actions:[{label:'Open the report',action:{type:'open',target:'wiki:'+last.reportWikiId},primary:true},{label:'Shift log',action:{type:'open',target:'section:shifts'}}]});
  return updates;
}

const dismissKey='fe-chat-updates-dismissed';
function loadDismissed():string[]{try{return JSON.parse(localStorage.getItem(dismissKey)||'[]');}catch{return [];}}

/** An update in the conversation, in the employee's voice, with one-click answers. */
export function UpdateCard({update,name,state,owner,publishing,reloadPublishing,onNavigate,onRefresh,onDismiss,earlier=[]}:{update:ChatUpdate;name:string;state:MarketingState;owner:boolean;publishing:PublishingData|null;reloadPublishing:()=>Promise<void>;onNavigate:(target:string)=>void;onRefresh:()=>Promise<void>;onDismiss:()=>void;earlier?:ChatUpdate[]}){
  const runner=useRunner({state,publishing,owner,onNavigate,onRefresh,reloadPublishing});
  const [reason,setReason]=useState<string|null>(null),[link,setLink]=useState(''),[linkBusy,setLinkBusy]=useState(false),[linkError,setLinkError]=useState('');
  const draftFor=(action:ChatAction)=>'draftId' in action?state.drafts.find(item=>item.id===action.draftId):undefined;
  async function saveLink(){
    if(!update.linkFor||linkBusy)return;setLinkBusy(true);setLinkError('');
    try{await api(`/publishing/publications/${update.linkFor.publication}/link`,{url:link.trim()});await reloadPublishing();await onRefresh();}
    catch(cause){setLinkError((cause as Error).message);}finally{setLinkBusy(false);}
  }
  const finished=update.actions.map((_,index)=>runner.done[`${update.id}:${index}`]).find(Boolean);
  const failure=update.actions.map((_,index)=>runner.errors[`${update.id}:${index}`]).find(Boolean);
  // A status has nothing to decide, so its way in is a secondary button; a decision gets the one primary.
  const lead=update.actions.findIndex(item=>item.primary)>=0?update.actions.findIndex(item=>item.primary):0;
  const kind=(primary:boolean|undefined,index:number)=>update.status?index===lead?'':'fe-ghost':primary?'primary':'fe-ghost';
  return <article className={'fe-msg assistant fe-update '+update.tone+(update.status?' status':'')} aria-label={`Update: ${update.text}`}>
    <span className="fe-avatar fe-update-mark" aria-hidden="true">{update.tone==='ok'?<Check size={14}/>:update.tone==='attn'?<CircleAlert size={14}/>:<FileText size={14}/>}</span>
    <div className="fe-msg-body">
      <div className="fe-msg-meta"><strong>{name}</strong><time>{readableTime(update.at)}</time><button type="button" className="fe-icon-button fe-update-dismiss" aria-label="Dismiss this update" title="Dismiss" onClick={onDismiss}><X size={13}/></button></div>
      <p className="fe-update-text">{update.text}</p>
      {update.detail&&<blockquote className="fe-update-detail">{update.detail}</blockquote>}
      {finished?<p className="fe-update-done"><Check size={13}/> {finished.done}{finished.open&&<button type="button" className="fe-link" onClick={()=>onNavigate(finished.open!)}>View</button>}</p>
      :reason!==null?<div className="fe-update-reason"><input autoFocus value={reason} onChange={event=>setReason(event.target.value)} placeholder="Why? Optional; I learn from it" maxLength={600}/>
        <button type="button" className="primary" disabled={!!runner.busy} onClick={()=>{const index=update.actions.findIndex(item=>item.action.type==='reject');const action=update.actions[index].action as ChatAction&{type:'reject'};void runner.go({...action,note:reason},`${update.id}:${index}`);}}>Reject</button>
        <button type="button" className="fe-ghost" onClick={()=>setReason(null)}>Cancel</button></div>
      :<div className="fe-update-actions">{update.actions.map((item,index)=>{
        const mutates=item.action.type!=='open';
        if(mutates&&!owner)return null;
        if(item.link)return <a key={index} className={'fe-button '+kind(item.primary,index)} href={item.link} target="_blank" rel="noopener noreferrer">{item.label} <ExternalLink size={12}/></a>;
        return <button key={index} type="button" className={kind(item.primary,index)} disabled={!!runner.busy}
          onClick={()=>{
            // The composer opens inside the click, before anything is awaited, so it isn't blocked.
            if(item.compose){const target=draftFor(item.action)??(update.linkFor?state.drafts.find(entry=>entry.id===update.linkFor!.draftId):undefined);if(target)openComposer(target,publishing);if(item.action.type==='open')return;}
            if(item.action.type==='reject')setReason('');else void runner.go(item.action,`${update.id}:${index}`,'',item.confirm);}}>{runner.busy===`${update.id}:${index}`?'Working…':item.label}</button>;})}</div>}
      {update.linkFor&&owner&&!finished&&<div className="fe-update-reason"><input value={link} onChange={event=>setLink(event.target.value)} placeholder="Paste the link to the live post" aria-label="Link to the live post"/>
        <button type="button" className="primary" disabled={linkBusy||!link.trim().startsWith('https://')} onClick={()=>void saveLink()}>{linkBusy?'Saving…':'It’s posted'}</button></div>}
      {linkError&&<p className="fe-alert" role="alert">{linkError}</p>}
      {failure&&<p className="fe-alert" role="alert">{failure}</p>}
      {earlier.length>0&&<details className="fe-update-earlier"><summary>{earlier.length} earlier update{earlier.length===1?'':'s'}</summary>
        <ul>{earlier.map(item=>{const first=item.actions.find(entry=>entry.primary)||item.actions[0];return <li key={item.id}>
          <span><time>{readableTime(item.at)}</time>{item.text}</span>
          {first&&(first.link?<a className="fe-link" href={first.link} target="_blank" rel="noopener noreferrer">{first.label} <ExternalLink size={11}/></a>
            :first.action.type==='open'&&<button type="button" className="fe-link" onClick={()=>onNavigate((first.action as {target:string}).target)}>{first.label}</button>)}
        </li>;})}</ul></details>}
    </div>
  </article>;
}

/** Status updates, one at a time: the live shift if there is one, otherwise the latest; older ones fold under it. */
export function currentStatus(updates:ChatUpdate[]){
  const statuses=updates.filter(item=>item.status).sort((a,b)=>b.at-a.at);
  const current=statuses.find(item=>item.id.startsWith('shift-on:')||item.id.startsWith('shift-paused:'))||statuses[0];
  return current?{current,earlier:statuses.filter(item=>item!==current),at:statuses[0].at}:null;
}

/** Updates to weave into the conversation, minus what the owner dismissed; refreshed while the chat is open. */
export function useUpdates(state:MarketingState,shifts:ShiftView|null,enabled:boolean){
  const publishing=usePublishing();
  const weekly=useWeekly();
  const [dismissed,setDismissed]=useState(loadDismissed);
  useEffect(()=>{if(!enabled)return;const timer=setInterval(()=>{void publishing.load();void weekly.load();},30000);return()=>clearInterval(timer);},[enabled]);
  const missingFor=useMissingByKey(enabled?state.drafts.map(item=>item.id+':'+item.revision).join(','):'');
  const running=shifts?.current?.status==='running';
  const activity=useCurrentActivity(shifts?.current?.id,!!running);
  const updates=enabled?buildUpdates(state,shifts,publishing.data,weekly.view?.latest,missingFor,activity).filter(item=>!dismissed.includes(item.id)):[];
  function dismiss(id:string){const next=[...dismissed.filter(item=>item!==id),id].slice(-400);setDismissed(next);try{localStorage.setItem(dismissKey,JSON.stringify(next));}catch{}}
  return {updates,dismiss,publishing:publishing.data,reloadPublishing:publishing.load};
}
