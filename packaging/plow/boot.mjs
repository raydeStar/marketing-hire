import {spawn} from 'node:child_process';
import {mkdir, writeFile} from 'node:fs/promises';
import {request as httpRequest} from 'node:http';
import {entrance} from './entrance.mjs';
import {randomBytes} from 'node:crypto';
import {companionConfig, runCompanion} from './companion-connector.mjs';
import {companionPairing} from './companion-pairing.mjs';

const fixture = process.env.HIREZERO_PLOW_FIXTURE === '1';
const localOrigin = process.env.HIREZERO_LOCAL_ORIGIN;
if (fixture && !localOrigin) throw new Error('Offline fixture mode requires the explicit local development entrance.');
const children = new Set();
let closing = false;
let host;
let starting;
let identity;
const pairing = companionPairing({file: '/var/lib/plow/companion/connection.json', identity: () => identity});
const lifetime = new AbortController();
const ingressSecret = randomBytes(48).toString('base64url');
let companionController;
async function stop(code = 0) {
  if (closing) return; closing = true;
  lifetime.abort(); companionController?.abort();
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
if (!fixture) {
  // Mirror Plow's hosted placeholder in the parent too, so cockpit CLI checks inherit the same route.
  // Local installs keep their real credential; the hosted platform supplies authentication at its proxy.
  process.env.PLOW_AGENT_TOKEN ||= 'proxied';
  launch(process.execPath, ['/opt/plow/boot/main.js'], process.env);
}
async function plowIdentity() {
  const base = (process.env.PLOW_API_BASE || 'https://api.plow.co').replace(/\/$/, '');
  const get = async path => {
    const response = await fetch(base + path, {headers: {Authorization: 'Bearer ' + process.env.PLOW_AGENT_TOKEN},
      redirect: 'error', signal: AbortSignal.any([lifetime.signal, AbortSignal.timeout(15000)])});
    if (!response.ok) throw new Error('Plow identity is temporarily unavailable.');
    return response.json();
  };
  const [self, owner] = await Promise.all([get('/v1/agents/me'), get('/v1/auth/owner-uid')]);
  if (!/^[a-f0-9]{32}$/.test(self.agent?.uid) || !/^[A-Za-z0-9_-]{3,128}$/.test(owner.owner_uid))
    throw new Error('Plow identity is incomplete.');
  return {workspace: self.agent.uid, ownerUid: owner.owner_uid, settings: self.agent.settings || {}};
}
async function startHost(origin, local, owner) {
  if (starting) return starting;
  starting = startHostOnce(origin, local, owner).catch(error => { if (!host) starting = undefined; throw error; });
  return starting;
}
async function startHostOnce(origin, local, owner) {
  if (!fixture && !identity) identity = await plowIdentity();
  const env = {...process.env,
    Thaddeus__Data: '/var/lib/plow/cockpit', Thaddeus__LocalOrigin: local ? origin : 'http://localhost:5184',
    Thaddeus__PhoneMode: 'plow', Thaddeus__PlowListenOrigin: 'http://127.0.0.1:5184',
    Thaddeus__PlowLocalDevelopment: local ? 'true' : 'false',
    Thaddeus__CredentialVault: 'plow-file', Thaddeus__PlowCredentialDirectory: '/var/lib/plow/credentials',
    Marketing__Transport: 'direct', Marketing__MainSession: 'agent:main:main',
    Marketing__Model: 'plow/z-ai/glm-5.2', Marketing__ShiftRuntime: fixture ? 'scripted' : 'openclaw',
    Marketing__WorkerThinking: 'off',
    Marketing__Container: 'plow-colocated-employee', Marketing__ShiftContainer: 'plow-colocated-employee',
    Marketing__SharedContainer: 'plow-separate-shared-gateway',
  };
  if (identity) Object.assign(env, {Thaddeus__CompanionOrigin: `https://${identity.workspace}.work.hirezero.app`,
    Thaddeus__CompanionWorkspace: identity.workspace, Thaddeus__CompanionOwner: identity.ownerUid, Thaddeus__CompanionSecret: ingressSecret});
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
}
const server = entrance({localOrigin, startHost, pairing: pairing.handle});
server.listen(3000, localOrigin ? '0.0.0.0' : '127.0.0.1', () => console.log('hirezero: cockpit entrance ready; approvals remain with the owner.'));
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => void stop());

if (!fixture) void (async () => {
  let credential;
  while (!closing) {
    try {
      const current = await plowIdentity();
      if (identity && (current.workspace !== identity.workspace || current.ownerUid !== identity.ownerUid))
        throw new Error('The workspace identity changed.');
      identity = current;
      // Many owners only ever text: the cockpit starts anyway, so what they ask for is worked on and they hear back.
      if (!starting) void startHost(localOrigin ?? `https://${identity.workspace}.plow.run`, Boolean(localOrigin), localOrigin ? 'plow-local-owner' : identity.ownerUid)
        .catch(() => console.error('hirezero: the cockpit could not start yet; it tries again with the next check.'));
      const configured = await pairing.read(current);
      if (configured?.version === 1 && configured.credential !== credential) {
        // Settings can deliver a key, never redirect one to another service.
        if (configured.brokerOrigin !== 'https://hirezero.app' || configured.workspaceOrigin !== `https://${identity.workspace}.work.hirezero.app` ||
            configured.workspace !== identity.workspace || configured.ownerUid !== identity.ownerUid) throw new Error('The companion settings do not match this workspace.');
        const config = companionConfig({...configured, ingressSecret});
        await startHost(`https://${identity.workspace}.plow.run`, false, identity.ownerUid);
        companionController?.abort(); companionController = new AbortController(); credential = configured.credential;
        let lastStatus;
        void runCompanion(config, {signal: companionController.signal, onStatus: status => {
          if (status !== lastStatus) console.log(`hirezero: shared entrance ${status}; one household, individual keys.`);
          lastStatus = status;
        }});
      } else if (!configured && credential) { companionController?.abort(); credential = undefined; }
    } catch { console.error('hirezero: shared entrance is waiting for its verified connection; the keys stay put.'); }
    await new Promise(resolve => {
      const finish = () => { clearTimeout(timer); lifetime.signal.removeEventListener('abort', finish); resolve(); };
      const timer = setTimeout(finish, 15000); lifetime.signal.addEventListener('abort', finish, {once: true});
    });
  }
})();
