import { spawn } from 'node:child_process';
import { createHash, randomBytes, randomUUID } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createServer } from 'node:net';
import { resolve } from 'node:path';

// A pre-agent experiment, never a production backend or an automatic fallback.
if (process.platform !== 'win32') throw new Error('This probe qualifies observations on Windows only.');
const root = resolve(process.argv[2] ?? 'artifacts/qemu-feasibility');
const output = resolve('artifacts', 'qemu-probe-' + Date.now() + '-' + randomUUID().slice(0, 8));
await mkdir(output, { recursive: false });
const lock = JSON.parse(await readFile('workers/qemu/feasibility-lock.json', 'utf8'));
async function hashFile(path, algorithm) {
  const hash = createHash(algorithm);
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
}
const files = [];
for (const file of lock.files) {
  const observed = await hashFile(resolve(root, file.path), file.algorithm);
  if (observed !== file.digest) throw new Error('Pinned input mismatch: ' + file.path);
  files.push({ ...file, observed });
}
const id = 'thaddeus-' + randomUUID().replaceAll('-', '');
const executable = resolve(root, 'qemu/qemu-system-x86_64.exe');
async function localChannel() {
  let accept, accepted = false;
  const connection = new Promise(resolve => { accept = resolve; });
  const server = createServer(socket => {
    if (accepted) { socket.destroy(); return; }
    accepted = true; server.close(); socket.setNoDelay(true); accept(socket);
  });
  await new Promise((resolve, reject) => {
    server.once('error', reject); server.listen(0, '127.0.0.1', resolve);
  });
  return { server, connection, port: server.address().port };
}
const consoleChannel = await localChannel(), brokerChannel = await localChannel();
const args = ['-machine', 'q35', '-accel', 'whpx', '-cpu', 'qemu64,-svm', '-m', '512', '-smp', '1',
  '-nodefaults', '-nic', 'none', '-display', 'none', '-monitor', 'none', '-no-reboot', '-S',
  '-name', id, '-qmp', 'stdio',
  '-chardev', 'socket,id=console,host=127.0.0.1,port=' + consoleChannel.port, '-serial', 'chardev:console',
  '-chardev', 'socket,id=broker,host=127.0.0.1,port=' + brokerChannel.port,
  '-device', 'virtio-serial-pci,id=transport',
  '-device', 'virtserialport,chardev=broker,name=org.thaddeus.probe',
  '-kernel', resolve(root, 'alpine/boot/vmlinuz-virt'),
  '-initrd', resolve(root, 'alpine/boot/initramfs-virt'),
  '-append', 'console=ttyS0,115200 modules=loop,squashfs,sd-mod,usb-storage quiet',
  '-blockdev', JSON.stringify({ driver: 'file', filename: resolve(root, 'downloads/alpine-virt-3.24.1-x86_64.iso'), 'node-name': 'cd-file', 'read-only': true }),
  '-blockdev', JSON.stringify({ driver: 'raw', file: 'cd-file', 'node-name': 'cd', 'read-only': true }),
  '-device', 'ide-cd,drive=cd'];
const payload = randomBytes(65536);
const payloadHash = createHash('sha256').update(payload).digest('hex');
const receipt = { schemaVersion: 1, authority: 'pre-agent-feasibility', productionQualified: false,
  observedAt: new Date().toISOString(), files, executable, args, qmp: [], stdout: '', stderr: '',
  transport: { bytes: payload.length, expectedSha256: payloadHash } };
