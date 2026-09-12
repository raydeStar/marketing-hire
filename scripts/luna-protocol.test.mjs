import test from 'node:test';
import assert from 'node:assert/strict';
import { completion, completionFrames, providerPrompt, proposalSchema } from './luna-protocol.mjs';

const body = { model:'gpt-5.6-luna',reasoning_effort:'high',messages:[{role:'user',content:'Transport fixture'}],
  tools:[{type:'function',function:{name:'thaddeus_ask_user',parameters:{type:'object'}}}],tool_choice:'required' };

test('general tool proposals preserve named function and JSON arguments without executing them',()=>{
  const result=completion(body,{text:'',tool_calls:[{name:'thaddeus_ask_user',arguments:'{"question":"Which audience?"}'}]},{input_tokens:10,output_tokens:4});
  assert.equal(result.choices[0].message.tool_calls[0].function.name,'thaddeus_ask_user');
  assert.equal(result.choices[0].finish_reason,'tool_calls');assert.equal(result.usage.total_tokens,14);
  assert.match(completionFrames(result),/"index":0/);assert.ok(completionFrames(result).endsWith('data: [DONE]\n\n'));
});
test('unknown usage stays unknown',()=>{
  const result=completion({...body,tool_choice:'auto'},{text:'Hello',tool_calls:[]});
  assert.equal(result.usage.prompt_tokens,null);assert.equal(result.usage.total_tokens,null);
});
test('unadvertised tools and mismatched tool choices are rejected',()=>{
  assert.throws(()=>completion(body,{text:'',tool_calls:[{name:'host_shell',arguments:'{}'}]}));
  assert.throws(()=>completion(body,{text:'Done',tool_calls:[]}));
  assert.throws(()=>completion({...body,tool_choice:'none'},{text:'',tool_calls:[{name:'thaddeus_ask_user',arguments:'{}'}]}));
});
test('Luna remains fixed to High and prompt includes the full proposal contract',()=>{
  assert.throws(()=>providerPrompt({...body,reasoning_effort:'low'}));
  assert.match(providerPrompt(body),/thaddeus_ask_user/);assert.match(providerPrompt(body),/external runtime owns all execution/);
  assert.deepEqual(proposalSchema().required,['text','tool_calls']);
});
