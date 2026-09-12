import { lstat, mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { configuration, runtimeVersion, stateRoot } from './configuration.mjs';

// Host calls this through ISandboxBackend only after qualification. It never starts inference itself.
try {
  process.stdin.setEncoding('utf8');
  let body = '';
  for await (const part of process.stdin) {
    body += part;
    if (Buffer.byteLength(body) > 150000) throw new Error('Worker binding exceeds the input limit.');
  }
  const prepared = configuration(JSON.parse(body));
  const version = execFileSync('openclaw', ['--version'], { encoding: 'utf8', timeout: 30000 });
  if (!version.split(/\s+/).includes(runtimeVersion)) throw new Error('OpenClaw version mismatch.');
  for (const directory of [stateRoot, '/home/agent/thaddeus-artifacts']) {
    await mkdir(directory, { recursive: true, mode: 0o700 });
    const stat = await lstat(directory);
    if (!stat.isDirectory() || stat.isSymbolicLink()) throw new Error('Worker directory is not private storage.');
  }
  // Exclusive creation makes an uncertain bootstrap inspectable; it is never silently replayed.
  await writeFile(`${stateRoot}/thaddeus-binding.json`, JSON.stringify(prepared.binding), { flag: 'wx', mode: 0o600 });
  for (const [name, value] of Object.entries({ '.env': prepared.environment,
    'thaddeus-context.json': JSON.stringify(prepared.context), 'openclaw.json': JSON.stringify(prepared.config) }))
    await writeFile(`${stateRoot}/${name}`, value, { flag: 'wx', mode: 0o600 });
  execFileSync('openclaw', ['config', 'validate', '--json'], { timeout: 30000, stdio: 'pipe' });
  process.stdout.write(JSON.stringify({ ...prepared.binding, status: 'configured', authority: 'worker-reported' }));
} catch {
  // Subprocess diagnostics may expand short-lived credentials. Keep those out of the product log.
  process.stderr.write('Worker configuration was not confirmed; inspect its private state before retrying. The keys remain off the calling card.\n');
  process.exitCode = 1;
}
