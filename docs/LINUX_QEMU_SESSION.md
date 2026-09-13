# Linux QEMU session integration

`QemuWorkerSession.StartLinux` uses the shared VM hardware, mutual TLS, QMP,
guest command and fixed broker-route contracts. `LinuxSystemdProcess` owns the
QEMU adapter through a transient user service. The Windows entry point retains
its Windows job owner. This is native session integration for development;
the product still refuses Linux worker admission.

The Linux adapter requires a complete x64 QEMU 11.1.0 runtime bundle. Its manifest
includes the dynamic loader, transitive libraries and firmware. The loader runs
with an explicit library directory, no loader cache, an explicit environment and
no QEMU user configuration. The tested build disables TCG and dynamic modules;
KVM is the selected accelerator, with no emulation fallback.

Unix file sharing flags alone do not prevent another process from writing a
runtime file. Admission therefore requires both a read-only filesystem and a
read-only mount. A writable bind mount or nested writable file/directory mount
is refused. Before admitting guest vCPU execution, the owner reads the actual
QEMU executable mappings and requires every file mapping to belong to the pinned
bundle. Anonymous/deleted executable mappings are refused; the kernel's vDSO and
vsyscall mappings are allowed. Mappings and cgroup controls are checked again
before guest commands and at shutdown. This is not protection from a compromised
host administrator.

The service memory cap is guest RAM plus 1 GiB, the CPU quota is one CPU per
requested guest vCPU, and the task cap is 128 processes/threads. Its inherited
stdio carries QMP; two separate authenticated loopback TLS connections carry
guest control and console traffic. Neither TLS connection creates a guest NIC.

The developer builder uses a bounded, owned Docker container to compile the
pinned upstream source and copy its dependencies. It changes no installed host
runtime. The source archive SHA-256 is
`6ee1d1a61f68212476b27108c26da5f449dc09b626d42f8279ba0dc2e08fa858`.
GnuPG verified the signature bytes but reported an expired signer. That status
is retained: this bundle is for development, not an authenticated product release.
The inventory does not substitute for publisher signing or update qualification.

```text
node scripts/prepare-linux-qemu.mjs FRESH-NAME
node scripts/seal-linux-qemu.mjs artifacts/qemu-linux-FRESH-NAME
```

The local native session fixtures execute OpenClaw's version command, write/read
a private guest file, inspect guest UID/network/mounts, exercise one inert broker
round trip and a denied path, and require independently observed guest shutdown.
They retain captured sources and process/resource observations. No model or GPU
call is required. They do not test the full research workflow or paid providers.

The Linux fixture uses an outer Linux KVM VM so its systemd 255 user manager and
KVM group changes remain inside a disposable image. The running WSL user manager
and user groups are preserved. Docker owns only the diagnostic outer QEMU and
its capped resources; the actual session under test runs in native Linux with
systemd/cgroup ownership. This fixture arrangement is not a consumer installation
requirement. Its boot guest has no NIC or host filesystem share.

Consumer Linux admission still needs backend lifecycle/recovery integration,
physical overlay-writer reconciliation, supervisor packaging, setup prerequisites
and release trust. macOS, Arm64 and physical phones require their own evidence.
The installed Windows study and its data remain separate from these fixtures.

## Native evidence, September 13

All six session checks passed under native Linux x64, systemd 255.4 and Linux
6.18.35. The actual OpenClaw version was 2026.9.4; both channels negotiated TLS
1.3. The owner observed 17 executable file mappings, all in the 201-file runtime
inventory. The cgroup reported 2,684,354,560 memory bytes, zero swap, 100,000 quota
microseconds per 100,000 microsecond period, and 128 tasks. These same controls
and mappings were recorded again before successful guest shutdown.

Private receipts and logs are under `artifacts/linux-qemu-session-20260913-b`.
The read-only payload SHA-256 is
`fb5d8ba869d34232e880a71127ec51bf4a2d04f1d2236febce99128428bd686d`;
its QEMU manifest SHA-256 is
`2be5f05f1fd110d9ad195f7dcfde85c1aa910df24a70630ac26352600f41f6bb`.
The outer KVM fixture had two vCPUs and 4 GiB guest RAM, a two-CPU/5-GiB Docker
cap, a 305-second outer deadline, and only the KVM device. The native session's
inner guest had one vCPU and 1.5 GiB RAM. Both VMs shut down; all owned containers
were removed, and no host service, account group, GPU or model was changed.

The initial Linux attempt hit the image-building deadline before boot. Moving
filesystem construction onto the builder's Linux storage resolved that issue;
only completed, checked images are copied out. The failed receipt is retained.
The fixture can reuse a previously prepared base image while capturing current
probe sources. On this development checkout, which has the pinned inputs listed
in the script:

```text
node scripts/linux-qemu-session-check.mjs FRESH-NAME artifacts/linux-qemu-session-20260913-b
node scripts/windows-qemu-session-check.mjs FRESH-NAME
```

The corresponding native Windows session passed the same six checks under WHPX
with its existing job owner. Its receipt is under
`artifacts/windows-qemu-session-20260913-b`. An initial fixture ID typo was
refused before VM startup, then corrected; the rejection receipt remains.
The full local check suite passed 630 backend tests, seven protocol tests and
the web build. None of these checks used a live model or hosted Actions.
