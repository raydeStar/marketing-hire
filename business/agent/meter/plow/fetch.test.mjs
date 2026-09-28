import assert from 'node:assert/strict';
import test from 'node:test';
import {createPlowRequestGuards, plowWorkerSession, MAX_BYTES} from './fetch.mjs';
import {plowApiEndpoints} from './api-endpoints.mjs';

const execution = 'a'.repeat(32);
const endpoint = 'https://api.plow.co/v1/chat/completions';
const active = () => ({execution_id: execution, accounting_mode: 'post_response', deadline_at: Date.now() / 1000 + 60});
test('environment supplies the deployment boundary; only an absent variable uses the public default', () => {
  const saved = process.env.PLOW_API_BASE;
  try {
    delete process.env.PLOW_API_BASE;
    assert.equal(plowApiEndpoints().completion, endpoint);
    process.env.PLOW_API_BASE = 'http://127.0.0.1:43127/install-fixture/';
    assert.equal(plowApiEndpoints().completion, 'http://127.0.0.1:43127/install-fixture/v1/chat/completions');
    process.env.PLOW_API_BASE = '';
    assert.equal(plowApiEndpoints(), null);
  } finally {
    if (saved === undefined) delete process.env.PLOW_API_BASE; else process.env.PLOW_API_BASE = saved;
  }
});
export function frames(changes = {}) {
  const chunk = {id: 'chat_fixture', object: 'chat.completion.chunk', model: 'z-ai/glm-5.2', created: 1};
  return [JSON.stringify({...chunk, choices: [{index: 0, delta: {role: 'assistant', content: 'Prepared fixture'}, finish_reason: null}]}),
    JSON.stringify({...chunk, choices: [{index: 0, delta: {}, finish_reason: 'stop'}]}),
    JSON.stringify({...chunk, choices: [], usage: {prompt_tokens: 5, completion_tokens: 3, total_tokens: 8}, ...changes}), '[DONE]'];
}
export function response(events = frames()) {
  return new Response(events.map(data => 'data: ' + data + '\n\n').join(''), {headers: {'Content-Type': 'text/event-stream'}});
}
function request(overrides = {}, headers = {}, target = endpoint) {
  return new Request(target, {method: 'POST', headers: {session_id: plowWorkerSession(execution), ...headers},
    body: JSON.stringify({model: 'z-ai/glm-5.2', messages: [{role: 'user', content: 'Fixture only'}], stream: true,
      stream_options: {include_usage: true}, max_tokens: 4096, ...overrides})});
}
function fixture(options = {}) {
  let reservations = 0, sends = 0; const receipts = [], holds = [];
  const guard = createPlowRequestGuards({apiBase: options.apiBase ?? 'https://api.plow.co',
    baseFetch: async incoming => {sends++; return options.response?.(incoming) || response();},
    activeExecution: options.active || active, reserveRequest: async hold => { holds.push(hold); return {admitted: ++reservations === 1 && (!options.budget || hold.reserved_tokens <= options.budget)}; },
    finishRequest: async receipt => {receipts.push(receipt); if (options.saveFails) throw new Error('Ledger unavailable');}});
  return {guard, receipts, holds, reservations: () => reservations, sends: () => sends,
    fetch: guard.modelFetch(incoming => guard.nativeFetch(incoming))};
}

