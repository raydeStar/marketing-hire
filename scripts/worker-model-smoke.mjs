// Explicit development probe: one Luna High inference, no tool execution and no GPU request.
import { mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const body = {
  model: 'gpt-5.6-luna', reasoning_effort: 'high', stream: false, max_completion_tokens: 2048,
  messages: [{ role: 'system', content: 'Transport verification. Propose the advertised question exactly once. Do not answer it or claim any external effect.' },
    { role: 'user', content: 'Ask which audience the research note should serve. Offer Beginners and Developers.' }],
  tools: [{ type: 'function', function: { name: 'thaddeus_ask_user', description: 'Propose a question. The caller owns its execution.',
    parameters: { type: 'object', properties: { operationId: { type: 'string' }, question: { type: 'string' }, choices: { type: 'array', items: { type: 'string' } } },
      required: ['operationId', 'question', 'choices'], additionalProperties: false } } }],
  tool_choice: 'required', parallel_tool_calls: false
};
const started = new Date();
const response = await fetch('http://127.0.0.1:5181/v1/chat/completions', {
  method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal: AbortSignal.timeout(240000)
});
if (!response.ok) throw new Error('Luna probe failed with status ' + response.status);
const result = await response.json();
const call = result.choices?.[0]?.message?.tool_calls?.[0];
if (result.model !== body.model || result.choices[0].finish_reason !== 'tool_calls' || result.choices[0].message.tool_calls.length !== 1 || call.function.name !== 'thaddeus_ask_user')
  throw new Error('Luna did not produce the expected single typed proposal.');
const args = JSON.parse(call.function.arguments);
if (typeof args.operationId !== 'string' || typeof args.question !== 'string' || !args.choices?.includes('Beginners') || !args.choices?.includes('Developers'))
  throw new Error('Question proposal has incomplete arguments.');
const receipt = { schemaVersion: 1, kind: 'real-inference-transport-probe', started: started.toISOString(), finished: new Date().toISOString(),
  model: body.model, reasoning: body.reasoning_effort, transport: 'Luna High development bridge',
  requestHash: createHash('sha256').update(JSON.stringify(body)).digest('hex'), result, toolExecutions: 0,
  proved: ['Real model returned general function proposal', 'Non-streaming compatible response retained provider usage'],
  unverified: ['OpenClaw native execution', 'MicroVM isolation', 'Product efficacy', 'Hard remote token ceiling'] };
await mkdir('artifacts/model-probes', { recursive: true });
const file = 'artifacts/model-probes/luna-tool-proposal-' + Date.now() + '.json';
await writeFile(file, JSON.stringify(receipt, null, 2));
console.log(JSON.stringify({ passed: true, file, model: body.model, reasoning: body.reasoning_effort, usage: result.usage, toolExecutions: 0 }));
