import assert from 'node:assert/strict';
import { test } from 'node:test';
import { configureAiTransportHost, getAiTransportHost } from '@openclaw/ai';
import { createOpenAIResponsesTransportStreamFn,
  requestPreparedOpenAIResponsesCompaction } from '@openclaw/ai/transports';
import { createMeteredFetch } from './metered-fetch.mjs';

const executionId = 'a'.repeat(32);
const model = {
  id: 'gpt-5.6-luna', name: 'GPT-5.6 Luna', provider: 'openai',
  api: 'openclaw-openai-chatgpt-responses-transport',
  baseUrl: 'https://chatgpt.com/backend-api/codex', reasoning: true,
  input: ['text'], cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0 },
  contextWindow: 200000, maxTokens: 1800,
};
const context = { systemPrompt: 'Fixture only',
  messages: [{ role: 'user', content: 'Fixture only', timestamp: 0 }], tools: [] };

async function runSse(onPayload) {
  const events = await createOpenAIResponsesTransportStreamFn()(model, context, {
    apiKey: 'dummy-offline-key', sessionId: `model-run-${executionId}`,
    transport: 'sse', signal: AbortSignal.timeout(3000), onPayload,
  });
  return events.result();
}

test('installed Responses HTTP transport refuses a first request before network dispatch', async () => {
  const previous = getAiTransportHost();
  let reservations = 0;
  let sends = 0;
  configureAiTransportHost({ ...previous, buildModelFetch: () => createMeteredFetch({
    baseFetch: async () => { sends++; throw Error('unexpected network send'); },
    activeExecution: async () => executionId,
    reserveRequest: async () => { reservations++; return { admitted: false }; },
  }) });
  try {
    const result = await runSse();
    assert.equal(result.stopReason, 'error');
    assert.equal(reservations, 1);
    assert.equal(sends, 0);
  } finally { configureAiTransportHost(previous); }
});

test('installed encrypted-content recovery cannot send an unreserved second request', async () => {
  const previous = getAiTransportHost();
  let reservations = 0;
  let sends = 0;
  configureAiTransportHost({ ...previous, buildModelFetch: () => createMeteredFetch({
    baseFetch: async () => {
      sends++;
      return new Response(JSON.stringify({ error: { message: 'invalid encrypted content',
        type: 'invalid_request_error', code: 'invalid_encrypted_content' } }),
      { status: 400, headers: { 'content-type': 'application/json' } });
    },
    activeExecution: async () => executionId,
    reserveRequest: async () => ({ admitted: ++reservations === 1 }),
  }) });
  try {
    const result = await runSse(payload => ({ ...payload, input: [
      ...payload.input, { type: 'compaction', encrypted_content: 'opaque-fixture' },
    ] }));
    assert.equal(result.stopReason, 'error');
    assert.equal(reservations, 2);
    assert.equal(sends, 1);
  } finally { configureAiTransportHost(previous); }
});

test('installed compact endpoint is denied before dispatch without a reservation', async () => {
  const previous = getAiTransportHost();
  let reservations = 0;
  let sends = 0;
  configureAiTransportHost({ ...previous, buildModelFetch: () => createMeteredFetch({
    baseFetch: async () => { sends++; throw Error('unexpected compact send'); },
    activeExecution: async () => executionId,
    reserveRequest: async () => { reservations++; return { admitted: false }; },
  }) });
  try {
    await assert.rejects(requestPreparedOpenAIResponsesCompaction(
      createOpenAIResponsesTransportStreamFn(), model, context,
      { apiKey: 'dummy-offline-key', sessionId: `model-run-${executionId}`,
        transport: 'sse', signal: AbortSignal.timeout(3000) },
    ));
    assert.equal(reservations, 1);
    assert.equal(sends, 0);
  } finally { configureAiTransportHost(previous); }
});
