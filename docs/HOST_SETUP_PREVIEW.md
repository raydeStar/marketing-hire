# Guided Windows development setup

This is an explicitly installed development preview, not a qualified release or
an automatic fallback from Docker Sandboxes. An ordinary installation remains
unconfigured until the host operator supplies a pinned worker package. The
browser cannot choose executables, file paths, or a different isolation backend.

## Owner workflow

The host operator sets `Thaddeus:DevelopmentWorkerInstallation` to an absolute
path containing the `kind: qemu` installation JSON produced by the managed QEMU
workflow. It must include the complete runtime manifest described in
[QEMU_RUNTIME_PACKAGE.md](QEMU_RUNTIME_PACKAGE.md). A separate loopback
`Thaddeus:WorkerPort` carries authenticated worker requests.

In Settings, **Set up this host** shows the configured installation, its preview
status, the model selection, and the usage/approval boundaries. **Check installed
worker** verifies the runtime tree, five VM inputs and executable version; it
does not boot a VM or dispatch a model. The owner can then enable research.

Enablement is stored against the exact installation/configuration digest.
Changed configuration requires a fresh check and enablement. Checks used to
enable expire after ten minutes. A failed recheck disables new research; every
worker launch also validates the pinned files. Setup changes and task admission
share a lock, and setup changes are refused while research is unfinished.
Disabling admission preserves cleanup of already recorded tasks.

The fixed guest relay address is `http://127.0.0.1:5182` **inside the VM**. Its
authenticated serial channel forwards to the separately configured host port.
These ports need not match. The browser and worker listeners remain separate.

This does not install QEMU for a nontechnical user, qualify signing/updates,
support a macOS/Linux worker, or verify a physical phone. Those delivery gates
remain open. A phone is a client of a supported host, not the VM host itself.

## Verification, 2026-09-13

- 391 backend tests passed, including installation-bound enrollment, expiry,
  failed-check disablement, admission serialization and owner-only routes.
- Twelve ordinary browser checks passed with unconfigured research disabled.
- The native browser fixture used the actual product entry point and configured
  worker factory, UI port 5182 and host worker port 5184. Setup check/enable,
  reload, selected memory, durable question, VM restart, one rejected quotation
  and bounded repair, exact approved import and reviewed workspace removal
  passed. Seven synthetic calls; zero live model calls or GPU inference.
- The final worker grant was revoked, the reviewed workspace was removed, and
  the test host/VM/listeners terminated. Desktop and 390-pixel setup layouts
  were visually inspected; this is responsive-layout evidence, not phone proof.

Private receipts: `artifacts/host-setup-20260913/native-browser-c/verified.json`,
`browser-results.json`, `setup-network.json`, and the setup screenshots.

Two earlier attempts are retained. Attempt A timed out waiting for the package
check; the exact cause was not established. A direct check and both later
browser checks completed in about ten seconds. Attempt B exposed a real routing
bug: OpenClaw received the host port instead of the fixed guest relay port. It
made zero model calls and was cancelled/retired with its private workspace kept.
Attempt C verifies the port correction through the complete workflow. No failed
attempt was overwritten or relabeled as a pass.
