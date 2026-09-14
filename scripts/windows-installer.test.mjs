import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, readFile, writeFile, unlink, rmdir, symlink} from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {readPackage, relativeFile, renderInstaller, nsisLiteral} from './windows-installer.mjs';
import {cleanArtifactPaths} from './artifact-storage.mjs';

const artifacts = path.resolve('artifacts');
const hash = data => createHash('sha256').update(data).digest('hex');
async function fixture(body) {
  const root = await mkdtemp(path.join(artifacts, 'installer-unit-'));
  const directory = path.join(root, 'package');
  await mkdir(directory);
  const content = {'Thaddeus.Host.exe': 'fixture only', 'Start Thaddeus.cmd': 'fixture',
    'launch-host.ps1': 'fixture', 'wwwroot/index.html': '<html>fixture</html>',
    'ThirdPartyNotices/Generated/bundle.json': '{"fixture":true}'};
  const files = [];
  for (const [name, text] of Object.entries(content)) {
    await mkdir(path.dirname(path.join(directory, name)), {recursive: true});
    await writeFile(path.join(directory, name), text);
    files.push({path: name, size: Buffer.byteLength(text), sha256: hash(text)});
  }
  const manifest = {schemaVersion: 1, kind: 'portable-development-package', runtime: 'win-x64',
    application: {runtime: 'win-x64', guardedLaunchVersion: 1}, signedRelease: false,
    isolationQualified: false, files, notices: {path: files[4].path, sha256: files[4].sha256}};
  const save = () => writeFile(path.join(directory, 'package-manifest.json'), JSON.stringify(manifest));
  await save();
  try { return await body({root, directory, manifest, save}); }
  finally { await cleanArtifactPaths(root, ['package']); await rmdir(root); }
}

test('the installer consumes an exact host graph and keeps its app subfolder intact', async () => {
  await fixture(async ({directory}) => {
    const input = await readPackage(directory);
    assert.equal(input.files.length, 6);
    const result = renderInstaller(await readFile('packaging/windows/installer.nsi', 'utf8'),
      await readFile('packaging/windows/guards.nsh', 'utf8'),
      {...input, snapshot: 'C:\\captured', output: 'C:\\output\\setup.exe', installerNotice: 'C:\\captured\\COPYING'});
    assert.equal(result.payloadFiles.length, 7);
    assert.ok(result.payloadFiles.includes('app/package-manifest.json'));
    assert.match(result.script, /RequestExecutionLevel user/);
    assert.doesNotMatch(result.script, /RMDir\s+\/r|Delete\s+"\$INSTDIR\\\*|SetRegView 32/i);
    assert.match(result.script, /WriteRegStr HKCU/);
    assert.match(result.script, /Function un\.AssertAvailable/);
  });
});

test('additional files and modified payload bytes fail before publication', async () => {
  await fixture(async ({directory}) => {
    const unexpected = path.join(directory, 'secret.txt');
    await writeFile(unexpected, 'not in the manifest');
    await assert.rejects(readPackage(directory), /additional files/);
    await unlink(unexpected);
    await writeFile(path.join(directory, 'Thaddeus.Host.exe'), 'changed binary');
    await assert.rejects(readPackage(directory));
  });
});

test('private configuration, worker inputs and case collisions are refused even if declared', async () => {
  for (const filename of ['launch.json', 'host-key.txt', '.data/study.db', 'worker/root.ext4', 'THADDEUS.HOST.EXE']) {
    await fixture(async ({directory, manifest, save}) => {
      manifest.files.push({...manifest.files[0], path: filename});
      await save();
      await assert.rejects(readPackage(directory), /private launch|Duplicate Windows/);
    });
  }
});

test('the generated host notice reference must bind its actual bytes', async () => {
  await fixture(async ({directory, manifest, save}) => {
    manifest.notices.sha256 = '0'.repeat(64);
    await save();
    await assert.rejects(readPackage(directory));
  });
});

test('a worker package or a changed target cannot be silently treated as the host installer', async () => {
  for (const mutate of [
    m => { m.kind = 'combined-worker-package'; },
    m => { m.runtime = 'linux-x64'; },
    m => { m.bundledWorker = {}; },
    m => { m.application.guardedLaunchVersion = 0; }
  ]) await fixture(async ({directory, manifest, save}) => {
    mutate(manifest); await save(); await assert.rejects(readPackage(directory));
  });
});

test('Windows traversal, device and alternate-stream names are refused', () => {
  for (const filename of ['../outside', '/absolute', 'C:/absolute', 'a\\b', 'name:stream', 'NUL.txt', 'part./file', 'part /file'])
    assert.throws(() => relativeFile(filename), /package path/);
  assert.equal(relativeFile('a/name with spaces.txt'), 'a/name with spaces.txt');
});

test('literal dollars and macro-looking paths do not become installer instructions', () => {
  assert.equal(nsisLiteral('$INSTDIR'), '$$INSTDIR');
  assert.equal(nsisLiteral('a"b'), 'a$\\"b');
  assert.throws(() => nsisLiteral('a\n!system injected'));
  const result = renderInstaller('@INSTALL_FILES@\n@BUILD_ID@', '', {files: [{path: '@BUILD_ID@-$name.txt', size: 1}],
    snapshot: 'C:\\source', output: 'C:\\setup.exe', manifestSha256: 'a'.repeat(64), installerNotice: 'C:\\COPYING'});
  assert.ok(result.script.includes('@BUILD_ID@-$$name.txt'));
  assert.ok(result.script.endsWith('a'.repeat(16)));
});

test('an actual filesystem link cannot serve as the verified package root', async () => {
  await fixture(async ({root, directory}) => {
    const linked = path.join(root, 'linked');
    await symlink(directory, linked, process.platform === 'win32' ? 'junction' : 'dir');
    try { await assert.rejects(readPackage(linked), /Linked compiler\/package/); }
    finally { if (process.platform === 'win32') await rmdir(linked); else await unlink(linked); }
  });
});
