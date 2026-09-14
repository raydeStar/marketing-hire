# Development without hosted Actions

## Storage and cleanup

The owner requires bounded storage and cleanup after testing. Local core/package
checks and the primary image/product runners check expected allocation plus a
10 GiB reserve before starting large work. This is an admission check, not a disk
quota; concurrent writers can still reduce available space. Do not run several
large checks concurrently or repeatedly republish an unchanged package.

Portable publication removes staging `node_modules`, `bin` and `obj` directories
in its final cleanup, retaining captured source, logs, manifests and the package.
Native package checks budget the extracted files plus 128 MiB for fixture data,
in addition to the reserve. They save `tested-package-manifest.json` and remove
their extracted package on success or failure after confirming owned process and
credential cleanup. Uncertain launcher exit retains the extraction for inspection.
`scratch-cleanup.json` records the outcome; failed cleanup fails the overall check.
The original publication and small fictional study/backup receipts remain.
The [combined host/worker publisher](PORTABLE_PACKAGES.md#one-archive-containing-the-host-and-worker)
streams one ZIP from existing pinned inputs without a large staging copy. It
budgets incompressible output plus the reserve, validates the completed archive,
and removes its builder output. Combined native verification budgets and removes
one full extraction; retain the verified compressed archive as the deliverable.
The Linux product runner removes its own disposable disks and tools after its
containers have been confirmed removed, on success or failure. Add
`--retain-fixture` only when a specific frozen follow-up needs those files; this
choice is recorded in its receipt. The repeat runner removes its own overlay and
preserves shared inputs. Failed process/container cleanup prevents disk deletion
and is reported as a failed check. Worker-image preparation keeps a successfully
verified base but removes its redundant root filesystem export.

`scripts/artifact-storage.mjs` confines cleanup to resolved child paths under
repository `artifacts`. It refuses traversal, linked roots and targets outside
that boundary. Logs and hash receipts document removed binaries; older evidence
can remain valid even when its disposable execution images have been pruned.
Do not claim a pruned fixture can be replayed without rebuilding it.

Keep the active host, its pinned runtime/kernel/initrd/base, the reusable Linux
fixture inputs, one rollback package, user data/backups and unrelated model or
benchmark work. Other legacy runners are not all converted yet: audit their
storage cost and explicitly clean their owned scratch before using them. Never
use indiscriminate Docker/system pruning to satisfy a test's storage needs.

## Running checks

Native portable publication includes `tools/Thaddeus.NoticeBundle`, using only
the restored NuGet/npm distributions and pinned `third-party/nuget` notices.
The core check fingerprints that catalog and exercises refusal cases. No live
notice download is part of publication; source updates require explicit reviewed
catalog changes. See [third-party notices](THIRD_PARTY.md) for coverage and the
separate worker-distribution boundary.

For native Linux in-app study handoff without another worker VM, use
`node scripts/linux-handoff-check.mjs FRESH-NAME`. It cross-publishes a captured
Linux host, then runs the real backup/restore/open workflow as a non-root user
inside an existing pinned, network-disabled container. It checks actual file-lock
failure, parent/child lifetime and retained history; it removes its own container,
package copies and build intermediates. This is same-build process evidence,
not graphical desktop or worker qualification. See [Linux study handoff](LINUX_STUDY_HANDOFF.md).

Native application-folder acceptance is explicitly opt-in. With a newly published
host-only package, set `THADDEUS_NATIVE_PICKER=1` and run
`node scripts/browser-check.mjs PACKAGE FRESH-EVIDENCE native-folder.spec.ts`.
It uses a disposable study, checks local-owner/CSRF and stale-review rejection,
cancels one real dialog, then waits up to three minutes for a desktop operator
to select the package folder in the second dialog. The expected path and pending
operation are written to `screenshots/picker-ready.json`. Select that folder using
the actual desktop UI; do not replace its result or stub the API. The test then
checks returned path, package review, reload, narrow/desktop layout and unchanged
study history, saving `picker-verified.json`. Normal suites skip this interactive
case. No VM, model, GPU or hosted Actions run is involved.

GitHub-hosted Actions are no longer part of routine development. Both workflows
are disabled in this private repository, and their source definitions accept only
manual dispatch. Do not enable or dispatch them without the owner's explicit
decision. A commit or push must not start paid runner work.

Use the installed .NET SDK, Node and web dependencies on the current computer:

```text
node scripts/check-local.mjs core change-name
```

This runs the secret scan, locked restore, backend tests, CPU-only protocol tests
and web build. It records source hashes, revision, platform, commands, results and
logs in a fresh `artifacts/local-check-change-name` folder. Checks stop on the
first failure. Source changes during a run invalidate its receipt. A local lock
prevents overlapping invocations; inspect a stale lock's process before removing
it. GPU inference, VM startup and live model/search requests are not part of this
command. Initial dependency installation remains `npm --prefix web ci`.

Source fingerprints include the native tools and solution file. The separate
[Linux process ownership fixture](LINUX_PROCESS_OWNERSHIP.md) can obtain actual
Linux process evidence locally through a bounded diagnostic VM. It is an explicit
additional check, not part of `core`, and does not establish KVM or Mac support.

The separate [QEMU session fixture](LINUX_QEMU_SESSION.md) now supplies actual
Linux KVM/OpenClaw and Windows WHPX regression evidence without hosted runners.
It uses inert broker requests and no model calls. Mac worker qualification still
requires a native Mac; these Linux/Windows receipts cannot supply that evidence.
The [Linux lifecycle fixture](LINUX_WORKER_LIFECYCLE.md) additionally checks native
stopping-point recovery, disk-writer exclusion and workspace removal. Its separate
tools disk avoids rebuilding the large immutable guest assets for each code edit.
The [Linux product check](LINUX_PRODUCT_PREVIEW.md) uses the actual packaged host
and authenticated research API, including restart and approved import. Its model
endpoint is entirely synthetic; it makes no live model request.

For intermittent Linux product failures, `repeat-linux-product-check.mjs` reuses
a passed fixture's frozen application and read-only base disks with a fresh
overlay. This avoids another web/.NET build and copying the large bases; see the
[repeat command and limits](LINUX_PRODUCT_PREVIEW.md#repeat-the-packaged-workflow-without-rebuilding).

For guest filesystem changes, the [shutdown check](GUEST_SHUTDOWN.md) observes
the stopped disk directly and verifies acknowledged files after restart, with
an active writer that ignores SIGTERM. It uses no model or GPU requests and
does not replace the separate authenticated product workflow check.

The [computer requirements check](HOST_REQUIREMENTS.md) also has contract and
packaged browser coverage. It reads the actual host capabilities without booting
a worker; Linux prerequisite evidence comes from the native product fixture.

For a package candidate, run separately when the changed behavior needs it:

```text
node scripts/check-local.mjs package candidate-name
```

This captures and builds a self-contained native package, executes its archive/
restore/launcher checks, checks native credential custody with fictional values,
and runs the browser suite on a disposable host. It requires Chromium already
installed for Playwright and a usable native credential service. Linux requires
libsecret and a working Secret Service session. The checks remove their own
fictional credential entries. They use fresh data and ports, preserve the current
study, and do not start a worker. The opt-in native research case remains separate.

For changes to guided app-version selection or its launcher verification, run
`node scripts/portable-check.mjs PUBLICATION FRESH-EVIDENCE --version-switch` on
the new host-only publication. It budgets one extra host copy inside the disposable
extraction, verifies selection of that distinct directory, refuses launch after
an intentional fixture payload change, then restores the fixture bytes and opens
the selected app through the generated launcher. Both copies are removed after
owned processes exit. An optional final argument selects a prior checked host
package under artifacts as the copied target; the receipt records whether the
selected host assembly actually differs. Without it, the fixture uses two copies
of one build. Neither mode proves a different-schema migration, another operating
system or a signed update. The separate return launcher is also executed against
the original study, including its newer edits.

Run the same command on Windows x64, Linux x64, Intel Mac or Apple silicon to
obtain evidence for that actual host. Windows testing does not qualify Mac/Linux
workers or packages. Reuse previous native receipts for unchanged packaged inputs;
record explicitly when changed code still awaits another operating system. Do not
rebuild the entire matrix for documentation, workflow or unrelated script changes.

For later automation, dedicated self-hosted runners can execute the same commands.
GitHub currently documents those runners as free to use with Actions, with the
machine's operating costs remaining ours. No runner service is installed by this
change, and this benchmark computer should not automatically execute repository
jobs. The repository stays private. See [GitHub self-hosted runners](https://docs.github.com/en/actions/concepts/runners/self-hosted-runners).

For Windows in-app study switching, set `THADDEUS_HANDOFF_PACKAGE` to a prior
checked host package under artifacts and run
`node scripts/browser-check.mjs NEW-PACKAGE FRESH-EVIDENCE study-handoff.spec.ts`.
It uses real different host builds and disposable study folders, with the default
browser option unchecked. It covers a locked target's failed startup, recovery,
both Open buttons and retained original/restored histories. Every target profile
is registered before launching. Final cleanup checks the exact package and
fixture-owned profile, holds a process handle and checks its start identity before
stopping a leftover target. Cleanup failure fails the run; only successful cleanup
permits an overall `verified.json`. No worker image, model call or GPU is involved.

Completed hosted run artifacts can still be downloaded without dispatching a new
job. They prove the revision recorded in their manifests, not subsequent changes.
Local receipts replace automatic hosted checks for daily development; signing,
worker qualification and physical-device acceptance remain separate requirements.
