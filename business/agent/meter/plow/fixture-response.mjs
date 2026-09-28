// Only loaded explicitly by the network-disabled Gateway check. Never packaged
// as a startup hook. The original fetch cannot send this fixture's model reply.
import {appendFileSync} from 'node:fs';
const original = globalThis.fetch;
globalThis.fetch = async (input, init) => {
  const request = new Request(input, init);
  const endpoint = (process.env.PLOW_API_BASE || 'https://api.plow.co').replace(/\/+$/, '') + '/v1/chat/completions';
  if (request.url !== endpoint) return original(input, init);
  if (request.headers.get('authorization') !== 'Bearer fictional-offline-agent') throw Error('Unresolved fictional credential');
  appendFileSync('/tmp/plow-meter-sends.jsonl', JSON.stringify({url: request.url, method: request.method,
    session: request.headers.get('session_id'), redirect: request.redirect, body: await request.json()}) + '\n');
  const common = {id: 'chat_fixture', object: 'chat.completion.chunk', created: 1, model: 'z-ai/glm-5.2'};
  const events = [{...common, choices: [{index: 0, delta: {role: 'assistant', content: 'Prepared offline campaign'}, finish_reason: null}]},
    {...common, choices: [{index: 0, delta: {}, finish_reason: 'stop'}]},
    {...common, choices: [], usage: {prompt_tokens: 5, completion_tokens: 3, total_tokens: 8}}];
  return new Response(events.map(event => 'data: ' + JSON.stringify(event) + '\n\n').join('') + 'data: [DONE]\n\n',
    {headers: {'Content-Type': 'text/event-stream'}});
};
