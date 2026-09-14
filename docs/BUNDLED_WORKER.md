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

- `artifacts/local-check-worker-progress-20260914-a`: 729 backend tests, ten
  protocol/storage tests, locked restore, secret scan and web build pass. Input hashes
  are unchanged through the check.
- `artifacts/local-check-worker-progress-delivery-20260914-a`: 17 native portable
  package, five native credential lifecycle and 20 ordinary browser checks pass.
  The publication removes its captured build intermediates after execution.
- `artifacts/bundled-worker-launcher-20260914-b/verified.json`: the desktop
  entry point discovers the worker without an installation profile field; the
  owner checks and enables it through the UI. After moving the app to a path
  containing spaces and Unicode, the Windows launcher requires a fresh check
  and the owner can enable it again. A changed descriptor hash refuses admission.
  The original base image hash is unchanged, and test hosts close through owner
  maintenance controls.
- `artifacts/bundled-worker-progress-20260914-a/completion.json` completes the
  independent full-copy proof: 3,394 files, distinct source/copy identities,
  desktop discovery and owner enablement, relocation and fresh enablement, and
  refusal of a damaged manifest pin. The original base hash is unchanged. The
  copied app/worker is removed and the original evidence is retained.

The launcher fixture uses hard links to existing immutable worker inputs. It
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
did not provide enough headroom at the time.

After the storage cleanup, full independent preparation completed under
`artifacts/bundled-worker-independent-20260914-{a,b}`: 3,394 files and
9,869,024,670 logical bytes. The tool verified every source and copied hash; file
identity checks confirm every copied input has one link and differs from its
source. The relative descriptor matches the earlier launcher fixture exactly.
Both attempts removed the copied application/worker after the owned host exited;
manifests, copy identities and logs remain. Their overall launcher receipts are
**failures**, however: setup did not enable within the test's 60-second window.
The second captures setup still busy with no completed check or integrity error.
Its owner-maintenance request returned 409 during that check; the test terminated
only its owned host before cleanup. This proves preparation and storage cleanup,
not timely admission of a freshly copied worker. Their 60-second deadline was
insufficient for the later observed first verification; they remain failed
receipts, not retroactively passing tests.

## Relocated Windows worker

Setup now reports the actual verification phase and the number of runtime files
whose hashes have matched. The owner sees elapsed time and can cancel the exact
running check. A recheck immediately invalidates the previous positive receipt;
cancellation, disconnect, the ten-minute limit or a late result cannot enable the
worker. Progress is held in memory and polled without starting another check.
Private filenames are not included. No VM or model starts during inspection.

The small diagnostic fixtures under `artifacts/worker-setup-timing-20260914-a`
and `artifacts/worker-hash-timing-20260914-a` did **not** reproduce the long
full-copy setup. The copied runtime verified in 7.2 seconds, and inspection with
an inert base took 8.1 seconds. A 512 MiB ordinary/sparse sample hashed in
0.29–0.31 seconds with both stream modes. These results rule out those narrow
cases; they do not establish a cause or predict full-image performance. Both
fixtures removed copied files and build intermediates after their processes
exited.

The later full-size observation passed its two owner-admission checks in 68.04
and 9.77 seconds. The first spent about 59 seconds on runtime-file verification
and another eight on the guest image. The cause of the first/repeated timing
difference is not established; the observations do not justify a caching or
antivirus claim. Progress remained visible, and the fixture allowed a bounded
four minutes per check instead of assuming completion within one minute.

The first script's final cleanup assertion mistakenly treated PowerShell's
exit code for an absent PID as a still-running host. Its failure is retained in
`verified.json`. Read-only process inspection confirmed that host had exited;
the follow-up `completion.json` then verifies the damaged-manifest refusal,
graceful exit of its own host and removal of the already-prepared copy. No
additional image copy was needed. These are preparation/admission checks; they
do not replace native execution or release qualification.

The relocated native research run was interrupted at the owner's request to
address disk exhaustion. Inspection afterward found it had already failed during
provisioning, before any model call: this pinned Windows QEMU image tool receives
Unicode command arguments through its legacy code page. The exact command fails
on the relocated path and succeeds on the same file through its existing Windows
short alias. Evidence is under `artifacts/bundle-path-diagnosis-20260914-a`.
The earlier run is not a passing VM workflow. A second attempt created the
overlay but exposed the same issue in QEMU's default firmware lookup. Explicitly
passing the verified runtime's firmware directory fixes that startup path too.
Both failed attempts used zero model calls and their disposable overlays were
removed after process exit; compact diagnostics remain.

`artifacts/bundled-worker-native-20260914-c` then passed the complete native
OpenClaw research workflow from the relocated path: selected memory, a durable
question and restart, bounded repair, exact approved import, revoked grants and
verified workspace removal. The worker is purged and its scratch directory is
absent. All seven model replies and 910 tokens were synthetic. This native run
used the hard-linked immutable fixture, independently of full-copy preparation.

Windows file arguments now use existing ASCII short aliases where necessary.
Canonical paths and hashes remain the installation identity. The product creates
no aliases, changes no filename and does not enable short names or change a
Windows setting. Setup checks this prerequisite before admission and explains
an unavailable alias. Refusal matters: ReFS and some NTFS directories have no
usable short names. Universal Unicode-path support in this QEMU build remains
an open distribution limitation. Contract checks cover file identity, new files,
unavailable aliases and disabled admission. Native workflow evidence is described
above; it does not establish full-copy admission performance.
See [Windows short-path behavior](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getshortpathnamew).

## Still open

Combined downloadable archives/installers, publisher signing, upgrades and
rollback, distribution qualification, native Linux bundle discovery and macOS
workers remain open. A relocated unsigned Windows fixture does not qualify those
platforms or close the six product delivery gates. Physical-phone setup remains
deferred and user-operated. No live model, GPU inference or GitHub Actions are
needed for the checks above.

Windows filesystem references: [compression inheritance](https://learn.microsoft.com/en-us/windows/win32/fileio/compression-attribute)
and [sparse file creation](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_set_sparse).
