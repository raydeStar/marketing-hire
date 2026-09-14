import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { openSync, closeSync, createReadStream } from 'node:fs';
import { chmod, cp, mkdir, readFile, readdir, rename, stat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { createServer as createHttpServer } from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireArtifactSpace, cleanArtifactPaths } from './artifact-storage.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [publishedArgument, evidenceArgument, option, versionTarget, ...extra] = process.argv.slice(2);
assert.ok(publishedArgument && evidenceArgument && !extra.length && (!option || option === '--version-switch') && (!versionTarget || option));
const [publishedPath, evidencePath] = [publishedArgument, evidenceArgument].map(value => path.resolve(value));
const versionSwitch = option === '--version-switch';
const privateRoot = path.join(repository, 'artifacts') + path.sep;
if (!publishedPath?.startsWith(privateRoot) || !evidencePath?.startsWith(privateRoot)) throw new Error('Use an existing publication and a fresh check folder inside repository artifacts.');
const alternatePackage = versionTarget ? path.resolve(versionTarget) : null;
if (alternatePackage) assert.ok(alternatePackage.startsWith(privateRoot));
await mkdir(evidencePath);
const published = JSON.parse(await readFile(path.join(publishedPath, 'published.json'), 'utf8'));
const rid = `${{ win32: 'win', darwin: 'osx', linux: 'linux' }[process.platform]}-${process.arch}`;
assert.equal(published.runtime, rid, 'This check must execute on the package target architecture.');
const digest = value => createHash('sha256').update(value).digest('hex');
async function fileDigest(file) { const hash = createHash('sha256'); for await (const chunk of createReadStream(file)) hash.update(chunk); return hash.digest('hex'); }
const checksum = (await readFile(path.join(publishedPath, 'SHA256SUMS'), 'utf8')).split('  ')[0];
assert.equal(await fileDigest(published.archive), checksum);
const publishedManifest = JSON.parse(await readFile(published.manifest ?? path.join(published.package, 'package-manifest.json'), 'utf8'));
const packageBytes = publishedManifest.files.reduce((total, file) => {
  assert.ok(Number.isSafeInteger(file.size) && file.size >= 0);
  return total + file.size;
}, 0);
if (versionSwitch) assert.ok(!published.includesWorker && publishedManifest.application?.guardedLaunchVersion === 1,
  'Version-switch proof uses a host-only package with compatibility metadata, never another copied worker disk.');
const alternateManifest = alternatePackage ? JSON.parse(await readFile(path.join(alternatePackage, 'package-manifest.json'), 'utf8')) : publishedManifest;
if (versionSwitch) assert.ok(!alternateManifest.bundledWorker && alternateManifest.runtime === rid && alternateManifest.application?.guardedLaunchVersion === 1);
const alternateBytes = versionSwitch ? alternateManifest.files.reduce((total, file) => { assert.ok(Number.isSafeInteger(file.size) && file.size >= 0); return total + file.size; }, 0) : 0;
assert.ok(alternateBytes <= 2 * 1024 ** 3);
await requireArtifactSpace(evidencePath, packageBytes + alternateBytes + 128 * 1024 ** 2, 'Native package extraction and fixture data');
const extraction = path.join(evidencePath, 'extracted');
const owned = new Set();
let nativeCleanupConfirmed = true, launcherExitConfirmed = true, verification, failure;
try {
await mkdir(extraction);
let unpack;
if (process.platform === 'win32') unpack = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', 'Expand-Archive -LiteralPath $env:THADDEUS_ARCHIVE_SOURCE -DestinationPath $env:THADDEUS_ARCHIVE_TARGET'],
  { windowsHide: true, encoding: 'utf8', env: { ...process.env, THADDEUS_ARCHIVE_SOURCE: published.archive, THADDEUS_ARCHIVE_TARGET: extraction } });
