import assert from 'node:assert/strict';
import { test } from 'node:test';
import { captureModelResponse } from './response-receipt.mjs';
import { createMeteredFetch } from './metered-fetch.mjs';
import { workerSession } from './worker-session.mjs';

const headers = { 'content-type': 'text/event-stream; charset=utf-8' };
const completed = { type: 'response.completed', response: { id: 'resp_fixture',
  status: 'completed', usage: { input_tokens: 5, output_tokens: 3, total_tokens: 8 },
  output: [{ text: 'Résumé 🧙' }] } };
const frame = event => `data: ${JSON.stringify(event)}\r\n\r\n`;

test('split UTF-8 SSE saves actual usage once and preserves original response bytes', async () => {
  const bytes = Buffer.from(`: heartbeat\r\n\r\n${frame(completed)}data: [DONE]\r\n\r\n`);
  let cursor = 0;
  const source = new Response(new ReadableStream({ pull(controller) {
    if (cursor === bytes.length) controller.close();
    else controller.enqueue(bytes.subarray(cursor, ++cursor));
  } }), { headers });
  const receipts = [];
  const response = await captureModelResponse(source, async receipt => receipts.push(receipt));
  assert.deepEqual(Buffer.from(await response.arrayBuffer()), bytes);
  assert.equal(receipts.length, 1);
  assert.equal(receipts[0].reported_tokens, 8);
  assert.equal(receipts[0].response_receipt.provider_response_id, 'resp_fixture');
  assert.match(receipts[0].response_receipt.evidence_digest, /^[a-f0-9]{64}$/);
});

test('missing, malformed, and inconsistent usage never become zero-token receipts', async () => {
  for (const input of [frame({ ...completed, response: { id: 'x' } }),
    frame({ ...completed, response: { usage: { input_tokens: 2, output_tokens: 3, total_tokens: 4 } } }),
    'data: {broken}\n\n', 'data: [DONE]\n\n', 'data: ' + 'x'.repeat(2 * 1024 * 1024 + 1)]) {
    const receipts = [];
    const response = await captureModelResponse(new Response(input, { headers }), async r => receipts.push(r));
    assert.equal(await response.text(), input);
    assert.equal(receipts.length, 1);
    assert.equal(receipts[0].status, 'unknown');
    assert.equal(receipts[0].reported_tokens, null);
  }
});

test('valid SSE is metered by its terminal evidence even without the expected MIME label', async () => {
  for (const type of [null, 'text/plain;charset=UTF-8', 'application/json']) {
    const receipts = [];
    const bytes = Buffer.from(frame(completed));
    const response = await captureModelResponse(new Response(bytes, {
      headers: type ? { 'content-type': type } : {},
    }), async receipt => receipts.push(receipt));
    assert.deepEqual(Buffer.from(await response.arrayBuffer()), bytes);
    assert.equal(receipts.length, 1);
    assert.equal(receipts[0].status, 'reported');
    assert.equal(receipts[0].reported_tokens, 8);
  }
});

test('HTTP errors and unsupported bodies retain unknown usage without storing error text', async () => {
  for (const options of [{ status: 400 }, { status: 200 }]) {
    const receipts = [];
    const response = await captureModelResponse(new Response('private server error', options), async r => receipts.push(r));
    assert.equal(await response.text(), 'private server error');
    assert.equal(receipts[0].reported_tokens, null);
    assert.equal(JSON.stringify(receipts).includes('private server error'), false);
  }
});

test('failed and incomplete responses can report real consumption', async () => {
  for (const type of ['response.failed', 'response.incomplete']) {
    const receipts = [];
    const response = await captureModelResponse(new Response(frame({ ...completed, type }), { headers }), async r => receipts.push(r));
    await response.text();
    assert.equal(receipts[0].reported_tokens, 8);
    assert.equal(receipts[0].response_receipt.terminal_type, type);
  }
});

test('disconnects and consumer cancellation hold the request instead of inventing usage', async () => {
  let count = 0;
  const receipts = [];
  const response = await captureModelResponse(new Response(new ReadableStream({ pull(controller) {
    if (count++ === 0) controller.enqueue(Buffer.from('data: {"type":"response.created"}\n\n'));
    else controller.error(Error('wire closed'));
  } }), { headers }), async r => receipts.push(r));
  await assert.rejects(response.text(), /wire closed/);
  assert.equal(receipts[0].status, 'unknown');
  let cancelled = false;
  const cancellation = [];
  const cancelResponse = await captureModelResponse(new Response(new ReadableStream({
    cancel() { cancelled = true; },
  }), { headers }), async r => cancellation.push(r));
  await cancelResponse.body.cancel();
  assert.equal(cancelled, true);
  assert.equal(cancellation[0].response_receipt.terminal_type, 'stream_cancelled');
});

test('receipt storage failure prevents the caller receiving a successful completion', async () => {
  const response = await captureModelResponse(new Response(frame(completed), { headers }), async () => {
    throw Error('ledger unavailable');
  });
  await assert.rejects(response.text(), /ledger unavailable/);
});

test('fetch binds one terminal usage receipt to the digest reserved before the physical send', async () => {
  const id = 'a'.repeat(32);
  const order = [];
  let reservation;
  let outcome;
  const guarded = createMeteredFetch({ activeExecution: async () => id,
    reserveRequest: async r => { order.push('reserve'); reservation = r; return { admitted: true }; },
    baseFetch: async () => { order.push('send'); return new Response(frame(completed), { headers }); },
    finishRequest: async r => { order.push('receipt'); outcome = r; },
  });
  const response = await guarded('https://chatgpt.com/backend-api/codex/responses', {
    method: 'POST', headers: { session_id: workerSession(id).header }, body: '{"model":"gpt-5.6-luna"}',
  });
  await response.text();
  assert.deepEqual(order, ['reserve', 'send', 'receipt']);
  assert.equal(outcome.request_id, reservation.request_id);
  assert.equal(outcome.request_digest, reservation.request_digest);
  assert.equal(outcome.reported_tokens, 8);
});
