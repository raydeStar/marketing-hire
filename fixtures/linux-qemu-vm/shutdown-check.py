"""No-model guest shutdown probe. Direct transport is fixture-only, not product admission."""
import hashlib
import json
import os
import pathlib
import socket
import struct
import subprocess
import sys
import threading
import time
import uuid

ROOT = pathlib.Path('/output')
PREFIX = ['/runtime/lib/ld-linux-x86-64.so.2', '--inhibit-cache', '--library-path', '/runtime/lib']
ENV = {'HOME': '/tmp', 'TMPDIR': '/tmp', 'LC_ALL': 'C', 'QEMU_MODULE_DIR': '/disabled'}
OVERLAY = ROOT / 'worker.qcow2'
receipt = {'passed': False, 'modelCalls': 0, 'gpuDevices': 0, 'guestNetwork': False, 'boots': [], 'commands': []}
mode = sys.argv[1] if len(sys.argv) == 2 else 'clean'
assert mode in ['clean', 'refuse-unclean']
receipt['mode'] = mode
active = None


def save():
    (ROOT / 'native.json').write_text(json.dumps(receipt, indent=2))


def tool(args):
    result = subprocess.run(PREFIX + ['/runtime/bin/qemu-img'] + args, env=ENV, capture_output=True, text=True, timeout=30)
    receipt['commands'].append({'args': args, 'code': result.returncode, 'stdout': result.stdout[-8192:], 'stderr': result.stderr[-8192:]})
    save()
    assert result.returncode == 0, 'Disk inspection failed'


def disk_state(name):
    target = ROOT / (name + '-superblock.bin')
    tool(['dd', '-f', 'qcow2', 'if=' + str(OVERLAY), 'of=' + str(target), 'bs=1024', 'count=2'])
    data = target.read_bytes()[1024:]
    assert len(data) == 1024 and struct.unpack_from('<H', data, 56)[0] == 0xef53, 'Expected an ext4 root'
    state = struct.unpack_from('<H', data, 58)[0]
    features = struct.unpack_from('<I', data, 96)[0]
    return {'state': state, 'needsJournalRecovery': bool(features & 4), 'clean': state == 1 and not features & 4,
            'superblockSha256': hashlib.sha256(data).hexdigest()}


