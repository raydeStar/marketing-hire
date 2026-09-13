import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { once } from 'node:events';
import { createInterface } from 'node:readline';

if (process.argv.length < 6 || process.argv.length > 7 || process.argv[6] && process.argv[6] !== '--host-crash')
  throw new Error('Usage: qemu-managed-integration-check.mjs QEMU_INPUTS WORKER_DISK_DIRECTORY scripted|scripted-web|luna RUNTIME_REFERENCE_JSON [--host-crash]');
const inputs = resolve(process.argv[2]);
const disk = resolve(process.argv[3]);
const mode = process.argv[4] ?? 'scripted';
if (!['scripted', 'scripted-web', 'luna'].includes(mode)) throw new Error('Choose scripted, scripted-web, or explicitly authorized luna.');
const crash = process.argv[6] === '--host-crash';
const runtimePackage = JSON.parse(await readFile(resolve(process.argv[5]), 'utf8'));
const root = resolve('artifacts', 'qemu-managed-' + mode + (crash ? '-crash' : '') + '-' + Date.now());
await mkdir(root, { recursive: false });
const dataRoot = resolve(root, 'data');
const name = 'thaddeus-' + randomUUID().replaceAll('-', '');
const receipts = []; let vm, fixture, witness, controller, failure, fixtureLogs = '';
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const lock = JSON.parse(await readFile('workers/qemu/feasibility-lock.json', 'utf8'));
const build = JSON.parse(await readFile(resolve(disk, 'build-receipt.json'), 'utf8'));
if (!build.passed || build.image !== 'sha256:d3fef0da199e9b1006d150950668bcb8580f9b7bd386152eb8674b66837cd2e8')
  throw new Error('The canonical worker image provenance is missing.');
for (const file of ['init', 'init.py', 'supervisor.mjs'])
  if (createHash('sha256').update(await readFile('workers/qemu/guest/' + file)).digest('hex') !== build.guestSources[file])
    throw new Error('Guest source changed; rebuild the worker disk before testing.');
