# Guest filesystem shutdown

The isolated worker's PID 1 must close the guest filesystem before powering off.
The former sequence sent SIGTERM, waited 0.2 seconds, called `sync`, and invoked
the reboot syscall. It neither waited for every process to exit nor remounted
the root read-only. A process ignoring SIGTERM could still write after the sync.

The new sequence gives remaining children two seconds to terminate, kills and
reaps survivors within another two seconds, checks `syncfs` for errors, and
remounts the fixed ext4 root read-only. It verifies the mount flag and the root
device's filesystem state before requesting poweroff. A failed step leaves a
private console diagnostic and does not issue a successful guest poweroff. The
existing host process owner still imposes its shutdown deadline and retains an
unconfirmed outcome. No task is automatically replayed or disk repaired.

This ordering follows the kernel's contracts: the
[reboot syscall does not sync the filesystem](https://github.com/torvalds/linux/blob/v6.18/kernel/reboot.c#L677),
and ext4 clears its recovery requirement when the journal is flushed for a
[read-only remount](https://github.com/torvalds/linux/blob/v6.18/fs/ext4/super.c#L5883).
The kernel can complete a remount even when marking the filesystem clean fails,
so a successful mount command alone is insufficient.

## Focused native check

```text
node scripts/qemu-shutdown-check.mjs FRESH-NAME PATH-TO-PREPARED-ROOT-DISK
```

This local diagnostic uses the pinned Linux QEMU/KVM runtime and guest kernel,
one vCPU, 1536 MiB guest memory, and a container limited to one CPU, 2 GiB memory
and 64 processes. Its only device grant is KVM. The container has no network,
GPU, model endpoint, or personal data. The base disk and runtime are read-only;
only a fresh test overlay is writable. The overall container has a 225-second
deadline, and its named container is removed when the runner finishes. Inputs,
commands, per-boot consoles, QMP observations and output remain in
`artifacts/qemu-shutdown-FRESH-NAME`.

The fixture writes and closes 32 known files without explicitly syncing them,
starts a bounded writer that ignores SIGTERM, and requests normal guest shutdown.
After QEMU exits, the host reads the ext4 superblock through `qemu-img dd` without
booting or repairing the image. A second boot checks every acknowledged file's
SHA-256, then shuts down again. Both stops must have a clean filesystem state,
no pending journal recovery and no captured ext4 error; all file hashes must
match. The direct fixture transport is not the product's authenticated transport
and does not qualify confinement or production admission.

The original image failed this check in
`artifacts/qemu-shutdown-before-20260913-a`: both QEMU exits succeeded and all
32 files survived, but both stops left the journal recovery flag set. That
establishes a shutdown defect, not data loss in that test or the cause of the
earlier intermittent Linux product continuation exceptions.

## Rebuilt image and product evidence

The canonical rebuilt root disk has SHA-256
`112b7aa056db418324d5ffa2cc22ebec9194eb89c83299ac5f83be65cefce22a`.
Its pinned OpenClaw image and integration sources are unchanged; only the guest
init source changed. The first build exceeded its existing 600-second disk-build
deadline while creating ext4 through the Windows bind mount. That attempt is
retained. The builder now creates and checks ext4 inside the build container,
then copies the checked disk to the output mount. The same bounded build passed
under `artifacts/qemu-worker-shutdown-20260913-b`.

The rebuilt image passes the focused check in
`artifacts/qemu-shutdown-after-20260913-a`: both stops leave clean filesystems
without pending journal recovery or captured ext4 errors, and all 32 acknowledged
files survive. A separate, deliberately damaged copy exercises `refuse-unclean`
mode in `artifacts/qemu-shutdown-refusal-20260913-a`. The init reports failure,
QMP still observes a running VM, and no guest shutdown event is emitted. The
fixture then forcibly closes only that test VM. This is a passed negative control,
not a claim that the damaged copy is usable. The canonical disk is unchanged.

Two authenticated Windows checks also pass with the exact rebuilt image:

- `artifacts/qemu-managed-scripted-1789350624255`: actual OpenClaw and QEMU/WHPX,
  a durable question, separate VM restart, continuation and exact approved import;
  four synthetic replies account for 520 fixture tokens.
- `artifacts/guest-shutdown-20260913/browser-receipt.json`: the actual product
  coordinator and browser flow, selected memory, public-page retrieval, a durable
  question, restart, one bounded quotation correction, approved import and reviewed
  workspace removal; seven synthetic replies account for 910 fixture tokens.

No live model, GPU inference or hosted Actions was used. These checks establish
the changed shutdown contract and this Windows workflow, not production
qualification. The earlier Linux continuation failures B/C remain unexplained;
their original disks remain retained. The exact failing continuation stage still
needs evidence before claiming that this shutdown change resolves those failures.

## Main Windows activation

The existing study was idle before maintenance. Its owner maintenance flow made
and verified the seven-file backup
`20260914-020017-b3a3527983224e869ce9a1e8f6850a97`, then closed the old host.
The same application package and data directory were reopened with the validated
replacement worker installation; the existing owner session checked its files
and enabled the new installation. This does not broaden task grants or budgets.

Before/after fingerprints match for all 20 runs, 232 events, five pages,
18 revisions, 12 chats, notes, host key, provider settings and saved sessions.
Only the worker enrollment is expected to change. Visible reported usage remains
116,213 tokens and the selected provider remains Luna High. The model bridge was
not restarted, and this activation started no model or worker task. The prior
profile, installation and images remain available. Activation evidence is under
`artifacts/guest-shutdown-20260913`.