class Boot:
    def __init__(self, name):
        self.directory = ROOT / name
        self.directory.mkdir()
        self.record = {'name': name, 'qmp': []}
        receipt['boots'].append(self.record)
        self.connections = []
        self.threads = []
        self.process = None
        self.stdout = self.stderr = self.qmp = self.control = None
        self.listeners = [socket.socket() for _ in range(2)]
        for listener in self.listeners:
            listener.bind(('127.0.0.1', 0))
            listener.listen(1)
            listener.settimeout(40)

    def start(self):
        console, control = self.listeners
        qmp_path = '/tmp/' + uuid.uuid4().hex + '.sock'
        args = PREFIX + ['/runtime/bin/qemu-system-x86_64', '-no-user-config', '-L', '/runtime/share/qemu',
            '-machine', 'q35', '-accel', 'kvm', '-cpu', 'host', '-smp', '1', '-m', '1536', '-nodefaults',
            '-nic', 'none', '-display', 'none', '-monitor', 'none', '-no-reboot', '-S',
            '-qmp', 'unix:' + qmp_path + ',server=on,wait=off',
            '-chardev', 'socket,id=console,host=127.0.0.1,port=' + str(console.getsockname()[1]), '-serial', 'chardev:console',
            '-chardev', 'socket,id=control,host=127.0.0.1,port=' + str(control.getsockname()[1]),
            '-device', 'virtio-serial-pci,id=transport', '-device', 'virtserialport,chardev=control,name=org.thaddeus.control',
            '-kernel', '/inputs/alpine/boot/vmlinuz-virt', '-initrd', '/inputs/alpine/boot/initramfs-virt',
            '-append', 'console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/opt/thaddeus/vm/init quiet',
            '-drive', 'file=' + str(OVERLAY) + ',format=qcow2,if=virtio']
        self.record['args'] = args
        self.stdout = (self.directory / 'stdout.log').open('wb')
        self.stderr = (self.directory / 'stderr.log').open('wb')
        self.process = subprocess.Popen(args, env=ENV, stdin=subprocess.DEVNULL, stdout=self.stdout, stderr=self.stderr)
        self.record['pid'] = self.process.pid
        save()
        console_stream = console.accept()[0]
        self.connections.append(console_stream)
        self.control = control.accept()[0]
        self.connections.append(self.control)
        self.control.settimeout(40)
        self.control_file = self.control.makefile('rb')

        def drain_console():
            with (self.directory / 'console.log').open('wb') as output:
                size = 0
                while chunk := console_stream.recv(4096):
                    size += len(chunk)
                    if size > 300000:
                        self.process.kill()
                        raise RuntimeError('Console bound exceeded')
                    output.write(chunk)
                    output.flush()
        thread = threading.Thread(target=drain_console, daemon=True)
        thread.start()
        self.threads.append(thread)
        self.qmp = socket.socket(socket.AF_UNIX)
        self.qmp.settimeout(10)
        self.connections.append(self.qmp)
        self.qmp.connect(qmp_path)
        self.qmp_file = self.qmp.makefile('rb')
        self.record['greeting'] = self.qmp_read()
        self.qmp_command('qmp_capabilities')
        self.record['cpus'] = self.qmp_command('query-cpus-fast')
        self.record['memory'] = self.qmp_command('query-memory-size-summary')
        assert len(self.record['cpus']) == 1 and self.record['memory']['base-memory'] == 1536 * 1024 * 1024
        self.qmp_command('cont')
        ready = json.loads(self.control_file.readline(2200001))
        assert ready['type'] == 'ready' and ready['uid'] == 1000
        self.record['ready'] = ready
        save()

    def qmp_read(self):
        message = json.loads(self.qmp_file.readline(300001))
        self.record['qmp'].append(message)
        return message

    def qmp_command(self, operation):
        identifier = uuid.uuid4().hex
        self.qmp.sendall((json.dumps({'execute': operation, 'id': identifier}) + '\n').encode())
        while True:
            message = self.qmp_read()
            if message.get('id') == identifier:
                assert 'error' not in message
                return message['return']

    def execute(self, program):
        identifier = uuid.uuid4().hex
        self.control.sendall((json.dumps({'type': 'execute', 'id': identifier, 'command': ['python3', '-c', program], 'input': None}) + '\n').encode())
        result = json.loads(self.control_file.readline(2200001))
        self.record.setdefault('executions', []).append(result)
        save()
        assert result['type'] == 'result' and result['id'] == identifier and result['exitCode'] == 0, 'Guest fixture command failed'
        return json.loads(result['output'])

    def stop(self):
        self.control.sendall(b'{"type":"shutdown"}\n')
        self.record['exitCode'] = self.process.wait(timeout=20)
        while line := self.qmp_file.readline(300001):
            self.record['qmp'].append(json.loads(line))
        for thread in self.threads:
            thread.join(timeout=2)
            assert not thread.is_alive()
        assert self.record['exitCode'] == 0 and any(item.get('event') == 'SHUTDOWN' and item.get('data', {}).get('guest') for item in self.record['qmp'])
        text = (self.directory / 'console.log').read_text(errors='replace')
        self.record['filesystemErrors'] = [line for line in text.splitlines() if 'EXT4-fs' in line and any(word in line.lower() for word in ['error', 'failed', 'corrupt'])]
        self.record['disk'] = disk_state(self.record['name'])
        save()

    def close(self):
        if self.process and self.process.poll() is None:
            self.record['forcedFixtureCleanup'] = True
            self.process.kill()
            self.process.wait(timeout=5)
        for item in [getattr(self, 'control_file', None), getattr(self, 'qmp_file', None), *self.connections, *self.listeners, self.stdout, self.stderr]:
            if item:
                item.close()

    def require_refusal(self):
        self.control.sendall(b'{"type":"shutdown"}\n')
        expected = 'THADDEUS_VM_SHUTDOWN_FAILED RuntimeError Guest root filesystem did not close cleanly.'
        end = time.monotonic() + 12
        while time.monotonic() < end:
            assert self.process.poll() is None, 'A rejected filesystem must not produce successful poweroff'
            if expected in (self.directory / 'console.log').read_text(errors='replace'):
                break
            time.sleep(.05)
        else:
            raise AssertionError('No specific filesystem refusal was observed')
        self.record['statusAfterRefusal'] = self.qmp_command('query-status')
        assert self.record['statusAfterRefusal']['running'] and not any(item.get('event') == 'SHUTDOWN' for item in self.record['qmp'])
        self.record['uncleanShutdownRefused'] = True
        save()


