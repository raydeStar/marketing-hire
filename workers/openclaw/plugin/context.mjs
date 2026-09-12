import { createHash } from 'node:crypto';

export function contextHooks(settings, context) {
  if (context.schemaVersion !== 1 || typeof context.text !== 'string' || Buffer.byteLength(context.text) > 90000 ||
      createHash('sha256').update(context.text).digest('hex') !== settings.contentHash ||
      context.contentHash !== settings.contentHash || context.profileDigest !== settings.profileDigest)
    throw new Error('Prepared context does not match the frozen task binding.');
  const bound = ctx => ctx?.agentId === 'thaddeus' && ctx.sessionKey === settings.sessionKey;
  return {
    before_prompt_build: (_event, ctx) => bound(ctx) ? { prependContext: context.text } : undefined,
    before_agent_run: (_event, ctx) => bound(ctx) ? { outcome: 'pass' } : {
      outcome: 'block', reason: 'thaddeus-session-mismatch',
      message: 'This worker belongs to a different task. Please resume through Thaddeus.' }
  };
}
