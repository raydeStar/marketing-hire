import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createReadStream, createWriteStream } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { Readable, Transform } from 'node:stream';
import { pipeline } from 'node:stream/promises';

// Extract portable diagnostic inputs. No installer, PATH, feature or service changes.
if (process.platform !== 'win32') throw new Error('These pinned diagnostic binaries are for Windows only.');
const root = resolve(process.argv[2] ?? 'artifacts/qemu-inputs-' + Date.now());
const lock = JSON.parse(await readFile('workers/qemu/feasibility-lock.json', 'utf8'));
await mkdir(dirname(root), { recursive: true });
await mkdir(root, { recursive: false }); // Refuse to overwrite an earlier preparation.
await mkdir(resolve(root, 'downloads'));
const receipt = { observedAt: new Date().toISOString(), root, downloads: [], extraction: [] };
async function hashFile(path, algorithm) {
  const hash = createHash(algorithm);
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
}
async function extract(executable, args) {
  const child = spawn(executable, args, { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  const record = { executable, args, stdout: '', stderr: '' }; receipt.extraction.push(record);
  const timer = setTimeout(() => { record.timeout = true; child.kill(); }, 60000);
  child.stdout.on('data', data => { record.stdout += data; if (record.stdout.length > 100000) { record.overflow = true; child.kill(); } });
  child.stderr.on('data', data => { record.stderr += data; if (record.stderr.length > 100000) { record.overflow = true; child.kill(); } });
  record.exitCode = await new Promise(resolve => {
    child.on('error', error => { record.error = error.message; });
    child.on('close', resolve);
  });
  clearTimeout(timer);
  if (record.exitCode !== 0 || record.timeout || record.overflow || record.error) throw new Error('Portable extraction failed.');
}
try {
  for (const file of lock.files.filter(file => file.url)) {
    const response = await fetch(file.url, { signal: AbortSignal.timeout(120000) });
    if (!response.ok || !response.body) throw new Error('Download failed: ' + file.path);
    let bytes = 0;
    const bound = new Transform({ transform(chunk, encoding, done) {
      bytes += chunk.length;
      done(bytes > 250000000 ? new Error('Download exceeded its bound.') : null, chunk);
    } });
    const path = resolve(root, file.path);
    await pipeline(Readable.fromWeb(response.body), bound, createWriteStream(path, { flags: 'wx' }));
    const observed = await hashFile(path, file.algorithm);
    receipt.downloads.push({ ...file, observed, bytes, finalUrl: response.url });
    if (observed !== file.digest) throw new Error('Pinned download mismatch: ' + file.path);
  }
  await extract(resolve(root, 'downloads/7zr.exe'), ['x', 'downloads/7z2603-x64.exe', '-o7zip', '7z.exe', '7z.dll', 'License.txt', '-y', '-bso0']);
  await extract(resolve(root, '7zip/7z.exe'), ['x', 'downloads/qemu-w64-setup-20260811.exe', '-oqemu', '-y', '-bso0']);
  await extract(resolve(root, '7zip/7z.exe'), ['x', 'downloads/alpine-virt-3.24.1-x86_64.iso', '-oalpine',
    'boot/vmlinuz-virt', 'boot/initramfs-virt', '-y', '-bso0']);
  for (const file of lock.files) {
    if (await hashFile(resolve(root, file.path), file.algorithm) !== file.digest)
      throw new Error('Prepared input mismatch: ' + file.path);
  }
  receipt.passed = true;
} catch (error) {
  receipt.passed = false; receipt.failure = error.message;
} finally {
  await writeFile(resolve(root, 'preparation.json'), JSON.stringify(receipt, null, 2));
}
console.log(JSON.stringify({ passed: receipt.passed, root, failure: receipt.failure,
  message: receipt.passed ? 'Portable inspection kit ready. No installer entered the estate.' : 'Preparation stopped; inspect its receipt before proceeding.' }, null, 2));
process.exitCode = receipt.passed ? 0 : 1;