try:
    assert not OVERLAY.exists()
    tool(['create', '-f', 'qcow2', '-F', 'raw', '-b', '/base', str(OVERLAY)])
    if mode == 'refuse-unclean':
        receipt['faultBeforeBoot'] = disk_state('fault-before')
        assert receipt['faultBeforeBoot']['state'] & 2, 'The negative control needs a pre-marked error state'
    # The fixed files are acknowledged after close, without asking the fixture to sync them.
    payload = ('The raven expects his notes to survive a closing door.\n' * 320).encode()
    expected = hashlib.sha256(payload).hexdigest()
    writer = "import os,pathlib,signal,time; signal.signal(signal.SIGTERM,signal.SIG_IGN); p=pathlib.Path('/home/agent/shutdown-check/churn'); p.mkdir(); (p/'ready').write_text('ready'); data=b'x'*1048576\nfor i in range(600):\n (p/'busy.bin').write_bytes(data); time.sleep(0.005)"
    prepare = f"""import hashlib,json,pathlib,subprocess,sys,time
root=pathlib.Path('/home/agent/shutdown-check'); root.mkdir()
payload={payload!r}
for i in range(32): (root/(str(i)+'.txt')).write_bytes(payload)
child=subprocess.Popen([sys.executable,'-c',{writer!r}],stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True)
for i in range(100):
 if (root/'churn/ready').exists(): break
 time.sleep(.01)
assert (root/'churn/ready').exists()
print(json.dumps({{'acknowledged':32,'sha256':hashlib.sha256(payload).hexdigest(),'writerPid':child.pid}}))
"""
    active = Boot('first')
    active.start()
    receipt['acknowledged'] = active.execute(prepare)
    if mode == 'refuse-unclean':
        active.require_refusal()
        active.close()
        active = None
        receipt['passed'] = True  # The negative control passed; this disk is explicitly unsafe to continue.
    else:
        active.stop()
        active.close()
        active = None
        # Restart only this fresh test overlay; the original failed product disks are not inputs.
        active = Boot('second')
        active.start()
        verification = active.execute("import hashlib,json,pathlib; p=pathlib.Path('/home/agent/shutdown-check'); print(json.dumps({'hashes':[hashlib.sha256((p/(str(i)+'.txt')).read_bytes()).hexdigest() for i in range(32)],'files':32}))")
        receipt['filesSurvived'] = verification['files'] == 32 and verification['hashes'] == [expected] * 32
        active.stop()
        active.close()
        active = None
        receipt['passed'] = receipt['filesSurvived'] and all(boot['disk']['clean'] and not boot['filesystemErrors'] for boot in receipt['boots'])
except Exception as error:
    receipt['error'] = type(error).__name__ + ': ' + str(error)
finally:
    if active:
        active.close()
    save()
print(json.dumps({'passed': receipt['passed'], 'filesSurvived': receipt.get('filesSurvived'), 'error': receipt.get('error'), 'boots': [{'name': boot['name'], 'disk': boot.get('disk'), 'filesystemErrors': boot.get('filesystemErrors')} for boot in receipt['boots']]}))
raise SystemExit(0 if receipt['passed'] else 1)
