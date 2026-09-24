import { createHash } from 'node:crypto';

const EXECUTION_ID = /^[a-f0-9]{32}$/;
// OpenClaw's native Responses transport clips session_id to 64 code points.
const INTERNAL_SESSION_HEADER = /^internal-session-effects-[a-f0-9]{32}-[a-f0-9]{6}$/;
const INTERNAL_SESSION_FULL = /^internal-session-effects-[a-f0-9]{32}-[a-f0-9]{16}$/;
const LEGACY_WORKER_HINT = /^model-run-[a-f0-9]{32}$/;

/** OpenClaw 2026.9.4 derives hidden model-run identity from idempotencyKey. */
export function workerSession(executionId) {
  if (!EXECUTION_ID.test(executionId)) throw new Error('Invalid worker execution ID');
  const digest = createHash('sha256').update(executionId).digest('hex').slice(0, 16);
  const suffix = `${executionId}-${digest}`;
  const id = `internal-session-effects-${suffix}`;
  return { id, header: [...id].slice(0, 64).join(''),
    key: `agent:runway-worker:internal-session-effects:${suffix}` };
}

export function isWorkerSessionHint(sessionId) {
  return INTERNAL_SESSION_HEADER.test(sessionId) || INTERNAL_SESSION_FULL.test(sessionId) ||
    LEGACY_WORKER_HINT.test(sessionId);
}
