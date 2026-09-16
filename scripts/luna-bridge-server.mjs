import http from 'node:http';
import {spawn} from 'node:child_process';
import {mkdir,writeFile,readFile,mkdtemp,rm} from 'node:fs/promises';
import {randomUUID} from 'node:crypto';
import path from 'node:path';
import os from 'node:os';
import {proposalSchema,providerPrompt,completion,completionFrames,extractImages} from './luna-protocol.mjs';

export async function createLunaBridge({executable,artifactDir,timeoutMs=600000,spawnProvider=spawn}){
  await mkdir(artifactDir,{recursive:true});
  let active=0;
  const server=http.createServer(async(req,res)=>{
    const port=server.address().port;
    if(req.headers.origin||!['127.0.0.1:'+port,'localhost:'+port].includes(req.headers.host)){res.writeHead(403);res.end();return;}
    if(req.url==='/v1/models'&&req.method==='GET'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({data:[{id:'gpt-5.6-luna'}],bridge:{activeRequests:active,maxConcurrentRequests:3,timeoutSeconds:timeoutMs/1000}}));return;}
    if(req.url!=='/v1/chat/completions'||req.method!=='POST'){res.writeHead(404);res.end();return;}
    let raw='',body,prompt;
    try{
      for await(const chunk of req){raw+=chunk;if(raw.length>8*1024*1024){res.writeHead(413);res.end();return;}}
      body=JSON.parse(raw);prompt=providerPrompt(body);
    }catch{if(!res.destroyed){res.writeHead(400);res.end('Invalid request for the fixed Luna High bridge.');}return;}
    if(active>=3){res.writeHead(429);res.end('All three provider slots are occupied.');return;}
    // Reserve after parsing, before awaiting: simultaneous uploads cannot overbook the desk.
    active++;
    const id=randomUUID(),started=new Date(),receipt={schemaVersion:3,id,model:'gpt-5.6-luna',reasoning:'high',started:started.toISOString(),status:'failed',phase:'prepare',toolExecutions:0,usage:null};
    let temp,child,timer,timedOut=false,aborted=false,overflow=false,stdout='',stderr='';
    const stop=()=>{if(child&&child.exitCode===null)child.kill();};
    const disconnect=()=>{aborted=true;stop();};res.on('close',disconnect);
    try{
      temp=await mkdtemp(path.join(os.tmpdir(),'thaddeus-luna-'));
      if(res.destroyed)throw new Error('Client closed before dispatch.');
      const schema=path.join(temp,'schema.json'),output=path.join(temp,'reply.json');
      await writeFile(schema,JSON.stringify(proposalSchema()));
      const args=['exec','--ignore-user-config','--ephemeral','--skip-git-repo-check','--sandbox','read-only','--model','gpt-5.6-luna','-c','model_reasoning_effort="high"','-c','features.shell_tool=false','-c','features.apply_patch_freeform=false','--output-schema',schema,'--output-last-message',output,'--json','-'];
      const images=extractImages(body);for(let index=0;index<images.length;index++){const file=path.join(temp,'attachment-'+index+'.'+images[index].extension);await writeFile(file,images[index].bytes);args.splice(args.length-1,0,'--image',file);}
      receipt.attachmentCount=images.length;
      receipt.phase='provider';
      child=spawnProvider(executable,args,{cwd:temp,windowsHide:true,stdio:['pipe','pipe','pipe']});
      const collect=(kind,c)=>{if(stdout.length+stderr.length+c.length>2000000){overflow=true;stop();return;}if(kind==='out')stdout+=c;else stderr+=c;};
      child.stdout.on('data',c=>collect('out',c));child.stderr.on('data',c=>collect('err',c));
      child.stdin.on('error',()=>{});
      timer=setTimeout(()=>{timedOut=true;stop();},timeoutMs);
      const exited=new Promise((resolve,reject)=>{child.once('close',resolve);child.once('error',reject);});
      child.stdin.end(prompt);receipt.exitCode=await exited;
      const events=stdout.split('\n').flatMap(line=>{try{return [JSON.parse(line)];}catch{return [];}});
      receipt.usage=events.findLast(event=>event.type==='turn.completed')?.usage??null;
      receipt.providerFailed=events.some(event=>event.type==='turn.failed'||event.type==='error');
      if(receipt.exitCode!==0||overflow||timedOut||aborted)throw new Error('Provider exited without a usable reply.');
      receipt.phase='tool-sentinel';
      receipt.toolExecutions=events.filter(event=>/command_execution|mcp_tool_call|web_search/.test(event.item?.type||'')).length;
      if(receipt.toolExecutions)throw new Error('Unexpected provider tool execution.');
      receipt.phase='parse-output';const reply=JSON.parse(await readFile(output,'utf8'));
      receipt.phase='validate-proposal';const result=completion(body,reply,receipt.usage);
      receipt.status='succeeded';receipt.phase='complete';receipt.output=result;
      res.writeHead(200,{'Content-Type':body.stream===true?'text/event-stream':'application/json'});res.end(body.stream===true?completionFrames(result):JSON.stringify(result));
    }catch(error){
      receipt.errorClass=error instanceof SyntaxError?'invalid-json':error.code==='ENOENT'?'missing-executable-or-output':'provider-or-proposal-error';
      if(receipt.phase==='validate-proposal')receipt.validationError=validationDiagnostic(error);
      if(!res.destroyed){res.writeHead(timedOut?504:502);res.end(JSON.stringify({error:{code:timedOut?'provider_timeout':'provider_bridge_failed',receiptId:id}}));}
    }finally{
      clearTimeout(timer);res.off('close',disconnect);
      Object.assign(receipt,{timedOut,aborted,overflow,stdoutCharacters:stdout.length,stderrCharacters:stderr.length,elapsedMs:Date.now()-started.getTime()});
      try{await writeFile(path.join(artifactDir,started.getTime()+'-'+id+'.json'),JSON.stringify(receipt,null,2));}
      catch{console.error('Could not save provider diagnostics; the raven claims no receipt.');}
      finally{
        try{if(temp){const resolved=path.resolve(temp);if(path.dirname(resolved)!==path.resolve(os.tmpdir())||!path.basename(resolved).startsWith('thaddeus-luna-'))throw new Error('Temporary cleanup path escaped.');await rm(resolved,{recursive:true,force:true});}}
        catch{console.error('Provider scratch cleanup failed; inspect its temporary directory.');}
        active--;
      }
    }
  });
  return server;
}

function validationDiagnostic(error){
  if(error instanceof SyntaxError)return 'Proposal contained invalid JSON.';
  const allowed=new Set([
    'Malformed inference proposal.','Model proposed a function outside the request.','Function arguments must be an object.',
    'Unexpected page design.','Malformed page design.','Conflicting page designs.','Function arguments exceeded the transport limit.',
    'Model proposal does not match tool choice.','Empty model reply.'
  ]);
  return allowed.has(error?.message)?error.message:'Proposal did not satisfy bridge validation.';
}
