import {createHash} from 'node:crypto';
import {AsyncLocalStorage} from 'node:async_hooks';
import {workerSession, isWorkerSessionHint} from '../worker-session.mjs';
import {captureCompletion} from './completion-receipt.mjs';

const MAX_BYTES = 20000;
const MAX_OUTPUT = 4096;
const endpoint = 'https://api.plow.co/v1/chat/completions';
export const route = 'plow/z-ai/glm-5.2';
export class PlowAdmissionError extends Error {name = 'PlowAdmissionError';}
// A fresh 2026.9.6 model-run has boundary zero; the runner clips its cache
// affinity ID to 64 characters including that suffix. No resumed boundary is
// admitted for this one-request worker grant.
export const plowWorkerSession = execution => workerSession(execution).id.slice(0, 62) + ':0';
const workerHint = session => isWorkerSessionHint(session) || session.startsWith('internal-session-effects-');

async function digest(request) {
  return createHash('sha256').update(request.method).update('\n').update(request.url).update('\n')
    .update(Buffer.from(await request.clone().arrayBuffer())).digest('hex');
}

export function createPlowRequestGuards({baseFetch, activeExecution, reserveRequest, finishRequest}) {
  const dispatch = new AsyncLocalStorage();
  async function meter(input, init, send, workerSend) {
    const request = new Request(input, init);
    const active = await activeExecution();
    const session = request.headers.get('session_id') || '';
    if (!active?.execution_id) {
      if (workerHint(session)) throw new PlowAdmissionError('Plow worker has no active assignment');
      return send(request);
    }
    if (request.url !== endpoint || request.method !== 'POST' || session !== plowWorkerSession(active.execution_id) ||
        active.accounting_mode !== 'post_response' || !Number.isFinite(active.deadline_at) || active.deadline_at * 1000 <= Date.now())
      throw new PlowAdmissionError('Plow request does not match its active worker assignment');
    const bytes = Buffer.from(await request.clone().arrayBuffer());
    if (!bytes.length || bytes.length > MAX_BYTES || request.headers.has('content-encoding'))
      throw new PlowAdmissionError('Plow worker packet exceeds its input allowance or encoding');
    let payload;
    try {payload = JSON.parse(bytes.toString('utf8'));}
    catch {throw new PlowAdmissionError('Plow worker packet is not JSON');}
    if (payload?.model !== 'z-ai/glm-5.2' || payload.stream !== true || payload.stream_options?.include_usage !== true ||
        !Array.isArray(payload.messages) || !payload.messages.length ||
        (payload.tools !== undefined && (!Array.isArray(payload.tools) || payload.tools.length)) ||
        (payload.n !== undefined && payload.n !== 1) || payload.max_completion_tokens !== undefined ||
        !Number.isInteger(payload.max_tokens) || payload.max_tokens < 1 || payload.max_tokens > MAX_OUTPUT)
      throw new PlowAdmissionError('Plow worker changed its model, tool scope or response policy');
    const remaining = Math.floor(active.deadline_at * 1000 - Date.now());
    if (remaining <= 0) throw new PlowAdmissionError('Plow worker grant expired');
    const admittedRequest = new Request(request, {redirect: 'error',
      signal: AbortSignal.any([request.signal, AbortSignal.timeout(Math.min(remaining, 2147483647))])});
    const requestDigest = await digest(admittedRequest);
    const reserved = await reserveRequest({request_id: active.execution_id, execution_id: active.execution_id,
      request_digest: requestDigest, reserved_tokens: 25000, accounting_mode: 'post_response'});
    if (reserved?.admitted !== true) throw new PlowAdmissionError('Plow worker reservation refused dispatch');
    let response;
    try {
      response = await dispatch.run({requestDigest, nativeSent: false}, () => workerSend(admittedRequest));
    } catch (error) {
      await finishRequest({request_id: active.execution_id, request_digest: requestDigest, status: 'unknown',
        reported_tokens: null, response_receipt: {terminal_type: 'dispatch_error'}});
      throw error;
    }
    return captureCompletion(response, outcome => finishRequest({request_id: active.execution_id,
      request_digest: requestDigest, ...outcome}));
  }
  return {
    modelFetch(send, workerSend = send) {return (input, init) => meter(input, init, send, workerSend);},
    async nativeFetch(input, init) {
      const request = new Request(input, init);
      const admitted = dispatch.getStore();
      if (admitted) {
        // One transport wrapper and one physical send. An internal retry is a second request.
        if (admitted.nativeSent || request.redirect !== 'error' || await digest(request) !== admitted.requestDigest)
          throw new PlowAdmissionError('Plow physical request changed or attempted an unreserved retry');
        admitted.nativeSent = true;
        return baseFetch(request);
      }
      const session = request.headers.get('session_id') || '';
      const url = new URL(request.url);
      const channelTraffic = url.origin === 'https://api.plow.co' &&
        (/^\/v1\/(?:chats|lines|agents|auth|identity)(?:\/|$)/.test(url.pathname) || url.pathname === '/v1/ws/ticket');
      if (channelTraffic && !workerHint(session)) return baseFetch(request);
      const active = await activeExecution();
      if (workerHint(session) || active?.execution_id)
        throw new PlowAdmissionError('Plow model send bypassed its worker transport guard');
      // Chat transport, identity lookup and reporter traffic remain available during a worker turn.
      return baseFetch(request);
    },
  };
}
