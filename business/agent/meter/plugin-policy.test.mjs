import assert from 'node:assert/strict';
import { test } from 'node:test';
import { configureAiTransportHost, getAiTransportHost } from '@openclaw/ai';
import meter from './index.mjs';
import { workerSession } from './worker-session.mjs';

function workerConfig() {
  return { plugins: { entries: { 'marketing-request-meter': {
    hooks: { allowConversationAccess: true },
  } } }, agents: { entries: { 'runway-worker': { tools: { deny: ['*'] },
    params: { maxTokens: 1800 }, models: {
    'openai/gpt-5.6-luna': { agentRuntime: { id: 'openclaw' },
      params: { transport: 'sse' } },
  } } }, defaults: { model: { primary: 'openai/gpt-5.6-luna', fallbacks: [] } } } };
}

test('unclaimed worker turns are blocked before inference while owner Chat stays available', () => {
  const previous = getAiTransportHost();
  const previousFetch = globalThis.fetch;
  const config = workerConfig();
  try {
    let beforeRun;
    let service;
    let status;
    meter.register({ config,
      on(name, callback) { if (name === 'before_agent_run') beforeRun = callback; },
      registerService(entry) { service = entry; },
      registerGatewayMethod(name, callback) {
        if (name === 'marketing.meter.status') status = callback;
      },
    });
    service.start();
    assert.equal(status().policyReady, true);
    assert.equal(status().guardInstalled, true);
    assert.equal(status().nativeGuarded, true);
    assert.equal(status().ready, true);
    assert.deepEqual(beforeRun({}, { agentId: 'main' }), { outcome: 'pass' });
    assert.deepEqual(beforeRun({}, { agentId: 'runway-worker',
      sessionKey: workerSession('a'.repeat(32)).key }),
    { outcome: 'block', reason: 'Marketing worker requires an active metered assignment' });
    config.agents.entries['runway-worker'].models['openai/gpt-5.6-luna'].params.transport = 'websocket';
    assert.equal(status().ready, false);
    config.agents.entries['runway-worker'].models['openai/gpt-5.6-luna'].params.transport = 'sse';
    config.plugins.entries['marketing-request-meter'].hooks.allowConversationAccess = false;
    assert.equal(status().ready, false);
    config.plugins.entries['marketing-request-meter'].hooks.allowConversationAccess = true;
    config.agents.entries['runway-worker'].tools.deny = [];
    assert.equal(status().ready, false);
  } finally { configureAiTransportHost(previous); globalThis.fetch = previousFetch; }
});

test('active worker repairs a late OpenClaw host replacement before inference', async () => {
  const previous = getAiTransportHost();
  const previousFetch = globalThis.fetch;
  const oldActive = process.env.OFFLINE_LEDGER_ACTIVE;
  process.env.OFFLINE_LEDGER_ACTIVE = '1';
  let beforeRun;
  let service;
  let status;
  let sends = 0;
  try {
    meter.register({ config: workerConfig(),
      on(name, callback) { if (name === 'before_agent_run') beforeRun = callback; },
      registerService(entry) { service = entry; },
      registerGatewayMethod(name, callback) {
        if (name === 'marketing.meter.status') status = callback;
      },
    });
    service.start();
    configureAiTransportHost({ ...getAiTransportHost(),
      buildModelFetch: () => async () => { sends++; throw Error('unexpected send'); } });
    globalThis.fetch = async () => { sends++; throw Error('unexpected native send'); };
    assert.deepEqual(beforeRun({}, { agentId: 'runway-worker',
      sessionKey: workerSession('a'.repeat(32)).key }),
    { outcome: 'pass' });
    assert.equal(status().guardInstalled, true);
    assert.equal(status().nativeGuarded, true);
    const fetch = getAiTransportHost().buildModelFetch({ provider: 'openai', id: 'gpt-5.6-luna' });
    await assert.rejects(fetch('https://api.openai.com/v1/responses',
      { method: 'POST', body: '{}' }), /outside the subscription/);
    assert.equal(sends, 0);
  } finally {
    configureAiTransportHost(previous);
    globalThis.fetch = previousFetch;
    if (oldActive === undefined) delete process.env.OFFLINE_LEDGER_ACTIVE;
    else process.env.OFFLINE_LEDGER_ACTIVE = oldActive;
  }
});
