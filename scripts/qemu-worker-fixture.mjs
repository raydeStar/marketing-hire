import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { readFile, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { resolve } from 'node:path';

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
async function hash(path, algorithm = 'sha256') {
  const digest = createHash(algorithm);
  for await (const chunk of createReadStream(path)) digest.update(chunk);
  return digest.digest('hex');
}
async function channel() {
  let accept, accepted = false;
  const connected = new Promise(resolve => { accept = resolve; });
  const server = createServer(socket => {
    if (accepted) { socket.destroy(); return; }
    accepted = true; server.close(); socket.setNoDelay(true); accept(socket);
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  return { server, connected, port: server.address().port };
}
export async function prepareVm(inputs, diskRoot, evidence) {
  const lock = JSON.parse(await readFile('workers/qemu/feasibility-lock.json', 'utf8'));
  for (const file of lock.files)
    if (await hash(resolve(inputs, file.path), file.algorithm) !== file.digest) throw new Error('QEMU input pin mismatch: ' + file.path);
  const build = JSON.parse(await readFile(resolve(diskRoot, 'build-receipt.json'), 'utf8'));
  for (const file of ['init', 'init.py', 'supervisor.mjs'])
    if (await hash('workers/qemu/guest/' + file) !== build.guestSources?.[file]) throw new Error('Guest source changed; rebuild the VM disk before testing.');
  if (!build.passed || build.image !== 'sha256:d3fef0da199e9b1006d150950668bcb8580f9b7bd386152eb8674b66837cd2e8' ||
      await hash(resolve(diskRoot, 'root.ext4')) !== build.diskSha256) throw new Error('Worker disk provenance mismatch.');
  const base = resolve(diskRoot, 'root.ext4'), overlay = resolve(evidence, 'worker.qcow2');
  const executable = resolve(inputs, 'qemu/qemu-img.exe');
  const child = spawn(executable, ['create', '-f', 'qcow2', '-F', 'raw', '-b', base, overlay], { windowsHide: true, stdio: 'ignore' });
  const timer = setTimeout(() => child.kill(), 15000);
  const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('close', resolve); });
  clearTimeout(timer); if (code !== 0) throw new Error('Private overlay creation failed.');
  const prepared = { inputs: resolve(inputs), base, overlay, diskSha256: build.diskSha256,
    imageToolSha256: await hash(executable), guestSources: build.guestSources };
  await writeFile(resolve(evidence, 'vm-inputs.json'), JSON.stringify(prepared, null, 2));
  return prepared;
}
export async function bootVm(prepared, evidence, forward) {
  const consoleChannel = await channel(), controlChannel = await channel();
  const id = randomUUID().replaceAll('-', '');
  const receipt = { id, observedAt: new Date().toISOString(), productionQualified: false, stdout: '', stderr: '', qmp: [], requests: [] };
  const path = resolve(evidence, 'vm-' + id + '.json');
  const args = ['-name', 'thaddeus-' + id, '-machine', 'q35', '-accel', 'whpx', '-cpu', 'qemu64,-svm',
    '-m', '4096', '-smp', '2', '-nodefaults', '-nic', 'none', '-display', 'none', '-monitor', 'none', '-no-reboot', '-qmp', 'stdio', '-S',
    '-chardev', 'socket,id=console,host=127.0.0.1,port=' + consoleChannel.port, '-serial', 'chardev:console',
    '-chardev', 'socket,id=control,host=127.0.0.1,port=' + controlChannel.port,
    '-device', 'virtio-serial-pci,id=transport', '-device', 'virtserialport,chardev=control,name=org.thaddeus.control',
    '-kernel', resolve(prepared.inputs, 'alpine/boot/vmlinuz-virt'), '-initrd', resolve(prepared.inputs, 'alpine/boot/initramfs-virt'),
    '-append', 'console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/opt/thaddeus/vm/init quiet',
    '-blockdev', JSON.stringify({ driver: 'file', filename: prepared.base, 'node-name': 'base-file', 'read-only': true }),
    '-blockdev', JSON.stringify({ driver: 'raw', file: 'base-file', 'node-name': 'base', 'read-only': true }),
    '-blockdev', JSON.stringify({ driver: 'file', filename: prepared.overlay, 'node-name': 'overlay-file' }),
    '-blockdev', JSON.stringify({ driver: 'qcow2', file: 'overlay-file', backing: 'base', 'node-name': 'worker' }),
    '-device', 'virtio-blk-pci,drive=worker'];
  receipt.args = args;
  const child = spawn(resolve(prepared.inputs, 'qemu/qemu-system-x86_64.exe'), args, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  receipt.pid = child.pid;
  let closed = false, stopping = false, failure, consoleSocket, controlSocket;
  const qmpReplies = new Map(), commands = new Map();
  const closedPromise = new Promise(resolve => child.once('close', (code, signal) => {
    closed = true; receipt.exitCode = code; receipt.signal = signal; resolve();
  }));
  function fail(error) { failure ??= error; if (!closed) child.kill(); }
  const deadline = setTimeout(() => fail(new Error('VM fixture exceeded its 12 minute lifetime.')), 720000);
  child.on('error', fail); child.stdin.on('error', error => { if (!stopping) fail(error); });
  function socketError(error) { if (!stopping || error.code !== 'ECONNRESET') fail(error); }
  child.stderr.on('data', data => { receipt.stderr += data; if (receipt.stderr.length > 100000) fail(new Error('VM diagnostic output exceeded its bound.')); });
  let qmpBuffer = '', qmpBytes = 0;
  child.stdout.on('data', data => {
    qmpBytes += data.length;
    if (qmpBytes > 300000) { fail(new Error('QMP output exceeded its bound.')); return; }
    qmpBuffer += data; let end;
    while ((end = qmpBuffer.indexOf('\n')) >= 0) {
      const line = qmpBuffer.slice(0, end); qmpBuffer = qmpBuffer.slice(end + 1);
      try { const message = JSON.parse(line); receipt.qmp.push(message); if (message.id) qmpReplies.set(message.id, message); }
      catch { fail(new Error('Invalid QMP response.')); }
    }
  });
  async function until(predicate, seconds) {
    const end = Date.now() + seconds * 1000;
    while (!predicate()) {
      if (failure) throw failure;
      if (closed) throw new Error('VM stopped before completing its observation.');
      if (Date.now() >= end) throw new Error('VM observation timed out.');
      await sleep(20);
    }
  }
  async function qmp(execute) {
    const id = randomUUID(); child.stdin.write(JSON.stringify({ execute, id }) + '\n');
    await until(() => qmpReplies.has(id), 10);
    const reply = qmpReplies.get(id); if (reply.error) throw new Error('QMP failed: ' + execute); return reply.return;
  }
  function send(message) {
    const body = JSON.stringify(message) + '\n';
    if (Buffer.byteLength(body) > 2200000 || controlSocket.writableLength > 3000000) throw new Error('Guest transport exceeded its bound.');
    controlSocket.write(body);
  }
  async function incoming(message) {
    if (message.type === 'ready') receipt.ready = message;
    else if (message.type === 'result') {
      const item = commands.get(message.id);
      if (item) { item.result = message; }
    } else if (message.type === 'request') {
      try {
        if (receipt.requests.length > 200) throw new Error('Too many guest requests.');
        const response = await forward(message);
        receipt.requests.push({ method: message.method, path: message.path, status: response.status });
        send({ type: 'response', id: message.id, ...response });
      } catch {
        receipt.requests.push({ method: message.method, path: message.path, status: 502 });
        send({ type: 'response', id: message.id, status: 502, body: '', contentType: 'application/json' });
      }
    } else throw new Error('Unrecognized guest frame.');
  }
  async function save() { receipt.failure = failure?.message; await writeFile(path, JSON.stringify(receipt, null, 2)); }
  async function cleanup() {
    clearTimeout(deadline); consoleSocket?.destroy(); controlSocket?.destroy();
    consoleChannel.server.close(); controlChannel.server.close(); await save();
  }
  try {
    consoleSocket = await Promise.race([consoleChannel.connected, closedPromise.then(() => { throw new Error('VM console did not connect.'); })]);
    consoleSocket.on('error', socketError);
    consoleSocket.on('data', data => { receipt.stdout += data; if (receipt.stdout.length > 300000) fail(new Error('Guest console output exceeded its bound.')); });
    controlSocket = await Promise.race([controlChannel.connected, closedPromise.then(() => { throw new Error('VM command channel did not connect.'); })]);
    controlSocket.on('error', socketError);
    let buffer = Buffer.alloc(0);
    controlSocket.on('data', data => {
      buffer = Buffer.concat([buffer, data]); if (buffer.length > 2200000) { fail(new Error('Guest frame exceeded its bound.')); return; }
      let end;
      while ((end = buffer.indexOf(10)) >= 0) {
        const line = buffer.subarray(0, end); buffer = buffer.subarray(end + 1);
        try { void incoming(JSON.parse(line)).catch(fail); } catch (error) { fail(error); }
      }
    });
    await until(() => receipt.qmp.some(message => message.QMP), 10);
    await qmp('qmp_capabilities'); receipt.cpus = await qmp('query-cpus-fast');
    receipt.memory = await qmp('query-memory-size-summary'); receipt.pci = await qmp('query-pci'); receipt.block = await qmp('query-block');
    await qmp('cont'); await until(() => receipt.ready, 30);
    if (receipt.ready.uid !== 1000 || receipt.cpus.length !== 2 || receipt.memory['base-memory'] !== 4294967296 ||
        receipt.pci.some(bus => bus.devices.some(device => (device.class_info.class >> 8) === 2))) throw new Error('VM observations did not match the fixture boundary.');
    await save();
    return {
      receipt,
      async execute(command, input = null) {
        if (commands.size >= 4) throw new Error('Guest command concurrency bound reached.');
        const id = randomUUID().replaceAll('-', ''), item = {}; commands.set(id, item);
        try { send({ type: 'execute', id, command, input }); await until(() => item.result, 60); return item.result; }
        finally { commands.delete(id); }
      },
      async stop() {
        if (!closed) { stopping = true; send({ type: 'shutdown' }); }
        const timer = setTimeout(() => fail(new Error('Guest shutdown was not confirmed.')), 15000);
        await closedPromise; clearTimeout(timer); await cleanup();
        if (failure || receipt.exitCode !== 0 || !receipt.qmp.some(message => message.event === 'SHUTDOWN' && message.data?.guest === true))
          throw failure ?? new Error('Guest shutdown did not pass.');
      }
    };
  } catch (error) { fail(error); await closedPromise; await cleanup(); throw error; }
}
