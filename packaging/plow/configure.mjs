// Keep Plow's identity, channel, provider, boot and reporter. Our web entrance takes port 3000.
export function configureCockpit(config) {
  config.gateway.port = 18789;
  config.gateway.controlUi.enabled = false;
  config.agents.ownership = 'explicit';
  config.agents.defaults.systemAgent = {agentId: 'main'};
  config.agents.defaults.heartbeat = { agentId: 'main', every: '0m' };
  config.cron = { enabled: false };
  config.agents.entries['runway-worker'] = {
    identity: { name: 'Marketing employee' }, workspace: '/var/lib/plow/runway-room',
    tools: { deny: ['*'] }, params: { maxTokens: 1800 },
    model: { primary: 'plow/z-ai/glm-5.2', fallbacks: [] },
    models: {'plow/z-ai/glm-5.2': {agentRuntime: {id: 'openclaw'}, params: {cacheRetention: 'short'}}},
  };
  const workerModel = config.models.providers.plow.models.find(model => model.id === 'z-ai/glm-5.2');
  if (!workerModel) throw new Error('Plow worker model changed; qualify the new route before booting.');
  workerModel.compat = {...workerModel.compat, sendSessionAffinityHeaders: true, supportsUsageInStreaming: true, maxTokensField: 'max_tokens'};
  for (const role of ['ceo', 'marketing', 'worker']) config.agents.entries['meeting-' + role] = {
    identity: { name: 'Marketing ' + role }, workspace: '/var/lib/plow/meeting-room', tools: { deny: ['*'] },
  };
  // This guard fails closed on an unqualified provider/version. Never silently substitute scripted work.
  config.plugins.load.paths.push('/app/marketing-meter');
  config.plugins.entries['marketing-request-meter'] = { enabled: true, hooks: { allowConversationAccess: true } };
  return config;
}
