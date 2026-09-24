// Disposable installed-Gateway protocol check. This fixture sends no model messages.
const WebSocket = require('/app/node_modules/ws');
const assert = require('node:assert/strict');

const endpoint = 'ws://127.0.0.1:18995';
const origin = 'http://localhost:18995';

function open(identity) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(endpoint, { headers: {
      origin, 'x-forwarded-user': identity, 'x-forwarded-for': '192.0.2.10',
      'x-forwarded-host': 'localhost', 'x-forwarded-proto': 'http'
    } });
    const pending = new Map();
    const timeout = setTimeout(() => reject(new Error('Gateway connect timed out')), 8000);
    ws.on('error', reject);
    ws.on('message', bytes => {
      let frame;
      try { frame = JSON.parse(String(bytes)); } catch { return; }
      if (frame.type === 'event' && frame.event === 'connect.challenge') {
        ws.send(JSON.stringify({ type: 'req', id: 'hello', method: 'connect', params: {
          minProtocol: 4, maxProtocol: 4,
          client: { id: 'openclaw-control-ui', version: '2026.9.4', platform: 'linux', mode: 'ui' },
          role: 'operator', scopes: ['operator.read', 'operator.write', 'operator.admin'],
          caps: [], commands: [], permissions: {}, locale: 'en-US', userAgent: 'marketing-suggestion-fixture/1'
        } }));
      } else if (frame.type === 'res' && frame.id === 'hello') {
        clearTimeout(timeout);
        if (!frame.ok) return reject(new Error(frame.error?.message || 'Gateway connect rejected'));
        resolve({
          ws,
          call(method, params) {
            return new Promise((done, fail) => {
              const id = Math.random().toString(16).slice(2);
              const limit = setTimeout(() => { pending.delete(id); fail(new Error(`RPC timed out: ${method}`)); }, 8000);
              pending.set(id, response => { clearTimeout(limit); done(response); });
              ws.send(JSON.stringify({ type: 'req', id, method, params }));
            });
          }
        });
      } else if (frame.type === 'res' && pending.has(frame.id)) {
        pending.get(frame.id)(frame);
        pending.delete(frame.id);
      }
    });
  });
}

async function main() {
  const runId = Date.now().toString(36);
  const collaboratorLabel = `fixture-collaborator-${runId}`;
  const privateLabel = `fixture-private-${runId}`;
  const sharedLabel = `fixture-shared-${runId}`;
  const owner = await open('owner-fixture@local.test');
  let collaborator = await open('collaborator-fixture@local.test');
  try {
    const collaboratorCreated = await collaborator.call('sessions.create', { agentId: 'shared-marketing', label: collaboratorLabel });
    assert.equal(collaboratorCreated.ok, true, collaboratorCreated.error?.message);
    const collaboratorList = await collaborator.call('sessions.list', {});
    const collaboratorRow = collaboratorList.payload.sessions.find(item => item.label === collaboratorLabel);
    const collaboratorProfile = collaboratorRow?.createdActor?.id;
    assert.ok(collaboratorProfile, 'Gateway must stamp the verified collaborator profile');
    const assigned = await owner.call('users.setRole', { profileId: collaboratorProfile, role: 'collaborator' });
    assert.equal(assigned.ok, true);
    collaborator.ws.close();

    const privateCreated = await owner.call('sessions.create', { agentId: 'main', label: privateLabel });
    const sharedCreated = await owner.call('sessions.create', { agentId: 'shared-marketing', label: sharedLabel });
    assert.equal(privateCreated.ok, true);
    assert.equal(sharedCreated.ok, true);
    const ownerList = await owner.call('sessions.list', {});
    const privateSession = ownerList.payload.sessions.find(item => item.label === privateLabel);
    const sharedSession = ownerList.payload.sessions.find(item => item.label === sharedLabel);
    assert.ok(privateSession?.key && sharedSession?.key);
    const visibility = await owner.call('session.visibility.set', { sessionKey: sharedSession.key, visibility: 'suggest' });
    assert.equal(visibility.ok, true);
    const member = await owner.call('session.members.add', { sessionKey: sharedSession.key, identityId: collaboratorProfile });
    assert.equal(member.ok, true);

    collaborator = await open('collaborator-fixture@local.test');
    const visible = await collaborator.call('sessions.list', {});
    assert.equal(visible.ok, true);
    const sharedListed = visible.payload.sessions.some(item => item.key === sharedSession.key);
    const privateListed = visible.payload.sessions.some(item => item.key === privateSession.key);
    const privateRead = await collaborator.call('sessions.describe', { key: privateSession.key });
    const privateHistory = await collaborator.call('chat.history', { sessionKey: privateSession.key });
    const mainCreate = await collaborator.call('sessions.create', { agentId: 'main', label: 'fixture-illicit-main' });
    const visibilityChange = await collaborator.call('session.visibility.set', { sessionKey: sharedSession.key, visibility: 'shared' });
    assert.equal(mainCreate.ok, false);
    assert.equal(visibilityChange.ok, false);

    const suggested = await collaborator.call('session.suggestions.add', {
      sessionKey: sharedSession.key,
      text: 'Revision constraint: mark the audience provisional and keep spending at zero.'
    });
    const suggestion = suggested.payload?.suggestion;
    const duplicateList = suggestion ? await collaborator.call('session.suggestions.list', { sessionKey: sharedSession.key }) : null;
    const illicitResolve = suggestion ? await collaborator.call('session.suggestions.resolve', {
      sessionKey: sharedSession.key, id: suggestion.id, resolution: 'dismiss'
    }) : null;
    // Negative control: a direct role-based connection is unsafe on 2026.9.4.
    assert.equal(sharedListed, true);
    assert.equal(privateListed, true);
    assert.equal(privateRead.ok, true);
    assert.equal(privateHistory.ok, true);
    assert.equal(suggested.ok, true, suggested.error?.message);
    assert.equal(suggestion.author.id, collaboratorProfile);
    assert.equal(illicitResolve.ok, false);
    console.log(JSON.stringify({
      installedVersion: '2026.9.4', modelMessagesSent: 0,
      directConnectionSafe: false,
      sharedListed, privateListed, privateReadAllowed: privateRead.ok,
      privateHistoryAllowed: privateHistory.ok, mainCreateAllowed: mainCreate.ok,
      sharingChangeAllowed: visibilityChange.ok, collaboratorResolveAllowed: illicitResolve?.ok ?? null,
      suggestionAdded: suggested.ok, suggestionError: suggested.error?.message ?? null,
      suggestionId: suggestion?.id ?? null,
      attributedToDistinctProfile: suggestion?.author.id === collaboratorProfile,
      suggestionState: suggestion?.state ?? null,
      duplicateRows: duplicateList?.payload?.suggestions.filter(item => item.id === suggestion.id).length ?? null
    }));
  } finally {
    collaborator.ws.close();
    owner.ws.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
