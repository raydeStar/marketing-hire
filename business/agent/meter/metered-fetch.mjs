import { createHash } from 'node:crypto';
import { isWorkerSessionHint, workerSession } from './worker-session.mjs';

const EXECUTION_ID = /^[a-f0-9]{32}$/;
const CODEX_RESPONSES_PATH = /^\/backend-api\/codex\/responses(?:\/compact)?$/;

/** Admit one exact network send for an active runway execution. */
export function createMeteredFetch({ baseFetch, activeExecution, reserveRequest }) {
  if (typeof baseFetch !== 'function' || typeof activeExecution !== 'function' || typeof reserveRequest !== 'function') {
    throw new TypeError('A fetch transport and durable meter are required');
  }
  return async (input, init) => {
    // A failed ledger read blocks the send. The owner can retry Chat after repair;
    // an unmetered worker request cannot be undone.
    const executionId = await activeExecution();
    const request = new Request(input, init);
    const session = request.headers.get('session_id') || '';
    const url = new URL(request.url);
    // This model is subscription-only in this product, even for owner Chat.
    // An accidental API-key route must never become a paid fallback.
    if (request.method !== 'POST' || url.origin !== 'https://chatgpt.com' ||
        !CODEX_RESPONSES_PATH.test(url.pathname) || url.search) {
      throw new Error('Model request target is outside the subscription Responses route');
    }
    if (!executionId) {
      if (isWorkerSessionHint(session)) throw new Error('Runway request has no active execution');
      return baseFetch(input, init);
    }
    if (!EXECUTION_ID.test(executionId) || session !== workerSession(executionId).header) {
      throw new Error('Active runway requires its exact worker session');
    }
    const body = Buffer.from(await request.clone().arrayBuffer());
    if (!body.length || body.length > 20000) {
      throw new Error('Runway request body exceeds its conservative input allowance');
    }
    const digest = createHash('sha256').update(request.method).update('\n').update(request.url)
      .update('\n').update(body).digest('hex');
    // One 25k reservation per execution also denies an internal retry before
    // network dispatch. A confirmed turn later reports its actual usage.
    const receipt = await reserveRequest({ request_id: executionId, execution_id: executionId,
      request_digest: digest, reserved_tokens: 25000 });
    if (receipt?.admitted !== true) throw new Error('Runway request was not admitted for dispatch');
    return baseFetch(input, init);
  };
}
