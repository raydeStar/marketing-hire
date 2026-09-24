import assert from 'node:assert/strict';
import { test } from 'node:test';
import { zstdCompressSync, zstdDecompressSync } from 'node:zlib';
import { createGlobalMeteredFetch, createMeteredFetch } from './metered-fetch.mjs';
import { workerSession } from './worker-session.mjs';

const executionId = 'a'.repeat(32);
const endpoint = 'https://chatgpt.com/backend-api/codex/responses';
const worker = { method: 'POST', headers: { session_id: workerSession(executionId).header }, body: '{"model":"gpt-5.6-luna"}' };

test('explicit post-response grant omits unsupported cap and records usage on a Request input', async () => {
  const active = { execution_id: executionId, accounting_mode: 'post_response', deadline_at: Date.now() / 1000 + 900 };
  const order = [];
  const receipts = [];
  const fetch = createGlobalMeteredFetch({ activeExecution: async () => active,
    reserveRequest: async receipt => { order.push('reserve'); assert.equal(receipt.accounting_mode, 'post_response'); return { admitted: true }; },
    finishRequest: async receipt => { order.push('receipt'); receipts.push(receipt); },
    baseFetch: async request => {
      order.push('send');
      const body = JSON.parse(await request.text());
      assert.equal('max_output_tokens' in body, false);
      assert.equal(body.model, 'gpt-5.6-luna');
      assert.equal(request.signal.aborted, false);
      return new Response('data: {"type":"response.completed","response":{"id":"fixture","usage":{"input_tokens":5,"output_tokens":3,"total_tokens":8}}}\n\n',
        { headers: { 'content-type': 'text/event-stream' } });
    },
  });
  const response = await fetch(new Request(endpoint, { ...worker,
    body: '{"model":"gpt-5.6-luna","max_output_tokens":1800}' }));
  await response.text();
  assert.deepEqual(order, ['reserve', 'send', 'receipt']);
  assert.equal(receipts[0].reported_tokens, 8);
});

test('post-response mode fails before dispatch without receipt storage or a current deadline', async () => {
  for (const [active, finishRequest] of [
    [{ execution_id: executionId, accounting_mode: 'post_response', deadline_at: Date.now() / 1000 + 900 }, undefined],
    [{ execution_id: executionId, accounting_mode: 'post_response', deadline_at: Date.now() / 1000 - 1 }, async () => {}],
    [{ execution_id: executionId, accounting_mode: 'unknown', deadline_at: Date.now() / 1000 + 900 }, async () => {}],
  ]) {
    let sends = 0;
    const fetch = createMeteredFetch({ activeExecution: async () => active, finishRequest,
      reserveRequest: async () => { throw Error('must not reserve'); },
      baseFetch: async () => { sends++; return new Response('unexpected'); },
    });
    await assert.rejects(fetch(endpoint, worker), /receipt writer|not recognized/);
    assert.equal(sends, 0);
  }
});

test('ordinary Chat passes only while no runway execution owns the transport', async () => {
  let sends = 0;
  const fetch = createMeteredFetch({ baseFetch: async () => { sends++; return new Response('ok'); },
    activeExecution: async () => null, reserveRequest: async () => { throw Error('unexpected reserve'); } });
  await fetch(endpoint, { method: 'POST', body: '{}' });
  assert.equal(sends, 1);
  await assert.rejects(fetch('https://api.openai.com/v1/responses',
    { method: 'POST', body: '{}' }), /outside the subscription/);
  await assert.rejects(fetch(endpoint, worker), /no active execution/);
  await assert.rejects(fetch(endpoint, { ...worker, headers: { session_id: `model-run-${executionId}` } }), /no active execution/);
  assert.equal(sends, 1);
});

test('one exact worker request is reserved before send and its replay is denied', async () => {
  let sends = 0;
  const reservations = [];
  const fetch = createMeteredFetch({ baseFetch: async () => { sends++; return new Response('ok'); },
    activeExecution: async () => executionId, reserveRequest: async receipt => {
      reservations.push(receipt);
      return { admitted: reservations.length === 1 };
    } });
  await fetch(endpoint, worker);
  assert.equal(sends, 1);
  assert.equal(reservations[0].request_id, executionId);
  assert.equal(reservations[0].execution_id, executionId);
  assert.equal(reservations[0].reserved_tokens, 25000);
  assert.match(reservations[0].request_digest, /^[a-f0-9]{64}$/);
  await assert.rejects(fetch(endpoint, worker), /not admitted/);
  assert.equal(sends, 1);
});

test('active work rejects missing identity, alternate endpoint, and oversized body before send', async () => {
  let sends = 0;
  let reserves = 0;
  const fetch = createMeteredFetch({ baseFetch: async () => { sends++; return new Response('ok'); },
    activeExecution: async () => executionId, reserveRequest: async () => { reserves++; return { admitted: true }; } });
  await assert.rejects(fetch(endpoint, { method: 'POST', body: '{}' }), /exact worker session/);
  await assert.rejects(fetch(endpoint, { ...worker, headers: { session_id: `model-run-${executionId}` } }), /exact worker session/);
  await assert.rejects(fetch('https://api.openai.com/v1/responses', worker), /outside the subscription/);
  await assert.rejects(fetch(endpoint, { ...worker, body: 'x'.repeat(20001) }), /input allowance/);
  assert.equal(sends, 0);
  assert.equal(reserves, 0);
});

