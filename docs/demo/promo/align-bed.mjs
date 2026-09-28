// Fits a generated music bed to the promo clock: finds the two drops (strongest energy onsets near 0:08 and 0:44), estimates
// tempo by onset autocorrelation, stretches/offsets so the drops land on the picture's hits, trims to 68 s with a tail fade,
// and muxes it onto the rendered picture without re-encoding video.
//   node docs/demo/promo/align-bed.mjs <take.flac> [out-name]
import path from 'node:path';
import {execFileSync} from 'node:child_process';
import fs from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import {DURATION,at,SECTIONS} from './timeline.mjs';

const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..'),root=path.join(repo,'artifacts/promo-20260927');
const ffmpeg=process.env.HIREZERO_DEMO_FFMPEG||'ffmpeg';
const src=path.resolve(process.argv[2]),name=process.argv[3]||path.basename(src).replace(/\.\w+$/,'');
const SR=11025,HS=220,HOP=HS/SR;
const decode=af=>{const b=execFileSync(ffmpeg,['-v','error','-i',src,'-ac','1','-ar',String(SR),...(af?['-af',af]:[]),'-f','f32le','-'],{maxBuffer:1<<30});return new Float32Array(b.buffer.slice(b.byteOffset,b.byteOffset+b.length));};
const x=decode(null),lo=decode('lowpass=f=150,lowpass=f=150');
const n=Math.floor(x.length/HS),envOf=sig=>{const e=new Float64Array(n);for(let i=0;i<n;i++){let s=0;const a=i*HS;for(let j=0;j<HS;j++)s+=sig[a+j]**2;e[i]=Math.log10(s/HS+1e-9);}return e;};
const env=envOf(x),low=envOf(lo);
// A drop is where the low end (kick + bass) jumps and then stays up: mean of the next 4 s against the previous 2 s.
const pre=Math.round(2/HOP),post=Math.round(4/HOP),onset=new Float64Array(n).fill(-9);
for(let i=pre;i<n-post;i++){let before=0,after=0;for(let k=1;k<=pre;k++)before+=low[i-k];for(let k=0;k<post;k++)after+=low[i+k];onset[i]=after/post-before/pre;}
const peak=(a,b)=>{let best=-9,bi=0;for(let i=Math.round(a/HOP);i<Math.min(n,Math.round(b/HOP));i++)if(onset[i]>best){best=onset[i];bi=i;}return {t:bi*HOP,strength:best};};
const want1=at(SECTIONS.reveal[0]),want2=at(SECTIONS.approve[0]);
const d1=peak(want1-3,want1+4),d2=peak(want2-5,want2+5);
// Tempo: autocorrelate onset flux at ~16 beats (7.65–8.35 s lag, i.e. 115–125 BPM; wider windows alias onto 15 or 17 beats) for 0.25% resolution; the score asks for 120 BPM.
const flux=Array.from({length:n},(_,i)=>i?Math.max(0,low[i]-low[i-1]):0);let bestLag=0,bestR=-1;
for(let lag=Math.round(7.65/HOP);lag<=Math.round(8.35/HOP);lag++){let r=0;for(let i=0;i+lag<n;i++)r+=flux[i]*flux[i+lag];r/=n-lag;if(r>bestR){bestR=r;bestLag=lag;}}
const bpm=16*60/(bestLag*HOP),duration=x.length/SR;
// Anchor on drop 2 (it follows a breakdown, so it is unambiguous); stretch by measured tempo; drop 1 is a consistency check.
// A render that kept the score's length and tempo is already on the picture's bar grid (bar 1 at 0:00): leave it alone.
// Only a drifted render is re-fitted, anchored on drop 2 (it follows a breakdown, so it is unambiguous).
const scoreLocked=Math.abs(duration-DURATION)<0.6&&Math.abs(bpm-120)<0.6;
const ratio=scoreLocked?1:Math.abs(bpm-120)<6?120/bpm:1,sourceGap=(want2-want1)/ratio,drop1Check=+(d2.t-sourceGap).toFixed(2);
const offset=scoreLocked?0:want2-d2.t/ratio; // seconds to delay (+) or trim (−) after stretching
const stretchOk=ratio!==1;
// How hard each picture hit lands in this take: low-band jump (dB-ish) across the cut.
const jump=t=>{const i=Math.round(t/HOP),w=Math.round(1/HOP);let b=0,a=0;for(let k=1;k<=w;k++){b+=low[Math.max(0,i-k)];a+=low[Math.min(n-1,i+k-1)];}return +(10*(a-b)/w).toFixed(1);};
const out=path.join(root,'yue2',`${name}-aligned.wav`);
const filters=[`atempo=${ratio.toFixed(5)}`];
if(offset>0)filters.push(`adelay=${Math.round(offset*1000)}:all=1`);else filters.push(`atrim=start=${(-offset).toFixed(3)}`,'asetpts=PTS-STARTPTS');
filters.push(`apad=whole_dur=${DURATION}`,`atrim=0:${DURATION}`,`afade=t=out:st=${DURATION-2}:d=2`);
execFileSync(ffmpeg,['-v','error','-y','-i',src,'-af',filters.join(','),'-ar','48000','-ac','2',out]);
const video=path.join(root,'deliverables','hirezero-promo.mp4'),mp4=path.join(root,'deliverables',`hirezero-promo-${name}.mp4`);
execFileSync(ffmpeg,['-v','error','-y','-i',video,'-i',out,'-map','0:v','-map','1:a','-c:v','copy','-c:a','aac','-b:a','256k','-af','loudnorm=I=-14:TP=-1.5:LRA=11','-shortest','-movflags','+faststart',mp4]);
const report={take:path.basename(src),sourceSeconds:+duration.toFixed(2),estimatedBpm:+bpm.toFixed(1),drop1:{found:+d1.t.toFixed(2),strength:+d1.strength.toFixed(2),expectedFromDrop2:drop1Check,agrees:Math.abs(d1.t-drop1Check)<0.35},drop2:{found:+d2.t.toFixed(2),strength:+d2.strength.toFixed(2)},
  scoreLocked,hitAtDrop1Db:jump(want1),hitAtDrop2Db:jump(want2),hitAtOutroDb:jump(at(SECTIONS.outro[0])),stretch:+ratio.toFixed(4),stretchApplied:stretchOk,offsetSeconds:+offset.toFixed(2),aligned:path.basename(out),video:path.basename(mp4)};
await fs.writeFile(path.join(root,'yue2',`${name}-align.json`),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report));
