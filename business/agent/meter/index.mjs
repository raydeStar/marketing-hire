import { spawnSync } from 'node:child_process';
// Load OpenClaw's own fetch policy before wrapping it. Its LLM facade would
// otherwise initialize lazily on the first worker turn and replace our guard.
import '/app/dist/plugin-sdk/llm.js';
import { configureAiTransportHost, getAiTransportHost } from '@openclaw/ai';
import { createMeteredFetch } from './metered-fetch.mjs';

const LEDGER = '/opt/hire/bin/runway.py';
const VERSION = 'marketing-meter-v1';

function ledger(action, payload) {
  const result = spawnSync('python3', [LEDGER, action], {
    input: payload === undefined ? undefined : JSON.stringify(payload),
    encoding: 'utf8', timeout: 5000, maxBuffer: 65536,
  });
  if (result.error || result.status !== 0) {
    throw new Error('Marketing request meter could not establish a durable receipt');
  }
  try { return JSON.parse(result.stdout); }
  catch { throw new Error('Marketing request meter returned an invalid receipt'); }
}

export default {
  id: 'marketing-request-meter',
  name: 'Marketing request meter',
  description: 'Preflight admission for the bounded Marketing worker transport',
  register(api) {
    const workerRouteReady = () => {
      const worker = api.config?.agents?.entries?.['runway-worker'];
      const workerModel = worker?.models?.['openai/gpt-5.6-luna'];
      const modelDefaults = api.config?.agents?.defaults?.model;
      return workerModel?.agentRuntime?.id === 'openclaw' &&
        workerModel?.params?.transport === 'sse' &&
        modelDefaults?.primary === 'openai/gpt-5.6-luna' &&
        Array.isArray(modelDefaults?.fallbacks) && modelDefaults.fallbacks.length === 0;
    };
    let meteredBuild;
    const installGuard = () => {
      const previous = getAiTransportHost();
      if (previous.buildModelFetch === meteredBuild) return;
      const baseBuild = previous.buildModelFetch;
      meteredBuild = (model, timeoutMs, options) => {
        const baseFetch = baseBuild(model, timeoutMs, options);
        if (model.provider !== 'openai' || model.id !== 'gpt-5.6-luna') return baseFetch;
        return createMeteredFetch({ baseFetch,
          activeExecution: () => ledger('meter-active').execution_id,
          reserveRequest: receipt => ledger('model-reserve', receipt),
        });
      };
      configureAiTransportHost({ ...previous, buildModelFetch: meteredBuild });
    };
    installGuard();
    // Gateway startup installs its own host policy after plugin registration.
    // Re-wrap that final policy before the Gateway accepts worker calls.
    api.registerService({ id: 'marketing-request-meter', start: installGuard, stop() {} });
    api.registerGatewayMethod('marketing.meter.status', () => ({
      version: VERSION, policyReady: workerRouteReady(),
      guardInstalled: getAiTransportHost().buildModelFetch === meteredBuild,
      ready: workerRouteReady() && getAiTransportHost().buildModelFetch === meteredBuild,
      route: 'openai/gpt-5.6-luna', transport: 'sse',
    }));
  },
};
