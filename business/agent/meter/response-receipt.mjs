import { createHash } from 'node:crypto';

const MAX_EVENT_BYTES = 2 * 1024 * 1024;
const terminalTypes = new Set(['response.completed', 'response.failed', 'response.incomplete']);

function terminalReceipt(event) {
  const response = event.response;
  const usage = response?.usage;
  const counts = [usage?.input_tokens, usage?.output_tokens, usage?.total_tokens];
  const validUsage = counts.every(value => Number.isSafeInteger(value) && value >= 0) &&
    counts[0] + counts[1] === counts[2];
  const responseId = typeof response?.id === 'string' && response.id.length <= 200 ? response.id : null;
  return {
    status: validUsage ? 'reported' : 'unknown',
    reported_tokens: validUsage ? usage.total_tokens : null,
    response_receipt: {
      terminal_type: event.type, provider_response_id: responseId,
      input_tokens: validUsage ? usage.input_tokens : null,
      output_tokens: validUsage ? usage.output_tokens : null,
      evidence_digest: createHash('sha256').update(JSON.stringify(event)).digest('hex'),
    },
  };
}

/** Observe the original SSE stream without a cloned, unbounded body buffer.
 * Save terminal usage before exposing that frame to the caller. Missing or
 * interrupted evidence holds the reservation; an HTTP error is never zero use.
 */
export async function captureModelResponse(response, saveReceipt) {
  if (typeof saveReceipt !== 'function') throw new TypeError('A durable response receipt writer is required');
  let settled = false;
  const save = async receipt => {
    if (settled) return;
    await saveReceipt(receipt);
    settled = true;
  };
  const unknown = reason => save({ status: 'unknown', reported_tokens: null,
    response_receipt: { terminal_type: reason, http_status: response.status } });
  if (!response.ok || !response.body || !/^text\/event-stream(?:\s*;|$)/i.test(response.headers.get('content-type') || '')) {
    await unknown(!response.ok ? 'http_error' : 'unsupported_response');
    return response;
  }
  const reader = response.body.getReader();
  const decoder = new TextDecoder('utf-8', { fatal: true });
  let buffer = '';
  let observationFailed = false;
  async function inspect(chunk, final = false) {
    if (settled || observationFailed) return;
    try {
      buffer += decoder.decode(chunk, { stream: !final });
      // Bound individual SSE events, not the complete response or its duration.
      while (true) {
        const separator = /\r?\n\r?\n/.exec(buffer);
        if (!separator) break;
        const frame = buffer.slice(0, separator.index);
        buffer = buffer.slice(separator.index + separator[0].length);
        if (Buffer.byteLength(frame) > MAX_EVENT_BYTES) throw Error('Oversized receipt event');
        const data = frame.split(/\r?\n/).filter(line => line.startsWith('data:'))
          .map(line => line.slice(5).replace(/^ /, '')).join('\n');
        if (!data || data === '[DONE]') continue;
        const event = JSON.parse(data);
        if (terminalTypes.has(event.type)) {
          // Keep storage failures outside the parse-error handler: a consumer
          // must not receive a successful completion without its saved receipt.
          return terminalReceipt(event);
        }
      }
      if (Buffer.byteLength(buffer) > MAX_EVENT_BYTES) throw Error('Oversized receipt event');
    } catch {
      observationFailed = true;
      await unknown('unreadable_response');
    }
    return null;
  }
  const body = new ReadableStream({
    async pull(controller) {
      try {
        const { value, done } = await reader.read();
        const receipt = await inspect(value, done);
        if (receipt) await save(receipt);
        if (done) {
          await unknown('stream_ended_without_receipt');
          controller.close();
        } else controller.enqueue(value);
      } catch (error) {
        try { await unknown('stream_interrupted'); }
        catch { /* Preserve the original failure and prevent a successful reply. */ }
        await reader.cancel().catch(() => {});
        controller.error(error);
      }
    },
    async cancel(reason) {
      try { await unknown('stream_cancelled'); }
      finally { await reader.cancel(reason); }
    },
  });
  return new Response(body, { status: response.status, statusText: response.statusText, headers: response.headers });
}
