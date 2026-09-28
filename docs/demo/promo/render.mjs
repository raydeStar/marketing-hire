// Renders promo.html frame by frame (deterministic seek, no real-time capture) and pipes PNGs straight into ffmpeg, so no frame
// files touch the disk. Usage:
//   node docs/demo/promo/render.mjs                       full video + music
//   node docs/demo/promo/render.mjs --stills 1,9,15       review stills only
//   node docs/demo/promo/render.mjs --music other.wav     mux a different music file (same 68 s, 120 BPM grid)
import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn,execFileSync} from 'node:child_process';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
import {chromium} from '../../../web/node_modules/playwright/index.mjs';
import {requireArtifactSpace,cleanArtifactPaths} from '../../../scripts/artifact-storage.mjs';
import * as TL from './timeline.mjs';

const here=path.dirname(fileURLToPath(import.meta.url)),repo=path.resolve(here,'../../..');
const root=path.join(repo,'artifacts/promo-20260927'),stage=path.join(root,'stage'),out=path.join(root,'deliverables');
const arg=name=>{const i=process.argv.indexOf(name);return i>0?process.argv[i+1]:null;};
const ffmpeg=process.env.HIREZERO_DEMO_FFMPEG||'ffmpeg',ffprobe=process.env.HIREZERO_DEMO_FFPROBE||'ffprobe';
await requireArtifactSpace(repo,1024**3,'Promo render');
await fs.mkdir(path.join(stage,'assets'),{recursive:true});await fs.mkdir(out,{recursive:true});

const captures=path.join(repo,'artifacts/marketing-demo-20260927/captures');
const assets={'business-brief.png':captures,'campaign-pieces-desktop.png':captures,'cockpit-phone.png':captures,'campaign-pieces-phone.png':captures,'shell-team-roles.png':path.join(repo,'docs/media')};
for(const [name,dir] of Object.entries(assets))await fs.copyFile(path.join(dir,name),path.join(stage,'assets',name));
const timeline=Object.fromEntries(Object.entries(TL).filter(([,v])=>typeof v!=='function'));
const html=(await fs.readFile(path.join(here,'promo.html'),'utf8')).replace('/*TIMELINE*/{}',JSON.stringify(timeline));
const page_=path.join(stage,'promo.html');await fs.writeFile(page_,html);

const browser=await chromium.launch({args:['--force-color-profile=srgb','--disable-lcd-text']});
const receipt={createdAt:new Date().toISOString(),fps:TL.FPS,duration:TL.DURATION,outputs:[]};
try{
  const page=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error')errors.push(m.text());});
  await page.goto(pathToFileURL(page_).href);await page.evaluate(()=>window.ready);
  if(errors.length)throw new Error('Page errors: '+errors.join('\n'));
  const cdp=await page.context().newCDPSession(page);
  const grab=async t=>{await page.evaluate(t=>window.seek(t),t);return Buffer.from((await cdp.send('Page.captureScreenshot',{format:'png',optimizeForSpeed:true})).data,'base64');};
  const stills=arg('--stills');
  if(stills){
    const dir=path.join(root,'stills');await fs.mkdir(dir,{recursive:true});
    for(const s of stills.split(',').map(Number)){await fs.writeFile(path.join(dir,`t${String(s.toFixed(2)).padStart(5,'0')}.png`),await grab(s));}
    console.log('stills →',dir);
  }else{
    const music=arg('--music')||path.join(root,'music.wav');
    const name=arg('--out')||'hirezero-promo.mp4',file=path.join(out,name);
    const frames=Math.round(TL.DURATION*TL.FPS);
    const ff=spawn(ffmpeg,['-y','-hide_banner','-loglevel','error','-f','image2pipe','-framerate',String(TL.FPS),'-c:v','png','-i','-','-i',music,
      '-map','0:v','-map','1:a','-c:v','libx264','-preset','slow','-crf','16','-pix_fmt','yuv420p','-profile:v','high','-tune','animation',
      '-c:a','aac','-b:a','256k','-af','loudnorm=I=-14:TP=-1.5:LRA=11','-shortest','-movflags','+faststart',file],{stdio:['pipe','inherit','inherit']});
    const done=new Promise((res,rej)=>{ff.once('error',rej);ff.once('exit',c=>c===0?res():rej(new Error('ffmpeg exit '+c)));});
    const t0=Date.now();
    for(let f=0;f<frames;f++){
      const buf=await grab(f/TL.FPS);if(!ff.stdin.write(buf))await new Promise(r=>ff.stdin.once('drain',r));
      if(f%150===0)console.log(`frame ${f}/${frames} (${((Date.now()-t0)/1000).toFixed(0)}s)`);
    }
    ff.stdin.end();await done;
    if(errors.length)throw new Error('Page errors during render: '+errors.join('\n'));
    const probe=JSON.parse(execFileSync(ffprobe,['-v','error','-show_streams','-show_format','-of','json',file],{encoding:'utf8'}));
    receipt.outputs.push({file:name,seconds:Number(probe.format.duration),streams:probe.streams.map(s=>s.codec_type+':'+s.codec_name),bytes:(await fs.stat(file)).size,
      sha256:createHash('sha256').update(await fs.readFile(file)).digest('hex'),music:path.basename(music),renderSeconds:Math.round((Date.now()-t0)/1000)});
    console.log('video →',file);
  }
}finally{
  await browser.close();
  receipt.removed=await cleanArtifactPaths(root,['stage']);
  await fs.writeFile(path.join(root,'render-receipt.json'),JSON.stringify(receipt,null,2)+'\n');
}
