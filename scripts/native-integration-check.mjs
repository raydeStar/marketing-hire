import { spawn, spawnSync } from 'node:child_process';
import { createInterface } from 'node:readline';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { resolve } from 'node:path';

const mode = process.argv[2] ?? 'scripted';
if (!['scripted', 'luna'].includes(mode)) throw new Error('Choose scripted or explicitly requested luna.');
const root = resolve('artifacts', `native-integration-${mode}-${Date.now()}`);
const name = 'thaddeus-' + randomUUID().replaceAll('-', '');
const image = 'thaddeus-openclaw:2026.9.4-context-dev';
const inspected = spawnSync('docker', ['image', 'inspect', image, '--format', '{{.Id}}'], { encoding: 'utf8', timeout: 10000 });
const imageId = inspected.stdout?.trim();
if (inspected.status !== 0 || !/^sha256:[a-f0-9]{64}$/.test(imageId)) throw new Error('Build the pinned worker image first.');
let controller, worker, fixture; const receipts = []; const requests = [];
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function exec(label, executable, args, input, timeout = 55000) {
  const child = spawn(executable, args, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  let output = '', error = ''; const timer = setTimeout(() => child.kill(), timeout);
  child.stdout.on('data', data => { output += data; if (output.length > 2000000) child.kill(); });
  child.stderr.on('data', data => { error += data; if (error.length > 2000000) child.kill(); });
  child.stdin.on('error', () => {}); child.stdin.end(input);
  const code = await new Promise((resolve, reject) => { child.on('error', reject); child.on('close', resolve); });
  clearTimeout(timer); receipts.push({ label, code, output, error });
  if (code !== 0) throw new Error(`${label} was not confirmed. Inspect the private receipt.`);
  return output;
}
async function control(path, method = 'POST') {
  const response = await fetch('http://127.0.0.1:' + controller.port + '/fixture/' + path,
    { method, headers: { Authorization: 'Bearer ' + controller.controlToken }, signal: AbortSignal.timeout(70000) });
  const result = await response.json();
  if (!response.ok) throw new Error(path + ': ' + result.error);
  return result;
}
async function waitState(expected) {
  const deadline = Date.now() + (mode === 'luna' ? 240000 : 60000);
  while (Date.now() < deadline) {
    const state = await control('state', 'GET');
    if (state.run.state === expected) return state;
    if (['failed', 'needsAttention', 'cancelled'].includes(state.run.state)) throw new Error('Native task stopped: ' + state.run.summary);
    await sleep(300);
  }
  throw new Error('Native task did not reach ' + expected + '.');
}

// A bounded test-only HTTP relay over Docker stdio lets --network none stay in force.
// It can reach only this fixture's two broker routes, never product APIs, arbitrary hosts or provider keys.
const relay = String.raw`
import http from 'node:http'; import { createInterface } from 'node:readline';
let sequence = 0; const pending = new Map();
createInterface({ input: process.stdin }).on('line', line => {
  const message = JSON.parse(line); const response = pending.get(message.id); if (!response) return;
  pending.delete(message.id); response.writeHead(message.status, message.headers); response.end(Buffer.from(message.body, 'base64'));
});
const server = http.createServer(async (request, response) => {
  const chunks = []; let length = 0;
  for await (const chunk of request) { length += chunk.length; if (length > 150000) { response.writeHead(413); response.end(); return; } chunks.push(chunk); }
  const id = ++sequence; pending.set(id, response);
  response.on('close', () => pending.delete(id));
  response.setTimeout(300000, () => { response.writeHead(504); response.end(); });
  process.stdout.write(JSON.stringify({ id, method: request.method, path: request.url, headers: request.headers, body: Buffer.concat(chunks).toString('base64') }) + '\n');
});
server.listen(5182, '127.0.0.1', () => process.stdout.write(JSON.stringify({ ready: true }) + '\n'));
`;
let failure;
try {
  fixture = spawn('dotnet', ['tools/Thaddeus.NativeCheck/bin/Debug/net10.0/Thaddeus.NativeCheck.dll', root, mode, name], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let fixtureLogs = ''; fixture.stdout.on('data', d => { fixtureLogs += d; }); fixture.stderr.on('data', d => { fixtureLogs += d; });
  for (let i = 0; i < 100; i++) {
    if (fixture.exitCode !== null) throw new Error('Fixture host stopped: ' + fixtureLogs);
    try { controller = JSON.parse(await readFile(resolve(root, 'controller.json'), 'utf8')); break; } catch { await sleep(100); }
  }
  if (!controller) throw new Error('Fixture host did not start.');
  worker = spawn('docker', ['run', '--rm', '-i', '--name', name, '--network', 'none', '--add-host', 'host.docker.internal:127.0.0.1',
    '--cpus', '2', '--memory', '4g', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
    '--entrypoint', 'node', imageId, '--input-type=module', '-e', relay], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  worker.stdin.on('error', () => {});
  let ready = false, workerLogs = '';
  worker.stderr.on('data', d => { workerLogs += d; });
  createInterface({ input: worker.stdout }).on('line', async line => {
    let message;
    try {
      message = JSON.parse(line); if (message.ready) { ready = true; return; }
      const allowed = ['/worker/' + controller.binding.runId + '/mcp', '/worker/' + controller.binding.runId + '/v1/chat/completions'];
      if (!allowed.includes(message.path) || !['GET', 'POST', 'DELETE'].includes(message.method)) throw new Error('Relay route denied');
      const headers = Object.fromEntries(Object.entries(message.headers).filter(([key]) => !['connection', 'content-length', 'transfer-encoding', 'expect'].includes(key)));
      const response = await fetch('http://127.0.0.1:' + controller.port + message.path, {
        method: message.method, headers, body: message.method === 'GET' ? undefined : Buffer.from(message.body, 'base64'),
        redirect: 'error', signal: AbortSignal.timeout(300000) });
      const body = Buffer.from(await response.arrayBuffer());
      if (body.length > 1500000) throw new Error('Relay response is too large');
      requests.push({ path: message.path, method: message.method, status: response.status, bytes: body.length,
        error: response.status >= 400 ? body.toString('utf8').slice(0,2000) : undefined });
      worker.stdin.write(JSON.stringify({ id: message.id, status: response.status,
        headers: { 'content-type': response.headers.get('content-type') ?? 'application/octet-stream' }, body: body.toString('base64') }) + '\n');
    } catch (error) {
      requests.push({ path: message?.path, error: error.message });
      if (message?.id) worker.stdin.write(JSON.stringify({ id: message.id, status: 502, headers: {}, body: '' }) + '\n');
    }
  });
  for (let i = 0; !ready && i < 100; i++) {
    if (worker.exitCode !== null) throw new Error('Worker relay stopped: ' + workerLogs);
    await sleep(100);
  }
  if (!ready) throw new Error('Worker relay did not start.');
  await exec('bootstrap', 'docker', ['exec', '-i', name, 'node', '/opt/thaddeus/bootstrap.mjs'], JSON.stringify(controller.binding));
  await exec('gateway-start', 'docker', ['exec', '-d', name, 'openclaw', 'gateway', 'run']);
  let gatewayReady = false;
  for (let i = 0; i < 8; i++) {
    try { await exec('gateway-health', 'docker', ['exec', name, 'openclaw', 'gateway', 'health', '--json', '--timeout', '2000'], null, 10000); gatewayReady = true; break; }
    catch { await sleep(500); }
  }
  if (!gatewayReady) throw new Error('Native Gateway did not become healthy.');
  receipts.push({ label: 'start', observation: await control('start') });
  const question = await waitState('awaitingInput');
  receipts.push({ label: 'durable-question', question: question.run.question, dispatches: question.run.modelDispatches });
  receipts.push({ label: 'abort-at-question', observation: await control('abort') });
  // Prove a native Gateway process boundary, not merely a successful restart command.
  const stopped = JSON.parse(await exec('gateway-restart-stop', 'docker', ['exec', name, 'python3', '-c', String.raw`
import os, signal, time, json
matches=[]
for p in os.listdir('/proc'):
    if not p.isdigit(): continue
    try:
        if open('/proc/'+p+'/comm').read().strip()=='openclaw-gatewa': matches.append(int(p))
    except FileNotFoundError: pass
assert len(matches)==1, 'Expected exactly one owned Gateway'
pid=matches[0]; os.kill(pid,signal.SIGTERM)
for _ in range(100):
    try:
        if open('/proc/'+str(pid)+'/stat').read().split()[2]=='Z': break
    except FileNotFoundError: break
    time.sleep(.1)
else: raise RuntimeError('Gateway did not stop')
print(json.dumps({'previousPid':pid,'stopped':True}))
`]));
  await exec('gateway-restart-start', 'docker', ['exec', '-d', name, 'openclaw', 'gateway', 'run']);
  gatewayReady = false;
  for (let i = 0; i < 8; i++) {
    try { await exec('gateway-restarted-health', 'docker', ['exec', name, 'openclaw', 'gateway', 'health', '--json', '--timeout', '2000'], null, 10000); gatewayReady = true; break; }
    catch { await sleep(500); }
  }
  if (!gatewayReady) throw new Error('Restarted Gateway did not become healthy.');
  const restarted = JSON.parse(await exec('gateway-restarted-identity', 'docker', ['exec', name, 'python3', '-c', String.raw`
import os, json
matches=[]
for p in os.listdir('/proc'):
    if not p.isdigit(): continue
    try:
        if open('/proc/'+p+'/comm').read().strip()=='openclaw-gatewa' and open('/proc/'+p+'/stat').read().split()[2]!='Z': matches.append(int(p))
    except FileNotFoundError: pass
assert len(matches)==1
print(json.dumps({'pid':matches[0]}))
`]));
  if (stopped.stopped !== true || restarted.pid === stopped.previousPid) throw new Error('A new Gateway process was not observed.');
  receipts.push({ label: 'resume', observation: await control('resume') });
  const proposal = await waitState('awaitingApproval');
  receipts.push({ label: 'native-proposal', approval: proposal.run.approval, dispatches: proposal.run.modelDispatches });
  receipts.push({ label: 'abort-at-proposal', observation: await control('abort') });
  receipts.push({ label: 'exact-import', run: await control('approve') });
  const final = await control('state', 'GET');
  if (final.run.state !== 'succeeded' || final.run.modelDispatches.length < 2 || final.run.modelDispatches.some(d => d.contextObserved !== true) ||
      final.run.capabilities.filter(c => c.name === 'thaddeus_ask_user' && !c.isError).length !== 1 ||
      !final.run.capabilities.some(c => c.name === 'thaddeus_read_note' && !c.isError) ||
      !final.run.capabilities.some(c => c.name === 'thaddeus_propose_import' && !c.isError) ||
      final.run.question.answer !== 'Developers' || final.run.validation?.passed !== true)
    throw new Error('Final broker receipts do not prove the required native path.');
  await writeFile(resolve(root, 'verified-state.json'), JSON.stringify(final, null, 2));
} catch (error) { failure = error.message; }
finally {
  if (worker) {
    try { await exec('native-gateway-log', 'docker', ['exec', name, 'python3', '-c',
      "import pathlib; print('\\n'.join(p.read_text()[-30000:] for p in pathlib.Path('/tmp/openclaw').glob('*.log')))"]); } catch {}
    try { await exec('native-private-diagnostics', 'docker', ['exec', name, 'python3', '-c',
      "import pathlib; p=pathlib.Path('/home/agent/.openclaw/thaddeus-rpc-last-error.json'); print(p.read_text() if p.exists() else '{}')"]); } catch {}
  }
  if (controller) { try { await control('shutdown'); } catch {} }
  if (fixture && fixture.exitCode === null) { await sleep(500); if (fixture.exitCode === null) fixture.kill(); }
  if (worker) spawnSync('docker', ['rm', '--force', name], { timeout: 15000, stdio: 'ignore' });
  await mkdir(root, { recursive: true });
  await writeFile(resolve(root, 'integration-receipt.json'), JSON.stringify({ schemaVersion: 1, mode, image, imageId, container: name,
    passed: !failure, failure, isolationQualified: false, brokerTransport: 'test-only-stdio-relay', receipts, requests }, null, 2));
}
console.log(JSON.stringify({ passed: !failure, mode, evidence: root, failure,
  message: failure ? 'The evidence found an unfinished connection; the butler will inspect it.' : 'The native loop crossed the brokers and left receipts at every door.' }));
if (failure) process.exitCode = 1;
