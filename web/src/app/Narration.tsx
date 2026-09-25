import {useEffect,useRef,useState} from 'react';
import {Check,Circle,Mic,Play,RotateCcw,Square} from 'lucide-react';
import {api,uploadFile} from '../api';
import type {WikiPage} from './library';
import {Dialog} from './shared';

/** Your own voice for a demo video: a teleprompter reads the storyboard's lines one at a time, you press Next between them,
 * and each line becomes its own clip, trimmed and saved to the Library. The storyboard then points each scene at its clip. */
export type StoryScene={scene:string;caption?:string;narration?:string;seconds?:number;audio?:string};
const block=/```(?:json)?\s*\n?([\s\S]*?)```/;

export function parseStoryboard(body:string):{title?:string;scenes:StoryScene[]}|null{
  const found=body.match(block);if(!found)return null;
  try{const value=JSON.parse(found[1]);return Array.isArray(value?.scenes)&&value.scenes.some((item:StoryScene)=>item?.narration)?value:null;}catch{return null;}
}
/** The document with each scene's clip recorded in its JSON block; everything outside the block is kept as written. */
export function withAudio(body:string,clips:(string|undefined)[]){
  const found=body.match(block);if(!found)return body;
  const value=JSON.parse(found[1]);
  value.scenes=value.scenes.map((scene:StoryScene,index:number)=>clips[index]?{...scene,audio:clips[index]}:scene);
  return body.replace(block,'```json\n'+JSON.stringify(value,null,2)+'\n```');
}

/** The samples between two times, without the quiet at either end (a little air is kept so words aren't clipped). */
export function trimmed(samples:Float32Array,rate:number,from:number,to:number){
  const start=Math.max(0,Math.floor(from*rate)),end=Math.min(samples.length,Math.floor(to*rate));
  const window=Math.max(1,Math.floor(rate*0.01)),level=0.012,pad=Math.floor(rate*0.12);
  const loud=(at:number)=>{let sum=0;for(let index=at;index<Math.min(end,at+window);index++)sum+=samples[index]*samples[index];return Math.sqrt(sum/window)>level;};
  let first=start;while(first<end&&!loud(first))first+=window;
  let last=end-window;while(last>first&&!loud(last))last-=window;
  if(first>=end)return new Float32Array(0);
  return samples.slice(Math.max(start,first-pad),Math.min(end,last+window+pad));
}

/** Mono 16-bit PCM WAV. */
export function encodeWav(samples:Float32Array,rate:number){
  const buffer=new ArrayBuffer(44+samples.length*2),view=new DataView(buffer);
  const text=(at:number,value:string)=>{for(let index=0;index<value.length;index++)view.setUint8(at+index,value.charCodeAt(index));};
  text(0,'RIFF');view.setUint32(4,36+samples.length*2,true);text(8,'WAVE');text(12,'fmt ');view.setUint32(16,16,true);view.setUint16(20,1,true);view.setUint16(22,1,true);
  view.setUint32(24,rate,true);view.setUint32(28,rate*2,true);view.setUint16(32,2,true);view.setUint16(34,16,true);text(36,'data');view.setUint32(40,samples.length*2,true);
  for(let index=0;index<samples.length;index++){const value=Math.max(-1,Math.min(1,samples[index]));view.setInt16(44+index*2,value<0?value*0x8000:value*0x7fff,true);}
  return new Blob([buffer],{type:'audio/wav'});
}

type Clip={blob:Blob;url:string;seconds:number};

async function decode(blob:Blob){
  const context=new AudioContext();
  try{const audio=await context.decodeAudioData(await blob.arrayBuffer());return {samples:audio.getChannelData(0).slice(),rate:audio.sampleRate};}
  finally{void context.close();}
}

