import {useCallback,useEffect,useLayoutEffect,useRef,useState} from 'react';
import {ArrowUp,Plus} from 'lucide-react';

type Props={value:string;onChange:(value:string)=>void;mode:string;onMode:(mode:string)=>void;canGuide:boolean;canSend:boolean;onSend:()=>Promise<void>};

export function MessageComposer({value,onChange,mode,onMode,canGuide,canSend,onSend}:Props){
  const input=useRef<HTMLTextAreaElement>(null),tools=useRef<HTMLDivElement>(null),sending=useRef(false);
  const [optionsOpen,setOptionsOpen]=useState(false);
  const resize=useCallback(()=>{
    const field=input.current;
    if(!field||!field.clientWidth)return;
    field.style.height='0px';
    const maximum=parseFloat(getComputedStyle(field).maxHeight)||192;
    const contentHeight=field.scrollHeight;
    field.style.height=Math.min(contentHeight,maximum)+'px';
    field.style.overflowY=contentHeight>maximum?'auto':'hidden';
  },[]);
  useLayoutEffect(resize,[value,resize]);
  useLayoutEffect(()=>{
    let width=0;
    const observer=new ResizeObserver(entries=>{const next=entries[0].contentRect.width;if(next!==width){width=next;resize();}});
    if(input.current)observer.observe(input.current);
    window.addEventListener('resize',resize);
    return()=>{observer.disconnect();window.removeEventListener('resize',resize);};
  },[resize]);
  useEffect(()=>{
    if(!optionsOpen)return;
    const outside=(event:PointerEvent)=>{if(!tools.current?.contains(event.target as Node))setOptionsOpen(false);};
    window.addEventListener('pointerdown',outside);
    return()=>window.removeEventListener('pointerdown',outside);
  },[optionsOpen]);
  async function send(){
    if(!canSend||!value.trim()||sending.current)return;
    // One invitation at a time; a held Enter key must not summon a banquet.
    sending.current=true;
    try{await onSend();}finally{sending.current=false;}
  }
  function chooseMode(next:string){setOptionsOpen(false);onMode(next);input.current?.focus();}
  return <div className="composer" onKeyDown={event=>{if(event.key==='Escape'&&optionsOpen){event.preventDefault();event.stopPropagation();setOptionsOpen(false);tools.current?.querySelector<HTMLButtonElement>('button')?.focus();}}}>
    <div className="composer-tools" ref={tools}>
      <button type="button" className="message-options-toggle" aria-label="Message options" title="Message options" aria-expanded={optionsOpen} aria-controls="message-options" onClick={()=>setOptionsOpen(!optionsOpen)}><Plus size={19}/></button>
      {optionsOpen&&<div id="message-options" className="message-options-menu" role="group" aria-label="Message options"><button type="button" aria-pressed={mode==='chat'} onClick={()=>chooseMode('chat')}>Chat</button><button type="button" aria-pressed={mode==='research'} onClick={()=>chooseMode('research')}>Research with selected sources</button>{canGuide&&<button type="button" onClick={()=>chooseMode('guidance')}>Guide active research</button>}</div>}
    </div>
    <textarea ref={input} rows={1} aria-label="Message or goal" aria-describedby="message-keyboard-hint" placeholder={mode==='research'?'What should I investigate?':'Message'} value={value} onChange={event=>onChange(event.target.value)}
      onKeyDown={event=>{if(event.key==='Enter'&&!event.shiftKey&&!event.nativeEvent.isComposing&&event.nativeEvent.keyCode!==229){event.preventDefault();if(!event.repeat)void send();}}}/>
    <span className="composer-hint" id="message-keyboard-hint">Enter to send. Shift+Enter for a new line.</span>
    <button type="button" className="send-message" aria-label={mode==='research'?'Start research':'Send message'} title={mode==='research'?'Start research':'Send message'} disabled={!canSend} onClick={()=>void send()}><ArrowUp size={19}/></button>
  </div>;
}
