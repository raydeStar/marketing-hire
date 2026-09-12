// Development-only transport for the user's explicitly selected Luna High.
// No shell arguments come from model output. The raven may draft; it may not run commands.
import http from 'node:http';
import { spawn } from 'node:child_process';
import { mkdir, writeFile, readFile, mkdtemp, rm } from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import { proposalSchema, providerPrompt, completion, completionFrames } from './luna-protocol.mjs';
const executable=process.env.THADDEUS_CODEX_EXE;
if(!executable)throw new Error('Set THADDEUS_CODEX_EXE to your installed Codex executable. No automatic installation.');
const port=Number(process.env.THADDEUS_BRIDGE_PORT||5181);
const artifactDir=path.resolve('artifacts/luna');await mkdir(artifactDir,{recursive:true});
let active=false;
http.createServer(async(req,res)=>{
  if(req.headers.origin || !['127.0.0.1:'+port,'localhost:'+port].includes(req.headers.host)){res.writeHead(403);res.end();return;}
  if(req.url==='/v1/models'&&req.method==='GET'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({data:[{id:'gpt-5.6-luna'}]}));return;}
  if(req.url!=='/v1/chat/completions'||req.method!=='POST'){res.writeHead(404);res.end();return;}
  if(active){res.writeHead(429);res.end();return;}
  let raw='';for await(const chunk of req){raw+=chunk;if(raw.length>150000){res.writeHead(413);res.end();return;}}
  let body;try{body=JSON.parse(raw);}catch{res.writeHead(400);res.end();return;}
  let prompt;try{prompt=providerPrompt(body);}catch{res.writeHead(400);res.end('Invalid request for the fixed Luna High development bridge.');return;}
  active=true;const temp=await mkdtemp(path.join(os.tmpdir(),'thaddeus-luna-'));
  try{
    const schema=path.join(temp,'schema.json'),output=path.join(temp,'reply.json');
    await writeFile(schema,JSON.stringify(proposalSchema()));
    const args=['exec','--ignore-user-config','--ephemeral','--skip-git-repo-check','--sandbox','read-only','--model','gpt-5.6-luna','-c','model_reasoning_effort="high"','-c','features.shell_tool=false','-c','features.apply_patch_freeform=false','--output-schema',schema,'--output-last-message',output,'--json','-'];
    const child=spawn(executable,args,{cwd:temp,windowsHide:true,stdio:['pipe','pipe','pipe']});
    let stdout='',stderr='',overflow=false;
    const collect=(kind,c)=>{if(stdout.length+stderr.length+c.length>2000000){overflow=true;child.kill();return;}if(kind==='out')stdout+=c;else stderr+=c;};
    child.stdout.on('data',c=>collect('out',c));child.stderr.on('data',c=>collect('err',c));
    const stop=()=>{if(child.exitCode===null)child.kill();};res.on('close',stop);const timer=setTimeout(stop,180000);
    child.stdin.end(prompt);
    const code=await new Promise((resolve,reject)=>{child.on('exit',resolve);child.on('error',reject);});clearTimeout(timer);res.off('close',stop);
    if(code!==0||overflow)throw new Error('Codex provider exited before a usable reply.');
    const events=stdout.split('\n').filter(Boolean).map(s=>{try{return JSON.parse(s);}catch{return {};}});
    if(events.some(e=>/command_execution|mcp_tool_call|web_search/.test(e.item?.type||'')))throw new Error('Unexpected tool use: the development model sentinel failed.');
    const reply=JSON.parse(await readFile(output,'utf8'));const usage=events.findLast(e=>e.type==='turn.completed')?.usage;
    const result=completion(body,reply,usage);
    const receipt={schemaVersion:2,model:'gpt-5.6-luna',reasoning:'high',transport:'Codex CLI data-only development bridge; buffered JSON or SSE',toolExecutions:0,usage:usage??null,output:result,time:new Date().toISOString()};
    await writeFile(path.join(artifactDir,Date.now()+'.json'),JSON.stringify(receipt,null,2));
    res.writeHead(200,{'Content-Type':body.stream===true?'text/event-stream':'application/json'});
    res.end(body.stream===true?completionFrames(result):JSON.stringify(result));
  }catch{if(!res.headersSent)res.writeHead(502);res.end('Provider bridge failed. No model fallback.');}
  finally{active=false;await rm(temp,{recursive:true,force:true});}
}).listen(port,'127.0.0.1',()=>console.log('Luna High development bridge ready on loopback. The GPU may keep its other appointment.'));
