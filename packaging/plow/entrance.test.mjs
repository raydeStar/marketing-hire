import assert from 'node:assert/strict';
import {createServer, request} from 'node:http';
import {once} from 'node:events';
import test from 'node:test';
import {entrance, publicOrigin} from './entrance.mjs';

const agentId = '0123456789abcdef0123456789abcdef';
const privateHost = `plow-agent-${agentId}.exe.xyz:3000`;
const browserOrigin = `https://${agentId}.plow.run`;

// Browser fetch does not permit a caller-supplied Host. Use real HTTP for the proxy contract.
function call(url, options = {}) {
  return new Promise((resolve, reject) => {
    const outgoing = request(url, {method: options.method || 'GET', headers: options.headers}, incoming => {
      let body = ''; incoming.on('data', data => body += data);
      incoming.on('end', () => resolve({status: incoming.statusCode, text: async () => body}));
    });
    outgoing.on('error', reject); outgoing.end(options.body);
  });
}

test('hosted origins must match the documented private Plow entrance', () => {
  assert.equal(publicOrigin(privateHost), browserOrigin);
  for (const host of ['evil.example:3000', privateHost + '.evil.example', privateHost.replace(':3000', ''), 'user@' + privateHost, 'claw.exe.xyz:3000', agentId + '.plow.run']) assert.throws(() => publicOrigin(host));
});

test('local entrance strips forged identity and forwarding headers and streams the real request', async () => {
  const requests = [];
  const upstream = createServer((incoming, outgoing) => {
    let body = ''; incoming.on('data', data => body += data);
    incoming.on('end', () => { requests.push({headers: incoming.headers, body}); outgoing.end('ready'); });
  });
  upstream.listen(0, '127.0.0.1'); await once(upstream, 'listening');
  let starts = 0;
  const proxy = entrance({localOrigin: 'http://localhost:5183', upstreamPort: upstream.address().port, startHost: async () => { starts++; }});
  proxy.listen(0, '127.0.0.1'); await once(proxy, 'listening');
  try {
    const url = 'http://127.0.0.1:' + proxy.address().port;
    const response = await call(url + '/api/example', {method: 'POST', headers: {host: 'localhost:5183', origin: 'http://localhost:5183', 'x-plow-user': 'forged-owner', 'x-forwarded-for': '203.0.113.8', 'x-exedev-user': 'forged'}, body: 'Owner’s direction → café'});
    assert.equal(await response.text(), 'ready');
    assert.equal(requests[0].headers['x-plow-user'], 'plow-local-owner');
    assert.equal(requests[0].headers['x-forwarded-for'], undefined);
    assert.equal(requests[0].headers['x-exedev-user'], undefined);
    assert.equal(requests[0].body, 'Owner’s direction → café');
    assert.equal((await call(url, {headers: {host: 'localhost:5183', origin: 'https://evil.example'}})).status, 403);
    assert.equal(starts, 1);
  } finally { proxy.closeAllConnections(); upstream.closeAllConnections(); await Promise.all([new Promise(resolve => proxy.close(resolve)), new Promise(resolve => upstream.close(resolve))]); }
});

test('hosted entrance requires proxy identity on every request and pins one origin', async () => {
  const forwarded = [];
  const upstream = createServer((incoming, outgoing) => {
    forwarded.push(incoming.headers);
    outgoing.end(incoming.headers.origin === 'https://' + incoming.headers.host ? 'saved' : 'ready');
  });
  upstream.listen(0, '127.0.0.1'); await once(upstream, 'listening');
  const starts = [];
  const proxy = entrance({upstreamPort: upstream.address().port, startHost: async origin => { starts.push(origin); }});
  proxy.listen(0, '127.0.0.1'); await once(proxy, 'listening');
  try {
    const url = 'http://127.0.0.1:' + proxy.address().port;
    assert.equal((await call(url, {headers: {host: privateHost}})).status, 403);
    assert.equal((await call(url, {headers: {host: privateHost, 'x-plow-user': 'usr_owner'}})).status, 200);
    const headers = {host: privateHost, origin: browserOrigin, 'x-plow-user': 'usr_owner', 'x-forwarded-host': 'evil.example'};
    const save = await call(url, {method: 'POST', headers, body: '{}'});
    assert.equal(save.status, 200);
    assert.equal(await save.text(), 'saved');
    assert.equal(forwarded[1]['x-forwarded-host'], undefined);
    assert.deepEqual(starts, [browserOrigin]);
    for (const origin of ['https://evil.example', 'https://' + privateHost, 'https://ffffffffffffffffffffffffffffffff.plow.run'])
      assert.equal((await call(url, {method: 'POST', headers: {...headers, origin}})).status, 403);
    assert.equal((await call(url, {headers: {...headers, 'sec-fetch-site': 'cross-site'}})).status, 403);
    assert.equal((await call(url, {headers: {...headers, host: 'plow-agent-ffffffffffffffffffffffffffffffff.exe.xyz:3000'}})).status, 403);
    assert.equal((await call(url, {headers: {host: privateHost}})).status, 403);
    assert.equal(forwarded.length, 2);
    const arrival = {host: privateHost, 'x-plow-user': 'usr_owner', 'sec-fetch-site': 'cross-site', 'sec-fetch-mode': 'navigate', 'sec-fetch-dest': 'document'};
    assert.equal((await call(url + '/', {headers: arrival})).status, 200);
    assert.equal(forwarded.at(-1)['sec-fetch-site'], 'cross-site'); // never forge browser context for the host
    for (const path of ['/api/session', '/api/marketing/state', '/api/marketing/profile', '/assets/app.js', '/?view=team'])
      assert.equal((await call(url + path, {headers: arrival})).status, 403);
    for (const changed of [{'sec-fetch-dest': 'iframe'}, {'sec-fetch-dest': 'empty'}, {'sec-fetch-mode': 'cors'}, {'x-plow-user': ''}, {origin: 'https://evil.example'}])
      assert.equal((await call(url + '/', {headers: {...arrival, ...changed}})).status, 403);
    for (const method of ['POST', 'PUT', 'DELETE', 'HEAD'])
      assert.equal((await call(url + '/', {method, headers: arrival})).status, 403);
    assert.equal(forwarded.length, 3);
  } finally { proxy.closeAllConnections(); upstream.closeAllConnections(); await Promise.all([new Promise(resolve => proxy.close(resolve)), new Promise(resolve => upstream.close(resolve))]); }
});
