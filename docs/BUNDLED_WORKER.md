# Included worker development preview

The host can discover a platform-matched `worker/installation.json` beside its
executable. Windows' PowerShell launcher and the portable desktop entry point use
the same fixed location. An explicit operator installation still takes precedence.
An owner checks and enables the worker in Settings; discovery starts no VM and
does not admit research by itself. Moving the package changes its installation
digest and invalidates the previous enrollment.

The version-one descriptor uses relative paths and pinned hashes. Resolution
rejects traversal, links, duplicate or unknown fields, invalid hashes, unsupported
platforms and executables outside the included runtime. The existing complete
runtime inventory and read locks remain responsible for verifying inputs before
use. This is integrity verification, not a publisher signature.

`tools/Thaddeus.WorkerBundle` prepares independent copies from an explicit pinned
operator installation. Every copied file is hashed again; the descriptor is
written last. Failed attempts cannot appear complete. New sparse files retain
zero-filled ranges; Windows also respects compression inherited from the chosen
destination. Free space for the entire logical copy plus a 10 GiB reserve is
required before output; the reserve is checked during copying. This is not a filesystem quota or a guarantee against
concurrent writers. The original images and runtime are never modified.
See [operator preparation and end-user setup](PORTABLE_PACKAGES.md#included-worker-preview).

## Evidence on Windows, 2026-09-14

- `artifacts/local-check-bundled-worker-20260914-b`: 722 backend tests, seven
  protocol tests, locked restore, secret scan and web build pass. Input hashes
  are unchanged through the check.
- `artifacts/local-check-bundled-worker-delivery-20260914-a`: native portable
  package, native credential lifecycle and ordinary browser checks pass.
- `artifacts/bundled-worker-launcher-20260914-a/verified.json`: the desktop
  entry point discovers the worker without an installation profile field; the
  owner checks and enables it through the UI. After moving the app to a path
  containing spaces and Unicode, the Windows launcher requires a fresh check
  and the owner can enable it again. A changed descriptor hash refuses admission.
  The original base image hash is unchanged, and test hosts close through owner
  maintenance controls.

The launcher fixture uses hard links to existing immutable worker inputs because
this computer cannot hold a second full copy with sufficient free space. It
verifies discovery, relocation and admission, **not independent full-size bundle
preparation**. The preparation tool itself does not create hard links. Tests
verify independent copies, tamper detection, interruption, sparse bytes and
mid-copy space loss with bounded inputs.

Three full-size preparation attempts remain recorded under
`artifacts/worker-bundle-20260914-{a,b,c}`. The first exhausted free space; the
following two stopped with the initial 512 MiB space guard, since raised to
10 GiB with a conservative preflight for the full copy. Their incomplete generated files
were removed after recording exact inventories to reclaim space; logs and
cleanup receipts remain. No installation descriptor was published, and the
original base hash still matches. Compression on newly created attempt folders
did not provide enough headroom. Full independent preparation remains unverified
at this size on this machine.

## Still open

The relocated native research run was interrupted at the owner's request to
address disk exhaustion. Its receipt is a failure/interruption, not another
passing VM workflow: `artifacts/bundled-worker-native-20260914-a`. No live model
or GPU was used. The separate launcher/admission proof above passed.

Combined downloadable archives/installers, publisher signing, upgrades and
rollback, distribution qualification, native Linux bundle discovery and macOS
workers remain open. A relocated unsigned Windows fixture does not qualify those
platforms or close the six product delivery gates. Physical-phone setup remains
deferred and user-operated. No live model, GPU inference or GitHub Actions are
needed for the checks above.

Windows filesystem references: [compression inheritance](https://learn.microsoft.com/en-us/windows/win32/fileio/compression-attribute)
and [sparse file creation](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_set_sparse).
