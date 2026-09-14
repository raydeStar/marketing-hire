# Linux packaged worker preview

The Linux x64 host now contains the systemd service supervisor and composes the
QEMU worker through the ordinary application factory. OpenClaw remains inside
the VM; the product, credentials, model admission and import approvals remain
outside it. This is an explicitly configured development preview. Default
installations still open ordinary chat without starting a worker.

## Host setup

Run the self-contained `Thaddeus.Host` through `start-thaddeus.sh`. The Linux
worker requires that actual application executable; a `dotnet` or test runner
cannot stand in for the supervisor. Its private `--linux-supervise` entry point
handles only the owned service request and exits before application startup.
Malformed service requests cannot fall through to opening a study or listener.

The configured installation uses the existing `kind: qemu` JSON with pinned
executable, image tool, kernel, initrd, base disk and full runtime manifest.
Linux requires the entire QEMU runtime and all boot inputs on read-only
filesystem storage. The development bundle and its trust limitations are
described in [Linux QEMU sessions](LINUX_QEMU_SESSION.md).

The current tested prerequisites are native Linux x64, accessible KVM, a systemd
255 user session and delegated cgroup v2 CPU, memory and process controllers.
The backend verifies actual resource values on each service start. It does not
downgrade missing controls, use software emulation, or start an unrestricted host
process. Installing these prerequisites for a nontechnical user is unfinished.

**Settings → Set up this host → Check this computer** now reads these
[prerequisites](HOST_REQUIREMENTS.md) before a package is configured. The owner
gets a separate result for each requirement, with next steps. No VM or model is
started, and a passing report does not enable research.

Set `developmentWorkerInstallation` in the portable launch profile to the
absolute installation JSON path, with a separate loopback `workerPort`. Then
open **Settings → Set up this host**, check the installed worker and explicitly
enable it. Configuration changes invalidate enrollment; the browser cannot choose
an arbitrary executable. See [host setup](HOST_SETUP_PREVIEW.md) for the shared
approval and expiration rules. Windows configuration digests remain compatible.

Linux registers the backend as `qemu-kvm`. macOS and Linux Arm workers remain
unavailable. A phone connects to a supported host; it does not run this worker.

## Native product check

```text
node scripts/linux-product-check.mjs FRESH-NAME
```

This explicit local check cross-publishes the captured Linux x64 host and fresh
PWA on the development computer, then executes that package in a disposable
Ubuntu 24.04/systemd VM. A read-only tools disk carries the package and Python
test driver, separate from the previously verified immutable runtime and worker
assets. An optional second argument reuses the verified base of an earlier
`linux-product-*` check; changed application and fixture files are still captured
and rebuilt. This is native execution evidence, not a claim of native compilation,
release signing or user acceptance on an independently installed Linux machine.

The test driver uses the product's authenticated HTTP API and a separate
loopback provider. It does not replace application services or the research
factory. It checks setup refusal before enrollment, actual VM resources, a
durable question across application restart, one rejected source quotation and
bounded correction, exact approved import, usage accounting and reviewed private
workspace removal. The deliberate malformed service and approval requests are
negative controls; no personal study is involved.

All six model replies are synthetic protocol fixtures, accounting for 780
synthetic tokens. The provider uses the development Luna High endpoint contract
inside the disposable VM; it never connects to Luna, another paid model, the
host's model bridge or a GPU. Both VMs have no network interface. Public research
and model quality are outside this fixture's evidence; the existing Windows
public-search workflow has its own receipts.

The outer VM uses two vCPUs and 6 GiB RAM, with a two-CPU/7-GiB container cap and
a bounded shutdown deadline. The actual worker requests two vCPUs and 4 GiB RAM;
its observed host service must enforce 5 GiB memory, zero swap, 200% CPU quota
and 128 processes. Only test-owned processes and fixture containers are stopped.
All disks, logs, source captures and failed attempts remain under `artifacts/`.