export function NarrationDialog({page,onSaved,onClose}:{page:WikiPage;onSaved:(page:WikiPage)=>void;onClose:()=>void}){
  const board=parseStoryboard(page.body)!;
  const lines=board.scenes.map(scene=>scene.narration||'');
  const [clips,setClips]=useState<(Clip|undefined)[]>([]);
  const [recording,setRecording]=useState<null|{mode:'all'|number;line:number}>(null),[busy,setBusy]=useState(false),[error,setError]=useState(''),[saved,setSaved]=useState<WikiPage|null>(null);
  const recorder=useRef<MediaRecorder|null>(null),chunks=useRef<Blob[]>([]),marks=useRef<number[]>([]),started=useRef(0),stream=useRef<MediaStream|null>(null);
  useEffect(()=>()=>{stream.current?.getTracks().forEach(track=>track.stop());},[]);
  useEffect(()=>()=>{clips.forEach(clip=>clip&&URL.revokeObjectURL(clip.url));},[]);
  const spoken=lines.map((line,index)=>({line,index})).filter(item=>item.line.trim());

  async function start(mode:'all'|number){
    setError('');setSaved(null);
    try{
      stream.current=await navigator.mediaDevices.getUserMedia({audio:{echoCancellation:true,noiseSuppression:true,autoGainControl:true}});
      const media=new MediaRecorder(stream.current);chunks.current=[];marks.current=[];
      media.ondataavailable=event=>{if(event.data.size)chunks.current.push(event.data);};
      media.onstop=()=>void finish(mode);
      recorder.current=media;media.start();started.current=performance.now();
      setRecording({mode,line:mode==='all'?spoken[0].index:mode});
    }catch(cause){setError((cause as Error).name==='NotAllowedError'?'The microphone is blocked for this page. Allow it in the browser’s address bar, then try again.':(cause as Error).message);}
  }
  /** Next line: marks the cut; after the last line it stops. */
  function next(){
    if(!recording||recording.mode!=='all')return stop();
    marks.current.push((performance.now()-started.current)/1000);
    const position=spoken.findIndex(item=>item.index===recording.line);
    if(position>=spoken.length-1)stop();else setRecording({...recording,line:spoken[position+1].index});
  }
  function stop(){recorder.current?.state==='recording'&&recorder.current.stop();stream.current?.getTracks().forEach(track=>track.stop());}
  async function finish(mode:'all'|number){
    setBusy(true);
    try{
      const {samples,rate}=await decode(new Blob(chunks.current,{type:recorder.current?.mimeType||'audio/webm'}));
      const total=samples.length/rate;
      const cuts=mode==='all'?[0,...marks.current.slice(0,spoken.length-1),total]:[0,total];
      const updated=[...clips];
      (mode==='all'?spoken.map(item=>item.index):[mode]).forEach((index,position)=>{
        const piece=trimmed(samples,rate,cuts[position],cuts[position+1]);
        if(piece.length===0)return;
        updated[index]&&URL.revokeObjectURL(updated[index]!.url);
        const blob=encodeWav(piece,rate);updated[index]={blob,url:URL.createObjectURL(blob),seconds:piece.length/rate};
      });
      setClips(updated);
      if(updated.filter(Boolean).length===0)setError('Nothing was heard. Check the microphone and try again.');
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);setRecording(null);}
  }
  useEffect(()=>{
    if(!recording)return;
    const key=(event:KeyboardEvent)=>{if(event.code==='Space'||event.key==='ArrowRight'){event.preventDefault();next();}if(event.key==='Escape'){event.preventDefault();stop();}};
    addEventListener('keydown',key);return()=>removeEventListener('keydown',key);
  });
  async function save(){
    setBusy(true);setError('');
    try{
      const ids:(string|undefined)[]=[];
      for(const [index,clip] of clips.entries()){
        if(!clip)continue;
        const uploaded=await uploadFile(new File([clip.blob],`narration-${page.id.slice(0,8)}-scene-${index+1}.wav`,{type:'audio/wav'}));
        ids[index]=uploaded.id;
      }
      const fields={id:page.id,version:page.version,scope:page.scope,scopeId:page.scopeId,title:page.title,body:withAudio(page.body,ids),kind:page.kind,status:page.status};
      const next=await api<WikiPage>('/company-wiki',{...fields,requestId:crypto.randomUUID()},'PUT');
      // The document refreshes when the dialog closes, so the confirmation stays in view until then.
      setSaved(next);
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  const current=recording?.line;
  const close=()=>{stop();if(saved)onSaved(saved);onClose();};
  return <Dialog title="Record the narration" onClose={close} wide>
    <div className="fe-narration">
      {recording?<div className="fe-teleprompter" aria-live="polite">
        <small><Circle size={10} className="fe-rec"/> Recording{recording.mode==='all'?` · line ${spoken.findIndex(item=>item.index===current)+1} of ${spoken.length}`:''}</small>
        <p>{lines[current!]}</p>
        <div className="fe-actions"><button type="button" className="primary" onClick={next}>{recording.mode==='all'&&spoken.findIndex(item=>item.index===current)<spoken.length-1?'Next line':'Done'} <kbd>Space</kbd></button>
          <button type="button" className="fe-ghost" onClick={stop}><Square size={13}/> Stop</button></div>
      </div>:<p className="fe-muted">Read each line in your own voice. Press <strong>Record all</strong>, read the line on screen, and press Space (or Next line) before the next one. Each line becomes its own clip; you can redo any of them.</p>}
      <ol className="fe-script">{board.scenes.map((scene,index)=>scene.narration?<li key={index} className={current===index?'active':''}>
        <div><strong>{scene.caption||scene.scene}</strong><p>{scene.narration}</p></div>
        <div className="fe-script-actions">
          {clips[index]?<><audio src={clips[index]!.url} controls aria-label={`Clip for line ${index+1}`}/><small>{clips[index]!.seconds.toFixed(1)} s</small></>:scene.audio?<small className="fe-muted"><Check size={12}/> Recorded earlier</small>:null}
          <button type="button" className="fe-ghost" disabled={!!recording||busy} onClick={()=>void start(index)}>{clips[index]||scene.audio?<><RotateCcw size={13}/> Redo</>:<><Mic size={13}/> Record</>}</button>
        </div></li>:null)}</ol>
      {error&&<p className="fe-alert" role="alert">{error}</p>}
      {saved&&<p className="fe-notice" role="status">Saved. The clips are in Library → Media, and the storyboard now points each scene at its clip.</p>}
      <footer>
        <button type="button" disabled={!!recording||busy||spoken.length===0} onClick={()=>void start('all')}><Mic size={14}/> Record all</button>
        {saved?<button type="button" className="primary" onClick={close}>Done</button>
          :<button type="button" className="primary" disabled={!!recording||busy||clips.filter(Boolean).length===0} onClick={()=>void save()}>{busy?'Saving…':'Save narration'}</button>}
      </footer>
      <small className="fe-muted">Playback: <Play size={11}/> on each clip. The recording stays in this browser until you save it.</small>
    </div>
  </Dialog>;
}
