import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {createServer} from 'node:net';
import {constants} from 'node:fs';
import {open,writeFile,chmod} from 'node:fs/promises';

const VERSION='2026.9.4', PROTOCOL=4, SCOPES=['operator.read','operator.write'];
const METHODS=new Set(['agent','agent.wait','chat.send','sessions.send','sessions.abort']);
const ROOT='/home/agent/.openclaw', LIMIT=100000;

// This is a transport, not an agent loop. One connection keeps one Gateway caller identity.
export class GatewayControl {
  constructor(token,{socketFactory=()=>new WebSocket('ws://127.0.0.1:18789'),timeoutMs=30000}={}) {
    assert.match(token,/^[a-f0-9]{64}$/);
    this.timeoutMs=timeoutMs;this.pending=new Map();this.sequence=0;this.failed=null;this.hello=null;
    this.ready=new Promise((resolve,reject)=>{this.resolveReady=resolve;this.rejectReady=reject;});
    this.ready.catch(()=>{}); // The caller receives the original failure; no unhandled-rejection detour.
    this.socket=socketFactory();
    this.handshake=setTimeout(()=>this.fail(new Error('Gateway handshake timed out.')),10000);
    this.socket.addEventListener('message',event=> {
      try {
        if(typeof event.data!=='string'||Buffer.byteLength(event.data)>4*1024*1024)throw new Error('Invalid Gateway frame.');
        const frame=JSON.parse(event.data);
        if(frame.type==='event') {
          if(frame.event!=='connect.challenge')return;
          if(this.connectId||this.hello)throw new Error('Repeated Gateway challenge.');
          this.connectId=randomUUID();
          this.socket.send(JSON.stringify({type:'req',id:this.connectId,method:'connect',params:{
            minProtocol:PROTOCOL,maxProtocol:PROTOCOL,
            client:{id:'gateway-client',displayName:'Thaddeus execution controller',version:VERSION,platform:'linux',mode:'backend'},
            role:'operator',scopes:SCOPES,caps:[],auth:{token}
          }}));
          return;
        }
        if(frame.type!=='res'||typeof frame.id!=='string')throw new Error('Unrecognized Gateway response.');
        if(frame.id===this.connectId) {
          if(this.hello)throw new Error('Repeated Gateway handshake.');
          if(frame.ok!==true)throw new Error('Gateway handshake rejected: '+String(frame.error?.code??'unknown'));
          const hello=frame.payload;
          if(hello?.type!=='hello-ok'||hello.protocol!==PROTOCOL||hello.server?.version!==VERSION||
             typeof hello.server?.connId!=='string'||hello.auth?.role!=='operator'||
             !Array.isArray(hello.auth.scopes)||hello.auth.scopes.length!==SCOPES.length||
             !SCOPES.every(scope=>hello.auth.scopes.includes(scope)))throw new Error('Unexpected Gateway version or authority.');
          this.hello={protocol:hello.protocol,serverVersion:hello.server.version,connectionId:hello.server.connId,scopes:[...hello.auth.scopes]};
          clearTimeout(this.handshake);this.resolveReady(this.hello);return;
        }
        const pending=this.pending.get(frame.id);
        // Agent admission can be followed by another response with the same ID. It is not a second dispatch.
        if(!pending)return;
        this.pending.delete(frame.id);clearTimeout(pending.timer);
        if(frame.ok!==true)pending.reject(new Error('Gateway request rejected: '+String(frame.error?.code??'unknown')+' '+String(frame.error?.message??'').slice(0,2000)));
        else if(!frame.payload||typeof frame.payload!=='object'||Array.isArray(frame.payload)||
          Buffer.byteLength(JSON.stringify(frame.payload))>LIMIT)pending.reject(new Error('Invalid or oversized Gateway result.'));
        else pending.resolve({report:frame.payload,...this.hello});
      } catch(error) {this.fail(error);}
    });
    this.socket.addEventListener('error',()=>this.fail(new Error('Gateway connection failed.')));
    this.socket.addEventListener('close',()=>this.fail(new Error('Gateway connection closed. Reconcile before starting another controller.')));
  }
  fail(error) {
    if(this.failed)return;
    this.failed=error;clearTimeout(this.handshake);this.rejectReady(error);
    for(const pending of this.pending.values()){clearTimeout(pending.timer);pending.reject(error);}
    this.pending.clear();
    try{this.socket.close();}catch{}
    this.onFailure?.(error);
  }
  async call(method,parameters) {
    assert.ok(METHODS.has(method),'Unsupported Gateway method.');
    assert.ok(parameters&&typeof parameters==='object'&&!Array.isArray(parameters));
    assert.ok(Buffer.byteLength(JSON.stringify(parameters))<=LIMIT);
    await this.ready;
    if(this.failed)throw this.failed;
    if(this.pending.size>=4||this.sequence>=512)throw new Error('Gateway control allowance exhausted.');
    const id='thaddeus-'+(++this.sequence)+'-'+randomUUID();
    return await new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>this.fail(new Error('Gateway request outcome is unknown. No reconnect or replay.')),this.timeoutMs);
      this.pending.set(id,{resolve,reject,timer});
      try{this.socket.send(JSON.stringify({type:'req',id,method,params:parameters}));}
      catch(error){this.fail(error);}
    });
  }
  close(){this.fail(new Error('Gateway controller stopped.'));}
}

