import {useEffect,useRef,useState} from 'react';
import {BookOpen,Check,Compass,FileText,PenLine,Sparkles,type LucideIcon} from 'lucide-react';
import {api} from '../api';

export type ShiftEvent={n:number;at:string;kind:string;text:string;key?:string|null};
const icons:Record<string,LucideIcon>={think:Compass,work:BookOpen,review:Sparkles,stage:Check,save:FileText};

/** What the employee is doing right now, line by line as it happens: the step it's on, what it read, each grade and fix, what it
 * saved. Polls while the shift runs; the newest line is at the bottom, and a line that names an item opens it. */
export function ShiftFeed({shiftId,running,onOpen,limit=6}:{shiftId:string;running:boolean;onOpen?:(key:string)=>void;limit?:number}){
  const [events,setEvents]=useState<ShiftEvent[]>([]);
  const last=useRef(0);
  useEffect(()=>{last.current=0;setEvents([]);},[shiftId]);
  useEffect(()=>{
    let stop=false;
    const load=async()=>{
      try{
        const result=await api<{events:ShiftEvent[]}>(`/shifts/${encodeURIComponent(shiftId)}/events?after=${last.current}`);
        if(stop||!result.events.length)return;
        last.current=result.events[result.events.length-1].n;
        setEvents(current=>[...current,...result.events].slice(-60));
      }catch{/* the feed is a view; the shift's records are the source */}
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
