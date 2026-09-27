import {spawn} from 'node:child_process';
import {mkdir, writeFile} from 'node:fs/promises';
import {request as httpRequest} from 'node:http';
import {entrance} from './entrance.mjs';

const fixture = process.env.HIREZERO_PLOW_FIXTURE === '1';
const localOrigin = process.env.HIREZERO_LOCAL_ORIGIN;
if (fixture && !localOrigin) throw new Error('Offline fixture mode requires the explicit local development entrance.');
const children = new Set();
let closing = false;
let host;
async function stop(code = 0) {
  if (closing) return; closing = true;
  server.close();
  for (const child of children) child.kill('SIGTERM');
  const deadline = setTimeout(() => { for (const child of children) child.kill('SIGKILL'); }, 10_000);
  await Promise.all([...children].map(child => child.exitCode !== null || child.signalCode !== null ? null : new Promise(resolve => child.once('exit', resolve))));
  clearTimeout(deadline); process.exitCode = code;
}
function launch(command, args, env) {
  const child = spawn(command, args, {stdio: 'inherit', env}); children.add(child);
  child.on('error', () => { console.error('hirezero: a required process could not start; the household is parked.'); void stop(1); });
  child.on('exit', code => { children.delete(child); if (!closing) void stop(code || 1); });
  return child;
}
await mkdir('/var/lib/plow/cockpit', {recursive: true});
for (const room of ['runway-room', 'meeting-room']) {
  await mkdir('/var/lib/plow/' + room, {recursive: true});
  await writeFile('/var/lib/plow/' + room + '/AGENTS.md', 'The host supplies a bounded marketing packet. You have no tools. Return the requested artifact; only the owner can approve it.\n');
}
if (!fixture) launch(process.execPath, ['/opt/plow/boot/main.js'], process.env);
const server = entrance({localOrigin, startHost: async (origin, local, owner) => {
  if (host) return;
  const env = {...process.env,
    Thaddeus__Data: '/var/lib/plow/cockpit', Thaddeus__LocalOrigin: local ? origin : 'http://localhost:5184',
    Thaddeus__PhoneMode: 'plow', Thaddeus__PlowListenOrigin: 'http://127.0.0.1:5184',
    Thaddeus__PlowLocalDevelopment: local ? 'true' : 'false',
    Thaddeus__CredentialVault: 'plow-file', Thaddeus__PlowCredentialDirectory: '/var/lib/plow/credentials',
    Marketing__Transport: 'direct', Marketing__MainSession: 'agent:main:main',
    Marketing__Model: 'plow/z-ai/glm-5.2', Marketing__ShiftRuntime: fixture ? 'scripted' : 'openclaw',
    Marketing__Container: 'plow-colocated-employee', Marketing__ShiftContainer: 'plow-colocated-employee',
    Marketing__SharedContainer: 'plow-separate-shared-gateway',
  };
  if (!local) env.Thaddeus__PhoneOrigin = origin; else delete env.Thaddeus__PhoneOrigin;
  // Fixture runs have no Plow child, reporter or provider. The production image always uses OpenClaw shifts.
  if (fixture) {
    env.Marketing__OpenClawScript = '/opt/hirezero/fixture-openclaw.mjs';
    env.Marketing__GatewayPasswordFile = '/var/lib/plow/fixture-password';
    env.Marketing__ShiftPump = 'off';
    env.Thaddeus__ApiRequestsPerMinute = '3000'; env.Thaddeus__AuthRequestsPerMinute = '120';
    await writeFile(env.Marketing__GatewayPasswordFile, 'fictional-offline-password\n');
  }
  host = launch('dotnet', ['/opt/hirezero/host/Thaddeus.Host.dll', '--contentRoot', '/opt/hirezero/host'], env);
  for (let attempt = 0; attempt < 120; attempt++) {
    if (host.exitCode !== null || host.signalCode !== null) throw new Error('Host exited before readiness');
    try {
      // The physical listener is private; readiness must carry the public Host just like the entrance.
      const status = await new Promise((resolve, reject) => {
        const probe = httpRequest({hostname: '127.0.0.1', port: 5184, path: '/',
          headers: {host: new URL(origin).host, 'x-plow-user': owner}, timeout: 300}, response => {
          response.resume(); resolve(response.statusCode);
        });
        probe.on('timeout', () => probe.destroy(new Error('Readiness timed out')));
        probe.on('error', reject); probe.end();
      });
      if (status === 200) return;
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error('Host readiness timed out');
}});
server.listen(3000, localOrigin ? '0.0.0.0' : '127.0.0.1', () => console.log('hirezero: cockpit entrance ready; approvals remain with the owner.'));
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => void stop());
