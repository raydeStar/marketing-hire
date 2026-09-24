// Network-disabled Gateway probe only. Never package this fake transport in the
// production meter image or run it with the owner's Gateway volume.
const { appendFileSync } = require('node:fs');
const { zstdDecompressSync } = require('node:zlib');

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
  const compressed = Buffer.from(await request.clone().arrayBuffer());
  const encoding = request.headers.get('content-encoding');
  const decoded = encoding === 'zstd' ?
    zstdDecompressSync(compressed, { maxOutputLength: 20000 }) : compressed;
  const payload = JSON.parse(decoded.toString('utf8'));
  appendFileSync('/tmp/offline-base-sends.jsonl', JSON.stringify({
    url: request.url, session: request.headers.get('session_id'),
    encoding, decodedBytes: decoded.length, model: payload.model,
    maxOutputTokens: payload.max_output_tokens ?? null,
  }) + '\n');
  return new Response(`data: ${JSON.stringify(event)}\n\n`, {
    status: 200, headers: { 'content-type': 'text/event-stream' },
  });
};
