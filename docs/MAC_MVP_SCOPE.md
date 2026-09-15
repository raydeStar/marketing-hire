# Mac MVP scope — September 15, 2026

This is a scope and recommendation, not an implementation or a verified Mac
release. The owner requested scoping only. Windows manual QA can continue now;
Mac work does not require another benchmark campaign.

## Recommendation

Keep the existing web UI, .NET host and capability broker. For local Mac research,
extend the existing QEMU backend to use Apple's Hypervisor.framework (HVF), with
native ARM64 and x64 worker packages. Ship a signed, notarized `Thaddeus.app`
through a normal download. Hide the VM machinery inside the product: end users
should not install Docker, Homebrew, Node, .NET or a hypervisor manager.

This is the route with the most reuse in this repository, not a claim that QEMU
is universally better than Apple's Virtualization.framework. QEMU documents HVF
on both Arm and x86 Mac hosts; Apple documents the hardware support check and
required hypervisor entitlement. Actual package compatibility still needs native
verification. [QEMU accelerators](https://www.qemu.org/docs/master/system/introduction.html),
[Apple Hypervisor](https://developer.apple.com/documentation/hypervisor).

Deliver Apple silicon first within the port, then verify the Intel build before
claiming both. Intel remains part of this scope; sequencing does not remove it
from the owner's cross-platform goal. Recommend finishing the current Windows
MVP/QA independently and treating this as a finite Mac milestone. That release
sequencing is a recommendation, not a decision already made by the owner.

## What exists and what must change

Audited against `af75fadb92f4d1cef28199ed4e1236c366aed07f`.

| Area | Existing code | Remaining Mac work |
| --- | --- | --- |
| Product and policy | Responsive UI, study persistence, approvals, accounting, research control and broker | Native acceptance of the same flows; no UI or policy rewrite planned |
| Host packaging | `publish-portable.mjs` accepts native `osx-arm64` and `osx-x64` targets; `.command` entry opens the self-contained host | Build and verify on actual Macs; add a consumer `.app`, visible failures, Dock reopen and clean quit |
| Credentials/data | `NativeCredentialVault.cs` implements Security.framework Keychain calls; `DesktopLaunch.cs` selects Application Support | Test Keychain denial/lock, relaunch, signed upgrades and backup/restore on Mac; implementation is not acceptance evidence |
| Hardware plan | `QemuLaunchArguments.cs` describes Mac x64/q35/HVF and ARM64/virt/HVF | Qualify each real machine/kernel/serial combination; these plan records do not enable a worker |
| Worker admission | `NativeWorkerPlatform.cs` selects Windows x64 and Linux x64 only | Mac prerequisite checks, runtime identities and explicit enrollment; refuse unsupported hardware clearly |
| Process ownership | `QemuHostProcess.cs` supplies Windows Job and Linux systemd implementations | Mac process owner, authenticated lifetime binding, teardown on host death, resource policy and observations |
| Runtime and disks | Full package manifests, immutable inputs and overlays; OS-specific locks | Mach-O dependency/signature inventory, library loading policy, Mac disk exclusion and immutable input handling |
| Guest | Pinned x64 Linux kernel/root filesystem, OpenClaw integration and broker protocol | ARM64 Linux kernel/initrd/root plus matching Node and native dependencies; separate pins and notices |
| Distribution | Portable publisher, package capabilities and study transition checks | Signed app/worker packaging, notarization, downloaded-file acceptance, upgrade/recovery and source/notice completion |

Important concrete gaps: `QemuSandboxBackend.Boot` starts only Windows or Linux
owners; `QemuRuntimeLease` accepts only Windows/Linux runtime kinds. Linux's OFD
disk-lock implementation is explicitly x64/Linux. Windows sharing flags and
Linux read-only-mount checks are not a proven Mac equivalent. Switching a platform
enum to `qemu-hvf` would skip these contracts.

The source supports Mac host packaging; this audit does not establish an existing
verified Mac deliverable. The current Mac launcher leaves a Terminal window open.

## Options and tradeoffs

| Route | User experience | Scope/tradeoff |
| --- | --- | --- |
| Browser connected to a supported Windows/Linux host | Open a paired URL on Mac or phone; no local worker install | Smallest client scope, but requires an awake host and working private HTTPS/pairing. It does not deliver standalone Mac research. Native Safari/device acceptance remains. |
| Bundled QEMU + HVF — recommended | Install one Mac app; local worker managed behind the UI | Reuses QMP, serial broker, QCOW2 overlays and much lifecycle code. Still needs Mac ownership, runtime integrity, ARM guest and distribution work. |
| Apple Virtualization.framework helper | Same one-app experience; Apple provides more of the VM device layer | Credible alternative, but introduces a Swift/Objective-C helper and replaces QEMU-specific control, transport attachment and disk lifecycle assumptions. Scope separately if the HVF feasibility gate fails. |
| User-managed Docker/Lima/Colima/UTM | Install/configure another application | Moves setup and lifecycle responsibilities onto nontechnical users. Not the recommended MVP path; no installation or experiment is authorized by this scope. |

Apple's Virtualization.framework supports custom Linux guests on Apple silicon
and Intel; the guest images must match the host architecture. The current x64
Linux estate therefore cannot be assumed to work unchanged on Apple silicon.
Use native ARM64 dependencies rather than adding translation as an MVP
requirement. [Apple Linux VM guidance](https://developer.apple.com/documentation/virtualization/creating-and-running-a-linux-virtual-machine),
[architecture-specific Linux images](https://developer.apple.com/documentation/virtualization/running-gui-linux-in-a-virtual-machine-on-a-mac).

The backend abstraction can hide platform differences from Thaddeus and its user.
It cannot remove the need for platform-specific implementations. A phone remains
a client of an awake computer or a separately operated hosted service. Hosted
compute, account operations and its recurring cost are not added to this MVP.

## Finite work plan and estimate

Planning estimate for one engineer with native Mac access: **15–30 focused
engineering days for Apple silicon and Intel**, excluding waiting for hardware,
account enrollment or signing access, and excluding unresolved shared worker
redistribution work. This is an estimate from the code audit, not a delivery date
or a benchmark-derived prediction. The largest uncertainty is the Mac process,
disk and resource boundary.

1. **Feasibility decision: 1–2 days.** On one Apple silicon Mac, exercise a
   deliberately small matching Linux guest with HVF, no guest NIC, and the existing
   authenticated broker channel. Prove native helper launch and abrupt-owner-loss
   behavior; identify enforceable host resource and disk controls. Document what
   is OS-enforced versus observed. Stop and revise this scope if the boundary
   cannot be met; do not silently fall back to host execution or software CPU
   emulation. This is a future gate, not a test started during scoping.
2. **Mac host and worker adapter: 4–8 days.** Implement ownership, cancellation,
   timeout/crash recovery, disk leases, integrity checks and prerequisite UI.
   Retain QMP/serial framing and capability routing where verified compatible.
3. **Native guest/package inputs: 3–6 days.** Produce one pinned ARM64 base with
   matching OpenClaw/Node/native modules and boot drivers. Bind source/notices to
   those actual versions. Package the Intel runtime and verify reuse of the x64
   guest rather than rebuilding it without cause.
4. **Consumer app and trust: 4–8 days.** Add a small native launcher, app identity,
   icon, Open/Show study and Quit behavior. Package only the selected architecture,
   sign nested native components, notarize/staple the deliverable, and verify
   Keychain identity, upgrades, restore and recovery through actual downloads.
5. **Targeted acceptance and fixes: 3–6 days.** Run the finite checks below on both
   architectures, remove all disposable fixtures and hand over the exact builds.

The estimate assumes the feasibility gate passes without an architecture change.
The workstream ranges total 15–30 days; they are not parallel delivery promises.
No Mac implementation, binary download, VM build or native Mac verification was
performed for this document.

## Installation experience and support boundary

Target flow: download the appropriate build, drag `Thaddeus.app` to Applications
or the user's Applications folder, open it, connect a model and enable local
research. The app checks hardware, memory and free disk before preparing its
worker. It reports download size and retained storage, exposes Stop research,
and opens the familiar study in the browser. A separate embedded webview is not
required for this milestone. Mac startup errors must remain visible without a
Terminal window. Closing a browser tab and quitting the host have distinct,
clear behavior.

Use separate Apple silicon and Intel downloads to avoid shipping two large guest
images. Propose macOS 14 or newer, subject to the pinned QEMU/toolchain's actual
support and native acceptance. Microsoft's current .NET 10 matrix includes
macOS 14, 15 and 26; that matrix alone does not certify this app or all future OS
versions. Older or unsupported Macs may use the browser-client route.
[Microsoft macOS support](https://learn.microsoft.com/en-us/dotnet/core/install/macos).

The current worker profile is 4 GiB guest RAM plus 1 GiB Windows host-process
allowance. Mac overhead and a minimum supported machine specification remain
unmeasured. Use a 16 GiB development Mac; do not advertise 8 GiB-machine support
until the bounded workflow passes there. Avoid broad performance work: measure
only enough to select a safe memory/disk admission rule.

Developer prerequisites are an Apple silicon Mac and access to an Intel Mac for
its acceptance, native build tools, and a Developer ID signing identity. Apple
Developer Program membership is currently USD 99/year (regional prices and
eligible fee waivers vary). That is a publisher expense; users do not need a
Docker account or paid Apple developer membership. No purchase/enrollment is
part of this scope. [Apple membership](https://developer.apple.com/programs/enroll/),
[Developer ID](https://developer.apple.com/developer-id/).

Apple requires valid signing of distributed executables, hardened runtime and
notarization for this distribution path. Review entitlements per executable:
the QEMU process needs hypervisor access, and the existing non-AOT .NET apphost
needs JIT permission. Do not blanket-copy development/debug entitlements into a
release. Keep manifests/hashes bound to final signed bytes and package identity
stable across upgrades. [Apple notarization](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution),
[Microsoft Mac publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos).

## Acceptance sufficient for this milestone

- Fresh downloaded app opens, shows understandable setup/errors, reopens the
  same study on a second launch, and quits without leaving its worker running.
- Keychain save/read/forget and denied or locked access work; no provider secret
  appears in the guest, its environment, fixtures or logs.
- One scripted research task follows selected note/link → broker → artifact →
  exact user approval → import. Usage, cancellation, guidance and history remain
  accurate. Synthetic model/search responses avoid consuming provider quota.
- Guest has no host shares or direct NIC. Unapproved capability calls and changed
  import content fail. The selected filesystem/network boundaries remain intact.
- Abrupt host/helper exit, timeout, sleep/wake and normal stop leave no orphaned
  worker; recovery never replays an uncertain consequential action automatically.
- Wrong architecture, altered runtime/base and occupied disks are refused. Show
  the actual resource policy; guest RAM/vCPU configuration alone must not be
  described as a hard cap on the entire host process. Finalize the Mac enforcement
  mechanism in feasibility before claiming parity with Windows/Linux.
- Upgrade and restore preserve a fictional study; removal preserves user data.
  Native results identify architecture, OS, final signed package and worker pins.

Retain at least 10 GiB free during build/test admission. Reuse one immutable base
per architecture and fresh small overlays; budget actual peak allocation before
the first ARM image. After owned processes exit, automatically remove disposable
packages, disks and test registrations, including on failures. Keep compact
receipts, one rollback, active inputs and user data. No GitHub Actions, live
model/search calls or GPU work in routine checks. Stop once the changed contracts
pass; benchmark optimization remains deferred.
