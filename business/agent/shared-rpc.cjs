// Host-only, bounded Gateway adapter. Input arrives on stdin from the
// authenticated cockpit; browsers cannot address this container or choose RPCs.
const fs = require('node:fs');
const net = require('node:net');
const WebSocket = require('/app/node_modules/ws');

const request = JSON.parse(fs.readFileSync(0, 'utf8'));
const identities = { owner: 'owner@cockpit.local', collaborator: 'collaborator@cockpit.local' };
const identity = identities[request.principal];
if (!identity) throw new Error('Known host principal required');
const clientIp = String(request.clientIp || '');
if (!net.isIP(clientIp) || clientIp === '127.0.0.1' || clientIp === '::1')
  throw new Error('An observed non-loopback client address is required for native identity ingress');

const endpoint = `ws://127.0.0.1:${Number(process.env.DEV_GATEWAY_PORT || 18995)}`;
const origin = `http://localhost:${Number(process.env.DEV_GATEWAY_PORT || 18995)}`;
const sessionKey = String(request.sessionKey || '');
if (request.action !== 'create' && !/^agent:shared-marketing:[a-zA-Z0-9:_-]{1,160}$/.test(sessionKey))
  throw new Error('Exact shared Marketing session required');

function connect() {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(endpoint, { headers: {
      origin, 'x-forwarded-user': identity, 'x-forwarded-for': clientIp,
      'x-forwarded-host': 'localhost', 'x-forwarded-proto': 'http'
    } });
    const pending = new Map();
    const limit = setTimeout(() => { ws.terminate(); reject(new Error('Shared Gateway connect timed out')); }, 8000);
    ws.on('error', error => { clearTimeout(limit); reject(error); });
    ws.on('message', bytes => {
      let frame;
      try { frame = JSON.parse(String(bytes)); } catch { return; }
      if (frame.type === 'event' && frame.event === 'connect.challenge') {
        ws.send(JSON.stringify({ type: 'req', id: 'hello', method: 'connect', params: {
          minProtocol: 4, maxProtocol: 4,
          client: { id: 'openclaw-control-ui', version: '2026.9.4', platform: 'linux', mode: 'ui' },
          role: 'operator', scopes: ['operator.read', 'operator.write'], caps: [], commands: [],
          permissions: {}, locale: 'en-US', userAgent: 'marketing-cockpit-relay/1'
        } }));
      } else if (frame.type === 'res' && frame.id === 'hello') {
        clearTimeout(limit);
        if (!frame.ok) return reject(new Error(frame.error?.message || 'Shared Gateway connection rejected'));
        resolve({ ws, call(method, params) {
          return new Promise((done, fail) => {
            const id = Math.random().toString(16).slice(2);
            const timeout = setTimeout(() => { pending.delete(id); fail(new Error(`Shared Gateway ${method} timed out`)); }, 10000);
            pending.set(id, result => { clearTimeout(timeout); done(result); });
            ws.send(JSON.stringify({ type: 'req', id, method, params }));
          });
        } });
      } else if (frame.type === 'res' && pending.has(frame.id)) {
        pending.get(frame.id)(frame);
        pending.delete(frame.id);
      }
    });
  });
}

function required(result, method) {
  if (!result.ok) throw new Error(`${method}: ${result.error?.message || 'rejected'}`);
  return result.payload;
}

async function main() {
  const connection = await connect();
  try {
    if (request.action === 'create') {
      if (request.principal !== 'owner' || !/^[a-f0-9]{32}$/.test(String(request.projectId || '')))
        throw new Error('Owner and exact project ID required');
      const label = `marketing-project-${request.projectId}`;
      const existing = required(await connection.call('sessions.list', {}), 'sessions.list').sessions.find(item => item.label === label);
      if (!existing) required(await connection.call('sessions.create', { agentId: 'shared-marketing', label }), 'sessions.create');
      const listed = required(await connection.call('sessions.list', {}), 'sessions.list').sessions;
      const row = listed.find(item => item.label === label);
      if (!row?.key) throw new Error('Created session could not be reconciled');
      required(await connection.call('session.visibility.set', { sessionKey: row.key, visibility: 'suggest' }), 'session.visibility.set');
      return { sessionKey: row.key, sessionId: row.sessionId, createdActor: row.createdActor };
    }
    if (request.action === 'suggest') {
      const content = String(request.content || '').trim();
      if (!content || content.length > 1000) throw new Error('Suggestion must be 1–1000 characters');
      return required(await connection.call('session.suggestions.add', { sessionKey, text: content }), 'session.suggestions.add');
    }
    if (request.action === 'suggestions')
      return required(await connection.call('session.suggestions.list', { sessionKey }), 'session.suggestions.list');
    if (request.action === 'history')
      return required(await connection.call('chat.history', { sessionKey, limit: 100 }), 'chat.history');
    throw new Error('Shared Gateway action is unavailable');
  } finally {
    connection.ws.close();
  }
}

main().then(value => process.stdout.write(JSON.stringify(value) + '\n')).catch(error => {
  process.stderr.write(error.message + '\n');
  process.exitCode = 1;
});
