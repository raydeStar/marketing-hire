"""Minimal PID 1 for a disposable development VM, not a production init system."""
import ctypes
import fcntl
import os
import pathlib
import signal
import socket
import struct
import subprocess
import time

if os.getpid() != 1:
    raise SystemExit("The VM init belongs in its own estate, as PID 1.")


def mount(kind, path):
    pathlib.Path(path).mkdir(parents=True, exist_ok=True)
    if not os.path.ismount(path):
        subprocess.run(["mount", "-t", kind, kind, path], check=True)


def reap_until(deadline):
    while True:
        try:
            pid, _ = os.waitpid(-1, os.WNOHANG)
        except ChildProcessError:
            return True
        except InterruptedError:
            continue
        if pid:
            continue
        if time.monotonic() >= deadline:
            return False
        time.sleep(0.02)


def signal_guests(number):
    # PID 1 and kernel threads are excluded; these are only this VM's processes.
    try:
        os.kill(-1, number)
    except ProcessLookupError:
        pass


def shutdown():
    if os.getpid() != 1:
        raise RuntimeError("Only the estate's own PID 1 may close its doors.")
    signal_guests(signal.SIGTERM)
    if not reap_until(time.monotonic() + 2):
        signal_guests(signal.SIGKILL)
        if not reap_until(time.monotonic() + 2):
            raise RuntimeError("Guest processes did not stop before the shutdown bound.")
    libc = ctypes.CDLL(None, use_errno=True)
    libc.syncfs.argtypes = [ctypes.c_int]
    libc.syncfs.restype = ctypes.c_int
    root = os.open("/", os.O_RDONLY | os.O_DIRECTORY)
    try:
        if libc.syncfs(root) != 0:
            number = ctypes.get_errno()
            raise OSError(number, "Guest root filesystem flush failed.")
    finally:
        os.close(root)
    subprocess.run(["mount", "--no-mtab", "-o", "remount,ro", "/"], check=True, timeout=5)
    if not os.statvfs("/").f_flag & os.ST_RDONLY:
        raise RuntimeError("Guest root filesystem remains writable.")
    # The pinned worker uses an ext4 root on vda. Read its on-disk state after the
    # read-only remount; a poweroff event alone is no evidence of a closed ledger.
    with open("/dev/vda", "rb", buffering=0) as disk:
        disk.seek(1024)
        block = disk.read(1024)
    if len(block) != 1024 or struct.unpack_from("<H", block, 56)[0] != 0xef53:
        raise RuntimeError("Guest root filesystem format was not confirmed.")
    if struct.unpack_from("<H", block, 58)[0] != 1 or struct.unpack_from("<I", block, 96)[0] & 4:
        raise RuntimeError("Guest root filesystem did not close cleanly.")
    print("THADDEUS_VM_FILESYSTEM_CLOSED", flush=True)
    if libc.reboot(0x4321FEDC) != 0:
        raise OSError(ctypes.get_errno(), "Guest poweroff failed.")
    raise RuntimeError("Guest poweroff unexpectedly returned.")


try:
    subprocess.run(["mount", "-o", "remount,rw", "/"], check=True)
    mount("proc", "/proc")
    mount("sysfs", "/sys")
    mount("devtmpfs", "/dev")
    mount("devpts", "/dev/pts")
    mount("tmpfs", "/run")
    mount("tmpfs", "/dev/shm")
    # devtmpfs starts restrictive; these standard devices must be usable by agent.
    for name in ["null", "zero", "full", "random", "urandom", "tty", "ptmx"]:
        if os.path.exists("/dev/" + name):
            os.chmod("/dev/" + name, 0o666)
    os.chmod("/dev/shm", 0o1777)
    for name, target in {"fd": "/proc/self/fd", "stdin": "/proc/self/fd/0",
                         "stdout": "/proc/self/fd/1", "stderr": "/proc/self/fd/2"}.items():
        if not os.path.lexists("/dev/" + name):
            os.symlink(target, "/dev/" + name)
    # Container roots need no iproute2 package just to activate guest loopback.
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as control:
        current = fcntl.ioctl(control, 0x8913, struct.pack("16sH22x", b"lo", 0))
        flags = struct.unpack_from("H", current, 16)[0]
        fcntl.ioctl(control, 0x8914, struct.pack("16sH22x", b"lo", flags | 1))
    ports = list(pathlib.Path("/sys/class/virtio-ports").glob("*/name"))
    matching = [p for p in ports if p.read_text().strip() == "org.thaddeus.control"]
    if len(matching) != 1:
        raise RuntimeError("Expected one explicit virtual command channel.")
    fd = os.open("/dev/" + matching[0].parent.name, os.O_RDWR)
    environment = {"PATH": "/usr/local/bin:/usr/bin:/bin", "HOME": "/home/agent",
                   "USER": "agent", "LANG": "C.UTF-8", "THADDEUS_CHANNEL_FD": str(fd)}
    child = subprocess.Popen(["node", "/opt/thaddeus/vm/supervisor.mjs"],
                             cwd="/home/agent", env=environment, pass_fds=(fd,),
                             user=1000, group=1000, extra_groups=[], start_new_session=True)
    os.close(fd)
    print("THADDEUS_VM_INIT_READY", flush=True)
    while True:
        try:
            pid, status = os.waitpid(-1, 0)
        except InterruptedError:
            continue
        if pid == child.pid:
            print("THADDEUS_VM_SUPERVISOR_EXIT", os.waitstatus_to_exitcode(status), flush=True)
            break
except Exception as error:
    print("THADDEUS_VM_INIT_FAILED", type(error).__name__, str(error), flush=True)
finally:
    try:
        shutdown()
    except Exception as error:
        # Leave an unconfirmed shutdown for the host's bounded process owner;
        # never manufacture a successful guest poweroff after a failed flush.
        print("THADDEUS_VM_SHUTDOWN_FAILED", type(error).__name__, str(error), flush=True)
    while True:
        time.sleep(1)
