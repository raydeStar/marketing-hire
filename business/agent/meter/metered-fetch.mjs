import { createHash } from 'node:crypto';
import { zstdCompressSync, zstdDecompressSync } from 'node:zlib';
import { isWorkerSessionHint, workerSession } from './worker-session.mjs';

const EXECUTION_ID = /^[a-f0-9]{32}$/;
const CODEX_RESPONSES_PATH = /^\/backend-api\/codex\/responses(?:\/compact)?$/;
const MAX_INPUT_BYTES = 20000;
const MAX_OUTPUT_TOKENS = 1800;

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
    const expectedSession = EXECUTION_ID.test(executionId) ? workerSession(executionId) : null;
    if (!expectedSession || (session !== expectedSession.header && session !== expectedSession.id)) {
      throw new Error('Active runway requires its exact worker session');
    }
    const body = Buffer.from(await request.clone().arrayBuffer());
    if (!body.length || body.length > MAX_INPUT_BYTES) {
      throw new Error('Runway request body exceeds its conservative input allowance');
    }
    const encoding = request.headers.get('content-encoding');
    if (encoding && encoding !== 'identity' && encoding !== 'zstd') {
      throw new Error('Runway request body uses an unsupported encoding');
    }
    let decoded = body;
    if (encoding === 'zstd') {
      try { decoded = zstdDecompressSync(body, { maxOutputLength: MAX_INPUT_BYTES }); }
      catch { throw new Error('Runway decoded request exceeds its conservative input allowance'); }
    }
    if (!decoded.length || decoded.length > MAX_INPUT_BYTES) {
      throw new Error('Runway decoded request exceeds its conservative input allowance');
    }
    let payload;
    try { payload = JSON.parse(decoded.toString('utf8')); }
    catch { throw new Error('Runway request body is not valid JSON'); }
    if (payload?.model !== 'gpt-5.6-luna' ||
        (payload.tools !== undefined &&
          (!Array.isArray(payload.tools) || payload.tools.length > 0))) {
      throw new Error('Runway request changed model or tool scope');
    }
    if (payload.max_output_tokens !== undefined &&
        (!Number.isInteger(payload.max_output_tokens) ||
          payload.max_output_tokens < 1 || payload.max_output_tokens > MAX_OUTPUT_TOKENS)) {
      throw new Error('Runway output ceiling is outside its grant');
    }
    payload.max_output_tokens ??= MAX_OUTPUT_TOKENS;
    const rewritten = Buffer.from(JSON.stringify(payload));
    if (rewritten.length > MAX_INPUT_BYTES) {
      throw new Error('Runway decoded request exceeds its conservative input allowance');
    }
    const sendBody = encoding === 'zstd' ? zstdCompressSync(rewritten) : rewritten;
    if (sendBody.length > MAX_INPUT_BYTES) {
      throw new Error('Runway request body exceeds its conservative input allowance');
    }
    const headers = new Headers(request.headers);
    headers.delete('content-length');
    const admittedRequest = new Request(request, { headers, body: sendBody });
    const digest = createHash('sha256').update(admittedRequest.method).update('\n')
      .update(admittedRequest.url).update('\n').update(sendBody).digest('hex');
    // One 25k reservation per execution also denies an internal retry before
    // network dispatch. A confirmed turn later reports its actual usage.
    const receipt = await reserveRequest({ request_id: executionId, execution_id: executionId,
      request_digest: digest, reserved_tokens: 25000 });
    if (receipt?.admitted !== true) throw new Error('Runway request was not admitted for dispatch');
    return baseFetch(admittedRequest);
  };
}

/** Cover the native Codex provider, which calls global fetch outside buildModelFetch. */
export function createGlobalMeteredFetch({ baseFetch, activeExecution, reserveRequest }) {
  if (typeof baseFetch !== 'function' || typeof activeExecution !== 'function' ||
      typeof reserveRequest !== 'function') throw new TypeError('A fetch transport and durable meter are required');
  return async (input, init) => {
    const executionId = await activeExecution();
    const request = new Request(input, init);
    const session = request.headers.get('session_id') || '';
    const url = new URL(request.url);
    const modelRoute = (url.origin === 'https://chatgpt.com' &&
      url.pathname.startsWith('/backend-api/codex/responses')) ||
      (url.origin === 'https://api.openai.com' && url.pathname.includes('/responses'));
    if (!executionId && !isWorkerSessionHint(session) && !modelRoute) return baseFetch(input, init);
    // An active worker owns all process fetch egress. Other simultaneous
    // requests fail closed until that execution settles.
    return createMeteredFetch({ baseFetch, activeExecution: async () => executionId,
      reserveRequest })(input, init);
  };
}
