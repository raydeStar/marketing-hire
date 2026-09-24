import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
// Load OpenClaw's own fetch policy before wrapping it. Its LLM facade would
// otherwise initialize lazily on the first worker turn and replace our guard.
import '/app/dist/plugin-sdk/llm.js';
import { configureAiTransportHost, getAiTransportHost } from '@openclaw/ai';
import { createGlobalMeteredFetch, createMeteredFetch } from './metered-fetch.mjs';
import { workerSession } from './worker-session.mjs';

const LEDGER = '/opt/hire/bin/runway.py';
const VERSION = 'marketing-meter-v5';
const COMPATIBLE_OPENCLAW = '2026.9.4';
// The pinned native Codex transport strips max_output_tokens, and the real
// subscription endpoint rejected that field with HTTP 400. A worker grant
// cannot claim an enforced output ceiling on this route yet.
const SUBSCRIPTION_OUTPUT_CAP_SUPPORTED = false;
const GLOBAL_GUARD_KEY = Symbol.for('marketing-request-meter.native-fetch-v5');
const installedOpenClaw = JSON.parse(readFileSync('/app/package.json', 'utf8')).version;

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
      const hookAccess = api.config?.plugins?.entries?.['marketing-request-meter']?.hooks?.allowConversationAccess;
      return workerModel?.agentRuntime?.id === 'openclaw' &&
        workerModel?.params?.transport === 'sse' &&
        Array.isArray(worker?.tools?.deny) && worker.tools.deny.includes('*') &&
        worker?.params?.maxTokens === 1800 &&
        hookAccess === true &&
        installedOpenClaw === COMPATIBLE_OPENCLAW &&
        modelDefaults?.primary === 'openai/gpt-5.6-luna' &&
        Array.isArray(modelDefaults?.fallbacks) && modelDefaults.fallbacks.length === 0;
    };
    let meteredBuild;
    // OpenClaw can register this plugin more than once in one process. A
    // process-wide identity prevents one physical native send from passing
    // through two copies of our reservation wrapper.
    let sharedGuard = globalThis[GLOBAL_GUARD_KEY];
    if (!sharedGuard) {
      sharedGuard = { fetch: createGlobalMeteredFetch({ baseFetch: globalThis.fetch,
        activeExecution: () => ledger('meter-active').execution_id,
        reserveRequest: receipt => ledger('model-reserve', receipt),
      }) };
      globalThis[GLOBAL_GUARD_KEY] = sharedGuard;
    }
    const meteredGlobalFetch = sharedGuard.fetch;
    const guardState = () => ({
      transportGuardInstalled: getAiTransportHost().buildModelFetch === meteredBuild,
      nativeFetchInstalled: globalThis.fetch === meteredGlobalFetch,
    });
    const installGuard = () => {
      const previous = getAiTransportHost();
      if (previous.buildModelFetch !== meteredBuild) {
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
      }
      if (globalThis.fetch !== meteredGlobalFetch) {
        globalThis.fetch = meteredGlobalFetch;
      }
    };
    installGuard();
    // Gateway startup installs its own host policy after plugin registration.
    // Re-wrap that final policy before the Gateway accepts worker calls.
    api.registerService({ id: 'marketing-request-meter', start: installGuard, stop() {} });
    api.on('before_agent_run', (_event, context) => {
      if (context.agentId !== 'runway-worker') return { outcome: 'pass' };
      try {
        const executionId = ledger('meter-active').execution_id;
        const exactSession = executionId && context.sessionKey === workerSession(executionId).key;
        // OpenClaw may refresh its transport host while preparing this turn,
        // after Gateway startup. The gate runs immediately before inference.
        if (executionId && exactSession && workerRouteReady()) installGuard();
        const guards = guardState();
        if (executionId && exactSession && workerRouteReady() && SUBSCRIPTION_OUTPUT_CAP_SUPPORTED &&
            guards.transportGuardInstalled && guards.nativeFetchInstalled) {
          return { outcome: 'pass' };
        }
      } catch { /* Missing ledger is a denial, never a fallback to inference. */ }
      return { outcome: 'block', reason: 'Marketing worker requires an active metered assignment' };
    });
    api.registerGatewayMethod('marketing.meter.status', () => {
      // OpenClaw can refresh the transport after a completed turn. The host
      // asks for status before admission; repair the exact pinned route here.
      try { if (workerRouteReady()) installGuard(); }
      catch { /* An unreadable guard is never reported ready. */ }
      const policyReady = workerRouteReady();
      const guards = guardState();
      const guardInstalled = guards.transportGuardInstalled && guards.nativeFetchInstalled;
      return { version: VERSION, policyReady, guardInstalled,
        nativeGuarded: guards.nativeFetchInstalled,
        outputCapSupported: SUBSCRIPTION_OUTPUT_CAP_SUPPORTED,
        ready: policyReady && guardInstalled && SUBSCRIPTION_OUTPUT_CAP_SUPPORTED,
        blocker: !policyReady ? 'worker_policy_incompatible' :
          !guardInstalled ? 'request_guard_unavailable' :
          !SUBSCRIPTION_OUTPUT_CAP_SUPPORTED ? 'subscription_endpoint_rejects_output_cap' : null,
        route: 'openai/gpt-5.6-luna', transport: 'sse',
      };
    });
  },
};
