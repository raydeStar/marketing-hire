import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash, randomUUID} from 'node:crypto';
import {createReadStream, openSync, closeSync} from 'node:fs';
import {copyFile, mkdir, readFile, writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const [name, basePath = 'artifacts/linux-qemu-session-20260913-b/payload-files/base', mode = 'clean'] = process.argv.slice(2);
assert.match(name ?? '', /^[a-z0-9-]{1,45}$/);
assert.ok(['clean', 'refuse-unclean'].includes(mode));
const root = path.resolve('artifacts/qemu-shutdown-' + name), base = path.resolve(basePath);
assert.ok(base.startsWith(path.resolve('artifacts') + path.sep));
await mkdir(root);
const runtime = JSON.parse(await readFile('artifacts/qemu-linux-runtime-20260913-b/runtime-reference.json', 'utf8'));
const reference = JSON.parse(await readFile('artifacts/linux-qemu-session-20260913-b/verified.json', 'utf8'));
const installation = JSON.parse(await readFile('artifacts/linux-qemu-session-20260913-b/payload-files/installation.json', 'utf8')).installation;
const inputs = path.resolve('artifacts/qemu-inputs-script-check');
const owner = 'thaddeus-shutdown-' + randomUUID().replaceAll('-', '');
const receipt = {passed: false, root, base, mode, owner, commands: [], liveModelCalls: 0, gpuDevices: 0, githubActionsStarted: 0};
let created = false;
async function hash(file) {
  const value = createHash('sha256');
  for await (const bytes of createReadStream(file)) value.update(bytes);
  return value.digest('hex');
}
async function run(label, args, seconds = 240) {
  const out = openSync(path.join(root, label + '.stdout.log'), 'wx');
  const err = openSync(path.join(root, label + '.stderr.log'), 'wx');
  const child = spawn('docker', args, {windowsHide: true, stdio: ['ignore', out, err]});
  closeSync(out); closeSync(err);
  const step = {args, started: new Date().toISOString()}; receipt.commands.push(step);
  const timer = setTimeout(() => { step.timeout = true; child.kill(); }, seconds * 1000);
  try { step.code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('close', resolve); }); }
  finally { clearTimeout(timer); step.finished = new Date().toISOString(); }
  return step;
}
try {
  await copyFile(fileURLToPath(import.meta.url), path.join(root, 'runner.mjs'));
  await copyFile('fixtures/linux-qemu-vm/shutdown-check.py', path.join(root, 'check.py'));
  assert.equal(reference.passed, true);
  assert.match(reference.fixtureImage, /^sha256:[a-f0-9]{64}$/);
  assert.equal(await hash(runtime.manifest.path), runtime.manifest.sha256);
  const manifest = JSON.parse(await readFile(runtime.manifest.path, 'utf8'));
  for (const file of manifest.files) assert.equal(await hash(path.join(runtime.root, file.path)), file.sha256);
  assert.equal(await hash(path.join(inputs, 'alpine/boot/vmlinuz-virt')), installation.kernel.sha256);
  assert.equal(await hash(path.join(inputs, 'alpine/boot/initramfs-virt')), installation.initrd.sha256);
  receipt.inputs = {baseSha256: await hash(base), runtimeManifestSha256: runtime.manifest.sha256,
    kernelSha256: installation.kernel.sha256, initrdSha256: installation.initrd.sha256,
    runnerSha256: await hash(path.join(root, 'runner.mjs')), fixtureSha256: await hash(path.join(root, 'check.py'))};
  await writeFile(path.join(root, 'verified.json'), JSON.stringify(receipt, null, 2));
  console.log('Checking the guest closing routine. The raven expects his notes to survive.');
  created = true;
  const step = await run('check', ['run', '--name', owner, '--network', 'none', '--read-only', '--tmpfs', '/tmp:rw,size=64m',
    '--cpus', '1', '--memory', '2g', '--pids-limit', '64', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--device', '/dev/kvm',
    '--mount', `type=bind,source=${root},target=/output`, '--mount', `type=bind,source=${base},target=/base,readonly`,
    '--mount', `type=bind,source=${runtime.root},target=/runtime,readonly`, '--mount', `type=bind,source=${inputs},target=/inputs,readonly`,
    '--entrypoint', '/usr/bin/timeout', reference.fixtureImage, '--signal=KILL', '225', '/usr/bin/python3', '/output/check.py', mode]);
  receipt.native = JSON.parse(await readFile(path.join(root, 'native.json'), 'utf8'));
  receipt.passed = step.code === 0 && !step.timeout && receipt.native.passed;
  if (!receipt.passed) process.exitCode = 1;
} catch (error) { receipt.error = error.message; process.exitCode = 1; }
finally {
  if (created) {
    try {
      const removed = await run('remove', ['rm', '--force', owner], 30);
      assert.ok(removed.code === 0 && !removed.timeout, 'Fixture cleanup failed');
    } catch (error) { receipt.passed = false; receipt.cleanupError = error.message; process.exitCode = 1; }
  }
  await writeFile(path.join(root, 'verified.json'), JSON.stringify(receipt, null, 2) + '\n');
}
console.log(JSON.stringify({passed: receipt.passed, root, error: receipt.error}));
