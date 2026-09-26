// Demo recorder: turns the employee's storyboard into a narrated screen recording of the real workspace.
// The employee writes the storyboard (scenes from the menu below, captions, narration, seconds) as a Library document;
// the owner records the narration in their own voice from that document (Record narration), which stores one clip per scene.
// This tool records the app scene by scene with a caption bar, lays each scene's clip under it, muxes with ffmpeg, and can
// upload the MP4 to the workspace Library (Media). Scenes without a clip are captions only; --voice uses the system speech
// engine for them instead (Windows System.Speech), which sounds robotic and is off unless asked for.
//
//   node tools/record-demo.mjs --data <host data folder> --storyboard <wiki id | file.json> --out <folder>
//        [--origin http://localhost:5190] [--voice "Microsoft Zira Desktop"] [--ffmpeg path] [--ffprobe path] [--upload]
//
// Only for workspaces you own: it signs in with the workspace's own host key. The recording browser changes nothing
// in the workspace; the only write is the optional upload of the finished video.
import {chromium} from '@playwright/test';
import {execFileSync} from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const args=Object.fromEntries(process.argv.slice(2).reduce((pairs,value,index,all)=>value.startsWith('--')?[...pairs,[value.slice(2),all[index+1]&&!all[index+1].startsWith('--')?all[index+1]:'true']]:pairs,[]));
const origin=args.origin||'http://localhost:5190';
const out=path.resolve(args.out||'demo-output');
fs.mkdirSync(out,{recursive:true});
const key=fs.readFileSync(path.join(args.data,'host-key.txt'),'utf8').trim();
const ffmpeg=args.ffmpeg||'ffmpeg',ffprobe=args.ffprobe||'ffprobe';

// Scenes the recorder knows how to show. The storyboard may only use these.
export const SCENES={
  intro:'Title card', chat:'Chat', cockpit:'The Work view', shifts:'The most productive recent shift report',
  research:'The competitive research document', wedge:'The wedge proposal', blog:'The blog post', calendar:'Content calendar',
  listening:'Listening', scorecard:'Scorecard', weekly:'This week', golive:'Go-live checklist', library:'The Library', outro:'Closing card'
};

const json={Origin:origin,'Content-Type':'application/json'};
async function session(){
  const login=await fetch(origin+'/api/auth/login',{method:'POST',headers:json,body:JSON.stringify({key})});
  if(!login.ok)throw new Error('Sign-in failed: '+login.status);
  return {cookie:login.headers.get('set-cookie')?.split(';')[0],csrf:(await login.json()).csrf};
}
async function get(signedIn,pathname){
  const response=await fetch(origin+'/api'+pathname,{headers:{Origin:origin,Cookie:signedIn.cookie}});
  if(!response.ok)throw new Error(`${pathname} → ${response.status}`);
  return response.json();
}

// 1. The storyboard: a Library document with a ```json block, or a file.
function parse(text){
  const block=text.match(/```(?:json)?\s*\n?([\s\S]*?)```/);
  const board=JSON.parse(block?block[1]:text);
  const scenes=(board.scenes||[]).filter(item=>SCENES[item.scene]).map(item=>({scene:item.scene,caption:String(item.caption||'').slice(0,110),narration:String(item.narration||'').slice(0,400),seconds:Math.min(15,Math.max(2,Number(item.seconds)||5)),
    clip:/^[0-9a-f]{32}$/.test(String(item.audio||''))?String(item.audio):null}));
  if(scenes.length<3)throw new Error('The storyboard needs at least three known scenes.');
  return {title:String(board.title||'First Employee').slice(0,80),subtitle:String(board.subtitle||'').slice(0,120),scenes};
}

