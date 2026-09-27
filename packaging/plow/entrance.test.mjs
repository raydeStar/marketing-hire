import assert from 'node:assert/strict';
import {createServer, request} from 'node:http';
import {once} from 'node:events';
import test from 'node:test';
import {entrance, publicOrigin} from './entrance.mjs';

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
  assert.equal(publicOrigin('claw-fixture.exe.xyz:3000'), 'https://claw-fixture.exe.xyz:3000');
  for (const host of ['evil.example:3000', 'claw.exe.xyz.evil.example:3000', 'claw.exe.xyz', 'user@claw.exe.xyz:3000']) assert.throws(() => publicOrigin(host));
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
  const upstream = createServer((incoming, outgoing) => outgoing.end('ready'));
  upstream.listen(0, '127.0.0.1'); await once(upstream, 'listening');
  const proxy = entrance({upstreamPort: upstream.address().port, startHost: async () => {}});
  proxy.listen(0, '127.0.0.1'); await once(proxy, 'listening');
  try {
    const url = 'http://127.0.0.1:' + proxy.address().port;
    assert.equal((await call(url, {headers: {host: 'claw.exe.xyz:3000'}})).status, 403);
    assert.equal((await call(url, {headers: {host: 'claw.exe.xyz:3000', 'x-plow-user': 'usr_owner'}})).status, 200);
    assert.equal((await call(url, {headers: {host: 'other.exe.xyz:3000', 'x-plow-user': 'usr_owner'}})).status, 403);
    assert.equal((await call(url, {headers: {host: 'claw.exe.xyz:3000'}})).status, 403);
  } finally { proxy.closeAllConnections(); upstream.closeAllConnections(); await Promise.all([new Promise(resolve => proxy.close(resolve)), new Promise(resolve => upstream.close(resolve))]); }
});
