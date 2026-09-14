import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, rmdir, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { cleanArtifactPaths } from './artifact-storage.mjs';

test('rejected package inventory removes extraction and preserves original publication', async () => {
  const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const artifacts = path.join(repository, 'artifacts'); await mkdir(artifacts, { recursive: true });
  const root = await mkdtemp(path.join(artifacts, 'portable-cleanup-contract-'));
  const rid = `${{ win32: 'win', darwin: 'osx', linux: 'linux' }[process.platform]}-${process.arch}`;
  const source = path.join(root, `thaddeus-${rid}`), evidence = path.join(root, 'check');
  const archive = path.join(root, process.platform === 'win32' ? 'fixture.zip' : 'fixture.tar.gz');
  const hash = value => createHash('sha256').update(value).digest('hex');
  try {
    await mkdir(source);
    await writeFile(path.join(source, 'sentinel.txt'), 'Preserve this original');
    await writeFile(path.join(source, 'package-manifest.json'), JSON.stringify({ runtime: rid, sourceHead: 'fixture',
      files: [{ path: 'sentinel.txt', size: 22, sha256: '0'.repeat(64) }] }));
    const pack = process.platform === 'win32'
      ? spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
        'Compress-Archive -LiteralPath $env:THADDEUS_FIXTURE_SOURCE -DestinationPath $env:THADDEUS_FIXTURE_ARCHIVE'],
      { windowsHide: true, encoding: 'utf8', env: { ...process.env, THADDEUS_FIXTURE_SOURCE: source, THADDEUS_FIXTURE_ARCHIVE: archive } })
      : spawnSync('tar', ['-czf', archive, '-C', root, path.basename(source)], { encoding: 'utf8' });
    assert.equal(pack.status, 0, pack.stderr);
    const checksum = hash(await readFile(archive));
    await writeFile(path.join(root, 'SHA256SUMS'), `${checksum}  ${path.basename(archive)}\n`);
    await writeFile(path.join(root, 'published.json'), JSON.stringify({ runtime: rid, sourceHead: 'fixture', package: source, archive }));
    const check = spawnSync(process.execPath, ['scripts/portable-check.mjs', root, evidence],
      { cwd: repository, windowsHide: true, encoding: 'utf8', timeout: 30_000 });
    assert.equal(check.error, undefined); assert.equal(check.status, 1);
    const receipt = JSON.parse(await readFile(path.join(evidence, 'verified.json'), 'utf8'));
    assert.equal(receipt.passed, false); assert.match(receipt.error, /sentinel.txt/);
    assert.equal(receipt.cleanup.passed, true); assert.deepEqual(receipt.cleanup.removed, ['extracted']);
    assert.equal(receipt.cleanup.ownedProcessesRemaining, 0);
    await assert.rejects(stat(path.join(evidence, 'extracted')), { code: 'ENOENT' });
    assert.equal(hash(await readFile(archive)), checksum);
    assert.equal(await readFile(path.join(source, 'sentinel.txt'), 'utf8'), 'Preserve this original');
  } finally {
    // This fixture never starts a host. Even the butler's miniature suitcase goes back on the shelf.
    await cleanArtifactPaths(root, [path.basename(source), 'check', path.basename(archive), 'SHA256SUMS', 'published.json']);
    await rmdir(root);
  }
});
