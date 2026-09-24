import assert from 'node:assert/strict';
import { test } from 'node:test';
import { zstdDecompressSync } from 'node:zlib';
import { configureAiTransportHost, getAiTransportHost } from '@openclaw/ai';
import { createOpenAIResponsesTransportStreamFn,
  requestPreparedOpenAIResponsesCompaction } from '@openclaw/ai/transports';
import { createGlobalMeteredFetch, createMeteredFetch } from './metered-fetch.mjs';
import { workerSession } from './worker-session.mjs';

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
    apiKey: 'dummy-offline-key', sessionId: workerSession(executionId).id,
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
      { apiKey: 'dummy-offline-key', sessionId: workerSession(executionId).id,
        transport: 'sse', signal: AbortSignal.timeout(3000) },
    ));
    assert.equal(reservations, 1);
    assert.equal(sends, 0);
  } finally { configureAiTransportHost(previous); }
});

test('OpenClaw lazy stream setup keeps the installed host fetch guard', async () => {
  const { stream } = await import('/app/dist/plugin-sdk/llm.js');
  const previous = getAiTransportHost();
  let activeReads = 0;
  let sends = 0;
  configureAiTransportHost({ ...previous, buildModelFetch: () => createMeteredFetch({
    baseFetch: async () => { sends++; throw Error('unexpected lazy stream send'); },
    activeExecution: async () => { activeReads++; return executionId; },
    reserveRequest: async () => ({ admitted: false }),
  }) });
  try {
    // The ordinary Responses alias omits the ChatGPT session header. The guard
    // must still run and refuse that mismatch after OpenClaw initializes lazily.
    const result = await stream({ ...model, api: 'openai-responses' }, context, {
      apiKey: 'dummy-offline-key', sessionId: workerSession(executionId).id,
      transport: 'sse', signal: AbortSignal.timeout(3000),
    }).result();
    assert.equal(result.stopReason, 'error');
    assert.ok(activeReads >= 1);
    assert.equal(sends, 0);
  } finally { configureAiTransportHost(previous); }
});

test('installed paid Responses transport cannot send without a runway claim', async () => {
  const { stream } = await import('/app/dist/plugin-sdk/llm.js');
  const previous = getAiTransportHost();
  let sends = 0;
  configureAiTransportHost({ ...previous, buildModelFetch: () => createMeteredFetch({
    baseFetch: async () => { sends++; throw Error('unexpected paid send'); },
    activeExecution: async () => null,
    reserveRequest: async () => { throw Error('unexpected reservation'); },
  }) });
  try {
    const result = await stream({ ...model, api: 'openai-responses',
      baseUrl: 'https://api.openai.com/v1' }, context, {
      apiKey: 'dummy-offline-key', transport: 'sse',
      signal: AbortSignal.timeout(3000),
    }).result();
    assert.equal(result.stopReason, 'error');
    assert.equal(sends, 0);
  } finally { configureAiTransportHost(previous); }
});

test('installed native OAuth SSE path admits one request and reports its usage', async () => {
  const { stream } = await import('/app/dist/plugin-sdk/llm.js');
  const previousFetch = globalThis.fetch;
  const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  const fakeOAuth = [encode({ alg: 'none' }), encode({ 'https://api.openai.com/auth':
    { chatgpt_account_id: 'offline-fixture-account' } }), 'offline'].join('.');
  const event = { type: 'response.completed', response: { id: 'resp_offline',
    status: 'completed', output: [{ type: 'message', id: 'msg_offline', role: 'assistant',
      content: [{ type: 'output_text', text: 'Offline receipt only' }] }],
    usage: { input_tokens: 5, output_tokens: 3, total_tokens: 8 } } };
  let sends = 0;
  const reservations = [];
  const outcomes = [];
  globalThis.fetch = createGlobalMeteredFetch({
    baseFetch: async request => {
      sends++;
      const bytes = Buffer.from(await request.arrayBuffer());
      const payload = JSON.parse((request.headers.get('content-encoding') === 'zstd' ? zstdDecompressSync(bytes) : bytes).toString('utf8'));
      assert.equal('max_output_tokens' in payload, false);
      return new Response(`data: ${JSON.stringify(event)}\n\n`,
        { status: 200, headers: { 'content-type': 'text/event-stream' } });
    },
    activeExecution: async () => ({ execution_id: executionId, accounting_mode: 'post_response', deadline_at: Date.now()/1000+900 }),
    reserveRequest: async receipt => {
      reservations.push(receipt);
      return { admitted: reservations.length === 1 };
    },
    finishRequest: async receipt => outcomes.push(receipt),
  });
  try {
    const result = await stream({ ...model, api: 'openai-chatgpt-responses' }, context,
      { apiKey: fakeOAuth, sessionId: workerSession(executionId).id,
        transport: 'sse', signal: AbortSignal.timeout(3000) }).result();
    assert.equal(result.stopReason, 'stop');
    assert.equal(result.content[0]?.text, 'Offline receipt only');
    assert.equal(result.usage.totalTokens, 8);
    assert.equal(sends, 1);
    assert.equal(reservations.length, 1);
    assert.equal(reservations[0].execution_id, executionId);
    assert.equal(reservations[0].accounting_mode, 'post_response');
    assert.equal(outcomes.length, 1);
    assert.equal(outcomes[0].request_digest, reservations[0].request_digest);
    assert.equal(outcomes[0].reported_tokens, result.usage.totalTokens);
    assert.equal(outcomes[0].response_receipt.terminal_type, 'response.completed');
    const repeated = await stream({ ...model, api: 'openai-chatgpt-responses' }, context,
      { apiKey: fakeOAuth, sessionId: workerSession(executionId).id,
        transport: 'sse', signal: AbortSignal.timeout(3000) }).result();
    assert.equal(repeated.stopReason, 'error');
    assert.equal(reservations.length, 2);
    assert.equal(sends, 1);
    assert.equal(outcomes.length, 1);
  } finally { globalThis.fetch = previousFetch; }
});
