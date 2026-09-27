import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {mkdir, readFile, readdir, writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {requireArtifactSpace} from './artifact-storage.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [image, name, mode] = process.argv.slice(2);
assert.ok(mode === undefined || mode === '--packaged');
const packaged = mode === '--packaged';
assert.match(image || '', /^hirezero-marketing:plow-package-[a-z0-9-]+$/);
assert.match(name || '', /^plow-meter-check-[a-z0-9-]+$/);
const evidence = path.join(repo, 'artifacts', name);
await mkdir(evidence);
const space = await requireArtifactSpace(repo, 512 * 1024 ** 2, 'Offline Plow meter check');
const commands = [], removed = [];
async function run(args, timeout = 15000) {
  const child = spawn('docker', args, {cwd: repo, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe']});
  let output = '', timedOut = false;
  child.stdout.on('data', data => {output += data;}); child.stderr.on('data', data => {output += data;});
  const timer = setTimeout(() => {timedOut = true; child.kill();}, timeout);
  let exit;
  try {exit = await new Promise((resolve, reject) => {child.on('error', reject); child.on('close', resolve);});}
  finally {clearTimeout(timer);}
  const log = `command-${commands.length}.log`;
  await writeFile(path.join(evidence, log), output); commands.push({args, exit, timedOut, log});
  return {exit, output, timedOut};
}
function requireSuccess(result) {
  assert.equal(result.exit, 0, result.output); assert.equal(result.timedOut, false, 'Offline fixture timed out');
  return result.output;
}
async function hashes() {
  const files = ['packaging/plow/configure.mjs', 'scripts/check-plow-meter.mjs'];
  async function visit(directory) {
    for (const entry of await readdir(path.join(repo, directory), {withFileTypes: true})) {
      assert.equal(entry.isSymbolicLink(), false, 'Linked fixture sources are refused');
      const item = directory + '/' + entry.name;
      if (entry.isDirectory()) await visit(item); else files.push(item);
    }
  }
  await visit('business/agent/meter');
  return Object.fromEntries(await Promise.all(files.sort().map(async file => [file,
    createHash('sha256').update(await readFile(path.join(repo, file))).digest('hex')])));
}
const before = await hashes();
let imageId, failure, owned = false;
try {
  imageId = JSON.parse(requireSuccess(await run(['image', 'inspect', image, '--format', '{{json .Id}}'])));
  const mount = (source, target) => ['--mount', `type=bind,source=${path.join(repo, source)},target=${target},readonly`];
  requireSuccess(await run(['create', '--name', name, '--label', 'hirezero.plow.fixture=' + name,
    '--network', 'none', '--entrypoint', 'node',
    ...(packaged ? [] : mount('business/agent/meter', '/app/marketing-meter')),
    ...(packaged ? [] : mount('packaging/plow/configure.mjs', '/opt/hirezero/configure.mjs')),
    ...mount('business/agent/meter/plow/fixture-ledger.py', '/opt/hire/bin/runway.py'), imageId,
    '--test', '/app/marketing-meter/plow/gateway.test.mjs', '/app/marketing-meter/plow/installed-transport.test.mjs']));
  owned = true;
  console.log('Checking the real Gateway with fictional replies and no external network; the purse stays closed.');
  requireSuccess(await run(['start', '--attach', name], 135000));
  const state = JSON.parse(requireSuccess(await run(['inspect', name, '--format', '{{json .State}}'])));
  assert.equal(state.Running, false); assert.equal(state.ExitCode, 0);
  assert.deepEqual(await hashes(), before, 'Fixture sources changed during verification');
} catch (error) {failure = error.message;}
finally {
  if (owned) try {
    const container = JSON.parse(requireSuccess(await run(['inspect', name])))[0];
    assert.equal(container.Config.Labels['hirezero.plow.fixture'], name);
    if (container.State.Running) requireSuccess(await run(['stop', '--time', '3', name]));
    requireSuccess(await run(['rm', name])); removed.push(name);
  } catch (error) {failure = [failure, 'Cleanup failed: ' + error.message].filter(Boolean).join('; ');}
  await writeFile(path.join(evidence, 'receipt.json'), JSON.stringify({image, imageId, packaged, sources: before,
    space, commands, failure, removed, network: 'none', fictionalLedger: true, syntheticResponses: true,
    liveModelCalls: false}, null, 2));
}
if (failure) throw Error(failure);
console.log('Gateway admission, one-send accounting and replay verified; the disposable household is swept away.');
