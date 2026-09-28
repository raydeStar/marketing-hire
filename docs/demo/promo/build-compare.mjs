// Writes artifacts/promo-20260927/deliverables/compare.html: the rendered picture with switchable, already-aligned music takes.
//   node docs/demo/promo/build-compare.mjs "Synth|synth.wav|note" "Take B|take-b-aligned.wav|note" ...
import fs from 'node:fs/promises';
import path from 'node:path';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';

const here=path.dirname(fileURLToPath(import.meta.url)),root=path.resolve(here,'../../../artifacts/promo-20260927');
const out=path.join(root,'deliverables'),music=path.join(out,'music'),ffmpeg=process.env.HIREZERO_DEMO_FFMPEG||'ffmpeg';
await fs.mkdir(music,{recursive:true});
const takes=[];
for(const spec of process.argv.slice(2)){
  const [name,file,note]=spec.split('|'),src=path.isAbsolute(file)?file:path.join(root,file),slug=name.toLowerCase().replace(/[^a-z0-9]+/g,'-');
  // Same loudness target as the final mux, so no take wins just by being louder.
  execFileSync(ffmpeg,['-v','error','-y','-i',src,'-af','loudnorm=I=-14:TP=-1.5:LRA=11','-ar','48000','-c:a','aac','-b:a','256k',path.join(music,slug+'.m4a')]);
  takes.push({name,note,src:'music/'+slug+'.m4a'});
}
const html=(await fs.readFile(path.join(here,'compare.html'),'utf8')).replace('/*TAKES*/[]',JSON.stringify(takes));
await fs.writeFile(path.join(out,'compare.html'),html);
console.log(path.join(out,'compare.html'),takes.map(t=>t.name).join(', '));