else if (published.archive.endsWith('.zip')) unpack = spawnSync('unzip', ['-q', published.archive, '-d', extraction], { encoding: 'utf8' });
else unpack = spawnSync('tar', ['-xzf', published.archive, '-C', extraction], { encoding: 'utf8' });
assert.equal(unpack.status, 0, unpack.stderr);
const packagePath = path.join(extraction, `thaddeus-${rid}`);
const executable = path.join(packagePath, `Thaddeus.Host${process.platform === 'win32' ? '.exe' : ''}`);
const manifest = JSON.parse(await readFile(path.join(packagePath, 'package-manifest.json'), 'utf8'));
assert.equal(manifest.runtime, rid); assert.equal(manifest.sourceHead, published.sourceHead);
async function files(directory, prefix = '') {
  const result = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    assert.equal(entry.isSymbolicLink(), false, 'Extracted package contains a link.');
    if (entry.isDirectory()) result.push(...await files(path.join(directory, entry.name), prefix + entry.name + '/'));
    else result.push(prefix + entry.name);
  }
  return result;
}
assert.deepEqual((await files(packagePath)).sort(), [...manifest.files.map(file => file.path), 'package-manifest.json'].sort());
for (const file of manifest.files) {
  assert.ok(!path.isAbsolute(file.path) && !file.path.split('/').includes('..'));
  const filePath = path.join(packagePath, file.path);
  assert.equal((await stat(filePath)).size, file.size); assert.equal(await fileDigest(filePath), file.sha256, file.path);
}
// Keep the inventory as evidence; the travelling butler returns his borrowed suitcase.
await writeFile(path.join(evidencePath, 'tested-package-manifest.json'), await readFile(path.join(packagePath, 'package-manifest.json')));
const runtime = JSON.parse(await readFile(path.join(packagePath, 'Thaddeus.Host.runtimeconfig.json'), 'utf8'));
assert.ok(runtime.runtimeOptions.includedFrameworks?.length > 0, 'The archive must include the runtime.');
assert.equal(runtime.runtimeOptions.framework, undefined);
if (process.platform !== 'win32') {
  assert.ok((await stat(executable)).mode & 0o100);
  assert.ok((await stat(path.join(packagePath, process.platform === 'darwin' ? 'Start Thaddeus.command' : 'start-thaddeus.sh'))).mode & 0o100);
}
const checks = ['Archive checksum and extracted file inventory match; bundled runtime and executable permissions are present'];
const data = path.join(evidencePath, 'private data-é');
const foreignData = path.join(evidencePath, 'inherited-data-must-not-exist');
let sequence = 0;
async function reserve() {
  const server = createServer(); await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); return server;
}
async function release(server) { await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve())); }
const first = await reserve(), second = await reserve();
const origin = `http://127.0.0.1:${first.address().port}`, workerPort = second.address().port;
await release(first); await release(second);
const settings = { schemaVersion: 1, dataDirectory: data, localOrigin: origin, workerPort };
async function start(profile, label) {
  const profilePath = path.join(evidencePath, `${label}-${++sequence}.json`);
  await writeFile(profilePath, JSON.stringify(profile));
  const out = openSync(path.join(evidencePath, `${label}-${sequence}.stdout.log`), 'wx');
  const err = openSync(path.join(evidencePath, `${label}-${sequence}.stderr.log`), 'wx');
  let child;
  try {
    const command = process.platform === 'win32' ? executable : path.join(packagePath, process.platform === 'darwin' ? 'Start Thaddeus.command' : 'start-thaddeus.sh');
    const args = [...(process.platform === 'win32' ? ['--desktop'] : []), '--no-browser', '--launch-profile', profilePath];
    child = spawn(command, args, { cwd: evidencePath, windowsHide: true,
      stdio: ['ignore', out, err], env: { ...process.env, DOTNET_ROOT: path.join(evidencePath, 'no-shared-runtime'), Thaddeus__Data: foreignData, Thaddeus__PhoneOrigin: 'https://192.0.2.99:7443' } });
  } finally { closeSync(out); closeSync(err); }
  owned.add(child);
  child.finished = new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', (code, signal) => { owned.delete(child); resolve({ code, signal }); }); });
  return child;
}
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
async function boundedExit(child) {
  let timer;
  try { return await Promise.race([child.finished, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Owned test host did not exit before deadline.')), 15_000); })]); }
  finally { clearTimeout(timer); }
}
async function startRestored(launcher, label = 'generated-launch') {
  const out = openSync(path.join(evidencePath, label + '.stdout.log'), 'wx');
  const err = openSync(path.join(evidencePath, label + '.stderr.log'), 'wx');
  let child;
  try { child = spawn(launcher.entryPoint, ['--no-browser'], { cwd: evidencePath, windowsHide: true, stdio: ['ignore', out, err],
    env: { ...process.env, DOTNET_ROOT: path.join(evidencePath, 'no-shared-runtime') } }); }
  finally { closeSync(out); closeSync(err); }
  owned.add(child);
  child.finished = new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', (code, signal) => { owned.delete(child); resolve({ code, signal }); }); });
  return child;
}
async function stop(child) {
  if (!owned.has(child)) return;
  child.kill(process.platform === 'win32' ? 'SIGTERM' : 'SIGINT');
  await boundedExit(child);
}
async function ready(child, expectedPackage = packagePath) {
  const expected = await readFile(path.join(expectedPackage, 'wwwroot/index.html'), 'utf8');
  for (let step = 0; step < 150; step++) {
    if (!owned.has(child)) throw new Error('Packaged host exited during startup; inspect its retained stderr log.');
    try {
      const response = await fetch(origin, { signal: AbortSignal.timeout(1000) });
      if (response.ok && await response.text() === expected) {
        for (const asset of [...expected.matchAll(/(?:src|href)="(\/assets\/[^"\s]+)"/g)].map(match => match[1])) {
          const result = await fetch(origin + asset);
          assert.equal(result.status, 200);
          assert.equal(digest(Buffer.from(await result.arrayBuffer())), digest(await readFile(path.join(expectedPackage, 'wwwroot', asset))));
        }
        return;
      }
    } catch { }
    await delay(200);
  }
  throw new Error('The exact packaged web client did not become ready.');
}
async function session(dataDirectory = data) {
  const key = (await readFile(path.join(dataDirectory, 'host-key.txt'), 'utf8')).trim();
  const headers = { Origin: origin, 'Content-Type': 'application/json' };
  const ticketResponse = await fetch(origin + '/api/auth/launch', { method: 'POST', headers, body: JSON.stringify({ key }) });
  assert.equal(ticketResponse.status, 200);
  const { ticket } = await ticketResponse.json(); assert.match(ticket, /^[a-f0-9]{48}$/);
  const claim = () => fetch(origin + '/api/auth/claim-launch', { method: 'POST', headers, body: JSON.stringify({ ticket }) });
  const response = await claim(); assert.equal(response.status, 200);
  const login = await response.json(); assert.equal(login.owner, true);
  assert.match(response.headers.get('set-cookie'), /httponly/i);
  assert.equal((await claim()).status, 401);
  const call = async (route, body, method = body ? 'POST' : 'GET', expectedStatus = 200) => {
    const result = await fetch(origin + '/api' + route, { method, headers: { ...headers, Cookie: response.headers.get('set-cookie').split(';')[0], 'X-CSRF': login.csrf }, body: body ? JSON.stringify(body) : undefined });
    assert.equal(result.status, expectedStatus, route); return result.json();
  };
  call.headers = { ...headers, Cookie: response.headers.get('set-cookie').split(';')[0], 'X-CSRF': login.csrf };
  return call;
}
function maintenance(operation, source, destination, expected = 0) {
  const result = spawnSync(executable, [operation, source, destination], { cwd: evidencePath, encoding: 'utf8', windowsHide: true, timeout: 30_000, maxBuffer: 32_000 });
  assert.equal(result.error, undefined, 'Packaged study maintenance failed to finish.');
  if (expected === 0) { assert.equal(result.status, 0, result.stderr); return JSON.parse(result.stdout); }
  assert.notEqual(result.status, 0, 'The maintenance operation should have been refused.');
  return result.stderr;
}
const fictionalKey = 'fictional-native-package-credential';
let discoveryRequests = 0, invalidDiscoveryRequests = 0, credentialSetupAttempted = false, credentialsRemoved = false;
const discovery = createHttpServer((request, response) => {
  discoveryRequests++;
  if (request.method !== 'GET' || request.url !== '/v1/models' || request.headers.authorization !== 'Bearer ' + fictionalKey || request.headers.cookie) invalidDiscoveryRequests++;
  response.writeHead(200, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify({ data: [{ id: 'fictional-native-package-model' }] }));
});
await new Promise(resolve => discovery.listen(0, '127.0.0.1', resolve));
async function removeFixtureCredentials(api) {
  let connection = await api('/settings/connection');
  for (const credential of connection.credentials) {
    await api(`/settings/connection/credentials/${credential.id}/remove`, { version: connection.version });
    connection = await api('/settings/connection');
  }
  assert.equal(connection.credentials.length, 0);
  let search = await api('/settings/search');
  for (const credential of search.credentials) {
    await api(`/settings/search/credentials/${credential.id}/remove`, { version: search.version });
    search = await api('/settings/search');
  }
  assert.equal(search.credentials.length, 0);
  credentialsRemoved = true;
}
nativeCleanupConfirmed = false;
try {
  const running = await start(settings, 'first-start'); await ready(running);
  checks.push('Published native executable serves exact PWA assets from an unrelated working directory without a shared .NET runtime');
  const api = await session(); await api('/demo/seed', {});
  const before = await api('/export'), state = await api('/state');
  assert.ok(before.pages.length > 0); assert.equal(before.runs.length, 0); assert.equal(state.research.enabled, false); assert.equal(state.phoneOrigin, null);
  if (published.includesWorker) {
    const worker = await api('/settings/worker');
    assert.equal(worker.worker?.backend, process.platform === 'win32' ? 'qemu-whpx' : 'qemu-kvm');
    assert.equal(worker.worker.developmentOnly, true); assert.equal(worker.status, 'check-required');
    assert.equal(worker.enabled, false); assert.equal(worker.canEnable, false); assert.equal(worker.lastCheck, null);
    assert.equal(manifest.bundledWorker.descriptor, 'worker/installation.json');
    assert.equal(await fileDigest(path.join(packagePath, manifest.bundledWorker.descriptor)), manifest.bundledWorker.descriptorSha256);
    checks.push('Relocated combined archive discovers its pinned worker without an operator installation path; research stays disabled pending an explicit check and enrollment');
  }
  await assert.rejects(stat(foreignData), { code: 'ENOENT' });
  if (process.platform !== 'win32') assert.equal((await stat(data)).mode & 0o777, 0o700);
  const keyHash = digest(await readFile(path.join(data, 'host-key.txt')));
  checks.push('One-use local owner login, seeded SQLite/Markdown data, private Unix directory and ignored inherited network/data settings');
  const originalConnection = await api('/settings/connection');
  credentialSetupAttempted = true;
  const savedConnection = await api('/settings/connection', { version: originalConnection.version, credentialMode: 'system', key: fictionalKey,
    provider: { kind: 'compatible', model: 'fictional-native-package-model', reasoning: 'high', endpoint: `http://127.0.0.1:${discovery.address().port}/v1` } }, 'PUT');
  assert.equal(JSON.stringify(savedConnection).includes(fictionalKey), false);
  const selectedConnection = await api('/settings/connection');
  assert.equal(selectedConnection.credentialMode, 'system'); assert.equal(selectedConnection.credentials.length, 1);
  await api('/settings/test', {}); assert.equal(discoveryRequests, 1); assert.equal(invalidDiscoveryRequests, 0);
  assert.equal(JSON.stringify(await api('/export')).includes(fictionalKey), false);
  checks.push('Owner saves an endpoint-bound key in the native store; authenticated model-list discovery makes no generation request and no data file or export contains the key');
  const searchKey = 'fictional-native-package-search-key';
  const emptySearch = await api('/settings/search');
  const savedSearch = await api('/settings/search', { version: emptySearch.version, storage: 'system', key: searchKey, retainResults: true }, 'PUT');
  assert.equal(savedSearch.summary.configured, true); assert.equal(savedSearch.summary.providerVerified, false);
  assert.equal(JSON.stringify(savedSearch).includes(searchKey), false);
  const searchCheck = await api('/settings/search/check', { version: savedSearch.version });
  assert.equal(searchCheck.available, true); assert.equal(searchCheck.providerVerified, false);
  assert.deepEqual(await api('/settings/connection'), selectedConnection);
  checks.push('Search credentials use a separate native-store reference; checking key availability makes no provider request or model-connection change');
  const duplicate = await start(settings, 'duplicate'); assert.notEqual((await boundedExit(duplicate)).code, 0);
  assert.ok(owned.has(running));
  const other = { ...settings, dataDirectory: path.join(evidencePath, 'other-data') };
  const conflicting = await start(other, 'occupied-port'); assert.notEqual((await boundedExit(conflicting)).code, 0);
  await assert.rejects(stat(other.dataDirectory), { code: 'ENOENT' });
  checks.push('Duplicate and occupied-port starts fail without replacing the active host or creating another store');
  const refusedBackup = path.join(evidencePath, 'live-backup-must-not-exist');
  maintenance('--study-backup', data, refusedBackup, 1); await assert.rejects(stat(refusedBackup), { code: 'ENOENT' });
  assert.ok(owned.has(running));
  await stop(running);
  const backup = path.join(evidencePath, 'closed-study-backup');
  const backupReceipt = maintenance('--study-backup', data, backup);
  assert.equal(backupReceipt.operation, 'backup'); assert.equal(backupReceipt.databaseSchemaVersion, before.databaseSchemaVersion);
  const backupManifestHash = digest(await readFile(path.join(backup, 'backup.json')));
  maintenance('--study-backup', data, backup, 1); assert.equal(digest(await readFile(path.join(backup, 'backup.json'))), backupManifestHash);
  checks.push('Packaged backup refuses an active host, snapshots the closed study including durable SQLite journal content, and never overwrites an existing backup');
  for (const file of await files(data)) assert.equal((await readFile(path.join(data, file))).includes(Buffer.from(fictionalKey)), false, 'A credential appeared in a private data file.');
  for (const file of await files(data)) assert.equal((await readFile(path.join(data, file))).includes(Buffer.from(searchKey)), false, 'A search credential appeared in a private data file.');
  const restarted = await start(settings, 'restart'); await ready(restarted);
  const afterApi = await session(), after = await afterApi('/export');
  assert.deepEqual(after, before); assert.equal(digest(await readFile(path.join(data, 'host-key.txt'))), keyHash);
  checks.push('Restart preserves every exported row and the exact access key, with no model task or worker start');
  assert.deepEqual(await afterApi('/settings/connection'), selectedConnection);
  await afterApi('/settings/test', {}); assert.equal(discoveryRequests, 2); assert.equal(invalidDiscoveryRequests, 0);
  checks.push('A restarted extracted host retrieves the saved native credential through the product transport and preserves connection metadata');
  assert.deepEqual(await afterApi('/settings/search'), savedSearch);
  assert.equal((await afterApi('/settings/search/check', { version: savedSearch.version })).available, true);
  checks.push('Restarted extracted host reads the original search key from its native store without contacting the search provider');
  await removeFixtureCredentials(afterApi);
  await afterApi('/settings/search/check', { version: (await afterApi('/settings/search')).version }, 'POST', 409);
  await afterApi('/settings/test', {}, 'POST', 409); assert.equal(discoveryRequests, 2);
  const removedConnection = await afterApi('/settings/connection');
  await afterApi('/settings/connection', { version: removedConnection.version, credentialMode: 'none', provider: originalConnection.provider }, 'PUT');
  assert.deepEqual(await afterApi('/export'), before);
  checks.push('Explicit credential removal is confirmed and blocks further provider requests; all fixture credentials are removed without creating a task');
  await afterApi('/knowledge', { path: 'notes/after-backup.md', content: 'A later original-study edit.', version: 'absent' }, 'PUT');
  await stop(restarted);
  const restoredData = path.join(evidencePath, "restored study ' " + 'deep'.repeat(12));
  const restoreReceipt = maintenance('--study-restore', backup, restoredData);
  assert.equal(restoreReceipt.operation, 'restore'); assert.equal(restoreReceipt.manifestSha256, backupManifestHash);
  maintenance('--study-restore', backup, data, 1);
  const restoredHost = await start({ ...settings, dataDirectory: restoredData }, 'restored-start'); await ready(restoredHost);
  const restoredApi = await session(restoredData);
  assert.deepEqual(await restoredApi('/export'), before); assert.deepEqual(await restoredApi('/settings/connection'), selectedConnection);
  assert.equal(digest(await readFile(path.join(restoredData, 'host-key.txt'))), keyHash);
  await restoredApi('/settings/test', {}, 'POST', 409); assert.equal(discoveryRequests, 2);
  assert.deepEqual(await restoredApi('/settings/search'), savedSearch);
  await restoredApi('/settings/search/check', { version: savedSearch.version }, 'POST', 409);
  await removeFixtureCredentials(restoredApi);
  const review = await restoredApi('/maintenance'); assert.equal(review.phase, 'ready'); assert.equal(review.canStart, true);
  assert.equal((await fetch(origin + '/api/maintenance')).status, 401);
  const badCsrf = await fetch(origin + '/api/maintenance/start', { method: 'POST', headers: { ...restoredApi.headers, 'X-CSRF': 'wrong' }, body: JSON.stringify({ version: review.version, mode: 'backup' }) });
  assert.equal(badCsrf.status, 403);
  await restoredApi('/maintenance/start', { version: 'stale', mode: 'backup' }, 'POST', 409);
  const streamAbort = new AbortController();
  const stream = await fetch(origin + '/api/events', { headers: restoredApi.headers, signal: streamAbort.signal }); assert.equal(stream.status, 200);
  const streamDone = stream.text();
  const closing = await restoredApi('/maintenance/start', { version: review.version, mode: 'backup' }); assert.equal(closing.phase, 'closing');
  const streamDeadline = setTimeout(() => streamAbort.abort(), 10_000);
  try { await streamDone; } finally { clearTimeout(streamDeadline); streamAbort.abort(); }
  async function maintenanceState(api, phase) {
    for (let step = 0; step < 100; step++) {
      assert.ok(owned.has(restoredHost), 'The owned host exited during maintenance. Inspect its retained stderr.');
      try { const state = await api('/maintenance'); if (state.phase === phase) return state; if (state.phase === 'failed') throw new Error(state.message); }
      catch (error) { if (error.message?.includes('completed backup')) throw error; }
      await delay(200);
    }
    throw new Error(`The maintenance screen did not reach ${phase}; inspect its retained host log.`);
  }
  const verifiedBackup = await maintenanceState(restoredApi, 'verified');
  assert.equal(verifiedBackup.receipt.directory, closing.destination);
  assert.equal(digest(await readFile(path.join(closing.destination, 'backup.json'))), verifiedBackup.receipt.manifestSha256);
  assert.deepEqual(JSON.parse(await readFile(path.join(verifiedBackup.backupRoot, verifiedBackup.version + '.receipt.json'), 'utf8')), verifiedBackup.receipt);
  await restoredApi('/state', undefined, 'GET', 503);
  await restoredApi('/chat', { content: 'This fictional request must not dispatch.' }, 'POST', 503);
  assert.equal(discoveryRequests, 2);
  const freeWorkerPort = createServer(); await new Promise((resolve, reject) => { freeWorkerPort.once('error', reject); freeWorkerPort.listen(workerPort, '127.0.0.1', resolve); }); await release(freeWorkerPort);
  checks.push('Local-owner maintenance closes an open event stream and all product/worker services, then verifies a real backup; unauthenticated, stale and wrong-CSRF requests are refused');
  await restoredApi('/maintenance/finish', { version: verifiedBackup.version, mode: 'reopen' });
  await ready(restoredHost);
  const reopenedApi = await session(restoredData); assert.deepEqual(await reopenedApi('/export'), before);
  await reopenedApi('/knowledge', { path: 'notes/guided-later.md', content: 'Keep the newer original study intact.', version: 'absent' }, 'PUT');
  const originalReturnExport = versionSwitch ? await reopenedApi('/export') : null;
  const stopReview = await reopenedApi('/maintenance');
  await reopenedApi('/maintenance/start', { version: stopReview.version, mode: 'stop' });
  const stopped = await maintenanceState(reopenedApi, 'stopped'); assert.equal(stopped.destination, null);
  const choices = await reopenedApi('/maintenance/backups'); assert.equal(choices.length, 1); assert.equal(choices[0].id, verifiedBackup.version);
  const selectedApplication = versionSwitch ? path.join(extraction, "selected application ' $ fixture") : packagePath;
  if (versionSwitch) await cp(alternatePackage ?? packagePath, selectedApplication, { recursive: true, errorOnExist: true, force: false });
  const selectedManifestHash = versionSwitch ? await fileDigest(path.join(selectedApplication, 'package-manifest.json')) : null;
  const differentApplicationBuild = versionSwitch && await fileDigest(path.join(selectedApplication, 'Thaddeus.Host.dll')) !== await fileDigest(path.join(packagePath, 'Thaddeus.Host.dll'));
  const restoreReview = await reopenedApi('/maintenance/restore/review', { backupId: verifiedBackup.version, ...(versionSwitch ? { packageDirectory: selectedApplication } : {}) });
  if (versionSwitch) {
    assert.equal(restoreReview.review.application.directory, selectedApplication);
    assert.equal(restoreReview.review.application.publisherVerified, false);
    assert.equal(restoreReview.review.application.manifestSha256, await fileDigest(path.join(selectedApplication, 'package-manifest.json')));
  }
  assert.equal(restoreReview.review.canPrepareLauncher, true);
  assert.equal(restoreReview.review.backup.manifestSha256, verifiedBackup.receipt.manifestSha256);
  await assert.rejects(stat(restoreReview.review.destination), { code: 'ENOENT' });
  await reopenedApi('/maintenance/restore/start', { reviewId: 'stale' }, 'POST', 409);
  const unauthorizedRestore = await fetch(origin + '/api/maintenance/restore/start', { method: 'POST', headers: { ...reopenedApi.headers, 'X-CSRF': 'wrong' }, body: JSON.stringify({ reviewId: restoreReview.review.id }) });
  assert.equal(unauthorizedRestore.status, 403);
  await reopenedApi('/maintenance/restore/start', { reviewId: restoreReview.review.id });
  let guided;
  for (let attempt = 0; attempt < 150; attempt++) {
    assert.ok(owned.has(restoredHost)); guided = await reopenedApi('/maintenance/restore');
    if (guided.phase !== 'restoring') break;
    await delay(200);
  }
  assert.equal(guided.phase, 'restored'); assert.ok(guided.launcher);
  assert.deepEqual(await reopenedApi('/maintenance/restore/start', { reviewId: restoreReview.review.id }), guided);
  for (const [name, hash] of Object.entries(guided.launcher.fileHashes)) assert.equal(digest(await readFile(path.join(guided.launcher.directory, name))), hash);
  assert.equal(await readFile(path.join(restoredData, 'knowledge/notes/guided-later.md'), 'utf8'), 'Keep the newer original study intact.');
  await assert.rejects(stat(path.join(guided.receipt.directory, 'knowledge/notes/guided-later.md')), { code: 'ENOENT' });
  assert.equal(digest(await readFile(path.join(guided.receipt.directory, 'host-key.txt'))), keyHash);
  checks.push('Guided restore binds the reviewed backup, rejects stale and unauthorized confirmation, preserves newer original edits, and records one verified copy plus a separate hashed launcher');
  await reopenedApi('/maintenance/finish', { version: stopped.version, mode: 'close' });
  assert.equal((await boundedExit(restoredHost)).code, 0);
  checks.push('The same packaged process reopens its study after maintenance and exits cleanly through the owner screen without a model request');
  if (versionSwitch) {
    assert.equal(guided.launcher.package, selectedApplication);
    const page = path.join(selectedApplication, 'wwwroot/index.html'), originalPage = await readFile(page);
    const database = path.join(guided.receipt.directory, 'ledger.sqlite'), databaseHash = await fileDigest(database);
    let refused;
    try {
      await writeFile(page, 'Changed after the application review.');
      refused = process.platform === 'win32'
        ? spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-File', path.join(guided.launcher.directory, 'Open restored study.ps1'), '-NoBrowser'],
          { windowsHide: true, encoding: 'utf8', timeout: 60_000 })
        : spawnSync(guided.launcher.entryPoint, ['--no-browser'], { encoding: 'utf8', timeout: 60_000 });
    } finally { await writeFile(page, originalPage); }
    await writeFile(path.join(evidencePath, 'changed-package-launch.stdout.log'), refused.stdout ?? '');
    await writeFile(path.join(evidencePath, 'changed-package-launch.stderr.log'), refused.stderr ?? '');
    assert.equal(refused.error, undefined); assert.notEqual(refused.status, 0);
    assert.equal(await fileDigest(database), databaseHash);
    await assert.rejects(stat(path.join(guided.receipt.directory, 'launcher-instance.json')), { code: 'ENOENT' });
    const closedPort = await fetch(origin, { signal: AbortSignal.timeout(1000) }).then(() => false, () => true); assert.equal(closedPort, true);
    checks.push('Selected application copy is bound to the review; the generated launcher refuses later payload changes before a host starts or restored database changes');
  }
  if (process.platform === 'win32') {
    const launchEvidence = path.join(evidencePath, 'generated-launcher');
    launcherExitConfirmed = false;
    const proof = spawnSync('powershell.exe', ['-NoProfile', '-File', path.join(repository, 'scripts/restored-launcher-check.ps1'),
      '-LauncherFolder', guided.launcher.directory, '-Package', selectedApplication, '-Evidence', launchEvidence],
      { cwd: repository, windowsHide: true, encoding: 'utf8', timeout: 150_000 });
    assert.equal(proof.error, undefined); assert.equal(proof.status, 0, proof.stderr);
    launcherExitConfirmed = true;
    const observed = JSON.parse((await readFile(path.join(launchEvidence, 'verified.json'), 'utf8')).replace(/^\uFEFF/, ''));
    assert.equal(observed.passed, true); assert.deepEqual(observed.export, before); assert.equal(observed.nextRestoreLauncherAvailable, true);
  } else {
    const generated = await startRestored(guided.launcher); await ready(generated, selectedApplication);
    const generatedApi = await session(guided.receipt.directory); assert.deepEqual(await generatedApi('/export'), before);
    const next = await generatedApi('/maintenance'); await generatedApi('/maintenance/start', { version: next.version, mode: 'stop' });
    let closed;
    for (let attempt = 0; attempt < 150; attempt++) {
      assert.ok(owned.has(generated));
      try { closed = await generatedApi('/maintenance'); if (closed.phase === 'stopped') break; } catch { }
      await delay(200);
    }
    assert.equal(closed.phase, 'stopped'); await generatedApi('/maintenance/finish', { version: closed.version, mode: 'close' });
    assert.equal((await boundedExit(generated)).code, 0);
  }
  checks.push('The generated platform launcher opens the separate restored history through the real product and shuts down through its owner API without an SDK, worker or model request');
  if (versionSwitch) checks.push(differentApplicationBuild
    ? 'A guarded launcher opens a different checked host assembly in the selected directory; this checks compatible application builds, not an OS or different-schema migration'
    : 'A guarded launcher opens a distinct selected application directory after verification; this checks switching between copies of the same build, not an OS or schema migration');
  if (versionSwitch) {
    assert.equal(guided.returnLauncher.package, packagePath);
    assert.equal(JSON.parse(await readFile(guided.returnLauncher.profile, 'utf8')).dataDirectory, restoredData);
    if (process.platform === 'win32') {
      const evidence = path.join(evidencePath, 'original-return-launcher'); launcherExitConfirmed = false;
      const proof = spawnSync('powershell.exe', ['-NoProfile', '-File', path.join(repository, 'scripts/restored-launcher-check.ps1'),
        '-LauncherFolder', guided.returnLauncher.directory, '-Package', packagePath, '-Evidence', evidence, '-OriginalStudy'],
        { cwd: repository, windowsHide: true, encoding: 'utf8', timeout: 150_000 });
      assert.equal(proof.error, undefined); assert.equal(proof.status, 0, proof.stderr); launcherExitConfirmed = true;
      const observed = JSON.parse((await readFile(path.join(evidence, 'verified.json'), 'utf8')).replace(/^\uFEFF/, ''));
      assert.equal(observed.passed, true); assert.deepEqual(observed.export, originalReturnExport);
    } else {
      const returned = await startRestored(guided.returnLauncher, 'original-return'); await ready(returned);
      assert.deepEqual(await (await session(restoredData))('/export'), originalReturnExport); await stop(returned);
    }
    checks.push('A separate return launcher opens the original study with its original app and newer edits, without depending on an earlier application version');
  }
  assert.equal(await readFile(path.join(data, 'knowledge/notes/after-backup.md'), 'utf8'), 'A later original-study edit.');
  await assert.rejects(stat(path.join(restoredData, 'knowledge/notes/after-backup.md')), { code: 'ENOENT' });
  checks.push('A restored study starts from the extracted package with identical history and owner key, preserves later original edits, and cannot resurrect a removed native credential');
  const foreign = await reserve(), spare = await reserve();
  try {
    const foreignSettings = { ...settings, dataDirectory: path.join(evidencePath, 'foreign-data'), localOrigin: `http://127.0.0.1:${foreign.address().port}`, workerPort: spare.address().port };
    await release(spare);
    const refused = await start(foreignSettings, 'foreign-listener'); assert.notEqual((await boundedExit(refused)).code, 0);
    assert.ok(foreign.listening); await assert.rejects(stat(foreignSettings.dataDirectory), { code: 'ENOENT' });
  } finally { await release(foreign); if (spare.listening) await release(spare); }
  checks.push('An unrelated listening socket remains intact and the refused data directory is absent');
  if (process.platform !== 'win32') {
    function pipe(file) {
      const result = spawnSync('mkfifo', [file], { encoding: 'utf8', timeout: 5000 });
      assert.equal(result.error, undefined); assert.equal(result.status, 0, result.stderr);
    }
    pipe(path.join(data, 'named-pipe'));
    assert.match(maintenance('--study-backup', data, path.join(evidencePath, 'pipe-backup-refused'), 1), /Non-seekable/);
    const pipeManifest = path.join(evidencePath, 'pipe-manifest-backup'); await mkdir(pipeManifest);
    pipe(path.join(pipeManifest, 'backup.json'));
    assert.match(maintenance('--study-restore', pipeManifest, path.join(evidencePath, 'pipe-manifest-refused'), 1), /Non-seekable/);
    const entries = JSON.parse(await readFile(path.join(backup, 'backup.json'), 'utf8')).files;
    const note = entries.find(file => file.path.startsWith('knowledge/') && file.path.endsWith('.md'));
    assert.ok(note);
    const payloadFile = path.join(backup, 'data', note.path);
    await rename(payloadFile, path.join(evidencePath, 'preserved-original-note.md')); pipe(payloadFile);
    assert.match(maintenance('--study-restore', backup, path.join(evidencePath, 'pipe-payload-refused'), 1), /Non-seekable/);
    checks.push('Actual Unix named pipes in source files, the manifest and restored payload are refused without waiting for a writer');
    const wide = path.join(evidencePath, 'wide-permissions'); await mkdir(wide); await chmod(wide, 0o755);
    const refused = await start({ ...settings, dataDirectory: wide }, 'wide-permissions'); assert.notEqual((await boundedExit(refused)).code, 0);
    assert.equal((await stat(wide)).mode & 0o777, 0o755);
    await assert.rejects(stat(path.join(wide, 'ledger.sqlite')), { code: 'ENOENT' });
    checks.push('Existing broadly readable Unix data is refused without changing its permissions or initializing a database');
  }
  verification = { passed: true, runtime: rid, os: os.version(), release: os.release(), sourceHead: manifest.sourceHead,
    sourceDirty: manifest.checkoutDirty, archiveSha256: checksum, checks, liveModelCalls: 0, gpuInference: 0, isolatedWorkerQualified: false,
    browserAutomaticallyOpened: false, versionSwitchChecked: versionSwitch, differentApplicationBuildChecked: differentApplicationBuild,
    selectedApplicationManifestSha256: selectedManifestHash, differentSchemaMigrationChecked: false,
    termination: process.platform === 'win32' ? 'owned test process terminated' : 'SIGINT graceful host shutdown' };
} finally {
  try {
    // A failed native write can still leave a pending entry. Use the product's recorded IDs to retire it.
    if (credentialSetupAttempted && !credentialsRemoved) {
      for (const child of [...owned]) await stop(child);
      const cleanup = await start(settings, 'credential-cleanup'); await ready(cleanup);
      await removeFixtureCredentials(await session());
    }
  } finally { for (const child of [...owned]) await stop(child); await release(discovery); }
  nativeCleanupConfirmed = true;
}
} catch (error) { failure = error; throw error; }
finally {
  const cleanup = { passed: false, removed: [], ownedProcessesRemaining: owned.size,
    nativeCleanupConfirmed, launcherExitConfirmed,
    retained: ['tested package manifest, logs and receipts', 'small fictional studies and backup evidence'] };
  try {
    assert.ok(nativeCleanupConfirmed && launcherExitConfirmed && owned.size === 0,
      'Package scratch retained: test process or credential cleanup was not confirmed. Inspect the receipt before removing it.');
    cleanup.removed = await cleanArtifactPaths(evidencePath, ['extracted']);
    cleanup.passed = true;
  } catch (error) {
    cleanup.error = error.message;
    if (!failure) { failure = error; process.exitCode = 1; }
  }
  await writeFile(path.join(evidencePath, 'scratch-cleanup.json'), JSON.stringify(cleanup, null, 2) + '\n', { flush: true });
  await writeFile(path.join(evidencePath, 'verified.json'), JSON.stringify({
    ...verification, passed: verification?.passed === true && !failure && cleanup.passed,
    runtime: rid, archiveSha256: checksum, error: failure?.message, cleanup
  }, null, 2) + '\n', { flush: true });
}
if (!failure) console.log(`${verification.checks.length} extracted ${rid} package checks passed and scratch was removed. The butler packed away his suitcase.`);
