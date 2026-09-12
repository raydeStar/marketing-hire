# Native OpenClaw inside a Windows VM

The September 12 development fixture runs the pinned OpenClaw loop inside a real
QEMU/WHPX Linux VM. It uses Thaddeus's existing MCP, capability, model, context,
durable-command and approval mechanisms outside that VM. Both selected-note and
public-page workflows passed a complete VM shutdown/restart before continuation
and exact approved import.

This is a native integration checkpoint, **not a product backend admission**.
The application still has no enabled production research worker. Docker Sandboxes
remains blocked by its Windows image service; none of the accepted delivery gates
is being declared complete by this fixture.

## Arrangement

- Windows runs the isolated NativeCheck data store and the real .NET brokers.
  Owner/controller credentials and model-provider access remain there.
- QEMU runs two vCPUs and 4 GiB RAM through explicit WHPX. The tested virtual CPU
  is `qemu64,-svm`; there is no accelerator fallback list.
- The guest uses the pinned worker's Ubuntu 26.04 filesystem and Alpine's pinned
  Linux `6.18.35-0-virt` kernel/initramfs. Observed user-space versions are
  OpenClaw `2026.9.4` and Node `24.19.0`; Python comes from the same pinned image.
- An 8 GiB ext4 base holds the worker package. Every integration run creates its
  own qcow2 overlay. QEMU opens the base read-only; only that run's overlay changes.
  No host folders, host disks, Docker socket, GPU or guest network adapter are
  attached. QMP and guest observations corroborate the configured devices.
- A small guest PID 1 mounts runtime filesystems, activates loopback, sets normal
  device permissions, and starts the command/HTTP relay as UID/GID 1000. OpenClaw
  and its commands run inside this guest as the agent user.
- The virtual serial channel carries bounded JSON messages between the guest
  relay and a separate host fixture. Host requests execute inside the guest.
  Guest HTTP requests can reach only the current task's MCP and model routes.
  Host-side loopback sockets join the fixture and QEMU; they are not guest NICs.
- `OpenClawBackend` still issues the same public Gateway RPCs. The fixture's
  `ISandboxBackend` transport uses a private authenticated host endpoint to reach
  the virtual channel. That endpoint's owner token is never sent to the guest.
  The guest receives only the short-lived, task-scoped broker grant.

The model response source is explicit: `scripted` and `scripted-web` are synthetic
tool proposals, not model-capability evidence. OpenClaw itself performs tool
execution, transcript persistence and continuation. `luna` remains a separately
authorized option; these VM cases made no live model calls or GPU requests.

## What the workflow proves

1. An authenticated host command reaches the native worker and observes the exact
   OpenClaw version. An unauthenticated command request receives HTTP 403.
2. The native engine reads the selected note through the real scoped MCP broker.
   The public variant also fetches the granted public page outside the VM; the
   complete retrieved text and URL are observed in subsequent native model input.
3. The engine creates one durable question. Shared execution control records a
   quiesce command and its acknowledgement before shutdown.
4. The entire QEMU process exits after an observed guest shutdown. A new QEMU
   process boots the same private overlay. The native Gateway starts from the
   persisted worker state, without replaying bootstrap or issuing a new grant.
5. The stored answer resumes the same OpenClaw session. The native file tool
   creates `summary.md`; a scoped MCP proposal names its exact proposed import.
6. The host reads the actual guest artifact before fixture approval, compares its
   contents with the proposal, and uses the existing exact approval/import path.
   Host validation and readback, rather than a worker completion claim, establish
   the fixture's successful result.

All four shared commands—start, quiesce, resume, quiesce—must be acknowledged, and
each model dispatch must show the frozen context. A VM process restart is required;
a Gateway-only restart or a successfully submitted restart command cannot pass.

The negative cases observed public TCP as `ENETUNREACH` (101), the host app's
loopback port as `ECONNREFUSED` (111), product/controller routes as relay-denied
502 responses, and a task MCP request without its grant as HTTP 403. These are
specific observations, not an exhaustive VM escape assessment.

## Inputs and reproduction

QEMU/kernel/download pins are in
[`feasibility-lock.json`](../workers/qemu/feasibility-lock.json). The worker image
used on this checkout is
`sha256:d3fef0da199e9b1006d150950668bcb8580f9b7bd386152eb8674b66837cd2e8`,
built from the [pinned OpenClaw package](../workers/openclaw/README.md). The disk
builder deliberately requires this locally available image; a new image pin is a
separate input change, not an implicit update. These are Windows developer checks,
not installer instructions for end users.

