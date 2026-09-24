// Network-disabled Gateway probe only. Never package this fake transport in the
// production meter image or run it with the owner's Gateway volume.
const { appendFileSync } = require('node:fs');

const event = { type: 'response.completed', response: {
  id: 'resp_offline_gateway', status: 'completed',
  output: [{ type: 'message', id: 'msg_offline_gateway', role: 'assistant',
    content: [{ type: 'output_text', text: 'Offline Gateway receipt only' }] }],
  usage: { input_tokens: 5, output_tokens: 3, total_tokens: 8 },
} };

globalThis.fetch = async (input, init) => {
  const request = new Request(input, init);
  if (request.method !== 'POST' ||
      request.url !== 'https://chatgpt.com/backend-api/codex/responses') {
    throw new Error('Offline fixture refuses non-model HTTP');
  }
  appendFileSync('/tmp/offline-base-sends.jsonl', JSON.stringify({
    url: request.url, session: request.headers.get('session_id'),
  }) + '\n');
  return new Response(`data: ${JSON.stringify(event)}\n\n`, {
    status: 200, headers: { 'content-type': 'text/event-stream' },
  });
};
