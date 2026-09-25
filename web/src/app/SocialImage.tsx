import {useEffect,useMemo,useRef,useState} from 'react';
import {Download,ImagePlus} from 'lucide-react';
import {uploadFile} from '../api';
import {Dialog} from './shared';

/** Social images from a draft, drawn on a canvas in the browser: no image model, no GPU, nothing leaves the workspace but the saved PNG. */
export type ImageTemplate='headline'|'quote'|'number';
export type ImageLook='ink'|'paper'|'brand';
export const imageSizes={
  landscape:{label:'Link preview · LinkedIn, X, Bluesky (1200×627)',width:1200,height:627},
  wide:{label:'Wide · X, YouTube (1600×900)',width:1600,height:900},
  square:{label:'Square · LinkedIn, Instagram, Threads (1080×1080)',width:1080,height:1080},
  portrait:{label:'Portrait · Instagram, Threads (1080×1350)',width:1080,height:1350},
} as const;
export type ImageSize=keyof typeof imageSizes;

export function sizeFor(channel:string):ImageSize{
  const name=channel.toLowerCase();
  return /instagram|threads/.test(name)?'portrait':/x|twitter|youtube/.test(name)&&!/linkedin/.test(name)?'wide':'landscape';
}

/** The draft's hook: its first sentence, without links, hashtags or mentions. */
export function hookFrom(content:string){
  const plain=content.replace(/https?:\/\/\S+/g,'').replace(/(^|\s)[#@][\w.-]+/g,'$1').replace(/\s+/g,' ').trim();
  const first=plain.match(/^(.{12,160}?[.!?])(\s|$)/)?.[1]||plain.slice(0,140);
  return first.trim();
}
/** A figure worth making big (a percentage, money or a count), with the words around it. */
export function numberFrom(content:string){
  const found=content.match(/(?:[$€£]\s?\d[\d,.]*\s?(?:[kmb]|million|billion)?|\d[\d,.]*\s?(?:%|x|hours?|days?|minutes?))/i);
  return found?found[0].replace(/\s+/g,' ').trim():'';
}

const looks:Record<ImageLook,{background:string;text:string;muted:string}>={ink:{background:'#111418',text:'#f5f6f7',muted:'#9aa3ad'},paper:{background:'#f7f7f5',text:'#15181c',muted:'#5d6670'},brand:{background:'',text:'#ffffff',muted:'rgba(255,255,255,.78)'}};

/** Wrap text into lines that fit a width, shrinking the type until the block fits the box. */
function fit(context:CanvasRenderingContext2D,text:string,width:number,height:number,largest:number,weight:number,leading=1.18){
  for(let size=largest;size>=18;size-=2){
    context.font=`${weight} ${size}px system-ui, -apple-system, "Segoe UI", sans-serif`;
    const lines:string[]=[];let line='';
    for(const word of text.split(/\s+/)){
      const next=line?line+' '+word:word;
      if(context.measureText(next).width<=width||!line)line=next;else{lines.push(line);line=word;}
    }
    if(line)lines.push(line);
    if(lines.length*size*leading<=height&&lines.every(item=>context.measureText(item).width<=width))return {size,lines,leading};
  }
  return {size:18,lines:[text.slice(0,120)],leading};
}

export function drawSocialImage(canvas:HTMLCanvasElement,{template,look,size,text,figure,brand,accent,footer}:{template:ImageTemplate;look:ImageLook;size:ImageSize;text:string;figure:string;brand:string;accent:string;footer:string}){
  const {width,height}=imageSizes[size];
  canvas.width=width;canvas.height=height;
  const context=canvas.getContext('2d');if(!context)return;
  const palette=looks[look];const background=look==='brand'?accent:palette.background;
  context.fillStyle=background;context.fillRect(0,0,width,height);
  const pad=Math.round(Math.min(width,height)*0.085);
  // A thin accent rule and the brand name: quiet, the way a company's own slides look.
  context.fillStyle=look==='brand'?'rgba(255,255,255,.9)':accent;context.fillRect(pad,pad,Math.round(width*0.06),Math.max(4,Math.round(height*0.008)));
  context.fillStyle=palette.muted;context.font=`600 ${Math.round(height*0.034)}px system-ui, -apple-system, "Segoe UI", sans-serif`;context.textBaseline='top';
  if(brand)context.fillText(brand.toUpperCase().split('').join(String.fromCharCode(8202)),pad,pad+Math.round(height*0.03));
  const top=pad+Math.round(height*0.11),bottom=height-pad-Math.round(height*0.08),boxWidth=width-pad*2;
  context.fillStyle=palette.text;
  let y=top;
  if(template==='number'&&figure){
    const big=fit(context,figure,boxWidth,(bottom-top)*0.5,Math.round(height*0.3),750,1.0);
    const bigHeight=big.lines.length*big.size*big.leading,gap=Math.round(height*0.03);
    const rest=fit(context,text,boxWidth,bottom-top-bigHeight-gap,Math.round(height*0.07),500);
    y=top+Math.max(0,((bottom-top)-(bigHeight+gap+rest.lines.length*rest.size*rest.leading))/2.4);
    context.font=`750 ${big.size}px system-ui, -apple-system, "Segoe UI", sans-serif`;
    context.fillStyle=look==='brand'?'#ffffff':accent;
    big.lines.forEach((line,index)=>context.fillText(line,pad,y+index*big.size*big.leading));
    y+=bigHeight+gap;
    context.font=`500 ${rest.size}px system-ui, -apple-system, "Segoe UI", sans-serif`;
    context.fillStyle=palette.text;
    rest.lines.forEach((line,index)=>context.fillText(line,pad,y+index*rest.size*rest.leading));
  }else{
    const words=template==='quote'?`“${text.replace(/^["“]|["”]$/g,'')}”`:text;
    const block=fit(context,words,boxWidth,bottom-top,Math.round(height*(template==='quote'?0.095:0.11)),template==='quote'?500:700);
    const blockHeight=block.lines.length*block.size*block.leading;
    y=top+Math.max(0,((bottom-top)-blockHeight)/2.4);
    block.lines.forEach((line,index)=>context.fillText(line,pad,y+index*block.size*block.leading));
  }
  if(footer){context.fillStyle=palette.muted;context.font=`500 ${Math.round(height*0.032)}px system-ui, -apple-system, "Segoe UI", sans-serif`;context.textBaseline='bottom';context.fillText(footer,pad,height-pad);}
}

// The brand name and color are remembered in this browser only, as a convenience.
function remembered(key:string,fallback:string){try{return localStorage.getItem(key)||fallback;}catch{return fallback;}}
function remember(key:string,value:string){try{localStorage.setItem(key,value);}catch{/* private window: nothing to keep */}}

export function SocialImageDialog({content,channel,onClose}:{content:string;channel:string;onClose:()=>void}){
  const figureFound=useMemo(()=>numberFrom(content),[content]);
  const [template,setTemplate]=useState<ImageTemplate>(figureFound?'number':'headline'),[look,setLook]=useState<ImageLook>('ink'),[size,setSize]=useState<ImageSize>(sizeFor(channel));
  const [text,setText]=useState(()=>hookFrom(content)),[figure,setFigure]=useState(figureFound),[name,setName]=useState(()=>remembered('fe-image-brand','')),[footer,setFooter]=useState('');
  const [accent,setAccent]=useState(()=>remembered('fe-image-accent','#2f5bd3')),[busy,setBusy]=useState(false),[saved,setSaved]=useState(''),[error,setError]=useState(''),[download,setDownload]=useState('');
  const canvas=useRef<HTMLCanvasElement>(null);
  useEffect(()=>{if(canvas.current)drawSocialImage(canvas.current,{template,look,size,text,figure,brand:name,accent,footer});setSaved('');},[template,look,size,text,figure,name,accent,footer]);
  useEffect(()=>()=>{if(download)URL.revokeObjectURL(download);},[download]);
  const fileName=`${channel.toLowerCase().replace(/[^a-z0-9]+/g,'-')||'social'}-${template}-${imageSizes[size].width}x${imageSizes[size].height}.png`;
  async function save(){
    if(!canvas.current)return;setBusy(true);setError('');
    try{
      const blob=await new Promise<Blob>((done,fail)=>canvas.current!.toBlob(value=>value?done(value):fail(new Error('The image could not be drawn.')),'image/png'));
      const file=new File([blob],fileName,{type:'image/png'});
      await uploadFile(file);remember('fe-image-brand',name.trim());remember('fe-image-accent',accent);
      setDownload(previous=>{if(previous)URL.revokeObjectURL(previous);return URL.createObjectURL(blob);});
      setSaved('Saved to Library → Media. Download it to attach when you post.');
    }catch(cause){setError((cause as Error).message);}finally{setBusy(false);}
  }
  return <Dialog title="Image for this post" onClose={onClose} wide>
    <div className="fe-social-image">
      <div className="fe-social-preview"><canvas ref={canvas} aria-label="Image preview"/></div>
      <div className="fe-form">
        <nav className="fe-segmented" aria-label="Layout">{([['headline','Headline'],['quote','Quote'],['number','Key number']] as const).map(([value,label])=><button type="button" key={value} aria-pressed={template===value} onClick={()=>setTemplate(value)}>{label}</button>)}</nav>
        <label>Words<textarea rows={3} maxLength={220} value={text} onChange={event=>setText(event.target.value)}/></label>
        {template==='number'&&<label>Key number<input maxLength={24} value={figure} onChange={event=>setFigure(event.target.value)} placeholder="e.g. 3×, 40%, $69"/></label>}
        <label>Brand name<input maxLength={40} value={name} onChange={event=>setName(event.target.value)}/></label>
        <label>Footer <span className="fe-muted">(optional: site or handle)</span><input maxLength={60} value={footer} onChange={event=>setFooter(event.target.value)} placeholder="example.com"/></label>
        <label>Size<select value={size} onChange={event=>setSize(event.target.value as ImageSize)}>{Object.entries(imageSizes).map(([key,item])=><option key={key} value={key}>{item.label}</option>)}</select></label>
        <div className="fe-social-look"><nav className="fe-segmented" aria-label="Colors">{([['ink','Dark'],['paper','Light'],['brand','Brand']] as const).map(([value,label])=><button type="button" key={value} aria-pressed={look===value} onClick={()=>setLook(value)}>{label}</button>)}</nav>
          <label className="fe-color">Brand color<input type="color" value={accent} onChange={event=>setAccent(event.target.value)}/></label></div>
        <p className="fe-muted">Only what the draft says: check the words and the number before you post.</p>
        {error&&<p className="fe-alert" role="alert">{error}</p>}
        {saved&&<p className="fe-muted" role="status">{saved}</p>}
        <footer>{download&&<a className="fe-button" href={download} download={fileName}><Download size={14}/> Download</a>}
          <button type="button" className="primary" disabled={busy||!text.trim()} onClick={()=>void save()}><ImagePlus size={14}/> {busy?'Saving…':'Save to Library'}</button></footer>
      </div>
    </div>
  </Dialog>;
}