From the checkout, with fresh artifact directory names:

```powershell
node scripts/prepare-qemu-probe.mjs artifacts/qemu-inputs-new
if ($LASTEXITCODE -ne 0) { throw 'Portable QEMU preparation failed' }
node scripts/build-qemu-worker.mjs artifacts/qemu-worker-new
if ($LASTEXITCODE -ne 0) { throw 'Worker disk build failed' }
dotnet restore --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed' }
dotnet build tools/Thaddeus.NativeCheck --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Native fixture build failed' }
node scripts/qemu-native-integration-check.mjs artifacts/qemu-inputs-new artifacts/qemu-worker-new scripted-web
if ($LASTEXITCODE -ne 0) { throw 'Native VM integration failed' }
```

The disk builder uses ordinary Docker **only as a build tool**: it copies the
pinned image, adds the guest init/relay files, exports the filesystem, and creates
ext4 with the image's existing `mke2fs` 1.47.2. A two-CPU build container mounts only
the fresh output directory and runs no agent. `e2fsck -fn` must pass. The receipt
records commands, source hashes and the finished disk hash. The integration tool
checks those hashes, including current guest source hashes, before booting.

The VM phase uses QEMU directly; it does not start a Docker worker. Every run has
a fresh private artifact directory. Receipts may contain fictional task text and
short-lived fixture credentials, so `artifacts/` remains excluded from Git.

## Current evidence

- Existing container control after the fixture transport refactor:
  `artifacts/native-integration-scripted-1789254273802` — passed.
- Native VM selected-note/restart/import case:
  `artifacts/qemu-native-scripted-1789254798236` — passed with four scripted model
  calls, five host capability calls, all context observations and all command
  acknowledgements. Both VM processes exited with code 0.
- Native VM public-source/restart/import case, including network/broker negatives:
  `artifacts/qemu-native-scripted-web-1789254989063` — passed with five scripted
  model calls and six host capability calls. It retrieved 9,529 bytes from the
  granted Docker FAQ; full source text/URL consumption was checked. The returned
  source text SHA-256 was
  `1337655061b6541cc00c114d1deaf583726aac8bcfa6d64abd508247002bc05f`.

These first passes used a copied diagnostic disk with exact-readback init fixes;
the original disk and failed attempts remain available. Initial failures exposed
an unsupported Windows memory-dump option, a WHPX failure with `-cpu max`, missing
`iproute2`, and restrictive initial `/dev/null` permissions. The corrected source
uses the previously demonstrated CPU profile, a Linux loopback ioctl and standard
device permissions. No new package installation was needed.

The final clean build, made entirely by `build-qemu-worker.mjs`, is
`artifacts/qemu-worker-20260912-native`. Its filesystem check passed and its base
disk SHA-256 is
`7bfb7ec84f87c9fd29d07c6c813ef12147535d588f0a5e64881a1342e4110351`.
The complete public workflow, negative cases, VM restart and exact import passed
against that build in `artifacts/qemu-native-scripted-web-1789255543339`.
This final case did not depend on the earlier diagnostic disk patches.
An independent post-run check confirmed the unchanged base hash and clean exit
of both observed QEMU processes (50196 and 54356); its receipt is
`independent-check.json` in the same integration directory. All owned test VMs,
builder containers and the separate fixture host were stopped afterward.

The 186-test backend suite and seven existing protocol tests also passed after
the fixture transport refactor. Guest init sources use explicit LF checkout
rules so Windows line-ending conversion cannot invalidate the Linux entrypoint.

## Still required

Production needs a real registered `ISandboxBackend`, guided admission, broker
transport/process authentication, cancellation of in-flight requests, bounded
concurrency under hostile input, crash/uncertain-effect recovery, Windows Job
Object ownership, disk quotas and native tool/process budgets. The fixture's
happy-path restart is not proof of power-loss recovery. Same-user loopback races,
unexpected host termination and deliberate resource exhaustion remain outside
the current evidence.

The prepared Ubuntu/QEMU combination also needs a maintained update and release
pipeline, binary provenance/signing and dependency/license review. The Windows
build's Authenticode chain remains unverified, as recorded in
[QEMU feasibility](QEMU_FEASIBILITY.md). No macOS/Linux host qualification or
physical-phone test is implied. The existing UI and running development host were
preserved; this native fixture is not silently selected by ordinary chat.