function pin(path) {
  const entry = lock.files.find(file => file.path === path);
  if (entry?.algorithm !== 'sha256') throw new Error('Missing exact input pin.');
  return { path: path.startsWith('qemu/') ? resolve(runtimePackage.root, path.slice(5)) : resolve(inputs, path), sha256: entry.digest };
}
const transportPath = resolve(root, 'managed-installation.json');
await writeFile(transportPath, JSON.stringify({ kind: 'qemu', installation: {
  executable: pin('qemu/qemu-system-x86_64.exe'), imageTool: pin('qemu/qemu-img.exe'),
  kernel: pin('alpine/boot/vmlinuz-virt'), initrd: pin('alpine/boot/initramfs-virt'),
  baseDisk: { path: resolve(disk, 'root.ext4'), sha256: build.diskSha256 }, runtimePackage
} }), { flag: 'wx', mode: 0o600 });
async function managedVm(start) {
  const observation = await control(start ? 'vm-start' : 'vm-state', start ? 'POST' : 'GET');
  receipts.push({ label: 'managed-vm-observation', observation });
  const resources = observation.hostResources;
  if (!resources || resources.limitFlags !== 8712 || resources.committedMemoryLimitBytes !== 5120 * 1024 * 1024 ||
      resources.activeProcessLimit !== 1 || resources.cpuControlFlags !== 5 || resources.cpuRate < 1 || resources.cpuRate > 10000)
    throw new Error('Native VM host resource boundaries were not independently queried.');
  return { receipt: { pid: observation.processId },
    execute: (command, input) => control('vm-execute', 'POST', { command, input }),
    stop: async () => {
      const stopped = await control('vm-stop'); receipts.push({ label: 'managed-vm-shutdown', ...stopped });
      if (!stopped.termination.guestShutdown || !stopped.termination.outcome.succeeded || stopped.termination.processId !== observation.processId)
        throw new Error('Owned VM shutdown lacks independent confirmation.');
      const finalResources = stopped.termination.hostResourcesBeforeStop;
      if (!finalResources || ['limitFlags', 'committedMemoryLimitBytes', 'activeProcessLimit', 'cpuControlFlags', 'cpuRate'].some(key => finalResources[key] !== resources[key]))
        throw new Error('Host resource boundaries changed before VM shutdown.');
      return stopped;
    } };
}
async function execute(label, command, input = null) {
  const result = await vm.execute(command, input); receipts.push({ label, ...result });
  if (result.exitCode !== 0) throw new Error(label + ' was not confirmed.'); return result.output;
}
async function control(path, method = 'POST', body) {
  const response = await fetch('http://127.0.0.1:' + controller.port + '/fixture/' + path, {
    method, headers: { Authorization: 'Bearer ' + controller.controlToken, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(90000) });
  const result = await response.json(); if (!response.ok) throw new Error(path + ': ' + result.error); return result;
}
async function state(expected) {
  const end = Date.now() + (mode === 'luna' ? 300000 : 90000);
  while (Date.now() < end) {
    const current = await control('state', 'GET');
    if (current.run.state === expected) return current;
    if (['failed', 'needsAttention', 'cancelled'].includes(current.run.state)) throw new Error('Native run stopped: ' + current.run.summary);
    await sleep(300);
  }
  throw new Error('Native run did not reach ' + expected);
}
async function startGateway() {
  await execute('gateway-start', ['python3', '-c',
    "import subprocess,json; f=open('/home/agent/.openclaw/gateway-console.log','ab',buffering=0); p=subprocess.Popen(['openclaw','gateway','run'],stdin=subprocess.DEVNULL,stdout=f,stderr=subprocess.STDOUT,start_new_session=True); print(json.dumps({'pid':p.pid}))"]);
  for (let i = 0; i < 8; i++) {
    try { await execute('gateway-health', ['openclaw', 'gateway', 'health', '--json', '--timeout', '2000']); return; }
    catch { await sleep(500); }
  }
  throw new Error('Guest Gateway did not become healthy.');
}
async function startFixture(recover = false) {
  fixture = spawn('dotnet', ['tools/Thaddeus.NativeCheck/bin/Debug/net10.0/Thaddeus.NativeCheck.dll', dataRoot, mode, name, transportPath, ...(recover ? ['recover'] : [])],
    { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  fixture.stdout.on('data', data => { fixtureLogs += data; }); fixture.stderr.on('data', data => { fixtureLogs += data; });
  for (let i = 0; i < 800; i++) {
    if (fixture.exitCode !== null) throw new Error('Native fixture stopped before startup.');
    try {
      const candidate = JSON.parse(await readFile(resolve(dataRoot, 'controller.json'), 'utf8'));
      if (candidate.hostProcessId === fixture.pid) { controller = candidate; return; }
    } catch {}
    await sleep(100);
  }
  throw new Error('Native fixture did not start.');
}
async function crashHost(question) {
  const old = controller;
  const refusal = await control('vm-reject-live-recovery');
  if (refusal.rejected !== true) throw new Error('Another backend recovered the live owner.');
  receipts.push({ label: 'live-owner-recovery-denied', ...refusal });
  const marker = 'interrupted-effect-' + randomUUID();
  // Deliberately interrupt after a durable private write but before its RPC completes.
  // This is a synthetic effect, not a model task or external mutation.
  const interrupted = vm.execute(['python3', '-c',
    "import os,time,sys; f=open('/home/agent/thaddeus-artifacts/crash-marker.txt','a'); f.write(sys.argv[1]+'\\n'); f.flush(); os.fsync(f.fileno()); f.close(); time.sleep(45)", marker], null)
    .then(result => ({ acknowledged: true, result }), () => ({ acknowledged: false }));
  let present = false;
  for (let i = 0; i < 30; i++) {
    const result = await vm.execute(['python3', '-c', "import pathlib; p=pathlib.Path('/home/agent/thaddeus-artifacts/crash-marker.txt'); print(p.read_text() if p.exists() else '',end='')"], null);
    if (result.output === marker + '\n') { present = true; break; }
    await sleep(100);
  }
  if (!present) throw new Error('Private write did not become visible before crash injection.');
  witness = spawn(resolve('tools/Thaddeus.ProcessCheck/bin/Debug/net10.0/Thaddeus.ProcessCheck.exe'), ['--watch-exit', String(vm.receipt.pid)],
    { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  const lines = createInterface({ input: witness.stdout })[Symbol.asyncIterator]();
  const first = JSON.parse((await lines.next()).value);
  if (first.watching !== vm.receipt.pid) throw new Error('Worker process witness did not attach.');
  const ownerPid = fixture.pid, workerPid = vm.receipt.pid;
  const ownerExit = once(fixture, 'exit');
  if (!fixture.kill()) throw new Error('Could not terminate the owned fixture host.');
  vm = null; await ownerExit;
  const gone = JSON.parse((await lines.next()).value);
  if (gone.exited !== workerPid || (await interrupted).acknowledged) throw new Error('Crash did not interrupt the owned worker and RPC.');
  receipts.push({ label: 'host-crash-with-interrupted-effect', ownerPid, workerPid, gone, acknowledged: false, marker });
  await startFixture(true);
  if (controller.hostProcessId === ownerPid || controller.binding.runId !== old.binding.runId ||
      controller.binding.context.contentHash !== old.binding.context.contentHash || controller.binding.grantToken === old.binding.grantToken ||
      controller.controlToken === old.controlToken) throw new Error('Recovery changed identity/context or retained old credentials.');
  for (const [path, token] of [['/fixture/state', old.controlToken], ['/worker/' + old.binding.runId + '/mcp', old.binding.grantToken]]) {
    const denied = await fetch('http://127.0.0.1:' + controller.port + path, { headers: { Authorization: 'Bearer ' + token } });
    if (denied.status !== 403) throw new Error('An old host or task credential survived recovery.');
    receipts.push({ label: 'old-credential-denied', path, status: denied.status });
  }
  const recovery = JSON.parse(await readFile(resolve(dataRoot, 'recovery.json'), 'utf8'));
  if (!recovery.executeBeforeRecoveryDenied || !recovery.vmStopped || recovery.recovered.status !== 'stopped' ||
      recovery.recovered.booted || recovery.recovered.replayedCommands || !recovery.recovered.imageCheck.succeeded || !recovery.recovered.overlayUnchanged ||
      recovery.recovered.retiredCredentials.filter(item => item.serverKeyExisted).length !== 2 ||
      recovery.task.question.id !== question.run.question.id || recovery.task.modelCalls !== question.run.modelCalls ||
      recovery.task.state !== 'awaitingInput') throw new Error('Recovery did not preserve task state and retire the abandoned channels.');
  receipts.push({ label: 'host-recovery', ...recovery });
  if (await control('vm-state', 'GET') !== null) throw new Error('Recovery silently booted a worker.');
  vm = await managedVm(true);
  receipts.push({ label: 'grant-refresh', ...await control('vm-refresh-grant') });
  const after = await execute('interrupted-effect-not-replayed', ['python3', '-c', "from pathlib import Path; print(Path('/home/agent/thaddeus-artifacts/crash-marker.txt').read_text(),end='')"]);
  if (after !== marker + '\n') throw new Error('The interrupted private write was lost or replayed.');
}
try {
  await startFixture();
  vm = await managedVm(false);
  await execute('native-version-and-boundary', ['python3', '-c',
    "import os,json,subprocess,pathlib; print(json.dumps({'uid':os.getuid(),'version':subprocess.check_output(['openclaw','--version'],text=True).strip(),'mounts':pathlib.Path('/proc/mounts').read_text(),'network':list(os.listdir('/sys/class/net')),'gpu':list(pathlib.Path('/dev').glob('nvidia*'))},default=str))"]);
  const unauthorized = await fetch('http://127.0.0.1:' + controller.port + '/fixture/vm-execute', { method: 'POST', body: '{}' });
  if (unauthorized.status !== 403) throw new Error('Managed command endpoint accepted an unauthenticated request.');
  receipts.push({ label: 'host-command-unauthenticated', status: unauthorized.status });
  const denied = JSON.parse(await execute('guest-network-and-broker-negatives', ['python3', '-c', String.raw`
import json, socket, sys, urllib.request, urllib.error
results={}
for label,host,port in [('publicTcp','1.1.1.1',443),('hostAppLoopback','127.0.0.1',5179)]:
    try:
        with socket.create_connection((host,port),timeout=1): results[label]='connected'
    except OSError as error: results[label]={'deniedOrUnavailable':True,'errno':error.errno}
    assert results[label]!='connected', label
for label,path,expected in [('productRoute','/api/state',502),('controllerRoute','/fixture/state',502),('missingGrant','/worker/'+sys.argv[1]+'/mcp',403)]:
    try:
        with urllib.request.urlopen('http://127.0.0.1:5182'+path,timeout=5) as response: status=response.status
    except urllib.error.HTTPError as error: status=error.code
    results[label]=status
    assert status==expected, (label,status)
print(json.dumps(results))
`, controller.binding.runId]));
  receipts.push({ label: 'negative-observations', ...denied });
  await execute('bootstrap', ['node', '/opt/thaddeus/bootstrap.mjs'], JSON.stringify(controller.binding));
  await startGateway();
  receipts.push({ label: 'start', observation: await control('start') });
  const question = await state('awaitingInput');
  receipts.push({ label: 'question', question: question.run.question });
  receipts.push({ label: 'quiesce-question', observation: await control('abort') });
  const previousPid = vm.receipt.pid;
  if (crash) await crashHost(question);
  else { await vm.stop(); vm = null; vm = await managedVm(true); }
  if (vm.receipt.pid === previousPid) throw new Error('A distinct VM process was not observed.');
  receipts.push({ label: 'vm-restart', previousPid, resumedPid: vm.receipt.pid, overlay: resolve(dataRoot, 'qemu-' + name, 'worker.qcow2') });
  await startGateway();
  receipts.push({ label: 'resume', observation: await control('resume') });
  const proposal = await state('awaitingApproval');
  receipts.push({ label: 'proposal', approval: proposal.run.approval });
  receipts.push({ label: 'quiesce-proposal', observation: await control('abort') });
  receipts.push({ label: 'exact-import', run: await control('approve') });
  const final = await control('state', 'GET');
  if (final.run.state !== 'succeeded' || final.run.modelDispatches.length < (mode === 'luna' ? 2 : mode === 'scripted-web' ? 5 : 4) ||
      final.run.modelDispatches.some(item => item.contextObserved !== true) || final.run.executionActiveSeconds <= 0 ||
      final.run.executionCommands.map(item => item.kind).join(',') !== 'start,quiesce,resume,quiesce' ||
      final.run.executionCommands.some(item => item.status !== 'acknowledged') || final.run.question.answer !== 'Developers' ||
      final.run.validation?.passed !== true || !final.run.capabilities.some(item => item.name === 'thaddeus_read_note' && !item.isError) ||
      !final.run.capabilities.some(item => item.name === 'thaddeus_propose_import' && !item.isError) ||
      final.run.capabilities.filter(item => item.name === 'thaddeus_ask_user' && !item.isError).length !== 1)
    throw new Error('Final host receipts do not prove the native VM workflow.');
  if (mode === 'scripted-web') {
    const source = final.run.capabilities.find(item => item.name === 'thaddeus_fetch_public_page' && !item.isError)?.result.source;
    if (!source || source.url !== 'https://docs.docker.com/ai/sandboxes/faq/' || source.trust !== 'untrusted-source-data')
      throw new Error('Public retrieval did not produce a scoped source receipt.');
    const consumed = JSON.parse(await readFile(resolve(dataRoot, 'synthetic-request-3.json'), 'utf8'));
    const delivered = consumed.messages.filter(message => message.role === 'tool' && typeof message.content === 'string')
      .map(message => { try { return JSON.parse(message.content).source; } catch { return undefined; } })
      .find(candidate => candidate?.textSha256 === source.textSha256);
    if (!delivered || delivered.text !== source.text || delivered.url !== source.url)
      throw new Error('Native VM model input did not receive the complete public source.');
  }
  await writeFile(resolve(root, 'verified-state.json'), JSON.stringify(final, null, 2));
  if (crash) {
    await vm.stop(); vm = null;
    const negatives = await control('vm-recovery-negatives');
    if (!negatives.passed || !negatives.originalUnchanged) throw new Error('Recovery corruption/missing/lock checks did not pass.');
    receipts.push({ label: 'recovery-negative-controls', ...negatives });
  }
} catch (error) { failure = error.message; }
finally {
  if (vm) {
    try { await execute('guest-diagnostics', ['python3', '-c', "import pathlib; print('\\n'.join(p.read_text()[-20000:] for p in pathlib.Path('/home/agent/.openclaw').glob('*error.json'))); p=pathlib.Path('/home/agent/.openclaw/gateway-console.log'); print(p.read_text()[-20000:] if p.exists() else '')"]); } catch {}
    try { await vm.stop(); } catch (error) { failure ??= error.message; }
  }
  if (controller) { try { await control('shutdown'); } catch {} }
  if (fixture && fixture.exitCode === null) { await sleep(500); if (fixture.exitCode === null) fixture.kill(); }
  if (witness && witness.exitCode === null) witness.kill();
  await writeFile(resolve(root, 'fixture-console.log'), fixtureLogs);
  await writeFile(resolve(root, 'integration-receipt.json'), JSON.stringify({ schemaVersion: 1, passed: !failure, failure,
    mode, hostCrash: crash, productionQualified: false, brokerTransport: 'managed-qemu-mutual-tls-virtio-serial', backend: 'QemuSandboxBackend', receipts }, null, 2));
}
console.log(JSON.stringify({ passed: !failure, mode, root, failure,
  message: failure ? 'The managed VM left a specific unfinished connection in its receipts.' : 'OpenClaw worked in its own estate, with the host holding the keys.' }));
process.exitCode = failure ? 1 : 0;
