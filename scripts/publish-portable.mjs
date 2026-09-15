import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { chmod, copyFile, mkdir, readFile, readdir, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { requireArtifactSpace,cleanBuildIntermediates } from './artifact-storage.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const [rid, name] = process.argv.slice(2);
const native = `${{ win32: 'win', darwin: 'osx', linux: 'linux' }[process.platform]}-${process.arch}`;
if (!['win-x64', 'linux-x64', 'osx-x64', 'osx-arm64'].includes(rid) || rid !== native || !/^[a-zA-Z0-9-]{1,60}$/.test(name ?? ''))
  throw new Error('Use publish-portable.mjs NATIVE-RID FRESH-NAME (win-x64, linux-x64, osx-x64 or osx-arm64).');
const root = path.join(repository, 'artifacts', `portable-${name}`);
const source = path.join(root, 'source');
const output = path.join(root, `thaddeus-${rid}`);
await mkdir(path.join(repository, 'artifacts'), { recursive: true });
await mkdir(root); // A previous package or proof is never overwritten.
await mkdir(source); await mkdir(output);
await requireArtifactSpace(root,2*1024**3,'Portable publication');
try {

function run(executable, args, cwd = source, capture = false) {
  const result = spawnSync(executable, args, { cwd, stdio: capture ? 'pipe' : 'inherit', encoding: 'utf8', shell: false });
  if (result.error || result.status !== 0) throw new Error(`${executable} failed (${result.status}): ${result.error?.message ?? result.stderr ?? ''}`);
  return result.stdout?.trim();
}
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const selected = run('git', ['-c', 'core.quotepath=false', 'ls-files', '--cached', '--others', '--exclude-standard', '-z', '--', 'src', 'web', 'fixtures', 'third-party', 'tools/Thaddeus.NoticeBundle', 'Directory.Build.props', 'global.json', 'scripts/publish-portable.mjs', 'scripts/artifact-storage.mjs', 'scripts/Start Thaddeus.command', 'scripts/launch-host.ps1', 'scripts/Start Thaddeus.cmd', 'docs/PORTABLE_PACKAGES.md', 'docs/MODEL_CONNECTIONS.md', 'docs/SEARCH_CONNECTIONS.md', 'docs/DESKTOP_REOPEN.md', 'docs/STUDY_BACKUPS.md', 'docs/THIRD_PARTY.md'], repository, true).split('\0').filter(Boolean);
const sources = [];
for (const relative of selected.sort()) {
  const original = path.join(repository, relative), target = path.join(source, relative);
  const bytes = await readFile(original);
  await mkdir(path.dirname(target), { recursive: true }); await writeFile(target, bytes);
  sources.push({ path: relative, sha256: digest(bytes) });
}
// Compile the exact captured web sources, not whatever happens to be in the live host's wwwroot.
if (process.platform === 'win32') {
  run(process.env.ComSpec ?? 'cmd.exe', ['/d', '/s', '/c', 'npm --prefix web ci']);
  run(process.env.ComSpec ?? 'cmd.exe', ['/d', '/s', '/c', 'npm --prefix web run build']);
} else {
  run('npm', ['--prefix', 'web', 'ci']); run('npm', ['--prefix', 'web', 'run', 'build']);
}
run('dotnet', ['restore', 'src/Thaddeus.Host/Thaddeus.Host.csproj', '--locked-mode']);
// Platform runtime restore can add RID entries; only the staging copy of the lockfiles may change.
run('dotnet', ['publish', 'src/Thaddeus.Host/Thaddeus.Host.csproj', '-c', 'Release', '-r', rid, '--self-contained', 'true', '-p:ContinuousIntegrationBuild=true', '--output', output, '--nologo']);
run('dotnet', ['restore', 'tools/Thaddeus.NoticeBundle', '--locked-mode']);
run('dotnet', ['run', '--project', 'tools/Thaddeus.NoticeBundle', '--no-restore', '--configuration', 'Release', '--', source, output, path.join(source, 'src/Thaddeus.Host/obj/project.assets.json')]);
await copyFile(path.join(source, 'docs/PORTABLE_PACKAGES.md'), path.join(output, 'README.md'));
for (const guide of ['MODEL_CONNECTIONS.md', 'SEARCH_CONNECTIONS.md', 'DESKTOP_REOPEN.md', 'STUDY_BACKUPS.md', 'THIRD_PARTY.md']) await copyFile(path.join(source, 'docs', guide), path.join(output, guide));
if (process.platform === 'win32') {
  for (const file of ['launch-host.ps1', 'Start Thaddeus.cmd']) await copyFile(path.join(source, 'scripts', file), path.join(output, file));
} else {
  const launcher = path.join(output, process.platform === 'darwin' ? 'Start Thaddeus.command' : 'start-thaddeus.sh');
  await copyFile(path.join(source, 'scripts/Start Thaddeus.command'), launcher);
  await chmod(launcher, 0o755); await chmod(path.join(output, 'Thaddeus.Host'), 0o755);
}
async function inventory(directory, prefix = '') {
  const result = [];
  for (const entry of (await readdir(directory, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name))) {
    const relative = prefix + entry.name, full = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error('Package sources must not contain links.');
    if (entry.isDirectory()) result.push(...await inventory(full, relative + '/'));
    else result.push({ path: relative, sha256: digest(await readFile(full)), size: (await stat(full)).size });
  }
  return result;
}
const application = JSON.parse(run(path.join(output, process.platform === 'win32' ? 'Thaddeus.Host.exe' : 'Thaddeus.Host'), ['--package-capabilities'], output, true));
if(application.formatVersion!==1||application.runtime!==rid||!Number.isInteger(application.studySchemaVersion)||application.guardedLaunchVersion!==1)throw new Error('The published application did not report supported compatibility metadata.');
const manifest = { schemaVersion: 1, kind: 'portable-development-package', runtime: rid, application, sourceHead: run('git', ['rev-parse', 'HEAD'], repository, true),
  checkoutDirty: Boolean(run('git', ['status', '--porcelain'], repository, true)), published: new Date().toISOString(),
  signedRelease: false, isolationQualified: false, sourceFiles: sources, files: await inventory(output),
  notices: { path: 'ThirdPartyNotices/Generated/bundle.json', sha256: digest(await readFile(path.join(output, 'ThirdPartyNotices/Generated/bundle.json'))) },
  resolvedLocks: await Promise.all(sources.filter(file => file.path.endsWith('/packages.lock.json')).map(async file => ({ path: file.path, sha256: digest(await readFile(path.join(source, file.path))) }))) };
await writeFile(path.join(output, 'package-manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
const archive = path.join(root, `thaddeus-${rid}.${process.platform === 'win32' ? 'zip' : 'tar.gz'}`);
if (process.platform === 'win32') {
  // The paths travel as environment data, not interpolated PowerShell source.
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', 'Compress-Archive -LiteralPath $env:THADDEUS_ARCHIVE_SOURCE -DestinationPath $env:THADDEUS_ARCHIVE_TARGET -CompressionLevel Optimal'],
    { cwd: root, stdio: 'inherit', env: { ...process.env, THADDEUS_ARCHIVE_SOURCE: output, THADDEUS_ARCHIVE_TARGET: archive } });
  if (result.error || result.status !== 0) throw new Error('Package archive creation failed.');
} else run('tar', ['-czf', archive, '-C', root, path.basename(output)]);
await writeFile(path.join(root, 'SHA256SUMS'), `${digest(await readFile(archive))}  ${path.basename(archive)}\n`);
await writeFile(path.join(root, 'published.json'), JSON.stringify({ archive, package: output, sourceHead: manifest.sourceHead, runtime: rid, signedRelease: false, verifiedOnTarget: false }, null, 2) + '\n');
console.log(`Portable ${rid} package created. The study travels; its private ledger stays home.`);
} finally {
  const removed=await cleanBuildIntermediates(source);
  await writeFile(path.join(root,'scratch-cleanup.json'),JSON.stringify({removed,retained:['captured source','package manifest','published package','archive']},null,2)+'\n');
}