test('one physical request is reserved once and its usage is saved before completion', async () => {
  const run = fixture();
  const reply = await run.fetch(request()); await reply.text();
  assert.equal(run.reservations(), 1); assert.equal(run.sends(), 1);
  assert.equal(run.receipts.length, 1); assert.equal(run.receipts[0].reported_tokens, 8);
  assert.equal(run.receipts[0].response_receipt.terminal_type, 'chat.completion.done');
  await assert.rejects(run.fetch(request()), /reservation refused/);
  assert.equal(run.sends(), 1);
});
for (const apiBase of ['http://127.0.0.1:43127', 'https://install.example.test/proxy/install-one/']) {
  test('supplied proxy is the only admitted worker and channel address: ' + apiBase, async () => {
    const api = apiBase.replace(/\/+$/, '') + '/v1';
    const run = fixture({apiBase});
    for (const target of [endpoint, api + '/chat/completions?other=1', api + '/chat/completions/extra',
      'http://127.0.0.1:43128/v1/chat/completions', 'https://install.example.test/proxy/install-two/v1/chat/completions'])
      await assert.rejects(run.fetch(request({}, {}, target)), /assignment/);
    await assert.rejects(run.fetch(request({model: 'anthropic/claude-sonnet-5'}, {}, api + '/chat/completions')), /policy/);
    await assert.rejects(run.fetch(request({}, {session_id: 'owner'}, api + '/chat/completions')), /assignment/);
    await assert.rejects(run.fetch(request({max_tokens: 4097}, {}, api + '/chat/completions')), /policy/);
    assert.equal(run.reservations(), 0); assert.equal(run.sends(), 0);
    await (await run.fetch(request({}, {}, api + '/chat/completions'))).text();
    assert.equal(run.reservations(), 1); assert.equal(run.sends(), 1); assert.equal(run.receipts[0].reported_tokens, 8);
    await assert.rejects(run.fetch(request({}, {}, api + '/chat/completions')), /reservation refused/);
    for (const path of ['/chats/owner?limit=1', '/lines', '/agents/me', '/auth/owner-uid', '/identity', '/ws/ticket'])
      await run.guard.nativeFetch(api + path);
    assert.equal(run.sends(), 7);
    for (const target of ['https://api.plow.co/v1/chats/owner', api + '/chat/completions', api + '/agents-evil',
      api + '/ws/ticket/extra', 'https://install.example.test/proxy/install-two/v1/chats/owner'])
      await assert.rejects(run.guard.nativeFetch(target), /bypassed/);
    await assert.rejects(run.guard.nativeFetch(new Request(api + '/chats/owner', {
      headers: {session_id: plowWorkerSession(execution)}})), /bypassed/);
    assert.equal(run.sends(), 7);
  });
}
test('invalid supplied address cannot fall back to the public API', async () => {
  for (const apiBase of ['', 'not a URL', 'file:///tmp/api', 'https://user:secret@example.test',
    'https://example.test?key=secret', 'https://example.test/#fragment']) {
    const run = fixture({apiBase});
    await assert.rejects(run.fetch(request()), /assignment/);
    await assert.rejects(run.guard.nativeFetch('https://api.plow.co/v1/chats/owner'), /bypassed/);
    assert.equal(run.reservations(), 0); assert.equal(run.sends(), 0);
  }
});
for (const [name, packet, headers, target] of [
  ['foreign session', {}, {session_id: 'owner-chat'}], ['changed model', {model: 'anthropic/claude-sonnet-5'}],
  ['resumed boundary', {}, {session_id: plowWorkerSession(execution).replace(':0', ':1')}],
  ['foreign execution', {}, {session_id: plowWorkerSession('b'.repeat(32))}],
  ['tools', {tools: [{type: 'function'}]}], ['oversized input', {messages: [{role: 'user', content: 'x'.repeat(MAX_BYTES)}]}],
  ['output cap', {max_tokens: 4097}], ['missing usage request', {stream_options: {}}], ['multiple completions', {n: 2}],
  ['foreign endpoint', {}, {}, 'https://api.openai.com/v1/chat/completions'], ['query', {}, {}, endpoint + '?other=1'],
]) test('refuses ' + name + ' before reservation or physical dispatch', async () => {
  const run = fixture(); await assert.rejects(run.fetch(request(packet, headers, target)));
  assert.equal(run.reservations(), 0); assert.equal(run.sends(), 0);
});
test('unclaimed worker and expired grant cannot spend; owner traffic remains available without a worker', async () => {
  const unclaimed = fixture({active: () => ({})}); await assert.rejects(unclaimed.fetch(request()), /no active/);
  const expired = fixture({active: () => ({...active(), deadline_at: 1})}); await assert.rejects(expired.fetch(request()), /assignment/);
  assert.equal(unclaimed.sends(), 0); assert.equal(expired.sends(), 0);
  await unclaimed.fetch(request({}, {session_id: 'owner'})); assert.equal(unclaimed.sends(), 1);
});

