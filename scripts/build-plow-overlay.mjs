import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {cp, mkdir, readFile, readdir, writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {requireArtifactSpace, cleanArtifactPaths} from './artifact-storage.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [base, name] = process.argv.slice(2);
assert.match(base || '', /^ghcr\.io\/raydestar\/hirezero-marketing@sha256:[a-f0-9]{64}$/);
assert.match(name || '', /^plow-package-[a-z0-9-]+$/);
const evidence = path.join(repo, 'artifacts', name), scratch = path.join(evidence, 'scratch');
await mkdir(evidence);
const space = await requireArtifactSpace(repo, 4 * 1024 ** 3, 'Small Plow application overlay');
await mkdir(scratch);
const commands = [];
async function run(command, args, cwd = repo) {
  const child = spawn(command, args, {cwd, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe']});
  let output = '';
  child.stdout.on('data', data => output += data); child.stderr.on('data', data => output += data);
  const exit = await new Promise((resolve, reject) => { child.once('error', reject); child.once('close', resolve); });
  const log = 'command-' + commands.length + '.log'; await writeFile(path.join(evidence, log), output);
  commands.push({command, args, exit, log});
  assert.equal(exit, 0, command + ' failed: ' + log); return output.trim();
}
async function hashes() {
  const result = {};
  async function walk(directory) {
    for (const entry of await readdir(directory, {withFileTypes: true})) {
      if (['bin', 'obj', 'wwwroot', 'node_modules'].includes(entry.name) || entry.name.endsWith('.tsbuildinfo')) continue;
      const file = path.join(directory, entry.name);
      assert.equal(entry.isSymbolicLink(), false);
      if (entry.isDirectory()) await walk(file);
      else result[path.relative(repo, file).replaceAll('\\', '/')] = createHash('sha256').update(await readFile(file)).digest('hex');
    }
  }
  for (const directory of ['src/Thaddeus.Host', 'src/Thaddeus.Core', 'src/Thaddeus.Infrastructure', 'web/src', 'web/public', 'packaging/plow', 'business/agent/meter/plow']) await walk(path.join(repo, directory));
  return result;
}
const sourceHashes = await hashes();
let image, failure;
try {
  const context = path.join(scratch, 'context'); await mkdir(context);
  await run('dotnet', ['publish', 'src/Thaddeus.Host/Thaddeus.Host.csproj', '-c', 'Release', '--self-contained', 'false', '-p:UseAppHost=false', '-p:RestoreLockedMode=true', '--artifacts-path', path.join(scratch, 'build'), '-o', path.join(context, 'host')]);
  await run(process.execPath, [path.join(repo, 'web/node_modules/typescript/bin/tsc'), '-b'], path.join(repo, 'web'));
  await run(process.execPath, [path.join(repo, 'web/node_modules/vite/bin/vite.js'), 'build', '--outDir', path.join(context, 'host/wwwroot')], path.join(repo, 'web'));
  for (const file of ['boot.mjs', 'entrance.mjs', 'companion-connector.mjs', 'companion-pairing.mjs']) await cp(path.join(repo, 'packaging/plow', file), path.join(context, file));
  // The meter plugin's runtime files (its tests stay out), with the base image's LF endings.
  await mkdir(path.join(context, 'meter'));
  for (const file of ['index.mjs', 'text-mode.mjs', 'fetch.mjs', 'api-endpoints.mjs', 'completion-receipt.mjs'])
    await writeFile(path.join(context, 'meter', file), (await readFile(path.join(repo, 'business/agent/meter/plow', file), 'utf8')).replace(/\r\n/g, '\n'));
  const revision = await run('git', ['rev-parse', 'HEAD']);
  await writeFile(path.join(context, 'Dockerfile'), `FROM ${base}\nENV AGENT_ID=hirezero-marketing\nCOPY --chown=node:node host/ /opt/hirezero/host/\nCOPY --chown=node:node boot.mjs entrance.mjs companion-connector.mjs companion-pairing.mjs /opt/hirezero/\nCOPY --chmod=755 meter/ /app/marketing-meter/plow/\nLABEL org.opencontainers.image.revision="${revision}" org.opencontainers.image.version="${name}"\n`);
  assert.deepEqual(await hashes(), sourceHashes, 'Inputs changed during publication.');
  await run('docker', ['build', '--platform', 'linux/amd64', '-t', 'hirezero-marketing:' + name, context]);
  image = await run('docker', ['image', 'inspect', 'hirezero-marketing:' + name, '--format', '{{.Id}}']);
  assert.deepEqual(await hashes(), sourceHashes, 'Inputs changed during image build.');
} catch (error) { failure = error.message; }
finally {
  const removed = await cleanArtifactPaths(evidence, ['scratch']);
  await writeFile(path.join(evidence, 'receipt.json'), JSON.stringify({base, image, space, sourceHashes, commands, failure, removed, liveModelCalls: false}, null, 2));
}
if (failure) throw new Error(failure);
console.log('Plow overlay built: ' + image + '. Same household, a smaller moving van.');
