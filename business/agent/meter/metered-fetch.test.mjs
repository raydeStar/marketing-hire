import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createMeteredFetch } from './metered-fetch.mjs';

const executionId = 'a'.repeat(32);
const endpoint = 'https://chatgpt.com/backend-api/codex/responses';
const worker = { method: 'POST', headers: { session_id: `model-run-${executionId}` }, body: '{"model":"gpt-5.6-luna"}' };

test('ordinary Chat passes only while no runway execution owns the transport', async () => {
  let sends = 0;
  const fetch = createMeteredFetch({ baseFetch: async () => { sends++; return new Response('ok'); },
    activeExecution: async () => null, reserveRequest: async () => { throw Error('unexpected reserve'); } });
  await fetch(endpoint, { method: 'POST', body: '{}' });
  assert.equal(sends, 1);
  await assert.rejects(fetch(endpoint, worker), /no active execution/);
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
