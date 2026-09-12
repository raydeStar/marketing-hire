import { createHash, randomBytes } from 'node:crypto';

export const runtimeVersion = '2026.9.4';
export const stateRoot = '/home/agent/.openclaw';
export const pluginRoot = '/opt/thaddeus/plugin';
export const sha256 = value => createHash('sha256').update(value).digest('hex');

// This is worker configuration, not a host permission boundary. The broker keeps the keys to the estate.
export function configuration(input) {
  const keys = ['schemaVersion', 'runId', 'brokerOrigin', 'model', 'reasoning', 'context', 'grantToken'];
  if (!input || Object.keys(input).some(key => !keys.includes(key)) || input.schemaVersion !== 1 ||
      !/^[a-f0-9]{32}$/.test(input.runId) || !/^[a-f0-9]{64}$/.test(input.grantToken))
    throw new Error('Invalid worker binding.');
  const origin = new URL(input.brokerOrigin);
  if (origin.protocol !== 'http:' || !['host.docker.internal', '127.0.0.1'].includes(origin.hostname) ||
      !origin.port || Number(origin.port) < 1024 || ['5179', '5181'].includes(origin.port) ||
      origin.pathname !== '/' || origin.search || origin.hash || origin.username || origin.password)
    throw new Error('A dedicated local worker broker origin is required.');
  if (typeof input.model !== 'string' || !/^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/.test(input.model) ||
      !['low', 'medium', 'high'].includes(input.reasoning)) throw new Error('An exact compatible model profile is required.');
  const context = input.context;
  if (!context || context.schemaVersion !== 1 || typeof context.text !== 'string' ||
      !context.text.trim() || Buffer.byteLength(context.text) > 90000 ||
      !/^[a-f0-9]{64}$/.test(context.profileDigest) || sha256(context.text) !== context.contentHash)
    throw new Error('The prepared context hash or schema is invalid.');
  const sessionKey = `agent:thaddeus:${input.runId}`;
  const broker = `${origin.origin}/worker/${input.runId}`;
  const config = {
    gateway: { mode: 'local', bind: 'loopback', port: 18789,
      auth: { mode: 'token', token: '${THADDEUS_GATEWAY_TOKEN}', allowTailscale: false },
      controlUi: { enabled: false }, tailscale: { mode: 'off' }, reload: { mode: 'off' } },
    update: { checkOnStart: false, auto: { enabled: false } },
    telemetry: { enabled: false }, discovery: { mdns: { mode: 'off' } },
    cron: { enabled: false }, browser: { enabled: false },
    models: { mode: 'replace', catalogRefresh: { enabled: false }, providers: {
      thaddeus: { baseUrl: `${broker}/v1`, api: 'openai-completions', apiKey: '${THADDEUS_WORKER_TOKEN}',
        agentRuntime: { id: 'openclaw' },
        authHeader: true, models: [{ id: input.model, name: 'Task model through Thaddeus',
          reasoning: true, input: ['text'], contextWindow: 32768, maxTokens: 4096,
          compat: { supportsStore: true, supportsDeveloperRole: true, supportsReasoningEffort: true,
            supportsPromptCacheKey: false, supportsTemperature: false, supportsUsageInStreaming: true,
            requiresStringContent: true, maxTokensField: 'max_completion_tokens' } }] }
    } },
    agents: { defaults: { workspace: '/home/agent/thaddeus-artifacts', skipBootstrap: true,
      skills: [], maxConcurrent: 1, heartbeat: { every: '0m' }, compaction: { enabled: false },
      model: { primary: `thaddeus/${input.model}`, fallbacks: [] }, thinkingDefault: input.reasoning,
      sandbox: { mode: 'off' } }, entries: { thaddeus: { name: 'Sir Thaddeus' } } },
    tools: { profile: 'full', allow: ['read', 'write', 'edit', 'exec', 'process', 'bundle-mcp'],
      elevated: { enabled: false }, exec: { host: 'gateway', security: 'full', ask: 'off',
        timeoutSeconds: 60, notifyOnExit: false },
      web: { search: { enabled: false }, fetch: { enabled: false } } },
    plugins: { allow: ['thaddeus-context'], slots: { memory: 'none' },
      load: { paths: [pluginRoot] }, entries: {
        'thaddeus-context': { enabled: true,
          hooks: { allowConversationAccess: true, allowPromptInjection: true },
          config: { sessionKey, contextPath: `${stateRoot}/thaddeus-context.json`,
            contentHash: context.contentHash, profileDigest: context.profileDigest } }
      } },
    mcp: { servers: { thaddeus: { enabled: true, transport: 'streamable-http', url: `${broker}/mcp`,
      headers: { Authorization: 'Bearer ${THADDEUS_WORKER_TOKEN}' },
      connectionTimeoutMs: 5000, requestTimeoutMs: 20000,
      toolFilter: { include: ['thaddeus_read_note', 'thaddeus_ask_user', 'thaddeus_propose_import'] } } } }
  };
  return { config, context, binding: { schemaVersion: 1, runId: input.runId, sessionKey,
    runtimeVersion, contentHash: context.contentHash, profileDigest: context.profileDigest,
    configHash: sha256(JSON.stringify(config)) },
    environment: `THADDEUS_WORKER_TOKEN=${input.grantToken}\nTHADDEUS_GATEWAY_TOKEN=${randomBytes(32).toString('hex')}\n` };
}