const child = spawn(executable, args, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
receipt.pid = child.pid;
let closed = false, shutdownRequested = false, consolePipe, broker, failure;
const closedPromise = new Promise(resolve => child.once('close', (code, signal) => {
  closed = true; receipt.exitCode = code; receipt.exitSignal = signal; resolve();
}));
function fail(error) {
  failure ??= error instanceof Error ? error : new Error(String(error));
  if (!closed) child.kill();
}
function channelError(channel, error) {
  (receipt.channelErrors ??= []).push({ channel, code: error.code, shutdownRequested });
  // Windows may reset the host socket as the guest powers off. The final check
  // still requires the independent QMP guest-shutdown event and process exit 0.
  if (!closed && !(shutdownRequested && error.code === 'ECONNRESET')) fail(error);
}
child.on('error', fail);
child.stdin.on('error', error => { if (!closed) fail(error); });
const timer = setTimeout(() => fail(new Error('Probe exceeded its 60 second deadline.')), 60000);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(predicate, seconds = 10) {
  const deadline = Date.now() + seconds * 1000;
  while (!predicate()) {
    if (failure) throw failure;
    if (closed) throw new Error('VM stopped before the observation completed.');
    if (Date.now() > deadline) throw new Error('Timed out waiting for a probe observation.');
    await sleep(20);
  }
}
async function connectChannel(channel) {
  return await Promise.race([channel.connection, closedPromise.then(() => { throw new Error('VM stopped before opening its channel.'); })]);
}
async function sendConsole(text) {
  // UART input can overrun; only this fixed bootstrap uses it. Payload uses virtio.
  for (let i = 0; i < text.length; i += 8) {
    if (closed || failure) throw failure ?? new Error('VM console closed.');
    consolePipe.write(text.slice(i, i + 8)); await sleep(5);
  }
}
child.stderr.on('data', data => {
  receipt.stderr += data;
  if (receipt.stderr.length > 100000) fail(new Error('Diagnostic output exceeded its bound.'));
});
try {
  let qmpBuffer = '', qmpBytes = 0, sequence = 0;
  const replies = new Map();
  child.stdout.on('data', data => {
    qmpBytes += data.length;
    if (qmpBytes > 250000) { fail(new Error('QMP output exceeded its bound.')); return; }
    qmpBuffer += data;
    let newline;
    while ((newline = qmpBuffer.indexOf('\n')) >= 0) {
      const line = qmpBuffer.slice(0, newline); qmpBuffer = qmpBuffer.slice(newline + 1);
      try {
        const message = JSON.parse(line); receipt.qmp.push(message);
        if (message.id) replies.set(message.id, message);
      } catch { fail(new Error('Invalid QMP JSON.')); }
    }
  });
  async function command(execute) {
    const commandId = execute + '-' + (++sequence);
    child.stdin.write(JSON.stringify({ execute, id: commandId }) + '\n');
    try { await until(() => replies.has(commandId)); }
    catch (error) { throw new Error(execute + ': ' + error.message); }
    const reply = replies.get(commandId);
    if (reply.error) throw new Error('QMP command failed: ' + execute);
    return reply.return;
  }
  // These sockets connect two host processes; the guest still has no network NIC.
  consolePipe = await connectChannel(consoleChannel);
  consolePipe.on('error', error => channelError('console', error));
  consolePipe.on('data', data => {
    receipt.stdout += data;
    if (receipt.stdout.length > 250000) fail(new Error('Console output exceeded its bound.'));
  });
  broker = await connectChannel(brokerChannel);
  broker.on('error', error => channelError('broker', error));
  let returned = Buffer.alloc(0);
  broker.on('data', data => {
    if (returned.length + data.length > payload.length) { fail(new Error('Virtual channel returned excess data.')); return; }
    returned = Buffer.concat([returned, data]);
  });
  await until(() => receipt.qmp.some(message => message.QMP));
  await command('qmp_capabilities');
  receipt.prelaunch = await command('query-status');
  receipt.cpus = await command('query-cpus-fast');
  receipt.memory = await command('query-memory-size-summary');
  receipt.block = await command('query-block');
  receipt.pci = await command('query-pci');
  await command('cont');
  await until(() => receipt.stdout.includes('localhost login:'), 20);
  await sendConsole('root\n');
  await until(() => receipt.stdout.includes('localhost:~#'));
  const script = `set -eu
echo THADDEUS_PROBE_BEGIN
uname -srmo
cat /etc/alpine-release
printf 'CPU_COUNT='; grep -c '^processor' /proc/cpuinfo
grep '^MemTotal:' /proc/meminfo
printf 'NETWORK_DEVICES='; echo /sys/class/net/*
cat /proc/mounts
test "$(ls /sys/class/net)" = lo
! grep -Eq ' (9p|virtiofs|cifs|nfs|fuse.vmhgfs-fuse) ' /proc/mounts
test ! -e /dev/nvidia0
test ! -e /var/run/docker.sock
test ! -e /host
test ! -e /mnt/c
mkdir -p /workspace
exec 3<> /dev/virtio-ports/org.thaddeus.probe
echo THADDEUS_PORT_READY
dd of=/workspace/roundtrip.bin bs=4096 count=16 iflag=fullblock <&3 2>/dev/null
sha256sum /workspace/roundtrip.bin
cat /workspace/roundtrip.bin >&3
echo THADDEUS_PROBE_PASSED
`;
  await sendConsole('echo ' + Buffer.from(script).toString('base64') + ' | base64 -d | sh\n');
  await until(() => receipt.stdout.includes('\r\nTHADDEUS_PORT_READY\r\n'));
  broker.write(payload);
  await until(() => returned.length === payload.length && receipt.stdout.includes('\r\nTHADDEUS_PROBE_PASSED\r\n'));
  receipt.transport.observedSha256 = createHash('sha256').update(returned).digest('hex');
  receipt.transport.exactRoundtrip = returned.equals(payload);
  if (!receipt.transport.exactRoundtrip || !receipt.stdout.includes(payloadHash + '  /workspace/roundtrip.bin'))
    throw new Error('Guest file or returned bytes did not match the host payload.');
  await command('stop'); receipt.paused = await command('query-status');
  await command('cont'); receipt.resumed = await command('query-status');
  shutdownRequested = true; await sendConsole('poweroff\n');
  await closedPromise;
} catch (error) {
  fail(error); await closedPromise;
} finally {
  clearTimeout(timer); consolePipe?.destroy(); broker?.destroy();
  consoleChannel.server.close(); brokerChannel.server.close();
}
receipt.failure = failure?.message;
receipt.passed = !failure && receipt.exitCode === 0 && receipt.prelaunch?.status === 'prelaunch' &&
  receipt.paused?.status === 'paused' && receipt.resumed?.running === true &&
  receipt.cpus?.length === 1 && receipt.memory?.['base-memory'] === 536870912 &&
  receipt.block?.length === 1 && receipt.block[0].inserted?.ro === true &&
  receipt.pci?.every(bus => bus.devices.every(device => (device.class_info.class >> 8) !== 2)) &&
  receipt.transport.exactRoundtrip === true && receipt.stdout.includes('Power down') &&
  receipt.qmp.some(message => message.event === 'SHUTDOWN' && message.data?.guest === true && message.data.reason === 'guest-shutdown');
await writeFile(resolve(output, 'receipt.json'), JSON.stringify(receipt, null, 2));
console.log(JSON.stringify({ passed: receipt.passed, productionQualified: false, output,
  failure: receipt.failure, transport: receipt.transport,
  message: receipt.passed ? 'The guest room works; its production locks still need qualification.' : 'The guest room inspection found unfinished business.' }, null, 2));
process.exitCode = receipt.passed ? 0 : 1;
