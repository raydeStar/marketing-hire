// Offline synth for the promo's music bed: 120 BPM, A minor (Am–F–C–G), arranged on the shared timeline so drops, clicks and
// pops line up with the picture. Deterministic (seeded noise); writes a 44.1 kHz 16-bit stereo WAV. No samples, no network.
import fs from 'node:fs/promises';
import {BEAT,BAR,BARS,DURATION,SECTIONS,at,COLD_HITS,COLD_STUTTER,CLICKS,CHIP_TIMES,RESULT_TIMES,APPROVE_TIMES} from './timeline.mjs';

const SR=44100,N=Math.ceil(DURATION*SR),TAU=Math.PI*2;
const out=process.argv[2]||'music.wav';
let seed=0x9e3779b9;const rnd=()=>{seed|=0;seed=seed+0x6d2b79f5|0;let t=Math.imul(seed^seed>>>15,1|seed);t=t+Math.imul(t^t>>>7,61|t)^t;return((t^t>>>14)>>>0)/4294967296;};
const noise=()=>rnd()*2-1;
const bus=()=>({L:new Float32Array(N),R:new Float32Array(N)});
const drums=bus(),bass=bus(),pads=bus(),arp=bus(),lead=bus(),fx=bus(),send=bus();
const put=(b,i,l,r=l)=>{if(i>=0&&i<N){b.L[i]+=l;b.R[i]+=r;}};
const mtof=m=>440*2**((m-69)/12);
const inSec=(name,t)=>t>=at(SECTIONS[name][0])&&t<at(SECTIONS[name][1]);
const barOf=t=>Math.floor(t/BAR);
const expRamp=(a,b,x)=>a*(b/a)**Math.min(1,Math.max(0,x));

class SVF{ // topology-preserving state-variable filter
  constructor(){this.a=0;this.b=0;}
  run(x,fc,q,mode){
    const g=Math.tan(Math.PI*Math.min(Math.max(fc,20),SR*0.45)/SR),k=1/q,a1=1/(1+g*(g+k)),a2=g*a1,a3=g*a2;
    const v3=x-this.b,v1=a1*this.a+a2*v3,v2=this.b+a2*this.a+a3*v3;this.a=2*v1-this.a;this.b=2*v2-this.b;
    return mode==='lp'?v2:mode==='bp'?v1:x-k*v1-v2;
  }
}
const blep=(p,dt)=>{if(p<dt){p/=dt;return p+p-p*p-1;}if(p>1-dt){p=(p-1)/dt;return p*p+p+p+1;}return 0;};
const saw=(p,dt)=>2*p-1-blep(p,dt);

// Chords per bar (voice-led) and bass roots.
const CHORDS=[[57,60,64,69],[57,60,65,69],[55,60,64,67],[55,59,62,67]];
const ROOTS=[45,41,48,43];
const chordAt=t=>CHORDS[barOf(t)%4],rootAt=t=>ROOTS[barOf(t)%4];

