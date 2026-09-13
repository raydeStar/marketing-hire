import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { openSync, closeSync } from 'node:fs';
import { chmod, mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [publishedPath, evidencePath] = process.argv.slice(2).map(value => path.resolve(value));
const privateRoot = path.join(repository, 'artifacts') + path.sep;
if (!publishedPath?.startsWith(privateRoot) || !evidencePath?.startsWith(privateRoot)) throw new Error('Use an existing publication and a fresh check folder inside repository artifacts.');
await mkdir(evidencePath);
const published = JSON.parse(await readFile(path.join(publishedPath, 'published.json'), 'utf8'));
const rid = `${{ win32: 'win', darwin: 'osx', linux: 'linux' }[process.platform]}-${process.arch}`;
assert.equal(published.runtime, rid, 'This check must execute on the package target architecture.');
const digest = value => createHash('sha256').update(value).digest('hex');
const checksum = (await readFile(path.join(publishedPath, 'SHA256SUMS'), 'utf8')).split('  ')[0];
assert.equal(digest(await readFile(published.archive)), checksum);
const extraction = path.join(evidencePath, 'extracted'); await mkdir(extraction);
let unpack;
if (process.platform === 'win32') unpack = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', 'Expand-Archive -LiteralPath $env:THADDEUS_ARCHIVE_SOURCE -DestinationPath $env:THADDEUS_ARCHIVE_TARGET'],
  { encoding: 'utf8', env: { ...process.env, THADDEUS_ARCHIVE_SOURCE: published.archive, THADDEUS_ARCHIVE_TARGET: extraction } });
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
  const bytes = await readFile(path.join(packagePath, file.path));
  assert.equal(bytes.length, file.size); assert.equal(digest(bytes), file.sha256, file.path);
}
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
const owned = new Set();
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
async function stop(child) {
  if (!owned.has(child)) return;
  child.kill(process.platform === 'win32' ? 'SIGTERM' : 'SIGINT');
  await boundedExit(child);
}
async function ready(child) {
  const expected = await readFile(path.join(packagePath, 'wwwroot/index.html'), 'utf8');
  for (let step = 0; step < 150; step++) {
    if (!owned.has(child)) throw new Error('Packaged host exited during startup; inspect its retained stderr log.');
    try {
      const response = await fetch(origin, { signal: AbortSignal.timeout(1000) });
      if (response.ok && await response.text() === expected) {
        for (const asset of [...expected.matchAll(/(?:src|href)="(\/assets\/[^"\s]+)"/g)].map(match => match[1])) {
          const result = await fetch(origin + asset);
          assert.equal(result.status, 200);
          assert.equal(digest(Buffer.from(await result.arrayBuffer())), digest(await readFile(path.join(packagePath, 'wwwroot', asset))));
        }
        return;
      }
    } catch { }
    await delay(200);
  }
  throw new Error('The exact packaged web client did not become ready.');
}
async function session() {
  const key = (await readFile(path.join(data, 'host-key.txt'), 'utf8')).trim();
  const headers = { Origin: origin, 'Content-Type': 'application/json' };
  const ticketResponse = await fetch(origin + '/api/auth/launch', { method: 'POST', headers, body: JSON.stringify({ key }) });
  assert.equal(ticketResponse.status, 200);
  const { ticket } = await ticketResponse.json(); assert.match(ticket, /^[a-f0-9]{48}$/);
  const claim = () => fetch(origin + '/api/auth/claim-launch', { method: 'POST', headers, body: JSON.stringify({ ticket }) });
  const response = await claim(); assert.equal(response.status, 200);
  const login = await response.json(); assert.equal(login.owner, true);
  assert.match(response.headers.get('set-cookie'), /httponly/i);
  assert.equal((await claim()).status, 401);
  return async (route, body) => {
    const result = await fetch(origin + '/api' + route, { method: body ? 'POST' : 'GET', headers: { ...headers, Cookie: response.headers.get('set-cookie').split(';')[0], 'X-CSRF': login.csrf }, body: body ? JSON.stringify(body) : undefined });
    assert.equal(result.status, 200, route); return result.json();
  };
}
try {
  const running = await start(settings, 'first-start'); await ready(running);
  checks.push('Published native executable serves exact PWA assets from an unrelated working directory without a shared .NET runtime');
  const api = await session(); await api('/demo/seed', {});
  const before = await api('/export'), state = await api('/state');
  assert.ok(before.pages.length > 0); assert.equal(before.runs.length, 0); assert.equal(state.research.enabled, false); assert.equal(state.phoneOrigin, null);
  await assert.rejects(stat(foreignData), { code: 'ENOENT' });
  if (process.platform !== 'win32') assert.equal((await stat(data)).mode & 0o777, 0o700);
  const keyHash = digest(await readFile(path.join(data, 'host-key.txt')));
  checks.push('One-use local owner login, seeded SQLite/Markdown data, private Unix directory and ignored inherited network/data settings');
  const duplicate = await start(settings, 'duplicate'); assert.notEqual((await boundedExit(duplicate)).code, 0);
  assert.ok(owned.has(running));
  const other = { ...settings, dataDirectory: path.join(evidencePath, 'other-data') };
  const conflicting = await start(other, 'occupied-port'); assert.notEqual((await boundedExit(conflicting)).code, 0);
  await assert.rejects(stat(other.dataDirectory), { code: 'ENOENT' });
  checks.push('Duplicate and occupied-port starts fail without replacing the active host or creating another store');
  await stop(running);
  const restarted = await start(settings, 'restart'); await ready(restarted);
  const afterApi = await session(), after = await afterApi('/export');
  assert.deepEqual(after, before); assert.equal(digest(await readFile(path.join(data, 'host-key.txt'))), keyHash);
  checks.push('Restart preserves every exported row and the exact access key, with no model task or worker start');
  await stop(restarted);
  const foreign = await reserve(), spare = await reserve();
  try {
    const foreignSettings = { ...settings, dataDirectory: path.join(evidencePath, 'foreign-data'), localOrigin: `http://127.0.0.1:${foreign.address().port}`, workerPort: spare.address().port };
    await release(spare);
    const refused = await start(foreignSettings, 'foreign-listener'); assert.notEqual((await boundedExit(refused)).code, 0);
    assert.ok(foreign.listening); await assert.rejects(stat(foreignSettings.dataDirectory), { code: 'ENOENT' });
  } finally { await release(foreign); if (spare.listening) await release(spare); }
  checks.push('An unrelated listening socket remains intact and the refused data directory is absent');
  if (process.platform !== 'win32') {
    const wide = path.join(evidencePath, 'wide-permissions'); await mkdir(wide); await chmod(wide, 0o755);
    const refused = await start({ ...settings, dataDirectory: wide }, 'wide-permissions'); assert.notEqual((await boundedExit(refused)).code, 0);
    assert.equal((await stat(wide)).mode & 0o777, 0o755);
    await assert.rejects(stat(path.join(wide, 'ledger.sqlite')), { code: 'ENOENT' });
    checks.push('Existing broadly readable Unix data is refused without changing its permissions or initializing a database');
  }
  await writeFile(path.join(evidencePath, 'verified.json'), JSON.stringify({ passed: true, runtime: rid, os: os.version(), release: os.release(), sourceHead: manifest.sourceHead,
    sourceDirty: manifest.checkoutDirty, archiveSha256: checksum, checks, liveModelCalls: 0, gpuInference: 0, isolatedWorkerQualified: false,
    browserAutomaticallyOpened: false, termination: process.platform === 'win32' ? 'owned test process terminated' : 'SIGINT graceful host shutdown' }, null, 2) + '\n');
  console.log(`${checks.length} extracted ${rid} package checks passed. The travelling butler kept the ledger intact.`);
} finally { for (const child of [...owned]) await stop(child); }
