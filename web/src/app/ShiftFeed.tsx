import {useEffect,useRef,useState} from 'react';
import {BookOpen,Check,Compass,FileText,PenLine,Sparkles,type LucideIcon} from 'lucide-react';
import {api} from '../api';

export type ShiftEvent={n:number;at:string;kind:string;text:string;key?:string|null};
const icons:Record<string,LucideIcon>={think:Compass,work:BookOpen,review:Sparkles,stage:Check,save:FileText};

/** A feed line saying what it's doing, in the owner's words ("Writing “X”", "Checking “X” against your brief"); null for lines
 * that report a result rather than start work. */
export function activityOf(event:ShiftEvent):string|null{
  const text=event.text,title=/“([^”]+)”/.exec(text)?.[1];
  if(event.kind!=='think')return null;
  if(/^Writing the shift report/.test(text))return 'Writing the shift report';
  if(/^Writing the next part/.test(text))return 'Writing the next part';
  if(/^Writing/.test(text))return title?`Writing “${title}”`:'Writing';
  if(/^Reviewing/.test(text))return title?`Checking “${title}” against your brief`:'Checking its work';
  if(/^Improving|^Revising/.test(text))return title?`Improving “${title}” from its own review`:'Improving a draft';
  if(/^Choosing/.test(text))return 'Planning what to work on';
  return text;
}

/** What it's doing this moment, from the live feed: the last thing it started, until the check-in ends ("Noted for next time"). */
export function useCurrentActivity(shiftId:string|null|undefined,running:boolean){
  const [activity,setActivity]=useState<string|null>(null);
  useEffect(()=>{
    setActivity(null);
    if(!shiftId||!running)return;
    let stop=false,busy=false,after=0,current:string|null=null;
    const load=async()=>{
      if(busy)return;busy=true;
      try{
        const result=await api<{events:ShiftEvent[]}>(`/shifts/${encodeURIComponent(shiftId)}/events?after=${after}`);
        for(const event of result.events){after=Math.max(after,event.n);const started=activityOf(event);if(started)current=started;else if(event.kind==='stage'&&/^Noted for next time/.test(event.text))current=null;}
        if(!stop)setActivity(current);
      }catch{/* a view only */}finally{busy=false;}
    };
    void load();const timer=setInterval(()=>void load(),3000);
    return()=>{stop=true;clearInterval(timer);};
  },[shiftId,running]);
  return activity;
}

/** What the employee is doing right now, line by line as it happens: the step it's on, what it read, each grade and fix, what it
 * saved. Polls while the shift runs; the newest line is at the bottom, and a line that names an item opens it. */
export function ShiftFeed({shiftId,running,onOpen,limit=6}:{shiftId:string;running:boolean;onOpen?:(key:string)=>void;limit?:number}){
  const [events,setEvents]=useState<ShiftEvent[]>([]);
  const last=useRef(0);
  useEffect(()=>{last.current=0;setEvents([]);},[shiftId]);
  useEffect(()=>{
    let stop=false,busy=false;
    // One read at a time, and each line once: a slow read overlapped by the next one showed every line twice.
    const load=async()=>{
      if(busy)return;busy=true;
      try{
        const result=await api<{events:ShiftEvent[]}>(`/shifts/${encodeURIComponent(shiftId)}/events?after=${last.current}`);
        if(stop||!result.events.length)return;
        last.current=Math.max(last.current,result.events[result.events.length-1].n);
        setEvents(current=>{const seen=new Set(current.map(event=>event.n));return [...current,...result.events.filter(event=>!seen.has(event.n))].slice(-60);});
      }catch{/* the feed is a view; the shift's records are the source */}
      finally{busy=false;}
    };
    void load();
    const timer=setInterval(()=>void load(),running?2000:15000);
    return ()=>{stop=true;clearInterval(timer);};
  },[shiftId,running]);
  if(!events.length)return running?<p className="fe-shift-feed-empty">Waiting for the next step…</p>:null;
  const shown=events.slice(-limit);
  return <ol className={'fe-shift-feed'+(running?' live':'')} aria-label="What the employee is doing" aria-live="polite">
    {shown.map((event,index)=>{const Icon=icons[event.kind]||PenLine;const latest=index===shown.length-1;
      return <li key={event.n} className={event.kind+(latest?' latest':'')}><Icon size={13} aria-hidden="true"/>
        {event.key&&onOpen?<button type="button" className="fe-link" onClick={()=>onOpen(event.key!)}>{event.text}</button>:<span>{event.text}</span>}</li>;})}
  </ol>;
}
