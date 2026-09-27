import {createHash} from 'node:crypto';
import {captureModelResponse} from '../response-receipt.mjs';

// Count a completion only after its finish, consistent usage and the SSE [DONE].
// Plow's proxy uses completions, so its evidence must not masquerade as Responses.
export function captureCompletion(response, save) {
  let id, finish, usage;
  const evidence = createHash('sha256');
  return captureModelResponse(response, save, data => {
    if (data === '[DONE]') {
      const valid = id && finish && usage && [usage.prompt_tokens, usage.completion_tokens, usage.total_tokens]
        .every(value => Number.isSafeInteger(value) && value >= 0) &&
        usage.prompt_tokens + usage.completion_tokens === usage.total_tokens;
      return {status: valid ? 'reported' : 'unknown', reported_tokens: valid ? usage.total_tokens : null,
        response_receipt: {terminal_type: 'chat.completion.done', provider_response_id: id || null,
          input_tokens: valid ? usage.prompt_tokens : null, output_tokens: valid ? usage.completion_tokens : null,
          evidence_digest: evidence.digest('hex')}};
    }
    const event = JSON.parse(data);
    if (event.object !== 'chat.completion.chunk' || typeof event.id !== 'string' || !event.id || event.id.length > 200 ||
        (id && id !== event.id) || !Array.isArray(event.choices) || event.choices.length > 1)
      throw new Error('Unexpected completion receipt');
    id = event.id;
    for (const choice of event.choices) {
      if (choice.index !== 0) throw new Error('Multiple completions exceed the worker assignment');
      if (choice.finish_reason != null) {
        if (finish || !['stop', 'length', 'content_filter'].includes(choice.finish_reason)) throw new Error('Unexpected completion finish');
        finish = choice.finish_reason;
      }
    }
    if (event.usage != null) {
      if (usage) throw new Error('Repeated completion usage');
      usage = event.usage;
    }
    evidence.update(data).update('\n');
    return null;
  });
}
