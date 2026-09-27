import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {cp, mkdir, readFile, readdir, writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {cleanArtifactPaths, requireArtifactSpace} from './artifact-storage.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const name = process.argv[2] || 'plow-package-' + Date.now();
assert.match(name, /^plow-package-[a-z0-9-]+$/);
const evidence = path.join(repo, 'artifacts', name);
await mkdir(evidence); // A receipt must never silently overwrite an earlier candidate.
const space = await requireArtifactSpace(repo, 4 * 1024 ** 3, 'Plow application package');
const scratch = path.join(evidence, 'scratch');
const context = path.join(scratch, 'context');
await mkdir(context, {recursive: true});
const commands = [];
async function run(command, args, cwd = repo) {
  const log = path.join(evidence, 'command-' + commands.length + '.log');
  let output = '';
  const child = spawn(command, args, {cwd, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe']});
  child.stdout.on('data', data => { output += data; }); child.stderr.on('data', data => { output += data; });
  const exit = await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', resolve); });
  await writeFile(log, output);
  commands.push({command, args, cwd, log: path.basename(log), exit});
  if (exit !== 0) throw new Error(command + ' failed; inspect ' + log);
  return output;
}
async function fingerprint() {
  const files = {};
  async function walk(directory) {
    for (const entry of await readdir(directory, {withFileTypes: true})) {
      if (['bin', 'obj', 'node_modules', 'wwwroot'].includes(entry.name)) continue;
      const file = path.join(directory, entry.name);
      if (entry.isSymbolicLink()) throw new Error('Linked package inputs are refused: ' + file);
      if (entry.isDirectory()) await walk(file);
      else if (!entry.name.endsWith('.tsbuildinfo')) files[path.relative(repo, file).replaceAll('\\', '/')] = createHash('sha256').update(await readFile(file)).digest('hex');
    }
  }
  for (const directory of ['src/Thaddeus.Host', 'src/Thaddeus.Core', 'src/Thaddeus.Infrastructure', 'web/src', 'packaging/plow', 'business/agent']) await walk(path.join(repo, directory));
  for (const file of ['Directory.Build.props', 'web/package-lock.json', 'web/tsconfig.json', 'web/vite.config.ts', 'scripts/build-plow-package.mjs']) files[file] = createHash('sha256').update(await readFile(path.join(repo, file))).digest('hex');
  return files;
}
const before = await fingerprint();
const base = JSON.parse(await readFile(path.join(repo, 'packaging/plow/base.json'), 'utf8'));
let image;
let failure;
try {
  await run('dotnet', ['publish', 'src/Thaddeus.Host/Thaddeus.Host.csproj', '-c', 'Release', '--self-contained', 'false', '-p:UseAppHost=false', '-p:RestoreLockedMode=true', '--artifacts-path', path.join(scratch, 'build'), '-o', path.join(context, 'host')]);
  await run(process.execPath, [path.join(repo, 'web/node_modules/typescript/bin/tsc'), '-b'], path.join(repo, 'web'));
  await run(process.execPath, [path.join(repo, 'web/node_modules/vite/bin/vite.js'), 'build', '--outDir', path.join(context, 'host/wwwroot')], path.join(repo, 'web'));
  for (const [source, destination] of [['packaging/plow', 'package'], ['business/agent/hire/bin', 'hire'], ['business/agent/meter', 'meter'], ['business/agent/prompt', 'prompt'], ['business/agent/skills', 'skills']])
    await cp(path.join(repo, source), path.join(context, destination), {recursive: true, filter: file => !/(?:^|[\\/])(?:node_modules|__pycache__|plow-credentials)(?:[\\/]|$)/.test(file)});
  await cp(path.join(repo, 'business/agent/hire/harken-requirements.lock'), path.join(context, 'package/harken-requirements.lock'));
  for (const file of ['Dockerfile', '.dockerignore']) await cp(path.join(repo, 'packaging/plow', file), path.join(context, file));
  assert.deepEqual(await fingerprint(), before, 'Source changed during packaging. Repeat after concurrent edits settle.');
  await run('docker', ['build', '--platform', 'linux/amd64', '-t', 'hirezero-marketing:' + name, context]);
  image = JSON.parse(await run('docker', ['image', 'inspect', 'hirezero-marketing:' + name, '--format', '{{json .Id}}']));
  assert.deepEqual(await fingerprint(), before, 'Source changed while the image was built. This candidate is not reviewable against the current checkout.');
} catch (error) { failure = error.message; }
finally {
  const removed = await cleanArtifactPaths(evidence, ['scratch']);
  await writeFile(path.join(evidence, 'receipt.json'), JSON.stringify({base, image, sourceHashes: before, space, commands, failure, removed, liveModelCalls: false, registered: false, pushed: false}, null, 2));
}
if (failure) throw new Error(failure);
console.log('Plow candidate built: ' + image + '. No live model, listing or deployment was started.');