// ---------- Drums ----------
const kicks=[];
function kick(t0,g=1,pitch=1){
  kicks.push(t0);const i0=Math.round(t0*SR);let ph=0;
  for(let j=0;j<0.6*SR;j++){const t=j/SR,f=(46+120*Math.exp(-t*26)+80*Math.exp(-t*180))*pitch;ph+=TAU*f/SR;
    let s=Math.tanh(Math.sin(ph)*Math.exp(-t*5)*2)*0.95*Math.min(1,t/0.0015);
    if(j<SR*0.004)s+=noise()*0.25*(1-j/(SR*0.004));put(drums,i0+j,s*g);}
}
function clap(t0,g=1){
  const f=new SVF(),i0=Math.round(t0*SR);
  for(let j=0;j<0.4*SR;j++){const t=j/SR;
    let env=0;for(const o of [0,0.011,0.022])if(t>=o)env+=Math.exp(-(t-o)*170);env+=t>0.028?0.55*Math.exp(-(t-0.028)*16):0;
    const s=f.run(noise(),1400,0.8,'bp')*env*1.5*g+Math.sin(TAU*185*t)*Math.exp(-t*38)*0.3*g;
    put(drums,i0+j,s,s*0.92);put(send,i0+j,s*0.35);}
}
function hat(t0,g=0.2,decay=48,pan=0){
  const f=new SVF(),i0=Math.round(t0*SR);
  for(let j=0;j<0.3*SR;j++){const t=j/SR,s=f.run(noise(),7500,0.7,'hp')*Math.exp(-t*decay)*g;put(drums,i0+j,s*(1-pan),s*(1+pan));}
}
function crash(t0,g=0.3,len=2.6){
  const f=new SVF(),i0=Math.round(t0*SR);
  for(let j=0;j<len*SR;j++){const t=j/SR,s=f.run(noise(),5200,0.6,'hp')*Math.exp(-t*2)*g;put(drums,i0+j,s,f.b*0+s*0.95);put(send,i0+j,s*0.4);}
}
function boom(t0,g=1,len=2.4){ // sub impact
  const i0=Math.round(t0*SR);let ph=0;
  for(let j=0;j<len*SR;j++){const t=j/SR,f=28+34*Math.exp(-t*3);ph+=TAU*f/SR;const s=Math.tanh(Math.sin(ph)*2.2)*Math.exp(-t*1.9)*g*Math.min(1,t/0.003);put(fx,i0+j,s);put(send,i0+j,s*0.15);}
}
function reverseCrash(tEnd,len=1.5,g=0.28){
  const f=new SVF(),i0=Math.round((tEnd-len)*SR);
  for(let j=0;j<len*SR;j++){const x=j/(len*SR),s=f.run(noise(),3000+6000*x,0.7,'hp')*x**3*g;put(fx,i0+j,s,s);put(send,i0+j,s*0.3);}
}
function riser(t0,t1,g=0.22){
  const f=new SVF(),i0=Math.round(t0*SR),len=(t1-t0)*SR;
  for(let j=0;j<len;j++){const x=j/len,s=f.run(noise(),expRamp(250,9000,x),2.2,'bp')*x**2*g;const w=Math.sin(TAU*x*3)*0.15;put(fx,i0+j,s*(1+w),s*(1-w));put(send,i0+j,s*0.4);}
}
function downlifter(t0,len=2.2,g=0.2){
  const f=new SVF(),i0=Math.round(t0*SR);
  for(let j=0;j<len*SR;j++){const x=j/(len*SR),s=f.run(noise(),expRamp(7000,200,x),1.8,'bp')*(1-x)**2*g;put(fx,i0+j,s);put(send,i0+j,s*0.5);}
}
function click(t0,g=0.35){ // UI click
  const f=new SVF(),i0=Math.round(t0*SR);
  for(let j=0;j<0.05*SR;j++){const t=j/SR,s=(f.run(noise(),4200,1.2,'bp')*Math.exp(-t*900)*1.2+Math.sin(TAU*2400*t)*Math.exp(-t*160)*0.35)*g;put(fx,i0+j,s);put(send,i0+j,s*0.2);}
}
function pop(t0,g=0.16,pitch=1){ // soft UI pop
  const i0=Math.round(t0*SR);let ph=0;
  for(let j=0;j<0.09*SR;j++){const t=j/SR,f=(900+900*Math.min(1,t/0.03))*pitch;ph+=TAU*f/SR;const s=Math.sin(ph)*Math.exp(-t*45)*g*Math.min(1,t/0.002);put(fx,i0+j,s*0.9,s);put(send,i0+j,s*0.4);}
}

const drop1=at(SECTIONS.reveal[0]),brk=at(SECTIONS.breakdown[0]),drop2=at(SECTIONS.approve[0]),outro=at(SECTIONS.outro[0]);
// Cold open: one sub boom per line, then kick+clap stabs climbing into the drop.
for(const t of COLD_HITS){boom(t,0.75,1.8);hat(t,0.12,20,0);}
COLD_STUTTER.forEach((t,i)=>{kick(t,0.8,1+i*0.08);clap(t,0.5+i*0.1);});
riser(at(2),drop1);reverseCrash(drop1,1.2);
// Grooves.
for(let t=drop1;t<outro;t+=BEAT){
  const inBreak=t>=brk&&t<drop2,b=barOf(t),beat=Math.round((t-at(b))/BEAT);
  if(!inBreak){
    kick(t,0.72);
    if(beat===1||beat===3)clap(t,0.8);
    hat(t+BEAT/2,0.42,36,0.2);
    if(b>=SECTIONS.shift[0])hat(t+BEAT/4,0.16,60,-0.3),hat(t+3*BEAT/4,0.16,60,-0.3);
    if((b+1)%4===0&&beat===3&&t+BEAT<brk)clap(t+BEAT*0.75,0.45); // pickup flam every 4 bars
  }
}
// Breakdown build: snare roll accelerating through the last bar before drop 2.
{const s=at(SECTIONS.breakdown[1]-1);for(let k=0;k<8;k++)clap(s+k*BEAT/4*0.999,0.18+k*0.03);for(let k=0;k<8;k++)clap(s+BEAT*2+k*BEAT/4,0.3+k*0.04);}
riser(at(SECTIONS.breakdown[1]-2),drop2,0.26);reverseCrash(drop2,1.5,0.32);downlifter(brk-0.1);
for(const t of [drop1,drop2,outro]){boom(t,1);crash(t,0.32);}
crash(at(SECTIONS.results[0]),0.16,1.6);crash(at(SECTIONS.shift[0]),0.16,1.6);crash(at(SECTIONS.anywhere[0]),0.2,1.8);
// Final button: one last hit as the URL lands.
kick(outro+4*BEAT,0.7);boom(outro+4*BEAT,0.5,2);
// UI sounds.
for(const t of CLICKS)click(t);
CHIP_TIMES.forEach((t,i)=>pop(t,0.13,1+i*0.06));
RESULT_TIMES.forEach((t,i)=>pop(t,0.12,1.1+i*0.04));
APPROVE_TIMES.forEach((t,i)=>pop(t,0.15,1.2+i*0.07));