async function readPrivate(name,limit=LIMIT) {
  const file=await open(ROOT+'/'+name,constants.O_RDONLY|constants.O_NOFOLLOW|constants.O_NONBLOCK);
  try {
    const info=await file.stat();
    assert.ok(info.isFile()&&info.size<=limit&&(info.mode&0o077)===0);
    const bytes=await file.readFile();assert.ok(bytes.length<=limit);
    return bytes.toString('utf8');
  }finally{await file.close();}
}

async function serve() {
  const lease=JSON.parse(await readPrivate('thaddeus-control-lease.json'));
  assert.match(lease.nonce,/^[a-f0-9]{32}$/);
  const binding=JSON.parse(await readPrivate('thaddeus-binding.json'));
  assert.match(binding.sessionKey,/^agent:thaddeus:[a-f0-9]{32}$/);
  const environment=await readPrivate('.env');
  const match=/^THADDEUS_WORKER_TOKEN=[a-f0-9]{64}\nTHADDEUS_GATEWAY_TOKEN=([a-f0-9]{64})\n$/.exec(environment);
  assert.ok(match);
  const controller=new GatewayControl(match[1]);
  const hello=await controller.ready;
  await writePrivate('thaddeus-control-hello.json',JSON.stringify({...hello,nonce:lease.nonce,processId:process.pid}));
  let server,clients=0;
  controller.onFailure=async error=>{
    await writePrivate('thaddeus-control-error.json',JSON.stringify({message:error.message.slice(0,3000)})).catch(()=>{});
    server?.close();
    process.exitCode=1;
  };
  server=createServer(socket=>{
    if(++clients>4){clients--;socket.destroy();return;}
    let body=Buffer.alloc(0),received=false;
    const timer=setTimeout(()=>socket.destroy(),45000);
    socket.on('error',()=>{});
    socket.on('close',()=>{clearTimeout(timer);clients--;});
    socket.on('data',async chunk=>{
      if(received){socket.destroy();return;}
      body=Buffer.concat([body,chunk]);
      if(body.length>LIMIT){socket.destroy();return;}
      const newline=body.indexOf(10);
      if(newline<0)return;
      received=true;
      try {
        assert.equal(newline,body.length-1,'One control request per connection.');
        const request=JSON.parse(body.subarray(0,newline).toString('utf8'));
        assert.equal(request.nonce,lease.nonce,'Controller lease mismatch.');
        assert.equal(request.version,VERSION);
        assert.ok(METHODS.has(request.method));
        const parameters=request.parameters;
        if(request.method!=='agent.wait')assert.equal(parameters?.sessionKey??parameters?.key,binding.sessionKey,'Task session mismatch.');
        const result=await controller.call(request.method,parameters);
        socket.end(JSON.stringify({ok:true,...result})+'\n');
      }catch(error){socket.end(JSON.stringify({ok:false,error:String(error.message).slice(0,3000)})+'\n');}
    });
  });
  await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(ROOT+'/thaddeus-control-'+lease.nonce+'.sock',resolve);});
  await chmod(ROOT+'/thaddeus-control-'+lease.nonce+'.sock',0o600);
  if(controller.failed){server.close();throw controller.failed;}
  console.log('Gateway controller ready. One calling card for this errand.');
}
async function writePrivate(name,value) {
  const file=await open(ROOT+'/'+name,constants.O_WRONLY|constants.O_CREAT|constants.O_TRUNC|constants.O_NOFOLLOW,0o600);
  try {assert.ok((await file.stat()).isFile());await file.writeFile(value);} finally {await file.close();}
}

if(process.argv[1]==='-'){
  process.umask(0o077);
  try{await serve();}
  catch(error){
    await writePrivate('thaddeus-control-error.json',JSON.stringify({message:String(error.message).slice(0,3000)})).catch(()=>{});
    console.error('Gateway controller failed. Reconcile before trying another calling card.');
    process.exitCode=1;
  }
}
