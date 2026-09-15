import assert from 'node:assert/strict';
import {spawn, execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';
import {mkdir, readFile, readdir, lstat, realpath, writeFile, stat} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {requireArtifactSpace, cleanArtifactPaths} from './artifact-storage.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');

export function relativeFile(value) {
  assert.ok(typeof value === 'string' && value.length < 500 && !/[\\<>:"|?*\x00-\x1f]/.test(value), 'Invalid Windows package path.');
  assert.ok(!path.posix.isAbsolute(value) && value.split('/').every(part =>
    part && part !== '.' && part !== '..' && !/[. ]$/.test(part) && !/^(con|prn|aux|nul|com[0-9]|lpt[0-9])(?:\.|$)/i.test(part)), 'Unsafe package path.');
  return value;
}

export function nsisLiteral(value) {
  assert.ok(typeof value === 'string' && !/[\x00-\x1f]/.test(value), 'NSIS literal contains a control character.');
  return value.replaceAll('$', () => '$$').replaceAll('"', () => '$\\"');
}

async function plain(value) {
  const full = path.resolve(value);
  assert.equal(await realpath(full), full, 'Linked compiler/package paths are refused.');
  assert.equal((await lstat(full)).isSymbolicLink(), false);
  return full;
}

export async function tree(directory, prefix = '') {
  await plain(directory);
  const files = [];
  for (const entry of await readdir(directory, {withFileTypes: true})) {
    const name = prefix + entry.name;
    relativeFile(name);
    const full = path.join(directory, entry.name);
    assert.equal(entry.isSymbolicLink(), false, 'Linked input is refused.');
    if (entry.isDirectory()) files.push(...await tree(full, name + '/'));
    else {
      assert.equal(entry.isFile(), true);
      files.push(name);
    }
  }
  return files.sort();
}

export async function readPackage(directory) {
  directory = await plain(directory);
  const raw = await readFile(path.join(directory, 'package-manifest.json'));
  assert.ok(raw.length < 4_000_000);
  const manifest = JSON.parse(raw);
  assert.equal(manifest.schemaVersion, 1);
  assert.equal(manifest.kind, 'portable-development-package', 'Use a host-only development package.');
  assert.equal(manifest.runtime, 'win-x64');
  assert.equal(manifest.application?.runtime, 'win-x64');
  assert.equal(manifest.application?.guardedLaunchVersion, 1);
  assert.ok(!manifest.bundledWorker && manifest.signedRelease === false && manifest.isolationQualified === false);
  assert.ok(Array.isArray(manifest.files) && manifest.files.length > 0 && manifest.files.length < 5000);
  const files = [], seen = new Set();
  let bytes = 0;
  for (const file of manifest.files) {
    relativeFile(file.path);
    assert.ok(!/^(?:worker|\.data)(?:\/|$)|(?:^|\/)(?:launch\.json|host-key\.txt|package-manifest\.json)$/i.test(file.path),
      'Worker inputs, private launch configuration and recursive manifests are not installer payload.');
    assert.ok(!seen.has(file.path.toLowerCase()), 'Duplicate Windows package filename.');
    assert.match(file.sha256, /^[a-f0-9]{64}$/);
    assert.ok(Number.isSafeInteger(file.size) && file.size >= 0 && file.size < 512 * 1024 ** 2);
    bytes += file.size;
    assert.ok(bytes < 512 * 1024 ** 2, 'This installer packages the small host only.');
    seen.add(file.path.toLowerCase());
    files.push({...file});
  }
  for (const required of ['Thaddeus.Host.exe', 'Start Thaddeus.cmd', 'launch-host.ps1', 'wwwroot/index.html'])
    assert.ok(seen.has(required.toLowerCase()), 'Incomplete host package: ' + required);
  assert.ok(manifest.notices?.path && seen.has(manifest.notices.path.toLowerCase()), 'Missing generated host notices.');
  files.push({path: 'package-manifest.json', sha256: hash(raw), size: raw.length});
  assert.deepEqual(await tree(directory), files.map(f => f.path).sort(), 'Package has missing or additional files.');
  for (const file of files) {
    const data = await readFile(path.join(directory, file.path));
    assert.equal(data.length, file.size, file.path);
    assert.equal(hash(data), file.sha256, file.path);
  }
  const notice = files.find(f => f.path === manifest.notices.path);
  assert.equal(notice.sha256, manifest.notices.sha256);
  return {directory, manifest, files, bytes: bytes + raw.length, manifestSha256: hash(raw)};
}

export function renderInstaller(template, guardTemplate, input) {
  const {files, snapshot, output, manifestSha256, installerNotice} = input;
  assert.match(manifestSha256, /^[a-f0-9]{64}$/);
  const buildId = manifestSha256.slice(0, 16);
  const entries = files.map(file => ({destination: 'app/' + relativeFile(file.path), source: path.join(snapshot, 'app', file.path)}));
  entries.push({destination: 'installer-notices.txt', source: installerNotice});
  const dirs = new Set();
  for (const file of entries) {
    let directory = path.posix.dirname(file.destination);
    while (directory !== '.') {
      dirs.add(directory);
      directory = path.posix.dirname(directory);
    }
  }
  const destination = relative => '$INSTDIR\\' + nsisLiteral(relative.replaceAll('/', '\\'));
  const install = [];
  for (const entry of entries) {
    const directory = path.posix.dirname(entry.destination);
    const target = directory === '.' ? '$INSTDIR' : destination(directory);
    install.push('  Push "' + target + '"', '  Call AssertPlainPath', '  SetOutPath "' + target + '"',
      '  File "/oname=' + nsisLiteral(path.posix.basename(entry.destination)) + '" "' + nsisLiteral(entry.source) + '"',
      '  IfErrors install_failed');
  }
  const preflight = ['  Push "$SMPROGRAMS\\Thaddeus 2\\Thaddeus 2 preview ' + buildId + '.lnk"', '  Call un.AssertPlainPath'];
  for (const entry of entries)
    preflight.push('  Push "' + destination(entry.destination) + '"', '  Call un.AssertPlainPath',
      '  Push "' + destination(entry.destination) + '"', '  Call un.AssertAvailable');
  const deletions = entries.map(entry => '  Delete "' + destination(entry.destination) + '"').join('\n');
  const directories = [...dirs].sort((a, b) => b.split('/').length - a.split('/').length || b.localeCompare(a))
    .map(dir => '  RMDir "' + destination(dir) + '"').join('\n');
  const failure = entries.flatMap(entry => ['  Push "' + destination(entry.destination) + '"', '  Call AssertPlainPath',
    '  Delete "' + destination(entry.destination) + '"']).join('\n') + '\n' + directories;
  const values = {
    GUARD_FUNCTIONS: (guardTemplate.replaceAll('@PREFIX@', '') + '\n' + guardTemplate.replaceAll('@PREFIX@', 'un.')).replaceAll('@BUILD_ID@', buildId),
    INSTALL_FILES: install.join('\n'), FAILURE_CLEANUP: failure,
    UNINSTALL_PREFLIGHT: preflight.join('\n'), UNINSTALL_FILES: deletions, UNINSTALL_DIRECTORIES: directories,
    OUTPUT: nsisLiteral(output), MANIFEST_SHA256: manifestSha256,
    SIZE_KIB: String(Math.ceil(files.reduce((sum, file) => sum + file.size, 0) / 1024)),
    REGISTRY: 'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Thaddeus2.Preview.' + buildId,
    BUILD_ID: buildId
  };
  // One pass keeps package names literal; SHA256 must still make it past the butler.
  const result = template.replace(/@([A-Z][A-Z0-9_]*)@/g, (_, key) => {
    assert.ok(Object.hasOwn(values, key), 'Unknown installer template field: ' + key);
    return values[key];
  });
  return {script: result, buildId, registryKey: values.REGISTRY, payloadFiles: entries.map(e => e.destination)};
}

async function runCompiler(executable, args, options) {
  const child = spawn(executable, args, {windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], ...options});
  let stdout = '', stderr = '';
  child.stdout.on('data', bytes => { stdout += bytes; });
  child.stderr.on('data', bytes => { stderr += bytes; });
  const status = await new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('close', resolve);
  });
  return {status, stdout, stderr, processId: child.pid, exited: true};
}

