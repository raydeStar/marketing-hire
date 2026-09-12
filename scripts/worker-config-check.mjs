import { spawnSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';

// Explicit CPU-only package check. No host mounts, GPU, network, provider credentials or production worker admission.
const image = process.argv[2] ?? 'thaddeus-openclaw:2026.9.4-context-dev';
if (!/^thaddeus-openclaw:[a-zA-Z0-9_.-]+$/.test(image)) throw new Error('Use a locally built Thaddeus worker tag.');
const program = String.raw`
import { spawnSync } from 'node:child_process';
import { readFileSync, statSync } from 'node:fs';
import { sha256 } from '/opt/thaddeus/configuration.mjs';
const text = 'Fictional test context. The butler keeps his test data in a separate drawer.';
const input = { schemaVersion: 1, runId: 'a'.repeat(32), brokerOrigin: 'http://host.docker.internal:5182',
  model: 'gpt-5.6-luna', reasoning: 'high', grantToken: 'b'.repeat(64),
  context: { schemaVersion: 1, text, contentHash: sha256(text), profileDigest: 'c'.repeat(64) } };
const checks = [];
function command(label, executable, args, body) {
  const result = spawnSync(executable, args, { input: body, encoding: 'utf8', timeout: 45000, maxBuffer: 2000000 });
  checks.push({ label, exitCode: result.status, stdout: result.stdout, stderr: result.stderr });
  if (result.status !== 0) throw new Error(label + ' failed');
  return result.stdout;
}
try {
  command('bootstrap', 'node', ['/opt/thaddeus/bootstrap.mjs'], JSON.stringify(input));
  const schema = JSON.parse(command('native-schema', 'openclaw', ['config', 'validate', '--json']));
  if (schema.valid !== true || schema.warnings?.length) throw new Error('Native schema is invalid or has warnings.');
  const inspection = JSON.parse(command('native-plugin', 'openclaw', ['plugins', 'inspect', 'thaddeus-context', '--runtime', '--json']));
  const names = inspection.typedHooks?.map(hook => hook.name).sort();
  if (inspection.plugin?.status !== 'loaded' || inspection.plugin?.activated !== true ||
      JSON.stringify(names) !== JSON.stringify(['before_agent_run', 'before_prompt_build']) ||
      inspection.policy?.allowPromptInjection !== true || inspection.policy?.allowConversationAccess !== true ||
      inspection.diagnostics?.length) throw new Error('The native runtime did not register the expected hooks and permissions.');
  const repeated = spawnSync('node', ['/opt/thaddeus/bootstrap.mjs'], { input: JSON.stringify(input), encoding: 'utf8', timeout: 45000 });
  if (repeated.status !== 1) throw new Error('An existing binding must not be overwritten.');
  for (const name of ['.env', 'openclaw.json', 'thaddeus-binding.json', 'thaddeus-context.json'])
    if ((statSync('/home/agent/.openclaw/' + name).mode & 0o077) !== 0) throw new Error('Task files are not private.');
  const binding = JSON.parse(readFileSync('/home/agent/.openclaw/thaddeus-binding.json', 'utf8'));
  if (binding.contentHash !== input.context.contentHash) throw new Error('Context binding changed.');
  process.stdout.write(JSON.stringify({ schemaVersion: 1, kind: 'native-package-compatibility', inferenceCalls: 0, checks }));
} catch (error) {
  process.stdout.write(JSON.stringify({ schemaVersion: 1, error: error.message, checks })); process.exitCode = 1;
}
`;
const identity = spawnSync('docker', ['image', 'inspect', image, '--format', '{{.Id}}'], { encoding: 'utf8', timeout: 10000 });
const imageId = identity.stdout?.trim();
if (identity.status !== 0 || !/^sha256:[a-f0-9]{64}$/.test(imageId)) throw new Error('Build the local worker image first.');
const containerName = 'thaddeus-config-check-' + randomUUID();
let result;
try {
  result = spawnSync('docker', ['run', '--rm', '-i', '--name', containerName, '--network', 'none', '--cpus', '2', '--memory', '4g',
    '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--entrypoint', 'node', imageId, '--input-type=module'],
    { input: program, encoding: 'utf8', timeout: 180000, maxBuffer: 2000000 });
} finally {
  // A timed-out Docker client need not stop its container. Clean up only this random, task-owned name.
  const remaining = spawnSync('docker', ['container', 'inspect', containerName], { timeout: 10000, stdio: 'ignore' });
  if (remaining.status === 0)
    spawnSync('docker', ['container', 'rm', '--force', containerName], { timeout: 10000, stdio: 'ignore' });
}
const directory = new URL('../artifacts/worker-config/', import.meta.url);
await mkdir(directory, { recursive: true });
const receipt = new URL(`check-${Date.now()}.json`, directory);
let report;
try { report = JSON.parse(result.stdout); } catch { report = { stdout: result.stdout, stderr: result.stderr }; }
await writeFile(receipt, JSON.stringify({ image, imageId, exitCode: result.status, report }, null, 2));
console.log(JSON.stringify({ passed: result.status === 0, receipt: fileURLToPath(receipt),
  note: 'Native package compatibility only; the VM boundary still awaits its own examination.' }));
if (result.status !== 0) { console.error(JSON.stringify(report)); process.exitCode = 1; }
