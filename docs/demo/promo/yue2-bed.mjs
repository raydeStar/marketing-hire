// Renders the promo's music bed with the local YuE2 worker (Framewright's tools/yue2 service on 127.0.0.1:5182) from a
// hand-written ABC score on the promo's 120 BPM, 34-bar grid: drops at bar 5 (0:08) and bar 23 (0:44), outro at bar 31.
// The Vocal voice is all rests so the bed stays instrumental under narration.
//   node docs/demo/promo/yue2-bed.mjs 424242 777001 90210     one render per seed → artifacts/promo-20260927/yue2/
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..');
const outDir=path.join(repo,'artifacts/promo-20260927/yue2'),worker='http://127.0.0.1:5182';
await fs.mkdir(outDir,{recursive:true});

const chords4=['Am','F','C','G'];
// [section, chords per bar, instrument bars] — each bar is 32 units of L:1/32.
const sections=[
  ['intro',chords4,['A4c4e4a4z16','A4c4f4a4z16','G4c4e4g4z16','G4B4d4g4d4g4b4d\'4']],
  ['chorus',[...chords4,...chords4],['e8z4d4c8A8','A8z4c4d8c8','e8z4g4e8d8','d8z4B4G16','e8z4d4c8A8','A8z4c4d8f8','e8g8a8g8','g8e8d8B8']],
  ['verse',['Am','F','C','G','Am','F','G'],['a4e4c4e4a4e4c4e4','a4f4c4f4a4f4c4f4','g4e4c4e4g4e4c4e4','g4d4B4d4g4d4B4d4','a4e4c4e4a4e4c4e4','a4f4c4f4a4f4c4f4','g4d4B4d4b8d\'8']],
  ['bridge',['F','G','E'],['c16A16','d16B16','^G8B8e8^g8']],
  ['chorus',[...chords4,...chords4],['e8z4d4c8A8','A8z4c4d8c8','e8z4g4e8d8','d8z4B4G16','e8z4d4c8A8','A8z4c4d8f8','e8g8a8g8','g8e8d8B8']],
  ['outro',['Am','F','C','Am'],['e8z4d4c8A8','A32','G32','A32']],
];
// v2 writes the energy arc into the notes: sparse intro, a rising 16th build, a driving 8th-note hook at both drops (the second
// an octave up), 16th arpeggios in the groove, held notes only in the breakdown.
const arp=(a,b,c,d)=>Array(4).fill(`${a}2${b}2${c}2${d}2`).join('');
const hook=["e4e4z2e2d4c4c4A8","A4A4z2A2c4d4d4c8","e4e4z2e2g4e4e4d8","d4d4z2d2B4G4G4B8","e4e4z2e2d4c4c4A8","A4A4z2A2c4d4d4f8","e4g4a4g4e4g4a4b4","g4e4d4B4d8B8"];
const up=bar=>bar.replace(/([A-Ga-g])('?)/g,(m,note,tick)=>note===note.toUpperCase()?note.toLowerCase()+tick:note+"'"+tick);
const v2=[
  ['intro',chords4,['A8z24','A8z24','G8z24','G2B2d2g2G2B2d2g2B2d2g2b2d2g2b2d\'2']],
  ['chorus',[...chords4,...chords4],hook],
  ['verse',['Am','F','C','G','Am','F','G'],[arp('A','c','e','a'),arp('A','c','f','a'),arp('G','c','e','g'),arp('G','B','d','g'),arp('A','c','e','a'),arp('A','c','f','a'),'G2B2d2g2G2B2d2g2B2d2g2b2d2g2b2d\'2']],
  ['bridge',['F','G','E'],['c32','d32','^G16B16']],
  ['chorus',[...chords4,...chords4],hook.map(up)],
  ['outro',['Am','F','C','Am'],['a8z24','A32','G32','A32']],
];
if(process.env.SCORE==='v2'){sections.length=0;sections.push(...v2);}
const bars=sections.reduce((n,[, c])=>n+c.length,0);
if(bars!==34)throw new Error('score must be 34 bars, got '+bars);
const rows=l=>{const out=[];for(let i=0;i<l.length;i+=4)out.push(l.slice(i,i+4).join('|')+'|');return out;};
let abc=`X:1\nT:HireZero\nM:4/4\nL:1/32\nQ:1/4=120\nV: Vocal clef=treble name="Vocal Melody" snm="Vocal"\nV: Ins clef=treble name="Ins Melody" snm="Inst."\nK:Am\n`;
for(const [name,chords,ins] of sections){
  abc+=`% ${name}\n`;
  const vRows=rows(chords.map(c=>`"${c}"z32`)),iRows=rows(ins);
  vRows.forEach((v,i)=>{abc+=`V: Vocal\n${v}\nV: Ins\n${iRows[i]}\n`;});
}
const style=['TITLE: HireZero.',
  'Instrumental only: no vocals, no singing, no choir, no spoken words.',
  'Modern cinematic electronic launch anthem for a premium tech product film; confident, uplifting, polished.',
  '120 BPM, 4/4, A minor. Progression Am - F - C - G.',
  'Punchy four-on-the-floor kick, tight claps on 2 and 4, crisp sixteenth hi-hats, deep side-chained driving bass, wide supersaw chords, bright plucked synth arpeggios, soaring synth lead carrying the hook.',
  'Bars 1-4: filtered and tense, sparse plucks, rising noise sweep and snare build into a huge drop exactly at bar 5.',
  'Bars 5-12: full drop with the lead hook. Bars 13-19: driving groove with arpeggios, slightly lighter.',
  'Bars 20-22: breakdown, drums out, pads and plucks, accelerating snare roll and riser into a massive second drop exactly at bar 23.',
  'Bars 23-30: biggest section, full lead hook, widest mix. Bars 31-34: one big impact, then a sustained A minor chord ringing out.',
  'Wide stereo, modern loud clean mix, festival energy with keynote polish.'].join(' ');
const lyrics='[Intro]\n\n[Chorus]\n\n[Verse]\n\n[Bridge]\n\n[Chorus]\n\n[Outro]\n';
const variant=process.env.SCORE==='v2'?'v2-':'';await fs.writeFile(path.join(outDir,variant+'score.abc'),abc);await fs.writeFile(path.join(outDir,'style.txt'),style+'\n');

const seeds=process.argv.slice(2).map(Number).filter(Boolean);if(!seeds.length)seeds.push(424242);
for(const seed of seeds){
  const id=`hirezero-promo-${variant}${seed}`;
  const res=await fetch(worker+'/render',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({id,style,lyrics,cot:'full',seed,abc})});
  const job=await res.json();if(!res.ok)throw new Error(JSON.stringify(job));
  const t0=Date.now();let state={state:"queued"},last='';
  while(!['completed','failed','cancelled'].includes(state.state)){
    await new Promise(r=>setTimeout(r,5000));
    state=await (await fetch(`${worker}/jobs/${job.jobId}`)).json();
    if(state.phase!==last){console.log(`${id}: ${state.phase} (${Math.round((Date.now()-t0)/1000)}s)`);last=state.phase;}
  }
  if(state.state!=='completed'){console.log(`${id}: ${state.state} ${state.error||''}`);continue;}
  const audio=Buffer.from(await (await fetch(`${worker}/jobs/${job.jobId}/audio`)).arrayBuffer());
  await fs.writeFile(path.join(outDir,`${id}.flac`),audio);
  console.log(`${id}: saved ${audio.length} bytes in ${Math.round((Date.now()-t0)/1000)}s`);
}
