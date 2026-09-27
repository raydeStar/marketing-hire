import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {spawn,execFileSync} from 'node:child_process';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
import {chromium} from '../../web/node_modules/playwright/index.mjs';
import {cleanArtifactPaths,requireArtifactSpace} from '../../scripts/artifact-storage.mjs';

const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const evidence=path.join(repo,'artifacts/marketing-demo-20260927');
const raw=path.join(evidence,'raw-video'),delivery=path.join(evidence,'deliverables');
await requireArtifactSpace(repo,512*1024**2,'Silent marketing video');
await fs.mkdir(raw,{recursive:true});await fs.mkdir(delivery,{recursive:true});
const board=JSON.parse(await fs.readFile(path.join(repo,'docs/demo/storyboard.json'),'utf8'));
const sources={};for(const scene of board.scenes){const name=scene.asset;if(!sources[name])sources[name]='data:image/png;base64,'+(await fs.readFile(path.join(evidence,'captures',name))).toString('base64');}
sources['campaign-pieces-phone.png']='data:image/png;base64,'+(await fs.readFile(path.join(evidence,'captures/campaign-pieces-phone.png'))).toString('base64');
const logo='data:image/svg+xml;base64,'+(await fs.readFile(path.join(repo,'web/public/icon.svg'))).toString('base64');
const html=String.raw`<!doctype html><html lang="en"><meta charset="utf-8"><title>HireZero marketing preview</title><style>
*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#f4f3ef;font-family:"Segoe UI",Arial,sans-serif}body{color:#1b1d23}#stage{position:relative;width:1920px;height:1080px;transform-origin:top left;overflow:hidden;background:#f4f3ef}.header{position:absolute;left:88px;right:88px;top:48px;display:flex;align-items:center;justify-content:space-between;z-index:9}.brand{display:flex;align-items:center;gap:17px;font-size:32px;font-weight:650;letter-spacing:-1px}.brand img{width:60px;height:60px;border-radius:14px}.tag{font-size:18px;letter-spacing:2px;font-weight:600;color:#637085}.footer{position:absolute;bottom:34px;left:88px;right:88px;display:flex;justify-content:space-between;align-items:center;font-size:17px;color:#647080;z-index:9}.footer .preview{padding:9px 15px;background:#fff;border:1px solid #dcdfe5;border-radius:30px}.footer .step{letter-spacing:.4px}.scene{position:absolute;inset:0}.kicker{color:#275dda;font-size:19px;font-weight:700;letter-spacing:2.3px;margin-bottom:23px}h1{font-size:78px;line-height:1.18;letter-spacing:-3.8px;margin:0;padding-bottom:16px;font-weight:650;white-space:pre-line}p{font-size:27px;line-height:1.5;color:#646975;margin:27px 0 0}.copy{position:absolute;left:100px;top:181px}.shot{position:absolute;overflow:hidden;border:1px solid #dce0e6;border-radius:15px;background:white;box-shadow:0 20px 80px #16284915}.shot img{display:block;width:100%;height:auto}.browser{height:37px;background:#f9fafc;border-bottom:1px solid #e3e6ed;display:flex;align-items:center;padding:0 16px;gap:7px}.browser i{width:7px;height:7px;border-radius:10px;background:#cbd1dc}.browser span{font-size:11px;margin-left:12px;color:#8892a1;letter-spacing:.4px}.wide .copy{top:156px}.wide h1{font-size:61px}.wide p{font-size:25px;margin-top:14px}.wide .shot{left:217px;top:365px;width:1486px;height:608px}.wide .shot img{width:1486px}.brief .copy{top:156px}.brief .shot{left:400px;top:389px;width:1120px;height:auto}.brief h1{font-size:72px}.brief p{margin-top:20px}.hero{background:#111a2a;color:#f5f6f8}.hero .copy{left:100px;top:309px;width:610px}.hero .kicker{color:#a9c2ff}.hero h1{font-size:103px;line-height:1.18;letter-spacing:-5px;max-width:580px}.hero p{font-size:31px;color:#bec9db;max-width:510px;margin-top:32px}.hero .shot{left:790px;top:197px;width:1046px;height:auto;box-shadow:0 25px 100px #0005;transform:rotate(-1deg)}.hero .shot img{width:1044px}.hero .browser{background:#f0f2f7}.hero::after{content:"";position:absolute;left:100px;top:757px;width:84px;height:4px;background:#719dfc}.opportunity .copy{top:340px;width:770px}.opportunity h1{font-size:76px}.opportunity p{width:630px}.opportunity .shot{left:1127px;top:181px;width:567px;height:auto;padding:0;box-shadow:0 25px 90px #172d6420}.opportunity .shot img{width:565px}.opportunity .decision{position:absolute;left:101px;top:743px;display:flex;gap:16px;font-size:22px}.decision span{padding:17px 22px;border:1px solid #d7dce6;border-radius:9px;background:white}.decision .primary{background:#245ddb;color:white;border-color:#245ddb}.revision .copy{top:199px}.revision .shot{left:134px;top:486px;width:1652px;height:auto;padding:22px;border-radius:17px}.revision .shot img{width:1606px}.revision .note{position:absolute;left:136px;top:436px;color:#505d73;font-size:23px}.revision .checklist{position:absolute;left:138px;top:889px;display:flex;gap:70px;font-size:24px;font-weight:600;color:#47546a}.phone .copy{top:338px;width:800px}.phone h1{font-size:82px}.phone p{width:630px}.phone .shot{left:1020px;top:168px;width:358px;height:778px;border:9px solid #202532;border-radius:32px;box-shadow:0 25px 80px #182c4020}.phone .shot img{width:340px}.phone .second{left:1432px;top:239px;width:307px;height:665px;border-radius:29px}.phone .second img{width:289px}.phone .small{position:absolute;left:1020px;top:969px;font-size:16px;color:#637085}.outro{background:#111a2a;color:#f5f6f8}.outro .copy{left:100px;top:289px;width:1100px}.outro .kicker{color:#9ebafb}.outro h1{font-size:105px;line-height:1.18;letter-spacing:-4px}.outro p{color:#bdc9dc;font-size:33px}.outro .end-mark{position:absolute;right:136px;top:300px;width:350px;height:350px;border:1px solid #35425b;border-radius:72px}.outro .url{position:absolute;left:101px;top:817px;font-size:33px;font-weight:600;color:#a9c2ff}.dark .header,.dark .footer{color:#e5ecf7}.dark .tag{color:#adbbcf}.dark .footer .preview{background:#1c293f;border-color:#36455e;color:#c9d4e5}.dark .footer .step{color:#bac7db}.progress{position:absolute;bottom:0;left:0;height:5px;background:#3475ed;z-index:10;transition:width .2s linear}.enter .copy{animation:arrive .55s ease-out both}.enter .shot{animation:arrive .68s ease-out both}.enter .decision{animation:arrive .8s ease-out both}@keyframes arrive{from{opacity:0;translate:0 15px}to{opacity:1;translate:0 0}}.poster .footer .step{display:none}
</style><div id="stage"><div id="scene"></div><header class="header"><div class="brand"><img id="logo" alt="HireZero mark"><span>HireZero</span></div><div class="tag">MARKETING LEAD</div></header><footer class="footer"><span class="preview">Product preview · illustrative workspace</span><span class="step" id="step"></span></footer><div class="progress" id="progress"></div></div><script>
const board=BOARD,sources=SOURCES,logo=LOGO;document.getElementById('logo').src=logo;
const esc=s=>s.replace(/[&<>\"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
function fit(){document.getElementById('stage').style.transform='scale('+Math.min(innerWidth/1920,innerHeight/1080)+')'}fit();addEventListener('resize',fit);
window.showScene=(id,animated=false)=>{const s=board.scenes.find(x=>x.id===id);document.getElementById('stage').className=(['hero','outro'].includes(s.layout)?'dark ':'')+(animated?'enter':'');
 const chrome=['hero','wide'].includes(s.layout)?'<div class="browser"><i></i><i></i><i></i><span>HireZero workspace</span></div>':'';
 let special=s.layout==='opportunity'?'<div class="decision"><span class="primary">Review</span><span>Change direction</span><span>Park</span></div>':s.layout==='revision'?'<div class="note">A real comparison view. Fictional draft content shown.</div><div class="checklist"><span>Compare versions</span><span>Inspect claims</span><span>Give precise feedback</span></div>':s.layout==='phone'?'<div class="shot second"><img src="'+sources['campaign-pieces-phone.png']+'"></div><span class="small">Responsive browser views</span>':'';
 const image=s.layout==='outro'?'<img class="end-mark" src="'+logo+'"><div class="url">Explore HireZero → hirezero.app</div>':'<div class="shot">'+chrome+'<img src="'+sources[s.asset]+'"></div>';
 document.getElementById('scene').innerHTML='<section class="scene '+s.layout+'"><div class="copy"><div class="kicker">'+esc(s.kicker)+'</div><h1>'+esc(s.title)+'</h1><p>'+esc(s.subtitle)+'</p></div>'+image+special+'</section>';
 document.getElementById('step').textContent=s.step;
};
window.play=plan=>{const total=plan.reduce((n,s)=>n+s.seconds,0);let begin=performance.now(),last='';window.done=false;
 function tick(){const t=(performance.now()-begin)/1000;let cursor=0,s=plan.at(-1);for(const item of plan){cursor+=item.seconds;if(t<cursor){s=item;break}}
 if(s.id!==last){window.showScene(s.id,true);last=s.id}document.getElementById('progress').style.width=Math.min(100,t/total*100)+'%';
 if(t<total)requestAnimationFrame(tick);else window.done=true;}tick();};
window.showScene('hello');
</script></html>`.replace('BOARD',JSON.stringify(board)).replace('SOURCES',JSON.stringify(sources)).replace('LOGO',JSON.stringify(logo));
const htmlPath=path.join(evidence,'preview.html');await fs.writeFile(htmlPath,html);
const ffmpeg=process.env.HIREZERO_DEMO_FFMPEG||'ffmpeg',ffprobe=process.env.HIREZERO_DEMO_FFPROBE||'ffprobe';
const receipt={createdAt:new Date().toISOString(),sourceCapture:'receipt.json',audioGenerated:false,fictionalData:true,composition:"UI captures with 0.4-second dissolves on an exact timeline",outputs:[],visualChecks:[]};
const browser=await chromium.launch();
try{
  const still=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1});await still.goto(pathToFileURL(htmlPath).href);await still.waitForFunction(()=>typeof window.showScene==='function');
  async function png(name,id,width=1920,height=1080){
    await still.setViewportSize({width,height});await still.evaluate(id=>window.showScene(id),id);await still.waitForTimeout(500);
    const errors=await still.evaluate(()=>[...document.querySelectorAll('.copy,.copy h1,.copy p,.kicker,.footer,.header')].filter(el=>el.getBoundingClientRect().right>innerWidth+1||el.scrollWidth>el.clientWidth+2||el.scrollHeight>el.clientHeight+2).map(el=>({element:el.tagName,class:el.className,text:el.textContent,scroll:[el.scrollWidth,el.scrollHeight],client:[el.clientWidth,el.clientHeight]})));
    const layout=await still.evaluate(()=>{
      const boxes=[...document.querySelectorAll('.copy,.shot,.note,.checklist,.decision,.url,.end-mark,.header,.footer')].map(el=>({name:el.className,rect:el.getBoundingClientRect()}));
      const faults=[];
      for(let i=0;i<boxes.length;i++){
        const a=boxes[i];
        if(a.rect.left<0||a.rect.top<0||a.rect.right>innerWidth+1||a.rect.bottom>innerHeight+1)faults.push(a.name+' outside frame');
        for(const b of boxes.slice(i+1)){
          if(Math.min(a.rect.right,b.rect.right)-Math.max(a.rect.left,b.rect.left)>2&&Math.min(a.rect.bottom,b.rect.bottom)-Math.max(a.rect.top,b.rect.top)>2)faults.push(a.name+' overlaps '+b.name);
        }
      }
      return faults;
    });
    const file=path.join(delivery,name);await still.screenshot({path:file});assert.deepEqual(errors,[],name+' text overflow');assert.deepEqual(layout,[],name+' composition faults');receipt.visualChecks.push({file:name,textOverflow:errors,layout});
    receipt.outputs.push({file:name,bytes:(await fs.stat(file)).size,sha256:createHash('sha256').update(await fs.readFile(file)).digest('hex')});
  }
  for(const s of board.scenes)await png('scene-'+s.id+'.png',s.id);
  await png('hirezero-cover-1920x1080.png','hello');await png('hirezero-campaign-1920x1080.png','campaign');await png('hirezero-social-1200x675.png','hello',1200,675);
  await still.setViewportSize({width:512,height:512});await still.setContent('<html><style>body{margin:0}img{display:block;width:512px;height:512px}</style><img src="'+logo+'" alt="HireZero"></html>');
  await still.screenshot({path:path.join(delivery,'hirezero-logo-512.png')});await still.close();
  async function film(name,plan){
    const seconds=plan.reduce((n,s)=>n+s.seconds,0);
    console.log('Composing '+name+' ('+seconds+' seconds), silently.');
    const output=path.join(delivery,name),blend=0.4;
    const inputs=[];
    for(let i=0;i<plan.length;i++)inputs.push('-loop','1','-framerate','30','-t',String(plan[i].seconds+(i<plan.length-1?blend:0)),'-i',path.join(delivery,'scene-'+plan[i].id+'.png'));
    const filters=plan.map((_,i)=>'['+i+':v]format=yuv420p,settb=AVTB,setpts=PTS-STARTPTS[v'+i+']');
    let offset=0,last='v0';
    for(let i=1;i<plan.length;i++){
      offset+=plan[i-1].seconds;
      const next='blend'+i;
      filters.push('['+last+'][v'+i+']xfade=transition=fade:duration='+blend+':offset='+offset+'['+next+']');
      last=next;
    }
    const args=['-y','-hide_banner','-loglevel','warning',...inputs,'-filter_complex_threads','1','-filter_complex',filters.join(';'),'-map','['+last+']','-t',String(seconds),'-an','-c:v','libx264','-preset','fast','-crf','20','-pix_fmt','yuv420p','-r','30','-threads','2','-movflags','+faststart',output];
    const child=spawn(ffmpeg,args,{windowsHide:true,stdio:['ignore','ignore','pipe']});let log='';child.stderr.on('data',x=>log+=x);
    const code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',resolve);});await fs.writeFile(path.join(evidence,name+'.ffmpeg.log'),log);assert.equal(code,0,log);
    const probe=JSON.parse(execFileSync(ffprobe,['-v','error','-show_streams','-show_format','-of','json',output],{encoding:'utf8'}));
    assert.equal(probe.streams.filter(s=>s.codec_type==='audio').length,0,'The owner asked for no generated audio.');assert.equal(probe.streams[0].codec_name,'h264');assert.ok(Math.abs(Number(probe.format.duration)-seconds)<1);
    receipt.outputs.push({file:name,seconds:Number(probe.format.duration),width:probe.streams[0].width,height:probe.streams[0].height,audioStreams:0,bytes:(await fs.stat(output)).size,sha256:createHash('sha256').update(await fs.readFile(output)).digest('hex')});
    console.log(name+' encoded; the orchestra remains off duty.');
  }
  if(!process.argv.includes('--stills-only')) {
    await film('hirezero-marketing-preview-silent.mp4',board.scenes);
    await film('hirezero-teaser-silent.mp4',board.teaser);
  }
  receipt.stillsOnly=process.argv.includes('--stills-only');
  receipt.passed=true;
}catch(error){receipt.error=error.stack;throw error;}
finally{await browser.close();receipt.removed=await cleanArtifactPaths(evidence,['raw-video']);await fs.writeFile(path.join(evidence,'render-receipt.json'),JSON.stringify(receipt,null,2)+'\n');}
console.log('Silent marketing assets are ready. The owner gets the microphone.');
