import test from 'node:test';
import assert from 'node:assert/strict';
import { configuration, sha256 } from './configuration.mjs';
import { contextHooks } from './plugin/context.mjs';

function fixture() {
  const text = 'Fictional scoped source: notes/demo.md. The raven has three appointments.';
  return { schemaVersion: 1, runId: 'a'.repeat(32), brokerOrigin: 'http://host.docker.internal:5182',
    model: 'gpt-5.6-luna', reasoning: 'high', grantToken: 'b'.repeat(64),
    context: { schemaVersion: 1, text, contentHash: sha256(text), profileDigest: 'c'.repeat(64) } };
}

test('worker receives only a task grant; model and MCP routes stay bound to the task broker', () => {
  const input = fixture(); const result = configuration(input); const wire = JSON.stringify(result.config);
  assert.ok(!wire.includes(input.grantToken));
  assert.match(result.config.models.providers.thaddeus.baseUrl, /:5182\/worker\/a{32}\/v1$/);
  assert.equal(result.config.mcp.servers.thaddeus.url, 'http://host.docker.internal:5182/worker/' + input.runId + '/mcp');
  assert.deepEqual(result.config.agents.defaults.model.fallbacks, []);
  assert.equal(result.config.models.providers.thaddeus.agentRuntime.id, 'openclaw');
  assert.equal(result.config.models.catalogRefresh.enabled, false);
  assert.equal(result.config.cron.enabled, false);
  assert.equal(result.config.agents.defaults.heartbeat.every, '0m');
  assert.deepEqual(result.config.agents.defaults.skills, []);
  assert.ok(result.config.mcp.servers.thaddeus.toolFilter.include.includes('thaddeus_search_public_web'));
  assert.deepEqual(result.binding.capabilities, result.config.mcp.servers.thaddeus.toolFilter.include);
  assert.equal(result.config.tools.web.search.enabled, false);
  assert.equal(result.config.tools.web.fetch.enabled, false);
  // Backends can provide a worker-local relay without encoding a VM vendor in the engine contract.
  const relayed = configuration({ ...input, brokerOrigin: 'http://127.0.0.1:5182' });
  assert.equal(relayed.config.mcp.servers.thaddeus.url, `http://127.0.0.1:5182/worker/${input.runId}/mcp`);
});

test('reject host API, direct model lane, internet destinations, credentials, and modified context', () => {
  for (const brokerOrigin of ['http://localhost:5182', 'http://host.docker.internal:5179',
    'http://host.docker.internal:5181', 'https://example.com:5182',
    'http://user:password@host.docker.internal:5182', 'http://host.docker.internal:5182/other'])
    assert.throws(() => configuration({ ...fixture(), brokerOrigin }));
  assert.throws(() => configuration({ ...fixture(), apiKey: 'never-copy-a-provider-key' }));
  const input = fixture(); input.context.text += ' tampered';
  assert.throws(() => configuration(input));
});

test('context delivery is bound to one agent and session; absent or different identity blocks the run', () => {
  const prepared = configuration(fixture());
  const hooks = contextHooks(prepared.config.plugins.entries['thaddeus-context'].config, prepared.context);
  const owner = { agentId: 'thaddeus', sessionKey: prepared.binding.sessionKey };
  assert.equal(hooks.before_prompt_build({}, owner).prependContext, prepared.context.text);
  assert.equal(hooks.before_agent_run({}, owner).outcome, 'pass');
  for (const other of [undefined, {}, { ...owner, agentId: 'main' }, { ...owner, sessionKey: 'agent:thaddeus:other' }]) {
    assert.equal(hooks.before_prompt_build({}, other), undefined);
    assert.equal(hooks.before_agent_run({}, other).outcome, 'block');
  }
  assert.throws(() => contextHooks(prepared.config.plugins.entries['thaddeus-context'].config,
    { ...prepared.context, profileDigest: 'd'.repeat(64) }));
});