// ---------- Sidechain ----------
kicks.sort((a,b)=>a-b);let kp=0;
function duck(t,depth){while(kp+1<kicks.length&&kicks[kp+1]<=t)kp++;const k=kicks[kp];if(k===undefined||k>t)return 1;const x=(t-k)/0.32;return x>=1?1:1-depth*(1-x)**2;}

// ---------- Pads (supersaw) ----------
{
  const det=[0,-0.11,0.11,-0.23,0.23],phase=[];for(let v=0;v<20;v++)phase.push(rnd());
  const fl=new SVF(),fr=new SVF();kp=0;
  for(let i=0;i<N;i++){
    const t=i/SR,ch=chordAt(t);let cut,lvl,dep;
    if(t<drop1){cut=expRamp(260,1900,t/drop1);lvl=0.30+0.2*t/drop1;dep=0;}
    else if(t<brk){cut=3300;lvl=0.42;dep=0.62;}
    else if(t<drop2){cut=expRamp(900,6000,(t-brk)/(drop2-brk));lvl=0.85;dep=0;}
    else if(t<outro){cut=4800;lvl=0.46;dep=0.65;}
    else{cut=expRamp(3200,300,(t-outro)/(DURATION-outro));lvl=0.5;dep=0;}
    let l=0,r=0;
    for(let n=0;n<4;n++)for(let v=0;v<5;v++){const idx=n*5+v,f=mtof(ch[n]+det[v]*1.0)*(1+det[v]*0.004),dt=f/SR;
      let p=phase[idx]+dt;if(p>=1)p-=1;phase[idx]=p;const s=saw(p,dt),pan=(v%2?1:-1)*(v?0.35+0.1*n:0);l+=s*(1-pan);r+=s*(1+pan);}
    const g=lvl*duck(t,dep)*0.05;put(pads,i,fl.run(l,cut,0.8,'lp')*g,fr.run(r,cut,0.8,'lp')*g);
    put(send,i,pads.L[i]*0.25,pads.R[i]*0.25);
  }
}

// ---------- Bass (driving 8ths + sub) ----------
{
  const f=new SVF();let ph=0,sph=0;kp=0;
  for(let i=Math.round(drop1*SR);i<Math.round(outro*SR);i++){
    const t=i/SR;if(t>=brk&&t<drop2)continue;
    const step=Math.floor((t-drop1)/(BEAT/2)),tn=t-drop1-step*BEAT/2,m=rootAt(t)-12+(step%2?12:0)*0,fr=mtof(m),dt=fr/SR;
    ph+=dt;if(ph>=1)ph-=1;sph+=fr/SR;if(sph>=1)sph-=1;
    const env=Math.exp(-tn*7)*Math.min(1,tn/0.004)*(step%2?1:0.8),cut=180+1400*Math.exp(-tn*22);
    const s=(f.run(saw(ph,dt),cut,1.1,'lp')*0.55+Math.sin(TAU*sph)*0.42)*env*duck(t,0.75)*0.5;
    put(bass,i,s);
  }
}

// ---------- Arp pluck (16ths) with ping-pong delay ----------
{
  const pattern=[0,1,2,3,2,1,2,3,0,2,3,1,2,3,2,1],start=at(SECTIONS.shift[0]),stop=at(SECTIONS.outro[0]+2);
  for(let t=start,k=0;t<stop;t+=BEAT/4,k++){
    const ch=chordAt(t),m=ch[pattern[k%16]]+12+(k%8>=6?12:0),inBreak=t>=brk&&t<drop2,fade=t>=outro?1-(t-outro)/(stop-outro):1;
    const g=(inBreak?0.13:0.1)*fade*(k%4===0?1.15:0.85),f=new SVF(),fr=mtof(m),dt=fr/SR,i0=Math.round(t*SR);let p=rnd();
    for(let j=0;j<0.22*SR;j++){const tt=j/SR;p+=dt;if(p>=1)p-=1;const raw=saw(p,dt)*0.6+(p<0.5?0.4:-0.4);
      const s=f.run(raw,500+4200*Math.exp(-tt*24),1.3,'lp')*Math.exp(-tt*14)*Math.min(1,tt/0.002)*g*(inBreak?1:duck(t+tt,0.4));
      put(arp,i0+j,s*0.9,s);}
  }
  const d=Math.round(0.375*SR),L=arp.L,R=arp.R;
  for(let i=d;i<N;i++){L[i]+=R[i-d]*0.34;R[i]+=L[i-d]*0.34;}
  for(let i=0;i<N;i++){send.L[i]+=L[i]*0.3;send.R[i]+=R[i]*0.3;}
}

