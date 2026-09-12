import { spawn } from 'node:child_process';
import { createReadStream, createWriteStream } from 'node:fs';
import { readFile } from 'node:fs/promises';
import http from 'node:http';

// Development transport inside the VM. It executes no commands on the host.
if (process.getuid() !== 1000) throw new Error('The guest steward must run as agent.');
const fd = Number(process.env.THADDEUS_CHANNEL_FD);
if (!Number.isInteger(fd) || fd < 3) throw new Error('Missing inherited virtual channel.');
const input = createReadStream(null, { fd, autoClose: false, highWaterMark: 65536 });
const output = createWriteStream(null, { fd, autoClose: false });
const maxFrame = 2200000;
let buffered = Buffer.alloc(0), sequence = 0;
const pending = new Map(), commands = new Map();
function send(message) {
  const bytes = Buffer.from(JSON.stringify(message) + '\n');
  if (bytes.length > maxFrame || output.writableLength + bytes.length > 5000000) process.exit(2);
  output.write(bytes);
}
input.on('error', () => process.exit(2)); output.on('error', () => process.exit(2));
input.on('end', () => process.exit(0));
async function execute(message) {
  const { id, command, input: stdin } = message;
  if (!/^[a-f0-9]{32}$/.test(id) || commands.has(id) || commands.size >= 4 ||
      !Array.isArray(command) || command.length < 1 || command.length > 128 ||
      command.some(arg => typeof arg !== 'string' || arg.includes('\0') || arg.length > 100000) ||
      (stdin !== null && stdin !== undefined && (typeof stdin !== 'string' || Buffer.byteLength(stdin) > 200000))) {
    send({ type: 'result', id, exitCode: -1, output: '', error: 'Invalid guest command envelope.' }); return;
  }
  const child = spawn(command[0], command.slice(1), { cwd: '/home/agent', detached: true, stdio: ['pipe', 'pipe', 'pipe'] });
  commands.set(id, child);
  let stdout = '', stderr = '', failed = false;
  function stop() { failed = true; try { process.kill(-child.pid, 'SIGKILL'); } catch {} }
  const timer = setTimeout(stop, 55000);
  child.stdin.on('error', () => {}); child.stdin.end(stdin);
  child.on('error', () => { failed = true; });
  child.stdout.on('data', data => { stdout += data; if (Buffer.byteLength(stdout) > 600000) stop(); });
  child.stderr.on('data', data => { stderr += data; if (Buffer.byteLength(stderr) > 600000) stop(); });
  child.on('close', code => {
    clearTimeout(timer); commands.delete(id);
    send({ type: 'result', id, exitCode: failed ? -1 : code ?? -1,
      output: stdout.slice(0, 600000), error: stderr.slice(0, 600000) });
  });
}
input.on('data', data => {
  buffered = Buffer.concat([buffered, data]);
  if (buffered.length > maxFrame) process.exit(2);
  let newline;
  while ((newline = buffered.indexOf(10)) >= 0) {
    const line = buffered.subarray(0, newline); buffered = buffered.subarray(newline + 1);
    let message;
    try { message = JSON.parse(line); } catch { process.exit(2); }
    if (message.type === 'execute') void execute(message);
    else if (message.type === 'response') {
      const response = pending.get(message.id);
      if (!response) continue;
      pending.delete(message.id);
      response.writeHead(message.status, { 'content-type': message.contentType ?? 'application/json' });
      response.end(Buffer.from(message.body, 'base64'));
    } else if (message.type === 'shutdown') process.exit(0);
    else process.exit(2);
  }
});
const server = http.createServer(async (request, response) => {
  if (pending.size >= 8) { response.writeHead(503); response.end(); return; }
  const chunks = []; let length = 0;
  try {
    for await (const chunk of request) {
      length += chunk.length;
      if (length > 150000) { response.writeHead(413); response.end(); return; }
      chunks.push(chunk);
    }
    const id = 'http-' + (++sequence); pending.set(id, response);
    response.on('close', () => pending.delete(id));
    response.setTimeout(300000, () => { response.writeHead(504); response.end(); });
    send({ type: 'request', id, method: request.method, path: request.url, headers: request.headers,
      body: Buffer.concat(chunks).toString('base64') });
  } catch { response.destroy(); }
});
server.listen(5182, '127.0.0.1', async () => send({ type: 'ready', uid: process.getuid(), node: process.version,
  kernel: (await readFile('/proc/sys/kernel/osrelease', 'utf8')).trim(),
  networks: (await readFile('/proc/net/dev', 'utf8')).trim() }));
