# Linux worker lifecycle and disk ownership

`QemuSandboxBackend` now has an explicit Linux x64 mode using a supplied native
supervisor and the pinned Linux QEMU runtime. It implements creation, execution,
verified stopping, stopped-state reconciliation, restart, retirement and removal
through the same backend used on Windows. The application factory and packaged
host supervisor are not wired for Linux yet; normal application admission remains
Windows-only. This is development qualification, not consumer release approval.

Linux registrations record `qemu-kvm` beside the worker image and broker binding.
A missing or different host binding refuses Linux execution. Windows retains
compatibility with older registrations. Every Linux boot input must reside on a
read-only filesystem, including the external manifest. Explicit `locking=on`
file nodes prevent QEMU from silently continuing without its locking protocol.

## Reconciliation boundary

Before each service starts, its private directory receives a flushed ownership
record containing the unit, kernel boot ID and user ID. Reconciliation reads the
actual user-manager state and cgroup membership. An active, queued, uninspectable
or foreign-user service refuses recovery. The code does not stop a process by a
saved PID. A missing required ownership record also refuses recovery. Records
from a previous kernel cannot identify a live local process.

`QemuDiskLease` uses Linux open-file-description locks. The inspection lease
follows the pinned QEMU 11.1.0 permission protocol: it claims consistent reading,
denies write/write-unchanged/resize, and checks for existing incompatible users.
A read-only `qemu-img check` can coexist with that lease; a writer cannot. Removal
holds an exclusive OFD lock through unlink. These are cooperative host locks,
not protection from a compromised administrator or a program that ignores locks.
See the [Linux OFD locking contract](https://man7.org/linux/man-pages/man2/F_OFD_SETLK.2const.html)
and QEMU's [image locking documentation](https://www.qemu.org/docs/master/system/images.html#image-locking).
The exact permission offsets and flags were checked in the already pinned
11.1.0 source archive's `block/file-posix.c`, `include/block/block-common.h` and
`util/osdep.c`; they are not inferred from current QEMU branding.

The recovery lease stays open through key retirement, the read-only consistency
check and before/after hashing. Explicit null backing prevents disk metadata from
selecting another host file or protocol. Failed consistency checks retain the disk
and `recovery-required` state. No repair, guest boot or command replay is automatic.
Retired workspace removal also checks service ownership, retains a durable removal
intent, and leaves imported notes and task receipts in the product store. Partial
removal can resume without requiring service records it already removed after
proving quiescence; the disk lock is reacquired if the overlay still exists.

## Native evidence, September 13

The final Linux fixture passed eight checks: refusal of a live writer/service,
verified guest stopping point, abrupt host-owner loss and read-only reconciliation,
refusal of a newly launched QEMU writer during inspection, restart with the same
guest file, damaged-disk refusal without repair/boot, verified stop/retirement,
and workspace purge preserving an imported note. Windows passed the corresponding
seven checks; Linux's extra check exercises its OFD permission protocol directly.
The shared session retains native resource observations and TLS evidence.

Receipts and captured sources:

- `artifacts/linux-backend-lifecycle-20260913-d/verified.json`
- `artifacts/windows-qemu-lifecycle-20260913-d/verified.json`
- `artifacts/local-check-linux-lifecycle-20260913/verified.json`

The local suite passes 639 backend tests, seven protocol tests and the web build.
The Linux fixture reuses immutable guest assets and a prepared systemd/KVM image,
while a separate read-only tools disk carries each captured probe build. It has
two outer vCPUs, 4 GiB outer guest RAM, a two-CPU/5-GiB Docker cap and a 305-second
outer deadline. No live model, GPU, hosted Actions or main-study data is used.

```text
node scripts/linux-qemu-backend-check.mjs FRESH-NAME artifacts/linux-backend-lifecycle-20260913-d
node scripts/windows-qemu-session-check.mjs FRESH-NAME backend
```

The first crash experiment killed a VM while it was active. Linux's read-only
check found 18 leaked clusters and retained the disk unchanged. Windows initially
reported SQLite I/O error 10 after that unclean owner loss; a later independent
integrity check returned `ok`. These failed receipts remain. They do not establish
automatic recovery from arbitrary active-worker crashes. The successful host-loss
case deliberately starts from an independently verified guest stopping point.
An early Linux purge attempt also found that the old .NET file probe conflicted
with the new lease; purge now retains the stronger lease without reopening the
same overlay for that weaker probe.

Next are the packaged Linux supervisor, application factory/setup integration and
native product workflow acceptance. Consumer installation, release signing,
cross-host workspace transfer, macOS/Arm workers and phone acceptance stay open.
