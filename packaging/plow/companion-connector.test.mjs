import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash, createHmac} from 'node:crypto';
import {createServer} from 'node:http';
import {companionConfig, validatePacket, forwardCompanion, runCompanion} from './companion-connector.mjs';

const workspace = 'a'.repeat(32);
const config = companionConfig({workspace, brokerOrigin: 'https://hirezero.example', workspaceOrigin: `https://${workspace}.work.example`,
  credential: 't'.repeat(64), ownerUid: 'owner_001', ingressSecret: 's'.repeat(64)});
function packet(overrides = {}) {
  return {protocol: 1, id: 'b'.repeat(48), workspace, origin: config.workspaceOrigin, method: 'POST', path: '/api/feedback',
    body: Buffer.from('{"note":"Fictional feedback"}').toString('base64'), headers: {'content-type': 'application/json'},
    identity: {subject: 'member_002', name: 'Reviewer', owner: false, session: 'c'.repeat(64), expires: Math.floor(Date.now() / 1000) + 60}, ...overrides};
}
async function listen(server) { await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); return server.address().port; }
async function close(server) { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }

test('only exact configured HTTPS origins and a fixed loopback upstream are accepted', () => {
  for (const bad of ['http://public.example', 'https://hirezero.example/path', 'https://user@hirezero.example', 'https://hirezero.example?x=1'])
    assert.throws(() => companionConfig({...config, brokerOrigin: bad}));
  assert.throws(() => companionConfig({...config, credential: ''}));
  assert.throws(() => companionConfig({...config, workspace: '../owner'}));
  assert.throws(() => companionConfig({...config, upstreamPort: 80}));
  assert.equal(companionConfig({...config, development: true, brokerOrigin: 'http://127.0.0.1:5183'}).brokerOrigin, 'http://127.0.0.1:5183');
});

test('member requests receive a short-lived signature bound to identity, workspace, method, body and path', () => {
  const input = packet({headers: {'x-plow-user': 'owner_001', 'x-hirezero-identity': 'forged', host: 'evil.example',
    authorization: 'Bearer private', cookie: 'hz_workspace=private; thaddeus-session=native-cookie'}});
  const {headers, body} = validatePacket(input, config);
  const identity = JSON.parse(Buffer.from(headers['x-hirezero-identity'], 'base64url'));
  assert.equal(identity.subject, 'member_002');
  assert.equal(identity.workspace, workspace);
  assert.equal(identity.method, 'POST');
  assert.equal(identity.path, '/api/feedback');
  assert.equal(identity.bodyHash, createHash('sha256').update(body).digest('hex'));
  assert.equal(identity.expires - identity.issued, 30);
  assert.equal(headers['x-hirezero-signature'], createHmac('sha256', config.ingressSecret).update(headers['x-hirezero-identity']).digest('hex'));
  assert.equal(headers.cookie, 'thaddeus-session=native-cookie');
  assert.equal(headers.host, new URL(config.workspaceOrigin).host);
  assert.equal(headers.origin, config.workspaceOrigin);
  assert.equal(headers.authorization, undefined);
  assert.equal(headers['x-plow-user'], undefined);
  assert.equal(identity.owner, undefined); // The host derives ownership from its pinned owner UID.
});

test('forged ownership, expired identities and alternate destinations fail before any upstream request', () => {
  const original = packet();
  for (const bad of [packet({workspace: 'd'.repeat(32)}), packet({origin: 'https://evil.example'}),
    packet({identity: {...original.identity, owner: true}}), packet({identity: {...original.identity, expires: 1}}),
    packet({body: 'not base64'}), packet({body: Buffer.alloc(150001).toString('base64')})]) assert.throws(() => validatePacket(bad, config));
  for (const path of ['//evil.example/api/session', '/api/../auth/login', '/api/%61uth/login', '/api/auth/login', '/api/pair/start',
    '/api/maintenance/shutdown', '/worker/key/mcp', '/api/session#fragment', '/api/session\r\nHost:evil'])
    assert.throws(() => validatePacket(packet({path}), config));
});

test('real loopback HTTP forwards each request once and streams bounded response frames', async () => {
  const seen = [];
  const host = createServer(async (request, response) => {
    const chunks = []; for await (const chunk of request) chunks.push(chunk);
    seen.push({headers: request.headers, body: Buffer.concat(chunks).toString()});
    response.writeHead(200, {'Content-Type': 'application/json', 'X-Private-Diagnostic': 'do not forward', 'Set-Cookie': 'thaddeus-session=fixture; Secure; HttpOnly; SameSite=Strict; Path=/'});
    response.end('x'.repeat(170000));
  });
  const port = await listen(host);
  try {
    const frames = [];
    await forwardCompanion(packet(), {...config, upstreamPort: port}, async frame => frames.push(frame));
    assert.equal(seen.length, 1);
    assert.equal(seen[0].body, '{"note":"Fictional feedback"}');
    assert.equal(frames[0].status, 200);
    assert.equal(frames[0].headers['x-private-diagnostic'], undefined);
    assert.equal(frames.at(-1).end, true);
    assert.deepEqual(frames.map(frame => frame.sequence), frames.map((_, index) => index));
    assert.equal(frames.map(frame => Buffer.from(frame.body, 'base64').toString()).join('').length, 170000);
    assert.ok(frames.every(frame => Buffer.from(frame.body, 'base64').length <= 65536));
  } finally { await close(host); }
});

test('the connector never follows a redirect to another host', async () => {
  const host = createServer((request, response) => response.writeHead(302, {location: 'https://elsewhere.example/private'}).end());
  const port = await listen(host);
  try { await assert.rejects(forwardCompanion(packet(), {...config, upstreamPort: port}, async () => assert.fail('No redirected response should be accepted.'))); }
  finally { await close(host); }
});

test('a failed response delivery never repeats the host write', async () => {
  let writes = 0;
  const host = createServer((request, response) => { writes++; response.writeHead(200).end('{}'); });
  const port = await listen(host);
  try {
    await assert.rejects(forwardCompanion(packet(), {...config, upstreamPort: port}, async () => { throw new Error('Lost broker response'); }));
    assert.equal(writes, 1);
  } finally { await close(host); }
});

test('outbound polling pins its destination and stops without leaving a sleeping retry', async () => {
  const controller = new AbortController();
  const statuses = [];
  await runCompanion(config, {signal: controller.signal, onStatus: status => statuses.push(status), fetchImpl: async (url, options) => {
    assert.equal(url, config.brokerOrigin + '/api/companion/agent/poll');
    assert.equal(options.redirect, 'error');
    assert.equal(options.headers.Authorization, 'Bearer ' + config.credential);
    assert.equal(JSON.parse(options.body).workspace, workspace);
    controller.abort();
    return new Response('null', {status: 200});
  }});
  assert.deepEqual(statuses, ['connected']);
});
