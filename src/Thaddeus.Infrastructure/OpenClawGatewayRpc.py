import ctypes, fcntl, hashlib, json, os, re, secrets, socket, stat, subprocess, sys, time
request = json.load(sys.stdin)
root = '/home/agent/.openclaw'
allowed = {'agent', 'agent.wait', 'chat.send', 'sessions.send', 'sessions.abort'}
assert request['method'] in allowed, 'Unsupported gateway method'
version = subprocess.run(['openclaw', '--version'], capture_output=True, text=True, timeout=30, check=True)
assert request['version'] in version.stdout.split(), 'OpenClaw version mismatch'
for path in ['/home', '/home/agent', root]:
    assert stat.S_ISDIR(os.lstat(path).st_mode), 'Linked state directory'
def read(name, limit=100000):
    fd = os.open(root + '/' + name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        info = os.fstat(stream.fileno())
        assert stat.S_ISREG(info.st_mode) and info.st_size <= limit and info.st_mode & 0o077 == 0
        data = stream.read(limit + 1)
        assert len(data) <= limit
        return data
def write(name, value, exclusive=False):
    flags = os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW | (os.O_EXCL if exclusive else os.O_TRUNC)
    fd = os.open(root + '/' + name, flags, 0o600)
    with os.fdopen(fd, 'w') as stream:
        assert stat.S_ISREG(os.fstat(stream.fileno()).st_mode)
        stream.write(value); stream.flush(); os.fsync(stream.fileno())
def invoke():
    script = request['controller']
    assert isinstance(script, str) and len(script.encode()) <= 50000
    digest = hashlib.sha256(script.encode()).hexdigest()
    with open('/proc/sys/kernel/random/boot_id') as stream:
        boot = stream.read().strip()
    assert re.fullmatch(r'[a-f0-9-]{36}', boot)
    lock = os.open(root + '/thaddeus-control.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    child = None
    try:
        assert stat.S_ISREG(os.fstat(lock).st_mode)
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            lease = json.loads(read('thaddeus-control-lease.json'))
        except FileNotFoundError:
            lease = None
        if lease is None or lease['bootId'] != boot:
            assert request['method'] in {'agent', 'sessions.send'}, 'Start or resume must establish the controller'
            if lease is not None:
                assert re.fullmatch(r'[a-f0-9]{32}', lease['nonce'])
                old_socket = root + '/thaddeus-control-' + lease['nonce'] + '.sock'
                try:
                    assert stat.S_ISSOCK(os.lstat(old_socket).st_mode)
                    os.unlink(old_socket)
                except FileNotFoundError:
                    pass
            lease = {'bootId': boot, 'nonce': secrets.token_hex(16), 'programHash': digest}
            # Claim before launch. Uncertain launch or a lost socket must never create a second caller.
            write('thaddeus-control-lease.json', json.dumps(lease), exclusive=not os.path.exists(root + '/thaddeus-control-lease.json'))
            log_fd = os.open(root + '/thaddeus-control-console.log', os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
            with os.fdopen(log_fd, 'wb', buffering=0) as log:
                assert stat.S_ISREG(os.fstat(log.fileno()).st_mode)
                child = subprocess.Popen(['node', '--input-type=module', '-'], stdin=subprocess.PIPE, stdout=log,
                    stderr=subprocess.STDOUT, start_new_session=True)
                child.stdin.write(script.encode()); child.stdin.close()
        assert lease['programHash'] == digest and re.fullmatch(r'[a-f0-9]{32}', lease['nonce']), 'Controller identity changed'
        address = root + '/thaddeus-control-' + lease['nonce'] + '.sock'
        if child is not None:
            deadline = time.monotonic() + 12
            while not os.path.exists(address):
                assert child.poll() is None, 'Controller did not become ready'
                assert time.monotonic() < deadline, 'Controller startup deadline exceeded'
                time.sleep(0.05)
        info = os.lstat(address)
        assert stat.S_ISSOCK(info.st_mode) and info.st_mode & 0o077 == 0, 'Invalid controller socket'
    finally:
        os.close(lock)
    envelope = json.dumps({'nonce': lease['nonce'], 'version': request['version'],
        'method': request['method'], 'parameters': request['parameters']}).encode() + b'\n'
    assert len(envelope) <= 100000
    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as client:
        client.settimeout(35); client.connect(address); client.sendall(envelope)
        output = b''
        while not output.endswith(b'\n'):
            part = client.recv(8192)
            assert part, 'Controller closed without a receipt'
            output += part
            assert len(output) <= 110000, 'Controller response exceeds limit'
    result = json.loads(output)
    assert result['ok'] is True, result.get('error', 'Gateway request failed')
    report = result['report']
    assert isinstance(report, dict)
    report['thaddeusGatewayConnectionId'] = result['connectionId']
    report['thaddeusGatewayScopes'] = result['scopes']
    if request['method'] == 'sessions.abort' and report.get('ok') is True:
        directory = os.open('/home/agent', os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            library = ctypes.CDLL(None, use_errno=True)
            if library.syncfs(directory) != 0:
                raise OSError(ctypes.get_errno(), 'Worker filesystem checkpoint failed')
        finally:
            os.close(directory)
        report['thaddeusFilesystemCheckpoint'] = 'syncfs'
    return report
try:
    print(json.dumps(invoke()))
except Exception as error:
    diagnostic = {'method': request['method'], 'error': str(error)[:4000]}
    try:
        diagnostic['controller'] = json.loads(read('thaddeus-control-error.json'))
    except Exception:
        pass
    write('thaddeus-rpc-last-error.json', json.dumps(diagnostic))
    print('Gateway request was not confirmed; the controller was not recreated.', file=sys.stderr)
    sys.exit(1)
