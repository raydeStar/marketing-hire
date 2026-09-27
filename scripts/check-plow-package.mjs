import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createServer} from 'node:net';
import {mkdir, writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {requireArtifactSpace} from './artifact-storage.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [image, name, upgradeImage] = process.argv.slice(2);
assert.match(image || '', /^hirezero-marketing:plow-package-[a-z0-9-]+$/);
assert.match(name || '', /^plow-check-[a-z0-9-]+$/);
if (upgradeImage) assert.match(upgradeImage, /^hirezero-marketing:plow-package-[a-z0-9-]+$/);
const evidence = path.join(repo, 'artifacts', name);
await mkdir(evidence);
const space = await requireArtifactSpace(repo, 256 * 1024 ** 2, 'Disposable Plow cockpit check');
const origin = 'http://localhost:5183';
const owned = {container: false, network: false, volume: false};
const commands = [], removed = [];
async function run(command, args, env = process.env) {
  const child = spawn(command, args, {cwd: repo, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe']});
  let output = '';
  child.stdout.on('data', data => output += data); child.stderr.on('data', data => output += data);
  const exit = await new Promise((resolve, reject) => {child.on('error', reject); child.on('close', resolve);});
  const log = 'command-' + commands.length + '.log';
  await writeFile(path.join(evidence, log), output); commands.push({command, args, exit, log});
  if (exit !== 0) throw new Error(command + ' failed; inspect ' + path.join(evidence, log));
  return output;
}
async function waitReady() {
  for (let attempt = 0; attempt < 120; attempt++) {
    try {if ((await fetch(origin + '/', {signal: AbortSignal.timeout(1000)})).status === 200) return;} catch {}
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error('Packaged cockpit did not become ready');
}
async function owner() {
  const response = await fetch(origin + '/api/session'); assert.equal(response.status, 200);
  const session = await response.json(); assert.equal(session.owner, true); assert.ok(session.csrf);
  const cookie = response.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
  return {session, cookie};
}
async function read(route, cookie) {
  const response = await fetch(origin + route, {headers: {cookie}}); assert.equal(response.status, 200, route);
  return response.json();
}
async function requireLabel(kind, target) {
  const labels = JSON.parse(await run('docker', [kind, 'inspect', target, '--format', kind === 'container' ? '{{json .Config.Labels}}' : '{{json .Labels}}']));
  assert.equal(labels['hirezero.plow.fixture'], name, 'Refusing cleanup of a fixture owned elsewhere');
}
async function startContainer(pinnedImage) {
  await run('docker', ['run', '-d', '--name', name, '--label', 'hirezero.plow.fixture=' + name,
    '--network', name, '-p', '127.0.0.1:5183:3000', '-v', name + '-state:/var/lib/plow',
    '-e', 'HIREZERO_PLOW_FIXTURE=1', '-e', 'HIREZERO_LOCAL_ORIGIN=' + origin, pinnedImage]);
  owned.container = true;
  await waitReady();
  assert.equal(JSON.parse(await run('docker', ['inspect', name, '--format', '{{json .Image}}'])), pinnedImage);
}
async function removeContainer() {
  await requireLabel('container', name); await run('docker', ['logs', name]);
  await run('docker', ['stop', '--time', '20', name]); await run('docker', ['rm', name]);
  owned.container = false;
}
async function snapshot(cookie) {
  const state = await read('/api/marketing/state', cookie);
  return {profile: state.profile, tasks: state.tasks, campaigns: await read('/api/campaigns', cookie),
    ownerDirection: (await read('/api/continuity', cookie)).changedMind};
}
let imageId, upgradeImageId, failure, persistence, vault, transitions = [];
try {
  // Port admission precedes allocation, and a collision never stops an existing listener.
  const port = createServer();
  await new Promise((resolve, reject) => {port.once('error', reject); port.listen(5183, '127.0.0.1', resolve);});
  await new Promise(resolve => port.close(resolve));
  imageId = JSON.parse(await run('docker', ['image', 'inspect', image, '--format', '{{json .Id}}']));
  if (upgradeImage) {
    upgradeImageId = JSON.parse(await run('docker', ['image', 'inspect', upgradeImage, '--format', '{{json .Id}}']));
    assert.notEqual(upgradeImageId, imageId, 'Upgrade must use a genuinely different packaged image');
  }
  await run('docker', ['network', 'create', '--label', 'hirezero.plow.fixture=' + name, name]); owned.network = true;
  await run('docker', ['volume', 'create', '--label', 'hirezero.plow.fixture=' + name, name + '-state']); owned.volume = true;
  await startContainer(imageId);
  const first = await owner();
  const forbidden = await fetch(origin + '/api/marketing/profile', {method: 'PUT',
    headers: {'Content-Type': 'application/json', cookie: first.cookie, Origin: origin}, body: '{}'});
  assert.equal(forbidden.status, 403, 'Plow owner bootstrap must preserve mutation CSRF protection');
  vault = await read('/api/vault?check=1', first.cookie);
  assert.equal(vault.health.store, 'Encrypted Plow volume'); assert.equal(vault.health.working, true);
  const config = path.join(evidence, 'playwright.config.mjs');
  await writeFile(config, `export default {testDir:${JSON.stringify(path.join(repo, 'web/tests'))},timeout:45000,workers:1,use:{baseURL:${JSON.stringify(origin)},headless:true},reporter:[['list'],['json',{outputFile:${JSON.stringify(path.join(evidence, 'browser-results.json'))}}]]};\n`);
  await run(process.execPath, [path.join(repo, 'web/node_modules/@playwright/test/cli.js'), 'test', 'magical-host.spec.ts', '--config', config],
    {...process.env, THADDEUS_TEST_ORIGIN: origin, THADDEUS_TEST_PLOW: '1', THADDEUS_SCREENSHOTS: path.join(evidence, 'screenshots')});
  const before = {state: await read('/api/marketing/state', first.cookie), campaigns: await read('/api/campaigns', first.cookie), continuity: await read('/api/continuity', first.cookie)};
  await requireLabel('container', name); await run('docker', ['restart', '--time', '20', name]);
  await waitReady(); const reopened = await owner();
  const after = {state: await read('/api/marketing/state', reopened.cookie), campaigns: await read('/api/campaigns', reopened.cookie), continuity: await read('/api/continuity', reopened.cookie)};
  assert.equal(reopened.session.accountId, first.session.accountId);
  assert.deepEqual(after.state.profile, before.state.profile); assert.deepEqual(after.state.tasks, before.state.tasks);
  assert.deepEqual(after.campaigns, before.campaigns); assert.deepEqual(after.continuity.changedMind, before.continuity.changedMind);
  assert.equal((await read('/api/vault?check=1', reopened.cookie)).health.working, true);
  const modes = (await run('docker', ['exec', name, 'stat', '-c', '%a', '/var/lib/plow/credentials', '/var/lib/plow/credentials/key'])).trim().split(/\r?\n/);
  assert.deepEqual(modes, ['700', '600']);
  persistence = {ownerAccountStable: true, profile: true, tasks: true, campaigns: true, ownerDirection: true, credentialVault: true, modes};
  if (upgradeImageId) {
    const expected = await snapshot(reopened.cookie);
    const vaultHash = (await run('docker', ['exec', name, 'sha256sum', '/var/lib/plow/credentials/key'])).split(/\s+/)[0];
    for (const [direction, target] of [['upgrade', upgradeImageId], ['rollback', imageId]]) {
      await removeContainer(); await startContainer(target);
      const session = await owner();
      assert.equal(session.session.accountId, first.session.accountId);
      assert.deepEqual(await snapshot(session.cookie), expected, direction + ' retained owner work');
      assert.equal((await read('/api/vault?check=1', session.cookie)).health.working, true);
      assert.equal((await run('docker', ['exec', name, 'sha256sum', '/var/lib/plow/credentials/key'])).split(/\s+/)[0], vaultHash);
      transitions.push({direction, imageId: target, ownerAccountStable: true, savedWorkRetained: true, vaultKeyRetained: true});
    }
  }
} catch (error) {failure = error.message;}
finally {
  try {
    if (owned.container) {
      await removeContainer(); removed.push(name);
    }
    if (owned.volume) {await requireLabel('volume', name + '-state'); await run('docker', ['volume', 'rm', name + '-state']); removed.push(name + '-state');}
    if (owned.network) {await requireLabel('network', name); await run('docker', ['network', 'rm', name]); removed.push('network:' + name);}
  } catch (error) {failure = [failure, 'Cleanup failed: ' + error.message].filter(Boolean).join('; ');}
  await writeFile(path.join(evidence, 'receipt.json'), JSON.stringify({image, imageId, upgradeImage, upgradeImageId, transitions, space, commands, persistence,
    vault: vault?.health, failure, removed, simulated: true, apiRoutesMocked: false,
    network: 'Disposable bridge; fixture disables Plow boot, reporting and inference. Browser blocks external origins.', liveModelCalls: false}, null, 2));
}
if (failure) throw new Error(failure);
console.log('Packaged Plow cockpit persistence verified; disposable household swept away.');