// ---------- Lead hook over drop 2 ----------
{
  // [bar offset in beats, midi, length in beats]
  const hook=[[0,76,1],[1.5,74,0.5],[2,72,1],[3,69,1],[4,69,1],[5.5,72,0.5],[6,74,1],[7,72,1],[8,76,1],[9.5,79,0.5],[10,76,1],[11,74,1],[12,74,1],[13.5,71,0.5],[14,67,1.5]];
  for(const rep of [0,16]){
    for(const [b,m,len] of hook){
      const t0=drop2+(rep+b)*BEAT,dur=len*BEAT,f=new SVF(),i0=Math.round(t0*SR);let p1=rnd(),p2=rnd();
      for(let j=0;j<(dur+0.25)*SR;j++){const t=j/SR,vib=1+0.004*Math.sin(TAU*5.5*t)*Math.min(1,t/0.25),fr=mtof(m)*vib;
        p1+=fr/SR;if(p1>=1)p1-=1;p2+=fr*1.006/SR;if(p2>=1)p2-=1;
        const env=Math.min(1,t/0.01)*(t<dur?0.75+0.25*Math.exp(-t*6):Math.exp(-(t-dur)*14));
        const s=f.run(saw(p1,fr/SR)+saw(p2,fr/SR),2600+1200*Math.exp(-t*8),0.9,'lp')*env*0.075*(rep?0.85:1);
        put(lead,i0+j,s*0.95,s);put(send,i0+j,s*0.5,s*0.5);}
    }
  }
}

// ---------- Reverb (Freeverb) on the send ----------
const wet=bus();
{
  const combs=[1116,1188,1277,1356,1422,1491,1557,1617],alls=[556,441,341,225];
  for(const [src,dst,spread] of [[send.L,wet.L,0],[send.R,wet.R,23]]){
    const cb=combs.map(c=>({buf:new Float32Array(c+spread),i:0,st:0})),ab=alls.map(a=>({buf:new Float32Array(a+spread),i:0}));
    for(let n=0;n<N;n++){const x=src[n]*0.015;let o=0;
      for(const c of cb){const y=c.buf[c.i];c.st=y*0.8+c.st*0.2;c.buf[c.i]=x+c.st*0.86;if(++c.i>=c.buf.length)c.i=0;o+=y;}
      for(const a of ab){const b=a.buf[a.i];const y=-o+b;a.buf[a.i]=o+b*0.5;if(++a.i>=a.buf.length)a.i=0;o=y;}
      dst[n]=o;}
  }
}

// ---------- Mix & master ----------
const mixL=new Float32Array(N),mixR=new Float32Array(N);
const gains=[[drums,0.62],[bass,0.55],[pads,1.7],[arp,1.5],[lead,1.5],[fx,0.6],[wet,1.3]];
for(const [b,g] of gains)for(let i=0;i<N;i++){mixL[i]+=b.L[i]*g;mixR[i]+=b.R[i]*g;}
const hpL=new SVF(),hpR=new SVF();let peak=0;
for(let i=0;i<N;i++){
  const t=i/SR,fade=Math.min(1,t/0.01)*Math.min(1,(DURATION-t)/1.6);
  mixL[i]=Math.tanh(hpL.run(mixL[i],28,0.7,'hp')*1.25)*fade;mixR[i]=Math.tanh(hpR.run(mixR[i],28,0.7,'hp')*1.25)*fade;
  peak=Math.max(peak,Math.abs(mixL[i]),Math.abs(mixR[i]));
}
const norm=0.89/peak,data=Buffer.alloc(44+N*4);
data.write('RIFF',0);data.writeUInt32LE(36+N*4,4);data.write('WAVEfmt ',8);data.writeUInt32LE(16,16);data.writeUInt16LE(1,20);data.writeUInt16LE(2,22);
data.writeUInt32LE(SR,24);data.writeUInt32LE(SR*4,28);data.writeUInt16LE(4,32);data.writeUInt16LE(16,34);data.write('data',36);data.writeUInt32LE(N*4,40);
for(let i=0;i<N;i++){for(const [c,ch] of [[0,mixL],[1,mixR]]){const v=Math.max(-1,Math.min(1,ch[i]*norm+(rnd()-rnd())/65536));data.writeInt16LE(Math.round(v*32767),44+i*4+c*2);}}
await fs.writeFile(out,data);
console.log(`music: ${DURATION}s, ${BARS} bars, peak normalised from ${peak.toFixed(3)} → ${out}`);
