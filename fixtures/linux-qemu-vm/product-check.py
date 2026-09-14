"""Native packaged application check. All task decisions use its normal authenticated API.

The loopback model endpoint sends six explicitly synthetic tool replies. It has
no upstream connection, and the diagnostic VM has no network interface.
"""
import hashlib
import http.cookiejar
import http.server
import json
import os
from pathlib import Path
import signal
import subprocess
import threading
import time
import traceback
import urllib.error
import urllib.request

ROOT = Path('/home/thaddeuscheck/product-check')
PACKAGE = Path('/opt/probe/tools/package')
HOST = PACKAGE / 'Thaddeus.Host'
ORIGIN = 'http://127.0.0.1:5179'
SOURCE = '# Workshop\nA fictional workshop lasts 45 minutes. Its audience has not been selected.\n'
QUOTE = SOURCE.splitlines()[1]
ROOT.mkdir(mode=0o700)
DATA = ROOT / 'study'
PROFILE = ROOT / 'launch.json'
PROFILE.write_text(json.dumps(dict(schemaVersion=1, dataDirectory=str(DATA), localOrigin=ORIGIN,
                                  workerPort=5183, developmentWorkerInstallation='/opt/probe/tools/installation.json')))
receipt = dict(checks=[], liveModelCalls=0, syntheticReplies=0, gpuDevices=0, githubActionsStarted=0,
               packageBuiltOn='Windows x64', executedOn='Linux x64', publicNetwork=False)
host = None
logs = []
csrf = ''
client = None
model_errors = []


def record(name, **evidence):
    receipt['checks'].append(dict(name=name, **evidence))
    print('PRODUCT_CHECK ' + json.dumps(receipt['checks'][-1]), flush=True)


def api(route, method='GET', body=None, status=200):
    data = None if body is None else json.dumps(body).encode()
    headers = {'Origin': ORIGIN, 'Content-Type': 'application/json', 'X-CSRF': csrf}
    request = urllib.request.Request(ORIGIN + route, data=data, method=method, headers=headers)
    try:
        response = client.open(request, timeout=90)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = response.read()
        assert response.code == status, (route, response.code, raw.decode(errors='replace'))
        return json.loads(raw) if raw else None


def start_host():
    global host, csrf, client
    log = (ROOT / ('host-%d.log' % len(logs))).open('wb')
    logs.append(log)
    environment = {key: os.environ[key] for key in ('HOME', 'XDG_RUNTIME_DIR', 'DBUS_SESSION_BUS_ADDRESS') if key in os.environ}
    environment.update(PATH='/usr/bin:/bin', LANG='C.UTF-8', DOTNET_CLI_TELEMETRY_OPTOUT='1')
    host = subprocess.Popen([str(HOST), '--desktop', '--no-browser', '--launch-profile', str(PROFILE)],
                            cwd=PACKAGE, env=environment, stdin=subprocess.DEVNULL, stdout=log, stderr=log)
    client = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    for attempt in range(100):
        assert host.poll() is None, ('Host exited before startup', log.name)
        try:
            with client.open(ORIGIN, timeout=1) as response:
                assert response.status == 200 and b'<html' in response.read()
            break
        except (OSError, urllib.error.URLError):
            time.sleep(.2)
    else:
        raise AssertionError('Packaged study did not start')
    csrf = api('/api/auth/login', 'POST', dict(key=(DATA / 'host-key.txt').read_text().strip()))['csrf']
    return host.pid


def stop_host():
    global host
    if host is not None and host.poll() is None:
        host.send_signal(signal.SIGINT)
        host.wait(timeout=30)
        assert host.returncode == 0, ('Host shutdown', host.returncode)


def worker_processes():
    found = []
    for directory in Path('/proc').iterdir():
        if not directory.name.isdigit():
            continue
        try:
            args = (directory / 'cmdline').read_bytes().split(b'\0')
            if b'/opt/probe/bin/runtime/bin/qemu-system-x86_64' not in args:
                continue
            group = (directory / 'cgroup').read_text().strip().removeprefix('0::')
            control = Path('/sys/fs/cgroup' + group)
            assert '/thaddeus-worker-' in group and group.endswith('.service')
            assert args[args.index(b'-nic') + 1] == b'none'
            resource = {name: (control / name).read_text().strip() for name in ('memory.max', 'memory.swap.max', 'cpu.max', 'pids.max')}
            found.append(dict(pid=int(directory.name), uid=directory.stat().st_uid, cgroup=group, resources=resource))
        except FileNotFoundError:
            continue
    return found