test('larger Unicode campaign context needs its larger durable reservation before the only send', async () => {
  const packet = request({messages: [{role: 'user', content: 'Evidence “quoted” — résumé. '.repeat(1600)}]});
  const size = (await packet.clone().arrayBuffer()).byteLength;
  assert.ok(size > 20000 && size < MAX_BYTES);
  const denied = fixture({budget: 25000});
  await assert.rejects(denied.fetch(packet.clone()), /reservation refused/);
  assert.equal(denied.sends(), 0);
  const run = fixture({budget: 100000});
  await (await run.fetch(packet)).text();
  assert.equal(run.holds[0].reserved_tokens, size + 4096 + 1024);
  assert.equal(run.sends(), 1); assert.equal(run.receipts[0].reported_tokens, 8);
});

test('worker transport does not inherit a redirecting owner dispatcher', async () => {
  const run = fixture(); let ownerCalls = 0;
  const fetch = run.guard.modelFetch(() => {ownerCalls++; throw Error('Owner dispatcher must not send a worker request');},
    request => {assert.equal(request.redirect, 'error'); return run.guard.nativeFetch(request);});
  await (await fetch(request())).text(); assert.equal(ownerCalls, 0); assert.equal(run.sends(), 1);
});
test('native provider bypass and second physical send are refused; Plow chat transport remains available', async () => {
  const run = fixture(); await assert.rejects(run.guard.nativeFetch(request()), /bypassed/);
  await assert.rejects(run.guard.nativeFetch('https://api.openai.com/v1/responses'), /bypassed/);
  await run.guard.nativeFetch('https://api.plow.co/v1/chats/owner'); assert.equal(run.sends(), 1);
  const double = run.guard.modelFetch(async incoming => {
    const retry = incoming.clone();
    await run.guard.nativeFetch(incoming); return run.guard.nativeFetch(retry);
  });
  await assert.rejects(double(request()), /unreserved retry/); assert.equal(run.sends(), 2);
  assert.equal(run.receipts[0].status, 'unknown');
});
for (const [name, events] of [
  ['missing usage', frames().filter(data => !data.includes('"usage"'))],
  ['inconsistent usage', frames({usage: {prompt_tokens: 5, completion_tokens: 3, total_tokens: 99}})],
  ['missing finish', frames().filter(data => !data.includes('"finish_reason":"stop"'))],
  ['missing done', frames().slice(0, -1)], ['crossed response ID', frames({id: 'another_reply'})],
  ['malformed SSE', ['{invalid JSON', '[DONE]']],
]) test(name + ' retains unknown usage rather than reporting zero or success', async () => {
  const run = fixture({response: () => response(events)}); await (await run.fetch(request())).text();
  assert.equal(run.receipts.length, 1); assert.equal(run.receipts[0].status, 'unknown');
  assert.equal(run.receipts[0].reported_tokens, null);
});
test('a receipt write failure prevents delivery of a successful completion', async () => {
  const run = fixture({saveFails: true}); await assert.rejects((await run.fetch(request())).text(), /Ledger unavailable/);
});
test('provider HTTP errors retain a reservation with unknown usage', async () => {
  const run = fixture({response: () => new Response('Provider unavailable', {status: 503})});
  assert.equal((await run.fetch(request())).status, 503);
  assert.equal(run.receipts[0].status, 'unknown'); assert.equal(run.receipts[0].response_receipt.http_status, 503);
});
