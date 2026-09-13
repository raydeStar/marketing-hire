import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';

// Re-extract the pinned archives into a fresh portable bundle. No installed application is changed.
if (process.platform !== 'win32' || process.argv.length !== 4)
  throw new Error('Usage on Windows: prepare-qemu-runtime.mjs EXISTING_PINNED_INPUTS FRESH_ARTIFACT_DIRECTORY');
const inputs = resolve(process.argv[2]), root = resolve(process.argv[3]);
const artifacts = resolve('artifacts') + '\\';
if (!inputs.startsWith(artifacts) || !root.startsWith(artifacts)) throw new Error('Use private artifact directories.');
const lock = JSON.parse(await readFile('workers/qemu/feasibility-lock.json', 'utf8'));
const receipts = []; let failure;
async function hash(path, algorithm = 'sha256') {
  const value = createHash(algorithm);
  for await (const bytes of createReadStream(path)) value.update(bytes);
  return value.digest('hex');
}
async function pinned(relative) {
  const entry = lock.files.find(file => file.path === relative);
  if (!entry || await hash(resolve(inputs, relative), entry.algorithm) !== entry.digest) throw new Error('Cached archive/tool pin differs: ' + relative);
  return entry;
}
async function extract(executable, args) {
  const child = spawn(executable, args, { cwd: root, windowsHide: true,
    env: { SystemRoot: process.env.SystemRoot, WINDIR: process.env.WINDIR, TEMP: root, TMP: root }, stdio: ['ignore', 'pipe', 'pipe'] });
  const record = { executable, args, output: '', error: '' }; receipts.push(record);
  const timer = setTimeout(() => { record.timeout = true; child.kill(); }, 60000);
  child.stdout.on('data', bytes => { record.output += bytes; if (record.output.length > 100000) { record.overflow = true; child.kill(); } });
  child.stderr.on('data', bytes => { record.error += bytes; if (record.error.length > 100000) { record.overflow = true; child.kill(); } });
  record.exitCode = await new Promise(done => { child.on('error', error => { record.startError = error.message; }); child.on('close', done); });
  clearTimeout(timer);
  if (record.exitCode !== 0 || record.timeout || record.overflow || record.startError) throw new Error('Pinned bundle extraction failed.');
}
await mkdir(dirname(root), { recursive: true }); await mkdir(root, { recursive: false });
try {
  await pinned('downloads/7zr.exe'); await pinned('downloads/7z2603-x64.exe');
  const archive = await pinned('downloads/qemu-w64-setup-20260811.exe');
  await extract(resolve(inputs, 'downloads/7zr.exe'), ['x', resolve(inputs, 'downloads/7z2603-x64.exe'), '-o7zip', '7z.exe', '7z.dll', 'License.txt', '-y', '-bso0']);
  await extract(resolve(root, '7zip/7z.exe'), ['x', resolve(inputs, archive.path), '-oqemu', '-y', '-bso0']);
  const files = [];
  async function walk(relative = '') {
    for (const item of await readdir(resolve(root, 'qemu', relative), { withFileTypes: true })) {
      const path = relative ? relative + '/' + item.name : item.name;
      if (item.isSymbolicLink()) throw new Error('Package contains a link.');
      if (item.isDirectory()) await walk(path);
      else if (item.isFile()) {
        if (files.length >= 4096) throw new Error('Package has too many files.');
        files.push({ path, sha256: await hash(resolve(root, 'qemu', path)) });
      } else throw new Error('Unsupported package entry.');
    }
  }
  await walk(); files.sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
  for (const path of ['qemu-system-x86_64.exe', 'qemu-img.exe'])
    if (files.find(file => file.path === path)?.sha256 !== lock.files.find(file => file.path === 'qemu/' + path)?.digest)
      throw new Error('Extracted executable differs from its independent pin.');
  const manifest = { schemaVersion: 1, kind: 'qemu-windows-runtime', version: '11.1.0', archive: { url: archive.url, algorithm: archive.algorithm, digest: archive.digest }, files };
  const manifestPath = resolve(root, 'runtime-manifest.json');
  await writeFile(manifestPath, JSON.stringify(manifest), { flag: 'wx' });
  await writeFile(resolve(root, 'runtime-reference.json'), JSON.stringify({ root: resolve(root, 'qemu'), manifest: { path: manifestPath, sha256: await hash(manifestPath) } }), { flag: 'wx' });
  receipts.push({ label: 'full-runtime-manifest', files: files.length, manifestSha256: await hash(manifestPath), archiveVerified: true });
} catch (error) { failure = error.message; }
await writeFile(resolve(root, 'preparation.json'), JSON.stringify({ passed: !failure, failure, receipts, installed: false, productionQualified: false }, null, 2), { flag: 'wx' });
console.log(JSON.stringify({ passed: !failure, root, failure, message: 'The portable estate comes with an inventory; no installer was invited in.' }));
process.exitCode = failure ? 1 : 0;
