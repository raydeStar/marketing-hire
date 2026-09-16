import test from 'node:test';
import assert from 'node:assert/strict';
import { completion, completionFrames, providerPrompt, proposalSchema, extractImages } from './luna-protocol.mjs';

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
  assert.deepEqual(proposalSchema().required,['text','tool_calls']);assert.equal(proposalSchema().properties.tool_calls.maxItems,1);
});

test('structured page code survives quotes, newlines and backslashes without nested JSON escaping',()=>{
  const request={...body,tools:[{type:'function',function:{name:'artifact_update',parameters:{type:'object',properties:{definition:{type:'object',properties:{title:{type:'string'},page:{type:'object'}}}}}}}]};
  const page={html:'<input placeholder="A \\"quoted\\" thought">',css:'.note::after{content:"\\\\"}',javaScript:'const note="one\\ntwo";\ndocument.title = `The "study"`;' };
  const prompt=providerPrompt(request),contract=JSON.parse(prompt.slice(prompt.lastIndexOf('\n')+1));
  assert.equal(contract.tools[0].function.parameters.properties.definition.properties.page,undefined);
  const result=completion(request,{text:'',tool_calls:[{name:'artifact_update',arguments:JSON.stringify({artifactId:'fictional',definition:{title:'Notes'}}),page}]});
  const args=JSON.parse(result.choices[0].message.tool_calls[0].function.arguments);
  assert.deepEqual(args.definition.page,page);assert.equal(args.artifactId,'fictional');
  const duplicate=completion(request,{text:'',tool_calls:[{name:'artifact_update',arguments:JSON.stringify({artifactId:'fictional',definition:{title:'Notes',page}}),page}]});
  assert.deepEqual(JSON.parse(duplicate.choices[0].message.tool_calls[0].function.arguments).definition.page,page);
  assert.throws(()=>completion(body,{text:'',tool_calls:[{name:'thaddeus_ask_user',arguments:'{}',page}]}));
  assert.throws(()=>completion(request,{text:'',tool_calls:[{name:'artifact_update',arguments:'{"definition":{"page":{}}}',page}]}),/Conflicting page designs/);
  assert.throws(()=>completion(request,{text:'',tool_calls:[{name:'artifact_update',arguments:'{"definition":{}}',page:{...page,extra:'not allowed'}}]}));
});


test('inline image data is removed from text and constrained before CLI dispatch',()=>{
 const bytes=Buffer.from([137,80,78,71,13,10,26,10]);
 const request={...body,messages:[{role:'user',content:[{type:'text',text:'Describe this fictional image'},{type:'image_url',image_url:{url:'data:image/png;base64,'+bytes.toString('base64')}}]}]};
 assert.deepEqual(extractImages(request),[{extension:'png',bytes}]);
 assert.ok(!providerPrompt(request).includes(bytes.toString('base64')));
 assert.match(providerPrompt(request),/Image 1 is attached/);
 assert.throws(()=>extractImages({...request,messages:[{role:'user',content:[{type:'image_url',image_url:{url:'https://private.invalid/image.png'}}]}]}));
 assert.throws(()=>extractImages({...request,messages:Array(5).fill(request.messages[0])}));
});
