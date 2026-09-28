import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdtemp, readFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {request} from 'node:http';
import {once} from 'node:events';
import test from 'node:test';
import {entrance} from './entrance.mjs';
import {companionPairing} from './companion-pairing.mjs';

test('owner-only bootstrap persists its exact binding and exposes only a hash', async () => {
  const workspace = 'a'.repeat(32), ownerUid = 'owner-fixture', origin = `https://${workspace}.plow.run`;
  const directory = await mkdtemp(path.join(tmpdir(), 'hirezero-pairing-'));
  const file = path.join(directory, 'connection.json');
  const pairing = companionPairing({file, identity: () => ({workspace, ownerUid})});
  const server = entrance({startHost: async () => {}, pairing: pairing.handle});
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  function call(method = 'GET', body, extra = {}) {
    return new Promise((resolve, reject) => {
      const req = request(`http://127.0.0.1:${server.address().port}/_hirezero/companion`, {method,
        headers: {host: `plow-agent-${workspace}.exe.xyz:3000`, 'x-plow-user': ownerUid, ...extra}}, res => {
        let text = ''; res.on('data', chunk => text += chunk); res.on('end', () => resolve({status: res.statusCode, text, value: text ? JSON.parse(text) : null}));
      });
      req.on('error', reject); req.end(body === undefined ? undefined : JSON.stringify(body));
    });
  }
  try {
    assert.equal((await call('GET', undefined, {'x-plow-user': 'other-person'})).status, 403);
    const status = (await call()).value;
    assert.equal(status.credentialHash, null);
    const value = {version: 1, workspace, ownerUid, brokerOrigin: 'https://hirezero.app',
      workspaceOrigin: `https://${workspace}.work.hirezero.app`, credential: 'c'.repeat(64)};
    const headers = {origin, 'content-type': 'application/json', 'x-hirezero-setup': status.nonce};
    for (const change of [{'x-hirezero-setup': ''}, {origin: 'https://evil.example'}, {'x-plow-user': 'other-person'}, {'content-type': 'text/plain'}])
      assert.equal((await call('POST', value, {...headers, ...change})).status, 403);
    for (const change of [{workspace: 'b'.repeat(32)}, {ownerUid: 'other-person'}, {brokerOrigin: 'https://evil.example'}])
      assert.equal((await call('POST', {...value, ...change}, headers)).status, 400);
    const saved = await call('POST', value, headers);
    assert.equal(saved.status, 200);
    assert.equal(saved.value.credentialHash, createHash('sha256').update(value.credential).digest('hex'));
    assert.ok(!saved.text.includes(value.credential));
    assert.deepEqual(JSON.parse(await readFile(file, 'utf8')), value);
    assert.deepEqual(await companionPairing({file, identity: () => ({workspace, ownerUid})}).read(), value);
    assert.equal((await call()).value.credentialHash, saved.value.credentialHash, 'Read back uncertain writes without replay.');
  } finally {
    server.closeAllConnections(); await new Promise(resolve => server.close(resolve));
    await rm(directory, {recursive: true});
  }
});