def wait_phase(run_id, phase, seconds=180):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        assert host.poll() is None, 'Host stopped during task'
        assert not model_errors, model_errors
        run = api('/api/runs/' + run_id)
        if run['research']['phase'] == phase:
            return run
        assert run['state'] not in ('failed', 'needsAttention', 'cancelled'), run
        time.sleep(.5)
    raise AssertionError(('Task did not reach ' + phase, run))


class SyntheticModel(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_POST(self):
        try:
            assert self.path == '/v1/chat/completions'
            self.connection.settimeout(15)
            if self.headers.get('Transfer-Encoding', '').lower() == 'chunked':
                chunks = bytearray()
                while True:
                    line = self.rfile.readline(128)
                    assert line.endswith(b'\r\n')
                    size = int(line.strip().split(b';', 1)[0], 16)
                    assert 0 <= size <= 150000 - len(chunks)
                    if size == 0:
                        assert self.rfile.readline(128) == b'\r\n'  # This .NET fixture sends no trailers.
                        break
                    part = self.rfile.read(size)
                    assert len(part) == size and self.rfile.read(2) == b'\r\n'
                    chunks.extend(part)
                body = json.loads(chunks)
            else:
                length = int(self.headers['Content-Length'])
                assert 0 < length <= 150000
                body = json.loads(self.rfile.read(length))
            current = api('/api/state')['runs'][0]
            stage = current['modelCalls']
            assert body['model'] == 'gpt-5.6-luna' and body['reasoning_effort'] == 'high'
            assert stage == receipt['syntheticReplies'] + 1 and 1 <= stage <= 6
            (ROOT / ('synthetic-request-%d.json' % stage)).write_text(json.dumps(body))
            if stage == 1:
                workers = worker_processes()
                assert len(workers) == 1 and workers[0]['uid'] == 1100, workers
                assert workers[0]['resources'] == {'memory.max': str(5 * 1024**3), 'memory.swap.max': '0',
                                                    'cpu.max': '200000 100000', 'pids.max': '128'}, workers
                record('packaged-supervisor-starts-constrained-native-worker', workers=workers)
            suffix = ['thaddeus_read_note', 'thaddeus_ask_user', 'write', 'thaddeus_propose_import', 'write', 'thaddeus_propose_import'][stage - 1]
            names = [tool['function']['name'] for tool in body['tools']]
            matches = [name for name in names if name == suffix or name.endswith('_' + suffix)]
            assert len(matches) == 1, (suffix, names)
            quote = 'Deliberately incorrect quotation for the correction fixture.' if stage <= 4 else QUOTE
            summary = '# Workshop summary\nAudience: Developers\nDuration: 45 minutes\nSource: notes/source.md\n> ' + quote + '\n'
            if suffix == 'thaddeus_read_note':
                arguments = dict(operationId='native-read', path='notes/source.md')
            elif suffix == 'thaddeus_ask_user':
                arguments = dict(operationId='native-question', question='Which audience should the workshop address?', choices=['Developers', 'Beginners'])
            elif suffix == 'write':
                arguments = dict(path='/home/agent/thaddeus-artifacts/summary.md', content=summary)
            else:
                evidence = [item for item in current['evidence'] if item['path'] == 'notes/source.md'][0]
                arguments = dict(operationId='native-import-repair' if stage > 4 else 'native-import', path='plans/summary.md', artifact='summary.md',
                                 citations=[dict(source=evidence['path'], version=evidence['hash'], quote=quote)])
            reply = dict(id='synthetic-packaged-%d' % stage, model=body['model'], created=int(time.time()),
                         choices=[dict(index=0, message=dict(role='assistant', content=None,
                            tool_calls=[dict(id='call-packaged-%d' % stage, type='function', function=dict(name=matches[0], arguments=json.dumps(arguments)))]),
                            finish_reason='tool_calls')], usage=dict(prompt_tokens=100, completion_tokens=30))
            data = json.dumps(reply).encode()
            receipt['syntheticReplies'] += 1
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        except Exception:
            model_errors.append(traceback.format_exc())
            self.send_error(500, 'Synthetic fixture rejected unexpected dispatch')


server = http.server.ThreadingHTTPServer(('127.0.0.1', 5181), SyntheticModel)
threading.Thread(target=server.serve_forever, daemon=True).start()
try:
    for arguments in (['--linux-supervise'], ['--linux-supervise', str(ROOT / 'missing.json'), '0' * 64]):
        refused = subprocess.run([str(HOST), *arguments], cwd=ROOT, capture_output=True, timeout=10)
        assert refused.returncode == 125 and b'The Linux steward refused admission' in refused.stderr
        assert not DATA.exists() and not (ROOT / 'host-key.txt').exists()
    record('service-entry-refuses-malformed-requests-before-opening-study')
    first_pid = start_host()
    requirements = api('/api/settings/worker/requirements', 'POST', {})
    assert requirements['passed'] and requirements['backend'] == 'qemu-kvm', requirements
    assert {check['id'] for check in requirements['checks']} == {'platform', 'application', 'virtualization', 'services', 'resources'}
    assert not worker_processes() and receipt['syntheticReplies'] == 0
    record('native-linux-prerequisites-without-vm-or-model', requirements=requirements)
    # Retain the outer fixture's storage observation before package admission; no worker or model is started.
    prepared = json.loads(Path('/opt/probe/tools/installation.json').read_text())['installation']['baseDisk']['path']
    mounts = Path('/proc/self/mountinfo').read_text().splitlines()
    receipt['workerBaseStorage'] = dict(path=prepared, exists=Path(prepared).is_file(), readable=os.access(prepared, os.R_OK),
        mounts=[line for line in mounts if '/opt/probe/' in line], commandLine=Path('/proc/cmdline').read_text().strip())
    if prepared.startswith('/opt/probe/worker-image/'):
        virtual = subprocess.run(['systemd-detect-virt'], capture_output=True, text=True, timeout=5)
        receipt['workerBaseStorage']['virtualization'] = virtual.stdout.strip()
        assert virtual.returncode == 0 and virtual.stdout.strip() in ('kvm', 'qemu'), receipt['workerBaseStorage']
        assert not any(Path(name).exists() for name in ('/.dockerenv', '/run/.containerenv', '/run/systemd/container'))
        mount = [line.split() for line in mounts if line.split()[4] == '/opt/probe/worker-image']
        assert len(mount) == 1 and 'ro' in mount[0][5].split(',') and 'ro' in mount[0][-1].split(','), receipt['workerBaseStorage']
        assert Path(prepared).is_file() and os.access(prepared, os.R_OK), receipt['workerBaseStorage']
    setup = api('/api/settings/worker')
    assert setup['worker']['backend'] == 'qemu-kvm' and not setup['enabled'], setup
    api('/api/settings/worker', 'POST', dict(installationDigest=setup['worker']['installationDigest'], enabled=True), status=409)
    checked = api('/api/settings/worker/check', 'POST', {})
    assert checked['canEnable'] and checked['lastCheck']['passed'] and not checked['enabled'], checked
    assert not worker_processes() and receipt['syntheticReplies'] == 0
    api('/api/settings/worker', 'POST', dict(installationDigest='0' * 64, enabled=True), status=409)
    enabled = api('/api/settings/worker', 'POST', dict(installationDigest=checked['worker']['installationDigest'], enabled=True))
    assert enabled['enabled']
    record('native-installation-check-and-exact-owner-enrollment', setup=enabled)
    api('/api/knowledge', 'PUT', dict(path='notes/source.md', content=SOURCE, version='absent'))
    api('/api/settings/provider', 'PUT', dict(kind='compatible', model='gpt-5.6-luna', reasoning='high', endpoint='http://127.0.0.1:5181/v1'))
    run = api('/api/chat', 'POST', dict(mode='research', content='Read notes/source.md. Ask which audience to use, then write summary.md and propose its import to plans/summary.md with a captured quotation. Fictional integration task.',
              readScope=['notes/source.md'], budget=dict(modelCalls=6, toolCalls=8, maxOutputTokens=4096, seconds=360, repairs=1, maxTotalTokens=64000)))
    run_id = run['id']
    paused = wait_phase(run_id, 'awaiting-input')
    assert paused['state'] == 'awaitingInput' and paused['question']['answer'] is None and paused['modelCalls'] == 2
    assert not worker_processes(), 'Saved question requires a stopped worker'
    stop_host()
    second_pid = start_host()
    restored = api('/api/runs/' + run_id)
    assert first_pid != second_pid and api('/api/settings/worker')['enabled']
    for key in ('question', 'preparedContext', 'modelDispatches', 'executionCommands', 'chargedTokens'):
        assert restored[key] == paused[key], key
    assert receipt['syntheticReplies'] == 2 and not worker_processes()
    record('host-restart-preserves-question-context-and-usage-without-replay', oldPid=first_pid, newPid=second_pid, modelCalls=2)
    api('/api/runs/' + run_id + '/answer', 'POST', dict(questionId=restored['question']['id'], answer='Developers'))
    review = wait_phase(run_id, 'awaiting-approval')
    assert review['modelCalls'] == 6 and review['repairs'] == 1
    assert [entry['status'] for entry in review['nativeProposals']] == ['repair-requested', 'passed']
    assert [entry['status'] for entry in review['artifactImports']] == ['repair-dispatched', 'ready-for-approval']
    assert review['artifactImports'][0]['approvalId'] is None
    assert review['research']['review']['sha256'] == review['artifactImports'][1]['sha256']
    assert not (DATA / 'knowledge/plans/summary.md').exists() and not worker_processes()
    (ROOT / 'before-approval.json').write_text(json.dumps(review))
    api('/api/runs/' + run_id + '/approve', 'POST', dict(approvalId=review['approval']['id'], digest='0' * 64, allow=True), status=409)
    assert not (DATA / 'knowledge/plans/summary.md').exists()
    api('/api/runs/' + run_id + '/approve', 'POST', dict(approvalId=review['approval']['id'], digest=review['approval']['digest'], allow=True))
    final = wait_phase(run_id, 'finished', seconds=60)
    exported = api('/api/export')
    page = [entry for entry in exported['pages'] if entry['path'] == 'plans/summary.md'][0]
    assert final['state'] == 'succeeded' and final['research']['workerRetained']
    assert any(criterion['status'] == 'unverified' for criterion in final['goal']['criteria'])
    assert QUOTE in page['content'] and hashlib.sha256(page['content'].encode()).hexdigest() == review['research']['review']['sha256']
    assert all(command['status'] == 'acknowledged' for command in final['executionCommands'])
    assert all(not call['isError'] for call in final['capabilities'])
    assert final['chargedTokens'] == 780 and receipt['syntheticReplies'] == 6
    readiness = [dict(path=str(file.relative_to(DATA)), receipt=json.loads(file.read_text()))
                 for file in DATA.glob('qemu-thaddeus-*/boot-*/command-result-*.json')]
    boots = {str(Path(entry['path']).parent) for entry in readiness}
    assert len(boots) == 3 and 3 <= len(readiness) <= 90
    for boot in boots:
        probes = sorted([entry['receipt'] for entry in readiness if str(Path(entry['path']).parent) == boot], key=lambda item: item['attempt'])
        assert [probe['attempt'] for probe in probes] == list(range(1, len(probes) + 1))
        assert all(probe['operation'] == 'gateway-health' for probe in probes) and probes[-1]['exitCode'] == 0
    (ROOT / 'gateway-health-observations.json').write_text(json.dumps(readiness))
    record('native-correction-exact-approved-import-and-synthetic-usage', syntheticTokens=780, syntheticReplies=6,
           importedSha256=review['research']['review']['sha256'], observedGatewayBoots=len(boots), observedReadinessReplies=len(readiness))
    storage = api('/api/runs/' + run_id + '/workspace/inspect', 'POST', {})
    assert storage['canRemove'] and storage['backend'] == 'qemu', storage
    api('/api/runs/' + run_id + '/workspace/remove', 'POST', dict(digest=storage['digest'], confirmation='REMOVE WORKSPACE'))
    after = api('/api/export')
    assert not after['runs'][0]['research']['workerRetained'] and not (DATA / ('qemu-' + final['execution']['sandboxId'])).exists()
    assert after['pages'] == exported['pages'] and not worker_processes()
    (ROOT / 'export.json').write_text(json.dumps(after))
    record('reviewed-workspace-removal-preserves-import-and-history')
    stop_host()
    receipt['passed'] = True
except Exception:
    receipt['error'] = traceback.format_exc()
    receipt['modelErrors'] = model_errors
    receipt['recoveryDiagnostics'] = []
    diagnostics = (list(DATA.glob('qemu-thaddeus-*/recovery-*.json')) + list(DATA.glob('qemu-thaddeus-*/boot-failure-*.json'))
                   + list(DATA.glob('qemu-thaddeus-*/boot-*/transport-failure.json'))
                   + list(DATA.glob('qemu-thaddeus-*/boot-*/gateway-startup-failure.json'))
                   + list(DATA.glob('qemu-thaddeus-*/boot-*/command-result-*.json')))
    for diagnostic in diagnostics:
        try:
            if diagnostic.stat().st_size <= 131072:
                receipt['recoveryDiagnostics'].append(dict(path=str(diagnostic.relative_to(DATA)), receipt=json.loads(diagnostic.read_text())))
        except (OSError, ValueError):
            pass
    print(receipt['error'], flush=True)
finally:
    if host is not None and host.poll() is None:
        try:
            stop_host()
        except Exception:
            host.kill()
            host.wait(timeout=10)
    for log in logs:
        log.close()
    server.shutdown()
    (ROOT / 'verified.json').write_text(json.dumps(receipt, indent=2))
    print('PRODUCT_VERIFIED ' + json.dumps(receipt), flush=True)
raise SystemExit(0 if receipt.get('passed') else 1)