test('ledger failure fails closed before any network send', async () => {
  let sends = 0;
  const fetch = createMeteredFetch({ baseFetch: async () => { sends++; return new Response('ok'); },
    activeExecution: async () => { throw Error('ledger unavailable'); }, reserveRequest: async () => ({ admitted: true }) });
  await assert.rejects(fetch(endpoint, worker), /ledger unavailable/);
  assert.equal(sends, 0);
});

test('native Codex request with the full session ID reaches reservation before send', async () => {
  let sends = 0;
  let reservations = 0;
  const guarded = createGlobalMeteredFetch({
    baseFetch: async () => { sends++; return new Response('unexpected send'); },
    activeExecution: async () => executionId,
    reserveRequest: async receipt => {
      reservations++;
      assert.equal(receipt.execution_id, executionId);
      return { admitted: false };
    },
  });
  await assert.rejects(guarded(endpoint, { ...worker,
    headers: { session_id: workerSession(executionId).id } }), /not admitted/);
  assert.equal(reservations, 1);
  assert.equal(sends, 0);
});

test('global guard keeps ordinary Chat available but rejects the paid Responses URL', async () => {
  let sends = 0;
  const guarded = createGlobalMeteredFetch({
    baseFetch: async () => { sends++; return new Response('ok'); },
    activeExecution: async () => null,
    reserveRequest: async () => { throw Error('unexpected reservation'); },
  });
  await guarded(endpoint, { method: 'POST', body: '{}' });
  await assert.rejects(guarded('https://api.openai.com/v1/responses',
    { method: 'POST', body: '{}' }), /outside the subscription/);
  assert.equal(sends, 1);
});

test('an active worker blocks unrelated fetch egress', async () => {
  let sends = 0;
  const guarded = createGlobalMeteredFetch({
    baseFetch: async () => { sends++; return new Response('unexpected send'); },
    activeExecution: async () => executionId,
    reserveRequest: async () => { throw Error('unexpected reservation'); },
  });
  await assert.rejects(guarded('https://example.com/',
    { method: 'GET' }), /outside the subscription/);
  assert.equal(sends, 0);
});

test('zstd requests are limited by decoded size before reservation', async () => {
  let sends = 0;
  let reserves = 0;
  const guarded = createMeteredFetch({
    baseFetch: async () => { sends++; return new Response('ok'); },
    activeExecution: async () => executionId,
    reserveRequest: async () => { reserves++; return { admitted: true }; },
  });
  const headers = { session_id: workerSession(executionId).id,
    'content-encoding': 'zstd' };
  await guarded(endpoint, { method: 'POST', headers,
    body: zstdCompressSync(JSON.stringify({ model: 'gpt-5.6-luna' })) });
  assert.equal(sends, 1);
  assert.equal(reserves, 1);
  await assert.rejects(guarded(endpoint, { method: 'POST', headers,
    body: zstdCompressSync('x'.repeat(50000)) }), /decoded request exceeds/);
  await assert.rejects(guarded(endpoint, { method: 'POST',
    headers: { ...headers, 'content-encoding': 'gzip' }, body: 'compressed?' }),
  /unsupported encoding/);
  assert.equal(sends, 1);
  assert.equal(reserves, 1);
});

test('the admitted native request carries the exact bounded output allowance', async () => {
  const order = [];
  const guarded = createMeteredFetch({
    baseFetch: async request => {
      order.push('send');
      assert.equal(request.headers.get('session_id'), workerSession(executionId).id);
      assert.equal(request.headers.get('content-encoding'), 'zstd');
      const bytes = Buffer.from(await request.arrayBuffer());
      const payload = JSON.parse(zstdDecompressSync(bytes).toString('utf8'));
      assert.equal(payload.model, 'gpt-5.6-luna');
      assert.equal(payload.max_output_tokens, 1800);
      return new Response('ok');
    },
    activeExecution: async () => executionId,
    reserveRequest: async receipt => {
      order.push('reserve');
      assert.equal(receipt.reserved_tokens, 25000);
      return { admitted: true };
    },
  });
  const request = new Request(endpoint, { method: 'POST',
    headers: { session_id: workerSession(executionId).id, 'content-encoding': 'zstd' },
    body: zstdCompressSync(JSON.stringify({ model: 'gpt-5.6-luna' })) });
  await guarded(request);
  assert.deepEqual(order, ['reserve', 'send']);
});

test('changed model, tools, and output ceiling fail before reservation or send', async () => {
  let reserves = 0;
  let sends = 0;
  const guarded = createMeteredFetch({
    baseFetch: async () => { sends++; return new Response('unexpected'); },
    activeExecution: async () => executionId,
    reserveRequest: async () => { reserves++; return { admitted: true }; },
  });
  const request = payload => guarded(endpoint, { ...worker, body: JSON.stringify(payload) });
  await assert.rejects(request({ model: 'gpt-6-luna' }), /model or tool scope/);
  await assert.rejects(request({ model: 'gpt-5.6-luna', tools: [{ type: 'function' }] }), /model or tool scope/);
  await assert.rejects(request({ model: 'gpt-5.6-luna', tools: {} }), /model or tool scope/);
  await assert.rejects(request({ model: 'gpt-5.6-luna', max_output_tokens: 1801 }), /output ceiling/);
  assert.equal(reserves, 0);
  assert.equal(sends, 0);
});
