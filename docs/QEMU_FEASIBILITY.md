# QEMU feasibility on Windows

September 12, 2026. A real Linux VM booted on this Windows host using QEMU and
Windows Hypervisor Platform (WHPX), with no Docker account or Docker runtime in
the execution path. It passed a bounded virtual-channel round trip and host
pause/resume/guest-shutdown checks. This establishes a candidate for an explicit
second backend. It does **not** qualify a production backend or close any of the
six [accepted delivery gates](IMPLEMENTATION_PLAN.md).

A subsequent [native VM integration](NATIVE_VM.md) now runs the pinned OpenClaw
package across the real brokers, including a complete VM restart and approved
import. That later evidence extends this initial probe without enabling a
production backend.

Docker Sandboxes remains the first backend named in the accepted contract. Its
Windows image-service failure is still unresolved. No product backend selection,
worker admission, Docker settings, host virtualization features, model service,
GPU allocation or existing user data was changed by these probes.

## What ran

| Component | Observed input or behavior |
|---|---|
| QEMU | Windows build dated 2026-08-11; reported `11.1.0 (v11.1.0-12130-ge470268ff4)` |
| Accelerator | Explicit `whpx`; no accelerator fallback list or TCG fallback |
| Guest | Alpine Virtual 3.24.1, Linux `6.18.35-0-virt`, x86-64 |
| Resources | QMP reported one vCPU and 536,870,912 bytes base RAM; guest reported one CPU |
| Storage | One read-only ISO block device; guest root/workspace used guest RAM |
| Network | `-nodefaults -nic none`; QMP PCI inventory had no network controller; guest saw only loopback |
| Host sharing | No filesystem shares, host disks, Docker socket, GPU, clipboard or credential forwarding configured |
| Control | QMP over inherited process stdio, outside the guest |
| Test channel | Guest virtio serial port to a host loopback socket; 65,536 random bytes saved in the guest and returned exactly |
| Lifecycle | QMP prelaunch, pause and resumed-running observations; guest shutdown event and process exit 0 |

The loopback sockets join the host probe process to QEMU's character-device
backends. They are **not guest network adapters**. Each listener binds only
`127.0.0.1` and closes after accepting one connection. This diagnostic uses no
credentials and provides no application API. It does not yet establish process
identity or protect that transport against another local process racing to
connect; production transport/authentication remains open.

The guest's own observations came from a fixed pre-agent script. They are useful
confinement evidence, corroborated by host launch arguments and QMP inventories,
but are not evidence that a hostile agent cannot escape QEMU or exhaust host
resources. Guest reports must never become authoritative effect receipts.

## Reproduce

These commands require Windows x64, Node, and an already-working WHPX facility.
They neither enable Windows features nor restart the computer. Run from the
repository root, using a **fresh** preparation directory:

```powershell
node scripts/prepare-qemu-probe.mjs artifacts/qemu-inputs-local
if ($LASTEXITCODE -ne 0) { throw 'QEMU probe preparation failed' }
node scripts/qemu-feasibility-check.mjs artifacts/qemu-inputs-local
if ($LASTEXITCODE -ne 0) { throw 'QEMU feasibility check failed' }
```

Preparation downloads the exact files in
[`feasibility-lock.json`](../workers/qemu/feasibility-lock.json), verifies their
digests, and extracts the installers as archives. It does not execute either
installer or change PATH. It refuses to overwrite an existing preparation
directory. The probe rechecks all pinned inputs before launching QEMU, uses a
60-second VM deadline, caps captured output and transport bytes, and terminates
only its owned process if the check fails. Each run writes a new private receipt
under `artifacts/qemu-probe-*`; failed evidence remains available.

This is a diagnostic tool, not a lifecycle service. In particular, abrupt death
of the diagnostic host is not yet covered by Windows Job Object cleanup or
durable process ownership recovery. Do not expose it as a user backend.

## Findings that changed the probe

- A large command sent in one write to the emulated UART lost characters. Small
  paced writes were sufficient for the fixed bootstrap. Actual payload transfer
  uses a virtio serial port and exact byte/hash comparison, rather than the UART.
- Windows QEMU named-pipe character devices did not complete the tested command
  exchange reliably: one attempt stalled after two QMP replies, another reached
  guest login but did not deliver the login input. The evidence establishes those
  observed failures, not their root cause. The passing probe uses QMP stdio and
  event-driven host loopback character-device sockets instead.
- Windows can report `ECONNRESET` while the guest powers off. A reset is tolerated
  only after the probe requested shutdown; success still requires the independent
  QMP `guest-shutdown` event, expected guest output and QEMU process exit 0. A
  transport reset during the exchange fails the probe.

Initial passing full-channel receipt:
`artifacts/qemu-probe-1789253049687-cf32d53b/receipt.json`.
Its 65,536-byte host/guest/returned SHA-256 was
`93bb2b5ae9f9619b709d90c167c15e38ac22efb595c7abe93a694bd775213705`.
Earlier paused-only, Linux-only, timed-out and shutdown-race receipts are retained.
No model calls were made; no OpenClaw instance ran inside these VMs.

The checked-in preparation script then independently downloaded and extracted a
fresh input directory, `artifacts/qemu-inputs-script-check`. The checked-in probe
passed against those inputs in
`artifacts/qemu-probe-1789253219831-a06c9b2d/receipt.json`, with host/guest/returned
SHA-256 `a96637650e35ae31b913e39b4961108066070af81f7af4e8cd12fa45438dde51`.

## Before product use

An explicit QEMU backend still needs a maintained worker image containing the
pinned OpenClaw package; a bounded, authenticated protocol for commands and broker
messages; external credential custody; private persistent disks and constrained
artifact transfer; process/resource ownership and crash recovery; reconciliation
and cancellation; and the same native workflow and negative controls required of
any other backend. Host loopback transport alone is not a permission system.

Distribution also needs maintained binary provenance, dependency/license and
security-update handling, installer/upgrade/rollback support, and a measured
support matrix. This downloaded Windows build is described by its publisher as
experimental. Its SHA-512 matched the publisher's checksum, but Windows reported
`UnknownError` for Authenticode validation; a valid code-signing chain was not
established. The diagnostic pin is not a release endorsement.

QEMU documents WHPX for Windows, HVF for macOS and KVM for Linux. That makes it a
plausible cross-platform implementation behind `ISandboxBackend`, not proof that
this implementation works on macOS or Linux. Phones remain browser clients of a
supported host; physical phone setup remains deferred.

Sources: [QEMU downloads](https://www.qemu.org/download/),
[Windows build publisher](https://qemu.weilnetz.de/w64/),
[QEMU accelerators](https://www.qemu.org/docs/master/system/introduction.html),
[QEMU devices and transports](https://www.qemu.org/docs/master/system/invocation.html),
[Alpine downloads](https://alpinelinux.org/downloads/),
[7-Zip downloads](https://www.7-zip.org/download.html).
