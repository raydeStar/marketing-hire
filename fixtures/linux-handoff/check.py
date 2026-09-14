"""Real packaged Linux hosts and owner APIs; no browser, worker or model endpoint.

Only the fresh container filesystem holds studies. Compact evidence is copied out
before its owned container is removed, on success or failure.
"""
import fcntl
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import select
import shutil
import signal
import subprocess
import time
import traceback
import urllib.error
import urllib.request

EVIDENCE = Path('/evidence')
ROOT = Path('/home/thaddeuscheck/handoff')
PACKAGE = ROOT / 'current application'
SELECTED = ROOT / "selected application ' $ fixture"
DATA = ROOT / 'study'
PROFILE = ROOT / 'launch.json'
ORIGIN = 'http://127.0.0.1:5179'
receipt = dict(checks=[], liveModelCalls=0, workerStarts=0, gpuDevices=0,
               executedOn='Linux x64 container', differentBuild=False,
               desktopVerified=False, browserAutomaticallyOpened=False)
owned = []
logs = []
csrf = ''
client = urllib.request.build_opener(urllib.request.ProxyHandler({}),
                                    urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def record(name, **evidence):
    receipt['checks'].append(dict(name=name, **evidence))
    print('HANDOFF_CHECK ' + json.dumps(receipt['checks'][-1]), flush=True)


def wait_for(check, label, seconds=30):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            result = check()
            if result:
                return result
        except (OSError, urllib.error.URLError, json.JSONDecodeError):
            pass
        time.sleep(.15)
    raise AssertionError('Timed out: ' + label)


def api(route, body=None, method=None, status=200, correct_csrf=True):
    payload = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(ORIGIN + '/api' + route, data=payload,
        method=method or ('POST' if body is not None else 'GET'),
        headers={'Origin': ORIGIN, 'Content-Type': 'application/json',
                 'X-CSRF': csrf if correct_csrf else 'invalid-fixture-csrf'})
    try:
        response = client.open(request, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = response.read()
        assert response.code == status, (route, response.code, raw.decode(errors='replace'))
        return json.loads(raw) if raw else None


def html_matches(package):
    with client.open(ORIGIN + '/?handoff-verification=1', timeout=2) as response:
        return response.status == 200 and response.read() == (package / 'wwwroot/index.html').read_bytes()


def product_export():
    # Maintenance intentionally refuses product routes while the host reopens.
    # urllib keeps those non-200 observations transient only in this readiness check.
    with client.open(ORIGIN + '/api/export', timeout=2) as response:
        return json.loads(response.read())


def claim(pid, package, profile, child=None):
    # A pidfd holds the actual process, so cleanup cannot follow a recycled PID.
    handle = os.pidfd_open(pid)
    try:
        command = (Path('/proc') / str(pid) / 'cmdline').read_bytes().split(b'\0')
        assert command[:5] == [str(package / 'Thaddeus.Host').encode(), b'--desktop', b'--no-browser',
                               b'--launch-profile', str(profile).encode()], command
        assert (Path('/proc') / str(pid) / 'exe').resolve() == package / 'Thaddeus.Host'
        assert package.is_relative_to(ROOT) and profile.is_relative_to(ROOT)
        process = dict(pid=pid, handle=handle, child=child, package=str(package), profile=str(profile))
        owned.append(process)
        return process
    except Exception:
        os.close(handle)
        raise


def exited(process):
    return bool(select.select([process['handle']], [], [], 0)[0])


def maintenance(mode):
    state = api('/maintenance')
    api('/maintenance/start', dict(version=state['version'], mode=mode))
    phase = 'verified' if mode == 'backup' else 'stopped'
    def completed():
        result = api('/maintenance')
        assert result['phase'] != 'failed', result
        return result if result['phase'] == phase else None
    return wait_for(completed, 'maintenance ' + phase)


def restore(backup):
    review = api('/maintenance/restore/review', dict(backupId=backup['version'], packageDirectory=str(SELECTED)))
    api('/maintenance/restore/start', dict(reviewId=review['review']['id']))
    def completed():
        result = api('/maintenance/restore')
        assert result['phase'] != 'failed', result
        return result if result['phase'] == 'restored' else None
    result = wait_for(completed, 'verified restored study')
    # Register every target before dispatch; these profiles also drive failure cleanup.
    receipt.setdefault('targets', []).extend([result['launcher'], result['returnLauncher']])
    return result


def open_study(restored, target, previous):
    launcher = restored['launcher' if target == 'restored' else 'returnLauncher']
    accepted = api('/maintenance/restore/open', dict(reviewId=restored['review']['id'], target=target, openBrowser=False))
    record_path = Path(launcher['directory']) / ('open-' + accepted['id'] + '.json')
    opened = wait_for(lambda: json.loads(record_path.read_text()) if record_path.exists() else None, 'open result')
    assert opened['result']['started'], opened
    selected = claim(opened['result']['processId'], Path(launcher['package']), Path(launcher['profile']))
    wait_for(lambda: exited(previous), 'previous host exit')
    assert not exited(selected), 'Selected host died with its parent'
    wait_for(lambda: html_matches(Path(launcher['package'])), 'selected app response')
    # Login is explicit here; desktop one-use links are a separate verification.
    global csrf
    launch = json.loads(Path(launcher['profile']).read_text())
    csrf = api('/auth/login', dict(key=(Path(launch['dataDirectory']) / 'host-key.txt').read_text().strip()))['csrf']
    record('Opened ' + target + ' study after previous host exited', result=opened, previousPid=previous['pid'])
    return selected


try:
    os.umask(0o077)
    assert not ROOT.exists(), 'Use a fresh container, never an existing study'
    storage = shutil.disk_usage(ROOT.parent)
    inputs = json.loads((EVIDENCE / 'manifest-input.json').read_text())
    peak = 2 * sum(file['size'] for file in inputs['files']) + 128 * 1024**2
    assert storage.free >= 10 * 1024**3 + peak, 'Insufficient Linux disk reserve before package copies'
    receipt['storage'] = dict(availableBytes=storage.free, additionalBytes=peak, reserveBytes=10 * 1024**3)
    ROOT.mkdir(mode=0o700)
    os.chown(ROOT, 1100, 1100)
    os.setgroups([])
    os.setgid(1100)
    os.setuid(1100)
    os.environ['HOME'] = str(ROOT)
    assert os.getuid() == 1100
    shutil.copytree('/package', PACKAGE)
    (PACKAGE / 'Thaddeus.Host').chmod(0o700)
    (PACKAGE / 'start-thaddeus.sh').chmod(0o700)
    capabilities = subprocess.run([str(PACKAGE / 'Thaddeus.Host'), '--package-capabilities'],
        cwd=PACKAGE, capture_output=True, text=True, check=True, timeout=10)
    inputs['application'] = json.loads(capabilities.stdout)
    assert inputs['application']['runtime'] == 'linux-x64' and inputs['application']['guardedLaunchVersion'] == 1
    for entry in inputs['files']:
        payload = PACKAGE / entry['path']
        assert payload.stat().st_size == entry['size']
        assert hashlib.sha256(payload.read_bytes()).hexdigest() == entry['sha256']
    manifest_bytes = (json.dumps(inputs, indent=2) + '\n').encode()
    (PACKAGE / 'package-manifest.json').write_bytes(manifest_bytes)
    (EVIDENCE / 'tested-package-manifest.json').write_bytes(manifest_bytes)
    shutil.copytree(PACKAGE, SELECTED)
    record('Native capabilities and both package inventories verified',
           manifestSha256=hashlib.sha256(manifest_bytes).hexdigest(), uid=os.getuid(), files=len(inputs['files']))
    PROFILE.write_text(json.dumps(dict(schemaVersion=1, dataDirectory=str(DATA), localOrigin=ORIGIN, workerPort=5183)))
    log = (ROOT / 'host.log').open('wb')
    logs.append(log)
    environment = dict(HOME=str(ROOT), PATH='/usr/bin:/bin', LANG='C.UTF-8', DOTNET_CLI_TELEMETRY_OPTOUT='1')
    host = subprocess.Popen([str(PACKAGE / 'Thaddeus.Host'), '--desktop', '--no-browser', '--launch-profile', str(PROFILE)],
        cwd=PACKAGE, env=environment, stdin=subprocess.DEVNULL, stdout=log, stderr=log)
    initial = claim(host.pid, PACKAGE, PROFILE, host)
    wait_for(lambda: html_matches(PACKAGE), 'initial host')
    csrf = api('/auth/login', dict(key=(DATA / 'host-key.txt').read_text().strip()))['csrf']
    before = api('/export')
    backup = maintenance('backup')
    api('/maintenance/finish', dict(version=backup['version'], mode='reopen'))
    wait_for(lambda: html_matches(PACKAGE), 'reopened product')
    wait_for(product_export, 'reopened export')
    api('/knowledge', dict(path='notes/newer.md', content='The original keeps this newer edit.', version='absent'), method='PUT')
    newer = api('/export')
    stopped = maintenance('stop')
    first = restore(backup)
    request = dict(reviewId=first['review']['id'], target='restored', openBrowser=False)
    api('/maintenance/restore/open', request, status=403, correct_csrf=False)
    api('/maintenance/restore/open', dict(request, reviewId='stale'), status=409)
    with (Path(first['receipt']['directory']) / 'launcher.lock').open('wb') as held:
        fcntl.flock(held.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        accepted = api('/maintenance/restore/open', request)
        result_path = Path(first['launcher']['directory']) / ('open-' + accepted['id'] + '.json')
        failure = wait_for(lambda: json.loads(result_path.read_text()) if result_path.exists() else None, 'failed launch receipt')
        assert not failure['result']['started'] and failure['result']['canReopenOriginal'], failure
        def recovered():
            state = api('/maintenance')
            return state if state['phase'] == 'failed' else None
        recovery = wait_for(recovered, 'same maintenance recovery')
        assert recovery['version'] == stopped['version']
        assert api('/maintenance/restore')['receipt'] == first['receipt']
        assert len(list(Path(str(DATA) + '-backups').glob('*.receipt.json'))) == 1
        assert not exited(initial)
    record('Linux flock failure recovered without another backup or study overwrite', failure=failure)
    original = open_study(first, 'original', initial)
    assert api('/export') == newer
    maintenance('stop')
    second = restore(backup)
    restored_host = open_study(second, 'restored', original)
    assert api('/export') == before
    assert (DATA / 'knowledge/notes/newer.md').read_text() == 'The original keeps this newer edit.'
    record('Both exports match their expected history; newer original edit preserved',
           originalSha256=hashlib.sha256(json.dumps(newer, sort_keys=True).encode()).hexdigest(),
           restoredSha256=hashlib.sha256(json.dumps(before, sort_keys=True).encode()).hexdigest())
    final = maintenance('stop')
    api('/maintenance/finish', dict(version=final['version'], mode='close'))
    wait_for(lambda: exited(restored_host), 'final owner-requested shutdown')
    record('Selected host closed through its owner API', pid=restored_host['pid'])
    receipt['passed'] = True
except Exception:
    receipt['error'] = traceback.format_exc()
    receipt['passed'] = False
    print(receipt['error'], flush=True)
finally:
    cleanup = dict(confirmed=False, processes=[])
    try:
        # Also recover a target whose host died before its result could be read.
        for target in receipt.get('targets', []):
            for directory in Path('/proc').iterdir():
                if not directory.name.isdigit() or int(directory.name) in [item['pid'] for item in owned]:
                    continue
                try:
                    command = (directory / 'cmdline').read_bytes().split(b'\0')
                except (FileNotFoundError, ProcessLookupError):
                    continue
                expected = [str(Path(target['package']) / 'Thaddeus.Host').encode(), b'--desktop', b'--no-browser',
                            b'--launch-profile', target['profile'].encode()]
                if command[:5] == expected:
                    claim(int(directory.name), Path(target['package']), Path(target['profile']))
        for process in reversed(owned):
            forced = not exited(process)
            if forced:
                signal.pidfd_send_signal(process['handle'], signal.SIGTERM)
                if not select.select([process['handle']], [], [], 10)[0]:
                    signal.pidfd_send_signal(process['handle'], signal.SIGKILL)
            assert select.select([process['handle']], [], [], 10)[0], 'Owned host remains alive'
            if process['child']:
                process['child'].wait(timeout=1)
            else:
                try:
                    os.waitpid(process['pid'], os.WNOHANG)
                except ChildProcessError:
                    pass
            cleanup['processes'].append(dict(pid=process['pid'], exited=True, cleanupSignalNeeded=forced))
            os.close(process['handle'])
        cleanup['confirmed'] = True
    except Exception:
        cleanup['error'] = traceback.format_exc()
        receipt['passed'] = False
    for log in logs:
        log.close()
    receipt['cleanup'] = cleanup
    # No private key or study database leaves this fictional fixture.
    for log in ROOT.glob('*.log'):
        shutil.copyfile(log, EVIDENCE / log.name)
    (EVIDENCE / 'native.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print('HANDOFF_VERIFIED ' + json.dumps(receipt), flush=True)
raise SystemExit(0 if receipt.get('passed') else 1)
