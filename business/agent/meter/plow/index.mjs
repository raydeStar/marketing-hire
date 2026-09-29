import {readFileSync} from 'node:fs';
import {spawnSync} from 'node:child_process';
import {createRequire} from 'node:module';
import {createPlowRequestGuards, PlowAdmissionError, route} from './fetch.mjs';
import {workerSession} from '../worker-session.mjs';
import {plowApiEndpoints} from './api-endpoints.mjs';
import {isTextTurn, markTextTurn, registerTextMode, settled} from './text-mode.mjs';

const VERSION = 'marketing-meter-plow-v1';
// 2026.9.6 snapshots a plugin's bare package imports into a private dependency
// graph. The transport belongs to the running host, not that independent copy.
const hostRequire = createRequire('/app/package.json');
hostRequire('/app/dist/plugin-sdk/llm.js');
const {configureAiTransportHost, getAiTransportHost} = hostRequire('@openclaw/ai');
const installed = JSON.parse(readFileSync('/app/package.json', 'utf8')).version;
const GLOBAL_GUARD = Symbol.for('hirezero.plow-request-meter.v1');
function ledger(action, input) {
  const result = spawnSync('python3', ['/opt/hire/bin/runway.py', action], {
    input: input === undefined ? undefined : JSON.stringify(input), encoding: 'utf8', timeout: 5000, maxBuffer: 65536,
  });
  if (result.error || result.status !== 0) throw new Error('Plow worker could not establish its durable receipt');
  return JSON.parse(result.stdout);
}

export function policyReady(config) {
  const endpoints = plowApiEndpoints();
  const worker = config?.agents?.entries?.['runway-worker'];
  const provider = config?.models?.providers?.plow;
  const model = provider?.models?.find(model => model.id === 'z-ai/glm-5.2');
  const runtime = worker?.models?.[route];
  return Boolean(endpoints) && installed === '2026.9.6' && provider?.baseUrl === endpoints.api && provider?.api === 'openai-completions' &&
    model?.compat?.sendSessionAffinityHeaders === true && model.compat.supportsUsageInStreaming === true && model.compat.maxTokensField === 'max_tokens' &&
    runtime?.agentRuntime?.id === 'openclaw' && runtime.params?.cacheRetention === 'short' &&
    worker?.model?.primary === route && Array.isArray(worker.model.fallbacks) && !worker.model.fallbacks.length &&
    worker?.params?.maxTokens === 4096 && Array.isArray(worker?.tools?.deny) && worker.tools.deny.includes('*') &&
    config?.plugins?.entries?.['marketing-request-meter']?.hooks?.allowConversationAccess === true;
}

export default {
  id: 'marketing-request-meter', name: 'Marketing request meter',
  description: 'Durable request admission for the pinned Plow worker route',
  register(api) {
    const shared = globalThis[GLOBAL_GUARD] ??= createPlowRequestGuards({
      baseFetch: globalThis.fetch, activeExecution: () => ledger('meter-active'),
      reserveRequest: input => ledger('model-reserve', input), finishRequest: input => ledger('model-finish', input),
    });
    function install() {
      const previous = getAiTransportHost();
      if (previous.buildModelFetch !== shared.meteredBuild) {
        const baseBuild = previous.buildModelFetch;
        // All plugin generations share one wrapper. Ordinary owner requests keep
        // the host transport; bounded worker sends use the supplied Plow endpoint
        // and redirect:error, with no hidden dispatcher retries or redirects.
        shared.meteredBuild = (model, timeout, options) => {
          const fetch = shared.modelFetch(baseBuild(model, timeout, options), request => {
            const headers = new Headers(request.headers);
            for (const [name, value] of headers) headers.set(name, getAiTransportHost().resolveSecretSentinel(value));
            return shared.nativeFetch(new Request(request, {headers}));
          });
          return async (input, init) => {
            try {return await fetch(input, init);}
            catch (error) {
              // A refusal is terminal, not a network outage to retry eight times.
              if (error instanceof PlowAdmissionError || error?.name === 'PlowAdmissionError')
                return Response.json({error: {message: error.message, type: 'invalid_request_error', code: 'hirezero_worker_refused'}}, {status: 400});
              throw error;
            }
          };
        };
        configureAiTransportHost({...previous, buildModelFetch: shared.meteredBuild});
      }
      globalThis.fetch = shared.nativeFetch;
    }
    function guarded() {return getAiTransportHost().buildModelFetch === shared.meteredBuild && globalThis.fetch === shared.nativeFetch;}
    install();
    api.registerService({id: 'marketing-request-meter', start: install, stop() {}});
    api.on('before_agent_run', async (_event, context) => {
      try {
        let active = ledger('meter-active');
        if (context.agentId !== 'runway-worker') {
          // A text can't be asked to try again (a refused one gets no reply): it waits for the worker's turn to settle.
          if (active.execution_id && isTextTurn(context)) active = await settled(() => ledger('meter-active'));
          if (active.execution_id) return {outcome: 'block', reason: 'The marketing employee is settling its current metered turn. Try again shortly.'};
          if (isTextTurn(context)) markTextTurn(context.runId);
          return {outcome: 'pass'};
        }
        if (active.execution_id && context.sessionKey === workerSession(active.execution_id).key && policyReady(api.config) &&
            active.accounting_mode === 'post_response' && Number.isFinite(active.deadline_at) && active.deadline_at * 1000 > Date.now()) {
          install(); if (guarded()) return {outcome: 'pass'};
        }
      } catch { /* An unreadable claim is never a free turn. */ }
      return {outcome: 'block', reason: 'Marketing worker requires an active metered Plow assignment'};
    });
    registerTextMode(api);
    api.registerGatewayMethod('marketing.meter.status', () => {
      const ready = policyReady(api.config);
      if (ready) install();
      return {version: VERSION, policyReady: ready, guardInstalled: guarded(), nativeGuarded: globalThis.fetch === shared.nativeFetch,
        ready: ready && guarded(), accountingMode: 'post_response', responseReceipts: true,
        outputCapSupported: false, route, transport: 'sse',
        blocker: !ready ? 'worker_policy_incompatible' : !guarded() ? 'request_guard_unavailable' : null};
    });
  },
};