export async function buildInstaller(packageArgument, toolchainArgument, name) {
  assert.equal(process.platform, 'win32'); assert.equal(process.arch, 'x64');
  assert.match(name ?? '', /^[A-Za-z0-9-]{1,60}$/);
  const toolchain = await plain(toolchainArgument);
  const lockPath = path.join(repository, 'packaging/windows/toolchain.json');
  const lock = JSON.parse(await readFile(lockPath, 'utf8'));
  assert.equal(lock.version, '3.12'); assert.equal(lock.schemaVersion, 1);
  assert.deepEqual(await tree(toolchain), lock.files.map(f => f.path).sort());
  for (const file of lock.files) {
    const data = await readFile(path.join(toolchain, relativeFile(file.path)));
    assert.equal(data.length, file.size); assert.equal(hash(data), file.sha256, file.path);
  }
  const input = await readPackage(packageArgument);
  const root = path.join(repository, 'artifacts', 'windows-installer-' + name);
  const admission = await requireArtifactSpace(path.dirname(root), 2 * input.bytes + 128 * 1024 ** 2, 'Windows installer publication');
  await mkdir(root);
  const snapshot = path.join(root, 'payload');
  let compiler, failure, rendered, setup, cleanup = [];
  const sources = ['scripts/windows-installer.mjs', 'scripts/artifact-storage.mjs',
    'packaging/windows/installer.nsi', 'packaging/windows/guards.nsh', 'packaging/windows/toolchain.json',
    'third-party/installer/nsis-COPYING.txt'];
  const sourceFiles = [];
  try {
    await mkdir(path.join(snapshot, 'app'), {recursive: true});
    for (const file of input.files) {
      const data = await readFile(path.join(input.directory, file.path));
      assert.equal(data.length, file.size); assert.equal(hash(data), file.sha256);
      const target = path.join(snapshot, 'app', file.path);
      await mkdir(path.dirname(target), {recursive: true});
      await writeFile(target, data, {flag: 'wx'});
    }
    const notice = await readFile(path.join(repository, 'third-party/installer/nsis-COPYING.txt'));
    assert.deepEqual(notice, await readFile(path.join(toolchain, 'COPYING')));
    const noticePath = path.join(snapshot, 'installer-notices.txt');
    await writeFile(noticePath, notice);
    for (const file of sources) {
      const bytes = await readFile(path.join(repository, file));
      sourceFiles.push({path: file, sha256: hash(bytes)});
      const target = path.join(root, 'source', file);
      await mkdir(path.dirname(target), {recursive: true}); await writeFile(target, bytes);
    }
    setup = path.join(root, 'Thaddeus-2-preview-' + input.manifestSha256.slice(0, 16) + '.exe');
    rendered = renderInstaller(
      await readFile(path.join(root, 'source/packaging/windows/installer.nsi'), 'utf8'),
      await readFile(path.join(root, 'source/packaging/windows/guards.nsh'), 'utf8'),
      {...input, snapshot, output: setup, installerNotice: noticePath});
    const script = path.join(root, 'installer.nsi');
    await writeFile(script, rendered.script, 'utf8');
    const executable = path.join(toolchain, 'Bin/makensis.exe');
    const args = ['/NOCONFIG', '/INPUTCHARSET', 'UTF8', '/WX', '/V3', script];
    compiler = await runCompiler(executable, args, {cwd: toolchain, env: {...process.env, NSISDIR: toolchain}});
    compiler.executable = executable; compiler.args = args;
    await writeFile(path.join(root, 'compiler.json'), JSON.stringify(compiler, null, 2) + '\n');
    assert.equal(compiler.status, 0, compiler.stderr + compiler.stdout);
    const unchanged = await readPackage(input.directory);
    assert.equal(unchanged.manifestSha256, input.manifestSha256);
    const bytes = await readFile(setup);
    await writeFile(path.join(root, 'SHA256SUMS'), hash(bytes) + '  ' + path.basename(setup) + '\n');
    await writeFile(path.join(root, 'published.json'), JSON.stringify({
      schemaVersion: 1, kind: 'windows-host-installer-preview', runtime: 'win-x64',
      installer: setup, sha256: hash(bytes), bytes: bytes.length,
      buildId: rendered.buildId, registryKey: rendered.registryKey,
      hostManifestSha256: input.manifestSha256, hostSourceHead: input.manifest.sourceHead,
      hostPackage: input.directory, payloadFiles: rendered.payloadFiles, sourceFiles,
      compilerVersion: lock.version, compilerArchiveSha256: lock.archiveSha256,
      unsigned: true, includesWorker: false, nativeInstallerVerified: false,
      builderHead: execFileSync('git', ['rev-parse', 'HEAD'], {cwd: repository, encoding: 'utf8', windowsHide: true}).trim(),
      createdAt: new Date().toISOString()
    }, null, 2) + '\n');
  } catch (error) { failure = error; }
  finally {
    // The direct compiler process has exited before its input copy is removed.
    cleanup = await cleanArtifactPaths(root, ['payload']);
    if (failure && setup) cleanup.push(...await cleanArtifactPaths(root, [path.basename(setup)]));
    await writeFile(path.join(root, 'receipt.json'), JSON.stringify({
      passed: !failure, admission, compilerExited: compiler?.exited ?? null,
      failure: failure?.message ?? null, removed: cleanup,
      retained: ['captured source', 'generated script', 'logs and manifests', ...(!failure ? ['installer executable'] : [])],
      modelCalls: 0, workerVmStarts: 0, applicationRebuilt: false
    }, null, 2) + '\n');
  }
  if (failure) throw failure;
  console.log('Windows installer ready. The application gets a room; the private ledger keeps its own.');
  return root;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  assert.equal(process.argv.length, 5, 'Use windows-installer.mjs HOST_PACKAGE PINNED_NSIS_DIRECTORY FRESH-NAME');
  await buildInstaller(...process.argv.slice(2));
}
