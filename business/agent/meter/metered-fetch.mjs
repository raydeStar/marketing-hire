import { createHash } from 'node:crypto';

const EXECUTION_ID = /^[a-f0-9]{32}$/;
const WORKER_SESSION = /^model-run-([a-f0-9]{32})$/;
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
    const hintedWorker = WORKER_SESSION.exec(session);
    if (!executionId) {
      if (hintedWorker) throw new Error('Runway request has no active execution');
      return baseFetch(input, init);
    }
    if (!EXECUTION_ID.test(executionId) || session !== `model-run-${executionId}`) {
      throw new Error('Active runway requires its exact worker session');
    }
    const url = new URL(request.url);
    if (request.method !== 'POST' || url.origin !== 'https://chatgpt.com' ||
        !CODEX_RESPONSES_PATH.test(url.pathname) || url.search) {
      throw new Error('Runway request target is outside the subscription Responses route');
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
