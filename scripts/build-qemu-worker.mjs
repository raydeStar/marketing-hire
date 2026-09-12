import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(process.argv[2] ?? 'artifacts/qemu-worker-' + Date.now());
const image = 'sha256:d3fef0da199e9b1006d150950668bcb8580f9b7bd386152eb8674b66837cd2e8';
const owned = 'thaddeus-build-' + randomUUID().replaceAll('-', '');
await mkdir(root, { recursive: false });
const receipt = { schemaVersion: 1, image, observedAt: new Date().toISOString(), commands: [], guestSources: {} };
async function execute(args, seconds = 60) {
  const child = spawn('docker', args, { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  const record = { args, stdout: '', stderr: '' }; receipt.commands.push(record);
  const timer = setTimeout(() => { record.timeout = true; child.kill(); }, seconds * 1000);
  child.stdout.on('data', data => { record.stdout += data; if (record.stdout.length > 100000) child.kill(); });
  child.stderr.on('data', data => { record.stderr += data; if (record.stderr.length > 100000) child.kill(); });
  record.exitCode = await new Promise(resolve => { child.on('error', error => { record.error = error.message; }); child.on('close', resolve); });
  clearTimeout(timer);
  if (record.exitCode !== 0 || record.timeout || record.error) throw new Error('Disk build step failed. Inspect its receipt.');
  return record.stdout;
}
try {
  for (const file of ['init', 'init.py', 'supervisor.mjs'])
    receipt.guestSources[file] = createHash('sha256').update(await readFile('workers/qemu/guest/' + file)).digest('hex');
  await execute(['image', 'inspect', image, '--format', '{{.Id}}']);
  await execute(['create', '--name', owned, '--network', 'none', '--cpus', '1', '--memory', '512m', '--user', 'root',
    '--entrypoint', '/bin/sh', image, '-c', 'set -eu; chmod 755 /opt/thaddeus/vm/init; test -x /usr/bin/python3; mke2fs -V']);
  await execute(['cp', resolve('workers/qemu/guest'), owned + ':/opt/thaddeus/vm']);
  await execute(['start', '--attach', owned]);
  await execute(['export', '--output', resolve(root, 'rootfs.tar'), owned], 300);
  await execute(['rm', owned]);
  await execute(['run', '--name', owned + '-disk', '--network', 'none', '--cpus', '2', '--memory', '2g', '--user', 'root',
    '--mount', 'type=bind,source=' + root + ',target=/output', '--entrypoint', '/bin/sh', image, '-c',
    'set -eu; mkdir /rootfs; tar --numeric-owner -xf /output/rootfs.tar -C /rootfs; truncate -s 8G /output/root.ext4; mke2fs -q -t ext4 -F -m 0 -L thaddeus-root -d /rootfs /output/root.ext4; e2fsck -fn /output/root.ext4'], 600);
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(resolve(root, 'root.ext4'))) hash.update(chunk);
  receipt.diskSha256 = hash.digest('hex'); receipt.passed = true;
} catch (error) { receipt.passed = false; receipt.failure = error.message; }
finally {
  for (const name of [owned, owned + '-disk']) { try { await execute(['rm', '--force', name]); } catch {} }
  await writeFile(resolve(root, 'build-receipt.json'), JSON.stringify(receipt, null, 2));
}
console.log(JSON.stringify({ passed: receipt.passed, root, diskSha256: receipt.diskSha256, failure: receipt.failure,
  message: receipt.passed ? 'The worker estate has a disk; its first boot comes next.' : 'Disk construction stopped with its receipts intact.' }));
process.exitCode = receipt.passed ? 0 : 1;
