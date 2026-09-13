# Linux worker process ownership

`LinuxSystemdProcess` supervises a trusted host adapter through one transient
systemd user service. It is a Linux counterpart to the Windows process owner,
not a permission boundary around arbitrary host-side agent commands. The agent
must still execute inside its VM. The application does not yet select this
primitive or expose a Linux QEMU worker.

The service owns its entire cgroup from startup. Its properties request a hard
memory limit, zero swap, a CPU quota, a task limit, a maximum lifetime and
control-group termination. Before starting the adapter, `LinuxServiceSupervisor`
reads the actual cgroup v2 controls and refuses mismatches, unlimited values or
membership in another service. Linux's task limit includes threads; its memory
accounting is not Windows job committed-memory accounting.

A separate Unix socket connects the supervisor to the owning host. A bounded
handshake binds the service, private request hash, nonce and observed resources
before admitting the adapter. This socket is independent of the adapter's stdin,
so blocked QMP input cannot hide owner loss. Closing the host's socket causes the
supervisor to exit; systemd then kills every remaining service process, including
detached descendants. Killing the supervisor has the same cgroup cleanup effect.
The service lifetime also applies while the owning host is paused. This is a
bounded deadline, not a claim that every stalled supervisor stops immediately.

The adapter inherits the service's stdio and receives only its explicitly supplied
environment and argv. No shell expands those arguments. The private launch
directory must be new; requests and observations remain there after termination.
An unexpected service exit with live cgroup members is a failure requiring
reconciliation. A cancellation or output limit cannot claim success from an OS
exit code alone. Output limits apply while the caller drains each stream.

The tested prerequisites are a native systemd 255 user session, a unified cgroup
v2 hierarchy with CPU/memory/pids delegation, and the ability to protect the
service's cgroup filesystem. Missing prerequisites refuse startup. No direct
process, container, root-service or host-agent fallback is selected. The helper
entry point currently exists only in `Thaddeus.LinuxProcessCheck`; application
packaging and worker admission remain future integration work. Adapter executable
and runtime package pinning remain responsibilities of the calling backend.

## Native evidence

Nine real-process checks passed in a disposable Linux x64 VM: literal argv and
stdio with explicit environment, detached-child cleanup after normal exit,
cancellation, owner deadline, observed CPU throttling, output overflow, abrupt
owner death, service deadline with a paused owner, and abrupt supervisor death.
An unrelated control process survives the crash checks. Sixteen deterministic
tests additionally reject invalid requests and mismatched resource observations.

The guest uses Ubuntu 24.04 userspace, systemd 255.4-1ubuntu8.17 and the already
pinned Linux 6.18.35 diagnostic kernel. Its user manager explicitly delegates
CPU/memory/pids controllers. These receipts qualify this process mechanism in
that environment; they do not establish default settings on every Linux desktop,
KVM access, a Linux OpenClaw workflow, Arm64 support, macOS support or a consumer
installer. Memory/swap/task controls were read from the kernel; the native pressure
check exercises CPU throttling, not memory exhaustion or fork exhaustion.

The Windows diagnostic VM has two vCPUs, 1536 MiB guest RAM, a 2560 MiB host job
memory cap, no NIC, no GPU and no host filesystem shares. Its owner holds the
verified QEMU runtime and boot inputs. A read-only payload disk carries the exact
captured check sources' Linux binary; an overlay preserves the base image.
Only image preparation downloads Ubuntu packages. Docker constructs these
development fixtures; this does not make Docker a Linux product prerequisite.

Private evidence is under `artifacts/linux-process-ownership-20260913-c`:
`preparation.json`, package inventory and base disk hashes describe the image;
`boot-deadline/verified.json` records captured sources, payload hash and all nine
checks; `vm-exit.json` and console logs retain the actual VM result. The earlier
`ownership-20260913-b` attempt failed because its image lacked udev. Its five-minute
timeout produced a failure receipt even though Windows returned exit code zero.

Reproduce on this Windows development host with Docker healthy and the configured
QEMU diagnostic installation present:

```text
node scripts/linux-process-vm-check.mjs FRESH-NAME
node scripts/boot-linux-process-check.mjs artifacts/linux-process-FRESH-NAME first
```

Subsequent boots capture current probe sources into a fresh payload and can reuse
the prepared base. Both scripts retain evidence and remove only their own named
containers. No model, GPU, hosted Actions run or main-study data is used.

Sources: systemd's [service termination contract](https://github.com/systemd/systemd/blob/v255/man/systemd.kill.xml),
[transient services and inherited stdio](https://github.com/systemd/systemd/blob/v255/man/systemd-run.xml),
and [resource controls](https://github.com/systemd/systemd/blob/v255/man/systemd.resource-control.xml).
