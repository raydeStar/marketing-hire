import assert from 'node:assert/strict';
import test from 'node:test';
import {stream} from '/app/dist/plugin-sdk/llm.js';
import {getAiTransportHost, configureAiTransportHost} from '@openclaw/ai';
import {createPlowRequestGuards, plowWorkerSession} from './fetch.mjs';

const execution = 'a'.repeat(32);
const model = {id: 'z-ai/glm-5.2', name: 'Plow fixture', provider: 'plow', api: 'openai-completions',
  baseUrl: 'https://api.plow.co/v1', input: ['text'], reasoning: false, contextWindow: 1048576, maxTokens: 4096,
  cost: {input: 0, output: 0, cacheRead: 0, cacheWrite: 0},
  compat: {sendSessionAffinityHeaders: true, supportsUsageInStreaming: true, maxTokensField: 'max_tokens'}};
const context = {messages: [{role: 'user', content: 'Offline fixture only', timestamp: 0}], tools: []};
function completed() {
  const common = {id: 'chat_fixture', object: 'chat.completion.chunk', created: 1, model: model.id};
  const events = [{...common, choices: [{index: 0, delta: {role: 'assistant', content: 'Prepared fixture'}, finish_reason: null}]},
    {...common, choices: [{index: 0, delta: {}, finish_reason: 'stop'}]},
    {...common, choices: [], usage: {prompt_tokens: 5, completion_tokens: 3, total_tokens: 8}}];
  return new Response(events.map(event => 'data: ' + JSON.stringify(event) + '\n\n').join('') + 'data: [DONE]\n\n',
    {headers: {'Content-Type': 'text/event-stream'}});
}
async function run(overrides = {}, options = {}) {
  const previous = getAiTransportHost(), oldFetch = globalThis.fetch;
  let reservations = 0, sends = 0; const receipts = [], packets = [];
  const apiBase = options.apiBase || 'https://api.plow.co';
  const guard = createPlowRequestGuards({apiBase, baseFetch: async request => {
    sends++; packets.push({url: request.url, session: request.headers.get('session_id'), body: await request.json()});
    return options.httpError ? new Response('Fixture failure', {status: 503}) : completed();
  }, activeExecution: () => ({execution_id: execution, accounting_mode: 'post_response', deadline_at: Date.now() / 1000 + 60}),
  reserveRequest: () => ({admitted: ++reservations === 1 && !options.deny}),
  finishRequest: receipt => receipts.push(receipt)});
  // The actual installed SDK builds this transport and supplies the real encoded request.
  configureAiTransportHost({...previous, buildModelFetch: () => guard.modelFetch(request => globalThis.fetch(request))});
  globalThis.fetch = guard.nativeFetch;
  try {
    const result = await stream({...model, baseUrl: apiBase + '/v1', ...overrides}, options.context || context, {apiKey: 'fictional-offline-key',
      sessionId: options.session || plowWorkerSession(execution), maxTokens: 4096, cacheRetention: 'short',
      signal: AbortSignal.timeout(5000)}).result();
    return {result, reservations, sends, receipts, packets};
  } finally {configureAiTransportHost(previous); globalThis.fetch = oldFetch;}
}
test('installed Plow completions SDK carries exact identity, capped output and terminal usage through one send', async () => {
  const result = await run(); assert.equal(result.result.stopReason, 'stop');
  assert.equal(result.reservations, 1); assert.equal(result.sends, 1);
  assert.equal(result.packets[0].session, plowWorkerSession(execution));
  assert.equal(result.packets[0].body.max_tokens, 4096); assert.equal(result.packets[0].body.stream_options.include_usage, true);
  assert.equal(result.receipts[0].reported_tokens, 8); assert.equal(result.receipts[0].response_receipt.terminal_type, 'chat.completion.done');
  assert.equal(result.result.usage.totalTokens, 8);
});
test('installed SDK uses the supplied per-install proxy and cannot fall back to the public API', async () => {
  const apiBase = 'http://127.0.0.1:43127/install-fixture';
  const result = await run({}, {apiBase});
  assert.equal(result.result.stopReason, 'stop'); assert.equal(result.sends, 1); assert.equal(result.reservations, 1);
  assert.equal(result.packets[0].url, apiBase + '/v1/chat/completions');
  assert.equal(result.packets[0].session, plowWorkerSession(execution));
  assert.equal(result.receipts[0].reported_tokens, 8);
  const foreign = await run({baseUrl: 'https://api.plow.co/v1'}, {apiBase});
  assert.equal(foreign.result.stopReason, 'error'); assert.equal(foreign.sends, 0); assert.equal(foreign.reservations, 0);
});

test('installed SDK carries a full campaign packet above the old input ceiling unchanged', async () => {
  const content = 'Owner asks and cited evidence “intact”. '.repeat(1300);
  const result = await run({}, {context: {messages: [{role: 'user', content, timestamp: 0}], tools: []}});
  assert.equal(result.result.stopReason, 'stop'); assert.equal(result.sends, 1);
  assert.ok(Buffer.byteLength(JSON.stringify(result.packets[0].body)) > 20000);
  assert.equal(result.packets[0].body.messages.at(-1).content, content);
  assert.equal(result.receipts[0].reported_tokens, 8);
});
for (const [name, overrides, options] of [
  ['refused reservation', {}, {deny: true}], ['foreign session', {}, {session: 'owner-chat'}],
  ['missing identity capability', {compat: {...model.compat, sendSessionAffinityHeaders: false}}, {}],
  ['missing streaming usage', {compat: {...model.compat, supportsUsageInStreaming: false}}, {}],
  ['foreign model', {id: 'anthropic/claude-sonnet-5'}, {}],
]) test('installed runtime refuses ' + name + ' before physical dispatch', async () => {
  const result = await run(overrides, options); assert.equal(result.result.stopReason, 'error'); assert.equal(result.sends, 0);
});
test('installed SDK does not silently retry a provider failure; unknown usage remains reserved', async () => {
  const result = await run({}, {httpError: true}); assert.equal(result.result.stopReason, 'error');
  assert.equal(result.sends, 1); assert.equal(result.reservations, 1); assert.equal(result.receipts[0].status, 'unknown');
});
