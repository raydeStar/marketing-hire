import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdtemp,writeFile,readFile,readdir,rm,access} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {createLunaBridge} from './luna-bridge-server.mjs';
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function until(check){for(let i=0;i<200;i++){if(await check())return;await delay(10);}throw new Error('Fixture deadline exceeded.');}

test('three bounded CLI slots, client cancellation, usable failure receipts and scratch cleanup',async()=>{
  const root=await mkdtemp(path.join(os.tmpdir(),'thaddeus-bridge-test-')),worker=path.join(root,'fake.mjs'),children=[],scratch=[];
  await writeFile(worker,`import {writeFile,access} from 'node:fs/promises';
const [output,release]=process.argv.slice(2);let prompt='';for await(const c of process.stdin)prompt+=c;
if(prompt.includes('fixture-hold'))while(!await access(release).then(()=>true,()=>false))await new Promise(r=>setTimeout(r,10));
if(prompt.includes('fixture-bad')){process.stderr.write('FAKE_SECRET_SHOULD_NOT_LEAK');await writeFile(output,'invalid JSON');}else await writeFile(output,JSON.stringify({text:'Fixture answer',tool_calls:[]}));
process.stdout.write(JSON.stringify({type:'turn.completed',usage:{input_tokens:10,output_tokens:5}})+'\\n');`);
  const server=await createLunaBridge({executable:'unused-real-provider',artifactDir:path.join(root,'receipts'),spawnProvider:(_exe,args,options)=>{
    assert.equal(args[args.indexOf('--model')+1],'gpt-5.6-luna');assert.ok(args.includes('model_reasoning_effort="high"'));assert.equal(options.windowsHide,true);
    scratch.push(options.cwd);const release=path.join(root,'release-'+children.length);
    const child=spawn(process.execPath,[worker,args[args.indexOf('--output-last-message')+1],release],options);const closed=new Promise(resolve=>child.once('close',resolve));children.push({child,release,closed});return child;
  }});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const origin='http://127.0.0.1:'+server.address().port;
  const request=(content,signal)=>fetch(origin+'/v1/chat/completions',{method:'POST',body:JSON.stringify({model:'gpt-5.6-luna',reasoning_effort:'high',messages:[{role:'user',content}]}),signal});
  const active=async()=> (await (await fetch(origin+'/v1/models')).json()).bridge.activeRequests;
  try{
    const abort=new AbortController();const one=request('fixture-hold',abort.signal).catch(()=>null),two=request('fixture-hold'),three=request('fixture-hold');
    await until(async()=>children.length===3);assert.equal((await request('fourth')).status,429);assert.equal(children.length,3);
    abort.abort();await one;await until(async()=>await active()===2);
    const quick=await request('chat can proceed');assert.equal(quick.status,200);assert.equal((await quick.json()).choices[0].message.content,'Fixture answer');
    for(const item of children.slice(0,3))await writeFile(item.release,'done');assert.equal((await two).status,200);assert.equal((await three).status,200);await until(async()=>await active()===0);
    assert.equal((await request('fixture-bad')).status,502);await until(async()=>await active()===0);
    const receipts=await Promise.all((await readdir(path.join(root,'receipts'))).map(file=>readFile(path.join(root,'receipts',file),'utf8')));
    assert.ok(receipts.every(text=>!text.includes('FAKE_SECRET_SHOULD_NOT_LEAK')));
    assert.ok(receipts.map(JSON.parse).some(r=>r.phase==='parse-output'&&r.errorClass==='invalid-json'));
    assert.ok(receipts.map(JSON.parse).some(r=>r.aborted));
    for(const dir of scratch)assert.equal(await access(dir).then(()=>true,()=>false),false);
  }finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));for(const {child,closed} of children){if(child.exitCode===null&&child.signalCode===null)child.kill();await closed;}await rm(root,{recursive:true,force:true});}
});

test('bridge deadline is distinguished from provider rejection',async()=>{
  const root=await mkdtemp(path.join(os.tmpdir(),'thaddeus-bridge-deadline-'));
  const server=await createLunaBridge({executable:'unused',artifactDir:root,timeoutMs:100,spawnProvider:(_exe,_args,options)=>spawn(process.execPath,['-e','process.stdin.resume();setInterval(()=>{},1000)'],options)});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const origin='http://127.0.0.1:'+server.address().port;
  try{
    const response=await fetch(origin+'/v1/chat/completions',{method:'POST',body:JSON.stringify({model:'gpt-5.6-luna',reasoning_effort:'high',messages:[{role:'user',content:'fixture'}]})});
    assert.equal(response.status,504);await until(async()=> (await (await fetch(origin+'/v1/models')).json()).bridge.activeRequests===0);
    const receipt=JSON.parse(await readFile(path.join(root,(await readdir(root))[0]),'utf8'));assert.equal(receipt.timedOut,true);assert.equal(receipt.phase,'provider');
  }finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));await rm(root,{recursive:true,force:true});}
});


test('image files reach the CLI image option and are removed after the provider exits',async()=>{
 const root=await mkdtemp(path.join(os.tmpdir(),'thaddeus-bridge-image-'));let imageFile,owned;
 const bytes=Buffer.from([137,80,78,71,13,10,26,10]);
 const server=await createLunaBridge({executable:'unused',artifactDir:root,spawnProvider:(_exe,args,options)=>{
   imageFile=args[args.indexOf('--image')+1];assert.ok(imageFile.startsWith(options.cwd+path.sep));assert.equal(args.at(-1),'-');
   owned=spawn(process.execPath,['-e',`const fs=require('fs');if(fs.readFileSync(process.argv[1]).toString('hex')!=='89504e470d0a1a0a')process.exit(2);process.stdin.resume();process.stdin.on('end',()=>fs.writeFileSync(process.argv[2],JSON.stringify({text:'Fictional image read',tool_calls:[]})));`,imageFile,args[args.indexOf('--output-last-message')+1]],options);return owned;
 }});
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{const response=await fetch('http://127.0.0.1:'+server.address().port+'/v1/chat/completions',{method:'POST',body:JSON.stringify({model:'gpt-5.6-luna',reasoning_effort:'high',messages:[{role:'user',content:[{type:'text',text:'Fictional image'},{type:'image_url',image_url:{url:'data:image/png;base64,'+bytes.toString('base64')}}]}]})});assert.equal(response.status,200);await until(async()=>!await access(imageFile).then(()=>true,()=>false));}
 finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));if(owned?.exitCode===null)owned.kill();await rm(root,{recursive:true,force:true});}
});
