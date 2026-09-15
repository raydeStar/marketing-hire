import assert from 'node:assert/strict';
import test from 'node:test';
import {GatewayControl} from '../src/Thaddeus.Infrastructure/OpenClawGatewayControl.mjs';

const token='7'.repeat(64);
const hello={type:'hello-ok',protocol:4,server:{version:'2026.9.4',connId:'fixture-caller'},
 auth:{role:'operator',scopes:['operator.read','operator.write']}};
class Gateway extends EventTarget {
 constructor(handle=()=>{},greeting=hello){
  super();this.sent=[];this.closed=false;this.handle=handle;this.greeting=greeting;
  queueMicrotask(()=>this.frame({type:'event',event:'connect.challenge',payload:{nonce:'fixture',ts:1}}));
 }
 frame(frame){this.dispatchEvent(new MessageEvent('message',{data:JSON.stringify(frame)}));}
 reply(request,payload){this.frame({type:'res',id:request.id,ok:true,payload});}
 send(value){
  const request=JSON.parse(value);this.sent.push(request);
  if(request.method==='connect')queueMicrotask(()=>this.reply(request,this.greeting));
  else this.handle(request,this);
 }
 close(){if(!this.closed){this.closed=true;this.dispatchEvent(new Event('close'));}}
}
function fixture(t,handle,options={},greeting=hello){
 const gateway=new Gateway(handle,greeting);let connections=0;
 const control=new GatewayControl(token,{socketFactory:()=>{connections++;return gateway;},...options});
 t.after(()=>control.close());
 return {gateway,control,connections:()=>connections};
}

test('start, steering and whole-session stop use one caller with only read/write authority',async t=>{
 const {gateway,control,connections}=fixture(t,(request,server)=>server.reply(request,{runId:'turn-'+request.method,status:'accepted'}));
 const receipts=[];
 for(const method of ['agent','chat.send','sessions.abort'])receipts.push(await control.call(method,{sessionKey:'task'}));
 assert.equal(connections(),1);
 assert.equal(gateway.sent.filter(r=>r.method==='connect').length,1);
 assert.deepEqual(gateway.sent[0].params.scopes,['operator.read','operator.write']);
 assert.equal(gateway.sent[0].params.client.id,'gateway-client');
 assert.equal(gateway.sent[0].params.client.mode,'backend');
 assert.equal(gateway.sent[0].params.device,undefined);
 assert.ok(receipts.every(r=>r.connectionId==='fixture-caller'));
 assert.deepEqual(gateway.sent.slice(1).map(r=>r.method),['agent','chat.send','sessions.abort']);
});

test('concurrent responses remain correlated even when returned in reverse order',async t=>{
 let received;const ready=new Promise(resolve=>received=resolve);
 const waiting=[];
 const {control,gateway}=fixture(t,request=>{waiting.push(request);if(waiting.length===2)received();});
 const first=control.call('agent.wait',{runId:'first'});
 const second=control.call('agent.wait',{runId:'second'});
 await ready;
 gateway.reply(waiting[1],{runId:'second'});gateway.reply(waiting[0],{runId:'first'});
 assert.equal((await first).report.runId,'first');assert.equal((await second).report.runId,'second');
});

test('a lost connection rejects outstanding and future requests without reconnect or replay',async t=>{
 let dispatched;const ready=new Promise(resolve=>dispatched=resolve);
 const {control,gateway,connections}=fixture(t,()=>dispatched());
 const request=control.call('agent',{message:'one fictional dispatch'});
 const rejected=assert.rejects(request,/closed/);
 await ready;gateway.close();await rejected;
 await assert.rejects(control.call('sessions.send',{message:'must not dispatch'}),/closed/);
 assert.equal(connections(),1);assert.equal(gateway.sent.length,2);
});

test('a missing acknowledgement fails the connection and never retries the effect',async t=>{
 const {control,gateway,connections}=fixture(t,()=>{}, {timeoutMs:20});
 await assert.rejects(control.call('sessions.send',{message:'one dispatch'}),/outcome is unknown/);
 await assert.rejects(control.call('sessions.send',{message:'no retry'}),/outcome is unknown/);
 assert.equal(connections(),1);assert.equal(gateway.sent.length,2);
});

test('known method rejection preserves the caller for a later inspection',async t=>{
 const {control,gateway}=fixture(t,(request,server)=>{
  if(request.method==='agent')server.frame({type:'res',id:request.id,ok:false,error:{code:'INVALID_REQUEST',message:'fixture refusal'}});
  else server.reply(request,{runId:'observed'});
 });
 await assert.rejects(control.call('agent',{}),/INVALID_REQUEST/);
 assert.equal((await control.call('agent.wait',{})).connectionId,'fixture-caller');
 assert.equal(gateway.sent.filter(r=>r.method==='connect').length,1);
});

test('extra authority, a different pin, or a different protocol refuses dispatch',async t=>{
 for(const greeting of [
  {...hello,auth:{role:'operator',scopes:['operator.read','operator.write','operator.admin']}},
  {...hello,server:{...hello.server,version:'2026.9.5'}},
  {...hello,protocol:3},
  {...hello,auth:{role:'node',scopes:['operator.read','operator.write']}}
 ]){
  const {control,gateway}=fixture(t,()=>{throw new Error('must not dispatch');},{},greeting);
  await assert.rejects(control.call('agent',{}),/Unexpected Gateway/);
  assert.equal(gateway.sent.length,1);
 }
});

test('unsupported methods and oversized requests never dispatch',async t=>{
 const {control,gateway}=fixture(t);
 await control.ready;
 await assert.rejects(control.call('config.set',{}),/Unsupported Gateway/);
 await assert.rejects(control.call('agent',{message:'x'.repeat(100001)}));
 assert.equal(gateway.sent.length,1);
});

test('oversized results cannot become successful receipts',async t=>{
 const {control}=fixture(t,(request,server)=>server.reply(request,{text:'x'.repeat(100001)}));
 await assert.rejects(control.call('agent.wait',{}),/oversized Gateway result/);
});

test('later agent completion frames do not dispatch again or replace admission',async t=>{
 const {control,gateway}=fixture(t,(request,server)=>{server.reply(request,{status:'accepted'});queueMicrotask(()=>server.reply(request,{status:'completed'}));});
 assert.equal((await control.call('agent',{})).report.status,'accepted');
 assert.equal((await control.call('agent.wait',{})).report.status,'accepted');
 assert.equal(gateway.sent.filter(r=>r.method==='agent').length,1);
});

test('malformed transport data rejects pending work rather than inventing a receipt',async t=>{
 const {control,gateway}=fixture(t,()=>queueMicrotask(()=>gateway.dispatchEvent(new MessageEvent('message',{data:'not-json'}))));
 await assert.rejects(control.call('agent.wait',{}));
 assert.equal(gateway.closed,true);
});