// 2. Narration: one WAV per scene from the system voice (Windows System.Speech); its length sets the scene's length.
function speak(text,file){
  const script=`Add-Type -AssemblyName System.Speech; $s=New-Object System.Speech.Synthesis.SpeechSynthesizer; ${args.voice&&args.voice!=='true'?`$s.SelectVoice('${args.voice.replace(/'/g,"''")}');`:''} $s.Rate=0; $s.SetOutputToWaveFile('${file.replace(/'/g,"''")}'); $s.Speak([Console]::In.ReadToEnd()); $s.Dispose()`;
  execFileSync('powershell',['-NoProfile','-Command',script],{input:text});
}
function duration(file){return Number(execFileSync(ffprobe,['-v','error','-show_entries','format=duration','-of','csv=p=0',file]).toString().trim())||0;}

async function main(){
  const signedIn=await session();
  const wiki=await get(signedIn,'/company-wiki');
  let board;
  if(fs.existsSync(args.storyboard||''))board=parse(fs.readFileSync(args.storyboard,'utf8'));
  else{
    const page=args.storyboard?wiki.find(item=>item.id===args.storyboard):wiki.filter(item=>/storyboard/i.test(item.title)&&item.status!=='archived').sort((a,b)=>b.updatedAt.localeCompare(a.updatedAt))[0];
    if(!page)throw new Error('No storyboard document found.');
    console.log('Storyboard:',page.title);board=parse(page.body);
  }
  const latest=pattern=>wiki.filter(item=>pattern.test(item.title)).sort((a,b)=>b.updatedAt.localeCompare(a.updatedAt))[0]?.id;
  // The shift scene shows the recent shift that produced the most, not whichever ran last.
  const shifts=await get(signedIn,'/shifts');
  const produced=shift=>(shift.cycles||[]).flatMap(cycle=>cycle.stages||[]).filter(stage=>stage.stage==='create').reduce((sum,stage)=>sum+(stage.outputs||[]).length,0);
  const best=[shifts.current,...(shifts.recent||[])].filter(shift=>shift?.reportWikiId).sort((a,b)=>produced(b)-produced(a))[0];
  const docs={research:latest(/compet/i),wedge:latest(/wedge/i),blog:latest(/^why we|blog/i),shifts:best?.reportWikiId};

  // Narration: the owner's recorded clip for each scene; the system voice only when asked for; otherwise captions alone.
  for(const [index,scene] of board.scenes.entries()){
    const file=path.join(out,`scene-${index}.wav`);
    if(scene.clip){
      const response=await fetch(`${origin}/api/uploads/${scene.clip}/content`,{headers:{Origin:origin,Cookie:signedIn.cookie}});
      if(!response.ok)throw new Error(`The clip for scene ${index+1} could not be read (${response.status}).`);
      fs.writeFileSync(file,Buffer.from(await response.arrayBuffer()));scene.audio=file;
    }else if(scene.narration&&args.voice){speak(scene.narration,file);scene.audio=file;}
    if(scene.audio)scene.seconds=Math.max(scene.seconds,duration(file)+0.6);
  }
  const voiced=board.scenes.filter(scene=>scene.clip).length;
  console.log(`Narration: ${voiced} recorded clip${voiced===1?'':'s'}${args.voice?', the rest from the system voice':', the rest captions only'}.`);
  const total=board.scenes.reduce((sum,scene)=>sum+scene.seconds,0);
  console.log(`${board.scenes.length} scenes, ${total.toFixed(1)} s`);

  // 3. Record the real app, scene by scene, without reloading between scenes.
  const issued=await (await fetch(origin+'/api/auth/launch',{method:'POST',headers:json,body:JSON.stringify({key})})).json();
  const browser=await chromium.launch();
  const context=await browser.newContext({viewport:{width:1440,height:810},deviceScaleFactor:1,recordVideo:{dir:out,size:{width:1280,height:720}}});
  const born=Date.now();
  await context.addInitScript(()=>{try{localStorage.setItem('fe-onboarding-dismissed','yes');localStorage.setItem('fe-getting-started-dismissed','yes');}catch{}});
  const page=await context.newPage();
  await page.goto(`${origin}/?pane=chat#launch=${issued.ticket}`);
  await page.waitForSelector('.fe-app',{timeout:30000});await page.waitForTimeout(2500);
  const go=async url=>{await page.evaluate(next=>{history.pushState(history.state,'',next);dispatchEvent(new PopStateEvent('popstate'));},url);await page.waitForTimeout(700);};
  const section=async label=>{await go('/?pane=work');await page.evaluate(name=>document.querySelector(`section[aria-label="${name}"]`)?.scrollIntoView({block:'start'}),label);};
  // Overlays are styled through the CSSOM: the app's content security policy (rightly) ignores inline style attributes.
  const card=async(title,subtitle)=>page.evaluate(([t,s])=>{
    document.getElementById('demo-card')?.remove();
    const el=document.createElement('div');el.id='demo-card';
    Object.assign(el.style,{position:'fixed',inset:'0',zIndex:'99999',display:'grid',placeItems:'center',background:'#0f1115',color:'#fff',textAlign:'center',fontFamily:'system-ui, sans-serif'});
    const inner=document.createElement('div');const head=document.createElement('div');const sub=document.createElement('div');
    head.textContent=t;Object.assign(head.style,{fontSize:'56px',fontWeight:'650',letterSpacing:'-0.01em',maxWidth:'1100px',lineHeight:'1.15'});
    sub.textContent=s;Object.assign(sub.style,{fontSize:'26px',fontWeight:'400',opacity:'.72',marginTop:'18px'});
    inner.append(head,sub);el.append(inner);document.body.append(el);},[title,subtitle]);
  const clearCard=async()=>page.evaluate(()=>document.getElementById('demo-card')?.remove());
  const caption=async text=>page.evaluate(value=>{
    let el=document.getElementById('demo-caption');
    if(!el){el=document.createElement('div');el.id='demo-caption';document.body.append(el);
      Object.assign(el.style,{position:'fixed',left:'50%',bottom:'34px',transform:'translateX(-50%)',zIndex:'99998',maxWidth:'82%',padding:'12px 24px',borderRadius:'10px',
        background:'rgba(15,17,21,.88)',color:'#fff',fontFamily:'system-ui, sans-serif',fontSize:'26px',fontWeight:'500',textAlign:'center',boxShadow:'0 6px 24px rgba(0,0,0,.25)'});}
    el.textContent=value;el.style.display=value?'block':'none';},text);
  // In chat the caption sits above the composer, so the app's own line under it (nothing goes out without approval) stays readable.
  const lift=async up=>page.evaluate(high=>{const el=document.getElementById('demo-caption');if(el)el.style.bottom=high?'150px':'34px';},up);
  // Stale update cards are hidden in the recording browser only; nothing is dismissed in the workspace.
  const quietChat=async()=>page.evaluate(()=>document.querySelectorAll('.fe-update').forEach(node=>{node.style.display='none';}));
  const start=(Date.now()-born)/1000;
  for(const scene of board.scenes){
    const began=Date.now();
    switch(scene.scene){
      case 'intro':await card(board.title,board.subtitle);break;
      case 'outro':await card(scene.caption||board.title,board.subtitle);break;
      case 'chat':await go('/?pane=chat');await quietChat();await clearCard();break;
      case 'cockpit':await go('/?pane=work');await clearCard();break;
      case 'calendar':await section('Content calendar');await clearCard();break;
      case 'listening':await section('Listening');await clearCard();break;
      case 'scorecard':await section('Scorecard');await clearCard();break;
      case 'weekly':await section('This week');await clearCard();break;
      case 'golive':await go('/?view=settings');await page.evaluate(()=>document.querySelector('section[aria-label="Go-live checklist"]')?.scrollIntoView({block:'start'}));await clearCard();break;
      case 'library':await go('/?view=library');await clearCard();break;
      default:{const id=docs[scene.scene];if(id)await go(`/?view=library&open=wiki:${id}`);else if(scene.scene==='shifts')await section('Shift log');else await go('/?view=library');await clearCard();}
    }
    await caption(scene.scene==='intro'||scene.scene==='outro'?'':scene.caption);await lift(scene.scene==='chat');
    // Documents scroll gently while they're on screen.
    const left=scene.seconds*1000-(Date.now()-began);
    if(['research','wedge','blog','shifts'].includes(scene.scene))await page.evaluate(ms=>{
      const el=[...document.querySelectorAll('.fe-window, .fe-window *')].filter(node=>/(auto|scroll)/.test(getComputedStyle(node).overflowY)&&node.scrollHeight>node.clientHeight+40)
        .sort((x,y)=>(y.scrollHeight-y.clientHeight)-(x.scrollHeight-x.clientHeight))[0];
      if(!el)return;const travel=Math.min(el.scrollHeight-el.clientHeight,ms*0.12);const step=travel/((ms-1200)/50);
      setTimeout(()=>{const timer=setInterval(()=>{el.scrollTop+=step;},50);setTimeout(()=>clearInterval(timer),ms-1200);},600);},left);
    if(left>0)await page.waitForTimeout(left);
  }
  const video=page.video();await context.close();await browser.close();
  const raw=await video.path();

  // 4. Narration track: each scene's speech padded to the scene's length, then muxed with the recording.
  const parts=[];let filters='';
  // Input n is the scene's clip or, for a scene without one, silence of the scene's length.
  board.scenes.forEach((scene,index)=>{
    if(scene.audio){parts.push('-i',scene.audio);filters+=`[${index}:a]aresample=44100,aformat=channel_layouts=mono,apad,atrim=0:${scene.seconds.toFixed(3)}[a${index}];`;}
    else{parts.push('-f','lavfi','-t',scene.seconds.toFixed(3),'-i','anullsrc=r=44100:cl=mono');filters+=`[${index}:a]anull[a${index}];`;}
  });
  const narration=path.join(out,'narration.wav');
  execFileSync(ffmpeg,['-y',...parts,'-filter_complex',filters+board.scenes.map((_,index)=>`[a${index}]`).join('')+`concat=n=${board.scenes.length}:v=0:a=1[out]`,'-map','[out]','-ar','44100',narration],{stdio:'ignore'});
  const mp4=path.join(out,'hirezero-demo.mp4');
  execFileSync(ffmpeg,['-y','-ss',start.toFixed(2),'-i',raw,'-i',narration,'-map','0:v','-map','1:a','-t',total.toFixed(2),'-c:v','libx264','-preset','veryfast','-crf','23','-pix_fmt','yuv420p','-c:a','aac','-b:a','128k','-movflags','+faststart',mp4],{stdio:'ignore'});
  fs.rmSync(raw,{force:true});
  console.log('Video:',mp4,`${(fs.statSync(mp4).size/1048576).toFixed(1)} MiB, ${duration(mp4).toFixed(1)} s`);

  // 5. Into the Library, where the Media viewer plays it.
  if(args.upload==='true'){
    const form=new FormData();form.append('file',new Blob([fs.readFileSync(mp4)],{type:'video/mp4'}),'hirezero-demo.mp4');
    const uploaded=await fetch(origin+'/api/uploads',{method:'POST',headers:{Origin:origin,Cookie:signedIn.cookie,'X-CSRF':signedIn.csrf},body:form});
    if(!uploaded.ok)throw new Error('Upload failed: '+uploaded.status+' '+await uploaded.text());
    console.log('Uploaded to the Library:',(await uploaded.json()).id);
  }
}
main().catch(error=>{console.error(error.message||error);process.exit(1);});
