import {createHash, createHmac} from 'node:crypto';
import {request as httpRequest} from 'node:http';

const uid = /^[a-f0-9]{32}$/;
const subject = /^[A-Za-z0-9_-]{3,128}$/;
const token = /^[A-Za-z0-9_-]{43,100}$/;
const requestHeaders = new Set(['accept', 'content-type', 'x-csrf', 'cookie', 'range', 'if-none-match', 'if-modified-since']);
const responseHeaders = new Set(['content-type', 'content-disposition', 'etag', 'last-modified', 'accept-ranges', 'content-range', 'set-cookie', 'cache-control']);

export function companionConfig({brokerOrigin, workspaceOrigin, workspace, credential, ownerUid, ingressSecret, upstreamPort = 5184, development = false}) {
  const origin = value => {
    const parsed = new URL(value);
    if (parsed.origin !== value || (parsed.protocol !== 'https:' && !(development && parsed.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(parsed.hostname))))
      throw new Error('The companion needs exact HTTPS origins.');
    return value;
  };
  if (!uid.test(workspace) || !subject.test(ownerUid) || !token.test(credential) || !token.test(ingressSecret) || !Number.isInteger(upstreamPort) || upstreamPort < 1024 || upstreamPort > 65535)
    throw new Error('The companion connection is incomplete.');
  return {brokerOrigin: origin(brokerOrigin), workspaceOrigin: origin(workspaceOrigin), workspace, credential, ownerUid, ingressSecret, upstreamPort};
}

export function validatePacket(packet, config, now = Date.now()) {
  if (!packet || packet.protocol !== 1 || packet.workspace !== config.workspace || packet.origin !== config.workspaceOrigin || !/^[a-f0-9]{48}$/.test(packet.id))
    throw new Error('The companion request belongs to another connection.');
  if (!['GET', 'HEAD', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(packet.method) || typeof packet.path !== 'string' || packet.path.length > 4096)
    throw new Error('Unsupported companion request.');
  const path = packet.path;
  if (!path.startsWith('/') || path.startsWith('//') || /[\\\s#\x00-\x1f\x7f]/.test(path)) throw new Error('Unsupported companion path.');
  const pathname = path.split('?')[0];
  if (decodeURIComponent(pathname) !== pathname || pathname.split('/').some(part => part === '.' || part === '..') ||
      /^\/(api\/(auth\/|pair\/|maintenance)|worker\/)/.test(pathname) ||
      !(pathname.startsWith('/api/') || pathname.startsWith('/assets/') || ['/', '/sw.js', '/manifest.webmanifest', '/favicon.ico'].includes(pathname)) ||
      (!pathname.startsWith('/api/') && !['GET', 'HEAD'].includes(packet.method))) throw new Error('Unsupported companion path.');
  const identity = packet.identity;
  if (!identity || !subject.test(identity.subject) || identity.owner !== (identity.subject === config.ownerUid) ||
      !/^[a-f0-9]{64}$/.test(identity.session) || typeof identity.name !== 'string' || identity.name.length > 60 ||
      !Number.isInteger(identity.expires) || identity.expires <= Math.floor(now / 1000)) throw new Error('A current individual identity is required.');
  if (typeof packet.body !== 'string' || packet.body.length > 200000 || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(packet.body))
    throw new Error('Invalid companion request body.');
  const body = Buffer.from(packet.body, 'base64');
  if (body.length > 150000 || (['GET', 'HEAD'].includes(packet.method) && body.length)) throw new Error('Invalid companion request body.');
  const headers = {};
  for (const [name, value] of Object.entries(packet.headers || {})) {
    if (!requestHeaders.has(name.toLowerCase())) continue;
    if (typeof value !== 'string' || value.length > 8192 || /[\r\n]/.test(value)) throw new Error('Invalid companion headers.');
    headers[name.toLowerCase()] = name.toLowerCase() === 'cookie'
      ? value.split(';').map(part => part.trim()).filter(part => part.startsWith('thaddeus-session=')).join('; ')
      : value;
  }
  const assertion = Buffer.from(JSON.stringify({version: 1, workspace: config.workspace, request: packet.id,
    subject: identity.subject, name: identity.name, session: identity.session,
    issued: Math.floor(now / 1000), expires: Math.min(identity.expires, Math.floor(now / 1000) + 30),
    method: packet.method, path, bodyHash: createHash('sha256').update(body).digest('hex')})).toString('base64url');
  headers['x-hirezero-identity'] = assertion;
  headers['x-hirezero-signature'] = createHmac('sha256', config.ingressSecret).update(assertion).digest('hex');
  headers.host = new URL(config.workspaceOrigin).host;
  headers.origin = config.workspaceOrigin;
  headers['content-length'] = String(body.length);
  return {body, headers};
}

export async function forwardCompanion(packet, config, reply, {signal} = {}) {
  const {body, headers} = validatePacket(packet, config);
  // The only upstream is the colocated host. A packet can never choose a URL,
  // proxy identity, credential, worker socket or a different household's host.
  const response = await new Promise((resolve, reject) => {
    const request = httpRequest({hostname: '127.0.0.1', port: config.upstreamPort, method: packet.method, path: packet.path, headers, signal}, resolve);
    request.setTimeout(110000, () => request.destroy(new Error('The companion host timed out.')));
    request.on('error', reject);
    request.end(body);
  });
  let sequence = 0;
  try {
    if (response.statusCode >= 300 && response.statusCode < 400) throw new Error('Companion redirects require an explicit handoff.');
    const safeHeaders = Object.fromEntries(Object.entries(response.headers).filter(([name]) => responseHeaders.has(name)));
    await reply({id: packet.id, sequence: sequence++, status: response.statusCode, headers: safeHeaders, body: '', end: false});
    for await (const chunk of response) {
      for (let offset = 0; offset < chunk.length; offset += 65536)
        await reply({id: packet.id, sequence: sequence++, body: chunk.subarray(offset, offset + 65536).toString('base64'), end: false});
    }
    await reply({id: packet.id, sequence: sequence++, body: '', end: true});
  } finally { response.destroy(); }
}

export async function runCompanion(config, {signal, fetchImpl = fetch, onStatus = () => {}} = {}) {
  const active = new Set();
  async function post(action, body) {
    const response = await fetchImpl(config.brokerOrigin + '/api/companion/agent/' + action, {
      method: 'POST', redirect: 'error', signal: AbortSignal.any([signal || new AbortController().signal, AbortSignal.timeout(30000)]),
      headers: {'Content-Type': 'application/json', Authorization: 'Bearer ' + config.credential},
      body: JSON.stringify({workspace: config.workspace, ...body})
    });
    if (!response.ok) throw new Error('The companion connection needs to reconnect.');
    return response.json();
  }
  while (!signal?.aborted) {
    try {
      if (active.size >= 12) { await Promise.race(active); continue; }
      const packet = await post('poll', {});
      onStatus('connected');
      if (!packet) continue;
      // Claim once, execute once. An uncertain write is never replayed by reconnect.
      const work = forwardCompanion(packet, config, frame => post('reply', {frame}), {signal})
        .catch(() => onStatus('request-unconfirmed')).finally(() => active.delete(work));
      active.add(work);
    } catch {
      if (signal?.aborted) break;
      onStatus('reconnecting');
      await new Promise(resolve => {
        const finish = () => { clearTimeout(timer); signal?.removeEventListener('abort', finish); resolve(); };
        const timer = setTimeout(finish, 2000);
        signal?.addEventListener('abort', finish, {once: true});
      });
    }
  }
  await Promise.allSettled(active);
}