Consumer provisioning, publisher trust, signed installation/updates, macOS/Arm
worker qualification, final UI acceptance and physical-phone verification remain
open. The existing active-worker crash limits also remain; this workflow restarts
the app only after the product has confirmed a stopped worker.

## Reusing images with limited disk space

The optional third argument supplies the prepared worker raw disk; a fourth
argument shares an earlier tools disk containing that exact worker image:

```text
node scripts/linux-product-check.mjs FRESH-NAME artifacts/linux-product-shared-images-20260914-c artifacts/qemu-worker-shutdown-20260913-b/root.ext4 artifacts/linux-product-startup-snapshot-20260913-a
```

The runner verifies the retained images' hashes, mounts them read-only, builds a
384 MiB disk for the changed application and uses a fresh writable outer overlay.
The shared worker is a fourth virtual disk; the native fixture requires its
read-only mount before package admission. The earlier interrupted case supplies
only its completed, verified disk contents, not passing product evidence.

The first two shared-image attempts failed before worker admission. The exported
root retained `/.dockerenv`, causing systemd to identify the VM as Docker.
Systemd's [container detection](https://github.com/systemd/systemd/blob/v255/src/basic/virt.c#L577)
and [command-line reader](https://github.com/systemd/systemd/blob/v255/src/basic/proc-cmdline.c#L122)
explain why the kernel's extra mount option was ignored. Preparation now removes
the known container markers only from a temporary copy, verifies that filesystem
and produces a standalone compressed root (about 235 MiB). Its original raw
source hash is checked again afterward. Future cases reuse that pinned compressed
root; no original disk is edited or repaired. This changes the disposable test
VM, not host Docker/systemd settings or the product's isolation rules.

`artifacts/linux-product-shared-images-20260914-c/verified.json` observes KVM and
the fourth disk's read-only mount, then passes all seven native workflow checks
with six synthetic replies (780 fixture tokens).

## Measured Gateway readiness, September 14

An earlier private startup snapshot caught a runnable OpenClaw process starting
its HTTP server when the previous ten quick probes expired. Readiness now uses
a monotonic 60-second window with two seconds between completed unsuccessful
probes and at most 30 replies. It starts one Gateway process, cancels a stalled
probe at the deadline, preserves owner cancellation and refuses a late success.
Failure still captures one bounded private observation where transport remains
available; there is no automatic process restart or task replay. Workspace
inspection recognizes only the exact bounded reply filenames through number 30.

The full suite passes 693 backend tests, seven protocol checks and the web build.
Tests advance a clock's timers and timestamps together to exercise the entire
window, a success after ten probes, a stalled probe, cancellation during a probe
or delay, late success, diagnostic failure and exact reviewed cleanup.

The new policy passes all seven Linux workflow checks in
`artifacts/linux-product-readiness-window-20260914-a/verified.json`: three Gateway
boots, 14 health replies, durable question/restart, correction, exact approved
import and reviewed workspace removal. All 143 captured application sources
match the full-suite receipt. The unchanged package also passes all seven checks
in `artifacts/linux-product-repeat-readiness-window-20260914-b/verified.json`.
That repeat verifies the compressed shared root path/format and reused inputs
without rebuilding the application or copying either large base disk. Each run
uses six synthetic replies (780 fixture tokens); no live inference, GPU or hosted
Actions is involved. These passes support the changed readiness contract and
this workflow, not universal startup reliability or production qualification.

## Evidence, September 13

All six native product checks passed in
`artifacts/linux-product-packaged-20260913-b/verified.json`. The captured package
used the actual application supervisor and research factory. Its full private
request/export evidence is retained in the diagnostic disk, with check summaries
in the outer console log. The first attempt is retained: its synthetic HTTP
server failed to read .NET's chunked request body. The corrected test server
passed without changing the application for that failure.

The same application code passed 639 backend tests, seven protocol tests and
the web build in `artifacts/local-check-linux-product-20260913/verified.json`.
A subsequent platform-neutral setup sentence is a web-only change; the native
workflow receipt retains its exact earlier PWA source. This is not final UI or
physical-device acceptance.

## Prerequisite and continuation follow-up

`artifacts/linux-product-requirements-20260913-d/verified.json` passes seven native
checks with the prerequisite endpoint added. It completes continuation, correction,
import and workspace removal with six synthetic replies (780 fixture tokens),
zero live provider calls and zero hosted Actions. The corresponding local core
suite passes 669 backend tests and seven protocol checks.

Retained attempts `requirements-20260913-b` and `requirements-20260913-c` failed
while resuming after application restart, before the third synthetic reply. In
attempt C, the completed disk inspection reported a consistent, byte-unchanged
overlay, followed by a continuation `IOException`. No exact cause has been
established. Attempt D adds private boot-stage failure diagnostics and passes;
this is successful execution evidence, not proof that the prior failures were
fixed. Linux continuation reliability remains an open qualification item. Failed
disks and receipts are preserved; no repair or automatic replay was attempted.

## Repeat the packaged workflow without rebuilding

To investigate an intermittent failure, reuse a completed, passing product
fixture's exact application/tools disk and base files:

```text
node scripts/repeat-linux-product-check.mjs artifacts/linux-product-requirements-20260913-d FRESH-NAME
```

This checks the saved disk hashes, runtime inventory, and pinned kernel/initrd,
then mounts the large inputs read-only. Only a fresh private overlay is written.
The diagnostic host retains the original bounds: two CPUs, 7 GiB container memory,
128 processes, no network interface, no GPU and a 650-second outer deadline.
The product inside the VM still uses its own independently checked worker limits.
The runner captures its source, commands, times, exact inputs and native result
under `artifacts/linux-product-repeat-FRESH-NAME`. It removes only its named
fixture container. Failure preserves the overlay and cannot trigger a retry.
No application rebuild, base-disk copy, model call or hosted job is needed.

Two unchanged-package repeats passed on September 14, at
`artifacts/linux-product-repeat-restart-20260914-a` and `…-b`. Both completed all
seven product checks with six synthetic replies each. Repeat B additionally
records the runner and outer kernel/initrd hashes; the initial runner used for A
did not record those hashes. These repeated passes do not explain or erase the
earlier continuation failures. The service-query hypothesis was checked against
[systemd 255's property-query implementation](https://github.com/systemd/systemd/blob/v255/src/systemctl/systemctl-show.c#L1964):
a missing collected unit is compatible with a successful `show` command, so the
current exit-code check was not relaxed.

### Retained failure C: later offline evidence

The outer fixture disk still had a pending ext4 journal. Its initial read-only
`debugfs` view therefore omitted later records. A separate raw diagnostic copy
was processed with `e2fsck -p -E journal_only`; that mode
[replays the journal without further checks or repairs](https://github.com/tytso/e2fsprogs/blob/v1.47.0/e2fsck/e2fsck.8.in#L206).
The original flat extraction's SHA-256 was unchanged, and neither original
failure disk was mounted by that operation. Command, exit status and before/after
hashes are retained in the case's `journal-only-receipt.json`.

The derived view includes the completed, unchanged-overlay recovery receipt and
the second boot's full observation: its guest reached readiness with the expected
resources. The first boot's console also contains ext4 write failures during
shutdown. The private records are `disk-inspection-journal-replayed.json` and
`second-boot-observation.txt` under the retained C case. These findings do not
identify the exact failed continuation operation or establish why the writes
failed. A successful QEMU exit and a clean qcow2 consistency check alone do not
establish that the guest filesystem saved every file successfully. No automatic
task retry, original-disk repair or application change follows from this evidence.

The nested worker disk was also extracted into a separate diagnostic image and
its journal replayed offline. The saved binding/context/configuration remain
parseable, their hashes agree, and the grant file has the expected structure.
The recovered Gateway log only establishes the first startup/shutdown; it does
not identify the later exception. Its trailing zero bytes also limit the
recovered log. The extraction commands, hashes and content-free checks are in
`worker-copy-receipt.json`; no credential values are included there. The separate
[guest shutdown check](GUEST_SHUTDOWN.md) now reproduces an unclean shutdown with
the old image and passes with the rebuilt image, including acknowledged-file
verification and refusal of a deliberately damaged copy. Authenticated Windows
product checks also pass. This does not establish the cause of failures B/C;
the exact failing Linux continuation stage remains the next diagnostic gap.

## Identifying a failed resume operation

The product now retains a host-defined step in a failed continuation receipt:
`reconcile`, `boot`, `refresh-grant`, `gateway-start`, `gateway-health`,
`resume-dispatch` or `repair-dispatch`. For example,
`resuming:refresh-grant:IOException` identifies a rejected grant refresh before
Gateway startup. Nested control calls preserve the first, most specific step.
Cancellation uses `interrupted`; cleanup failure still appends
`cleanup-unconfirmed`. Native output, exception messages and credentials are not
copied into the public failure code. This adds diagnosis, not retry authority.

The first VM transport exception is also written to a bounded private
`transport-failure.json` in that boot's directory before stopping the process.
It records the exception type, HRESULT and up to 2,000 message characters, without
command envelopes. The file is flushed and cannot overwrite an earlier receipt;
a failed diagnostic write cannot prevent containment. It is not part of the
product run/export. The native fixture includes it in its private failure evidence.

Eight injected failure/interruption cases use the actual OpenClaw wake sequence
through the product coordinator. They verify the saved step, revoked grant, absent
resume dispatch and absence of private command text in exported run/events. A
later controller tick cannot repeat the failed command or import an artifact.

To qualify a replacement prepared worker disk with a newly captured application,
pass it as the third argument to the product runner:

```text
node scripts/linux-product-check.mjs FRESH-NAME artifacts/linux-product-requirements-20260913-d artifacts/qemu-worker-shutdown-20260913-b/root.ext4
```

The runner records the replacement disk hash, embeds a sparse copy in the
read-only tools disk and changes only the fixture's installation pin. The old
payload and failed workspaces remain unchanged. The tools disk grows to 9 GiB
logical size; process, memory, network and time limits are unchanged. Subsequent
frozen repeats can reuse that tools disk without another image or application
build. A passing workflow alone does not establish why an older attempt failed.

The first replacement-image run, `artifacts/linux-product-resume-stages-20260913-a`,
reproduced the failure after two synthetic replies (260 fixture tokens). Its
receipt identifies `resuming:gateway-health:IOException:cleanup-unconfirmed`:
boot, grant refresh and the Gateway launch command completed before the health
check failed. The retained first-boot console confirms
`THADDEUS_VM_FILESYSTEM_CLOSED`. Thus the shutdown fix alone does not resolve this
continuation failure. Offline inspection used only diagnostic copies and preserved
the original overlay hash. The recovered Gateway logs have trailing zero bytes
and do not establish the underlying health/transport exception. This failed run
predates the new first-transport-error receipt; that receipt needs a fresh run.

The updated diagnostic build in `artifacts/linux-product-resume-stages-20260913-b`
and its unchanged-build repeat in
`artifacts/linux-product-repeat-transport-20260913-a` both pass all seven product
checks, with six synthetic replies (780 fixture tokens) each. Neither reproduced
the failure, so neither exercised the new transport-error file in a failing native
run. The file's bounded, first-write behavior is covered by a deterministic host
test. That checkpoint left a transport exception versus ten completed but
unsuccessful health probes unresolved; another passing repeat could not resolve
that distinction.

### Readiness failure captured before cleanup

The next fixture, `artifacts/linux-product-gateway-health-20260913-a`, retains
each completed Gateway health reply in its private boot directory. It reproduced
the continuation failure after two synthetic replies (260 fixture tokens).
The first boot became healthy on probe seven. All ten probes on the resumed boot
completed with exit code 1 and `ECONNREFUSED` at the guest loopback Gateway; no VM
transport exception was recorded. The ten failed replies span about 12.3 seconds.
This identifies an unreachable Gateway within the readiness window, not its cause.

Offline extraction and journal-only recovery were performed on separate raw
diagnostic copies, without booting either failed guest. The original outer overlay
and extracted worker overlay hashes were unchanged. The recovered first-boot
console confirms clean filesystem closure; the Gateway logs end at that first
shutdown followed by zero bytes. The second startup's cause therefore cannot be
recovered from those logs. Commands, hashes and private extracts are retained under
the failed case's `diagnostics` directory.

The product now also takes one read-only snapshot after the existing ten probes
fail and before cleanup. It captures a bounded Gateway console tail plus Linux
process state for Node/OpenClaw, without environment variables or command lines.
The private `gateway-startup-failure.json` retains at most 20,000 output characters
and 2,000 error characters. Like the health replies and first transport failure,
it is flushed, cannot replace an earlier receipt, and is absent from product
history/export. Failure to capture it does not replace the original readiness
failure or prevent containment. There is no additional Gateway start, task replay,
health retry or expanded readiness allowance.

The final local suite passes 682 backend tests, seven protocol checks and the web
build. Its new snapshot checks cover bounded first-write retention and a failed
observation without masking the readiness error or restarting the Gateway. The
native `startup-snapshot-20260913-a` fixture was deliberately interrupted when the
owner requested computer shutdown. Its separate `interruption.json` distinguishes
that cancellation from a product failure or success. Native execution of the new
snapshot remains unverified; resume with a fresh fixture, not the interrupted disk.

### September 14 continuation and diagnostic inventory

After explicit owner continuation, a frozen prepared build can be used from an
owner-interrupted case without another application or base-disk copy:

```text
node scripts/repeat-linux-product-check.mjs artifacts/linux-product-startup-snapshot-20260913-a startup-resumed-20260914-a --from-interrupted
```

This option requires the matching interruption record, completed publish/disk
preparation and successful container cleanup. The runner hashes the interruption
record, verifies all frozen inputs, records the source disposition and creates a
new outer overlay. It never boots the interrupted overlay. Ordinary repeats still
require a passing source case; the interrupted result is never relabeled as passed.

That attempt completed all three Gateway starts (seven, eight and ten probes),
the durable question/restart, bounded correction and exact approved import. It
made six synthetic replies accounting for 780 fixture tokens. Its final workspace
inspection refused the newly added diagnostic files because the removal inventory
still allowed only four older boot entries. Thus the run is failed, not qualified,
despite successful research. The separate intermittent startup error did not occur.

The removal inventory now recognizes health replies 01 through 10, the two exact
private failure-snapshot filenames and host boot-failure receipts. The boot entry
limit accounts for those files, the three ordinary logs/observation, the Linux
service receipt and the existing exact empty Windows cache tree. Each file still
participates in the reviewed inventory digest, regular-file/link checks and locked
file preflight. Unknown filenames, out-of-range reply numbers and credentials
remain refused. A regression fails before the fix; focused tests verify completed
removal, retained imports/history and refusal of post-review diagnostic changes.

The updated captured native case,
`artifacts/linux-product-cleanup-diagnostics-20260914-a`, failed during resumed
Gateway readiness after two synthetic replies (260 fixture tokens), before the
new removal path. It now contains the first successful native execution of
`gateway-startup-failure.json`: exit code zero, a runnable `openclaw-gatewa`
process (PID 1004), and a new Gateway log sequence ending in `starting HTTP
server...` at 11:51:59.093 UTC. The snapshot was recorded at 11:51:59.741 UTC.
The ten health probes completed with connection refused; no transport exception
was recorded. Thus the worker process was alive and actively initializing when
the probe allowance expired. Eventual readiness beyond that cutoff is not proven,
and the run was not resumed or replayed after failure.

All 688 backend tests, seven protocol checks and the web build pass for these
sources. The captured native package's 142 source files match that local receipt.
The startup snapshot has now been exercised in a real failing VM. A readiness
policy correction and native confirmation of reviewed removal remain the next
work; neither the failure observation nor the local tests close those requirements.
