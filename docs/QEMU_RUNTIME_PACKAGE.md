# Full QEMU runtime package verification

New managed QEMU launches require a host-selected runtime manifest, alongside the
existing executable, image-tool, kernel, initrd and base-disk pins. Checking only
the two executable hashes left their libraries and firmware outside the verified
set. The complete portable vendor tree is now checked, including those dependencies.

`QemuRuntimeLease` verifies the manifest's SHA-256 before parsing it, checks the
supported schema/version and bounded file list, and requires the configured
executables to belong to that manifest. Relative paths cannot select parent paths,
absolute paths, alternate data streams, Windows device names or ambiguous trailing
characters. Duplicate and case-colliding names are refused. The actual file tree
must match the declaration exactly; missing, unexpected, case-changed or linked
entries prevent execution.

Every existing runtime file is hashed and kept open with read sharing for the
backend's lifetime. On Windows, these handles prevent write/delete access to those
files. Inventory is checked again before a later host launch. Failure or disposal
releases the handles. The check does not grant an agent access to the package tree.
It also does not protect against a compromised host or make directory mutation
impossible: a new file introduced after a check is detected at the next inventory
check. Windows/system libraries remain part of the trusted OS, outside this vendor
manifest. Portable parser tests do not establish Unix read-share enforcement.

## Preparing the portable bundle

The preparation tool uses already cached inputs from `prepare-qemu-probe.mjs`.
It verifies the pinned 7-Zip tool/archive and vendor installer archive before
extracting a fresh directory. It runs no installer, changes no service or PATH,
and preserves the original input tree. The extracted QEMU executables must also
match their separate known SHA-256 pins. A manifest and its pinned reference are
written only after extraction and hashing succeed; failures retain their receipts.

```powershell
node scripts/prepare-qemu-runtime.mjs artifacts/qemu-inputs-script-check artifacts/qemu-runtime-FRESH
if ($LASTEXITCODE -ne 0) { throw 'Portable package preparation failed' }
dotnet build --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
node scripts/qemu-managed-integration-check.mjs artifacts/qemu-inputs-script-check artifacts/qemu-worker-20260912-native scripted-web artifacts/qemu-runtime-FRESH/runtime-reference.json
```

The generated `runtime-reference.json` contains the runtime directory and a pinned
`runtime-manifest.json` path/hash. The integration runner includes this reference
in its new `managed-installation.json`. That installation can then be used by new
Native Lab registrations. Older five-file registrations and captures remain
readable and can be regraded; new registration/startup refuses a missing runtime
package rather than silently selecting another installation. Existing stopped
workspaces need an explicitly verified installation configuration for further
execution; no historical registration is rewritten or replayed automatically.

This establishes byte integrity relative to the host's trusted pin. It does not
claim publisher code signing, secure automatic updates, a signed product release,
or completed production admission.

The [Linux session integration](LINUX_QEMU_SESSION.md) uses a separate Linux x64
manifest kind. It requires read-only filesystem storage and verifies actual
executable mappings against its bundled loader/libraries. Windows read sharing
is not treated as a portable Unix protection. The Linux development build's
expired signer is recorded explicitly; publisher qualification remains open.

## September 13 evidence

Preparation at `artifacts/qemu-package-integrity-20260913/prepared-a` produced
3,389 files from the pinned vendor archive. Its manifest SHA-256 is
`75496438a5326e277451748cf249c74c224864223235ee3284fa914c57dfdc4d`.
The archive URL and SHA-512 provenance, extraction commands and outcomes are
retained in the manifest and preparation receipt. No source disk or existing
portable tree was replaced.

At source `3ad2211`, the real OpenClaw/QEMU public-source workflow passed using
that bundle: selected note, scoped retrieval, durable question, entire VM shutdown,
new process on the saved overlay, continuation and exact approved import. Receipt:
`artifacts/qemu-managed-scripted-web-1789308260562`. It used five synthetic replies
and 650 synthetic tokens, with zero live inference or GPU use. Both boots retained
the previously verified host resource controls. No worker or temporary host remained.

Post-run checks independently rehashed all 3,389 runtime files and six top-level
pins, and matched the saved host file to the approval content. The complete backend
suite has 379 passing tests, including 20 package cases. Windows tests check actual
write-handle refusal and release; the Unix-specific link fixture runs in Linux CI.
Read-only regrading preserved both the earlier six-case synthetic protocol pass
and the earlier live contract-2 pass, with their original evidence and efficacy
limits intact. An attempted manual live-lock observation occurred after the VM
had exited and supplies no additional lock evidence; the real handle checks are
the explicitly identified fixture tests.

The preparation/reference files are developer tooling. Guided install/update,
release trust, transport abuse qualification, broader recovery and non-Windows
VM hosting remain separate delivery work. The running personal development host
and its data were not changed by this checkpoint.
