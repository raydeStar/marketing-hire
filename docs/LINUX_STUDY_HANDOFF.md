# Native Linux study handoff

The in-app **Open original study** and **Open restored study** actions now have
native Linux process evidence. This is a headless Linux x64 container check of
the packaged application. It does not qualify a Linux desktop chooser, default
browser launch, installer, Mac or worker isolation. The optional historical
baseline mode also checks different-build upgrade and rollback within one
supported study schema; it does not claim a schema migration.

## Bounded local command

```text
node scripts/linux-handoff-check.mjs FRESH-NAME
```

To exercise different application revisions, supply a previous successful
Linux handoff evidence directory with its retained captured sources and original
lockfiles:

```text
node scripts/linux-handoff-check.mjs FRESH-NAME artifacts/linux-handoff-20260914-b
```

The runner verifies the historical source/package receipt and source hashes,
then builds a separate copy from those recorded inputs. It preserves the prior
evidence and refuses changed or linked source files. The historical binary was
removed by its original cleanup; the new manifest explicitly identifies the
rebuilt baseline. The older and current source revisions, host assemblies and
HTML must differ. Both executables must independently report native Linux
capabilities and the same supported study schema before this case proceeds.

The runner uses the already installed, immutable diagnostic image
`sha256:091661a81cd600896c635dd7fa70f6b28773ee3bbbe15316c7efde5029e4a413`.
It refuses a missing image instead of pulling or installing one. No host ports
are published, no network or GPU is attached, and no worker or model is started.
The container gets two CPUs, 2 GiB memory and a 128-process limit. The application
and its handoff targets run as the existing unprivileged fixture user, UID 1100.

Windows can cross-publish the self-contained Linux binary here because the
result is actually executed on Linux before any native claim is recorded. The
ordinary portable publisher still requires its native operating system. The
fixture obtains compatibility metadata from the actual Linux executable, checks
every published file and prepares two separate directories containing that same
build in the default mode. The optional baseline mode instead starts the older
application, opens a restored study with the newer application, writes through
the newer application, then opens a reviewed restore with the older application.
It compares exact exported history after each transition and preserves edits
made to both original studies after their respective backups.

Before building, the runner admits 2 GiB of additional host allocation (4 GiB
when rebuilding a historical baseline) plus a
10 GiB reserve. Before copying packages inside Linux, the fixture separately
checks space for both copies, 128 MiB of study scratch and a 10 GiB reserve. The
Linux filesystem's free-space report is not a claim of extra physical Windows
capacity; the Windows admission applies to the same machine's storage.

Source snapshots, original and RID-resolved lockfiles, package manifests,
commands, small logs and receipts are retained under `artifacts/linux-handoff-*`.
The Linux studies and both package copies live only in the owned container's
writable layer. They disappear when that container is removed. The Windows
published fixtures and staging `node_modules`, `bin` and `obj` directories are
removed after process/container cleanup, on success or failure. This fixture
requires rebuilding from its captured sources to replay after cleanup.

## What the fixture verifies

- Real backup, reopen, a newer original edit and a separate verified restore.
- Wrong-CSRF and stale-review refusal before opening a study.
- An actual Linux `flock` on the restored study's launcher lock makes the target
  fail to start. Maintenance recovers with the same review and backup count.
- The original study opens with its newer history, then the restored study opens
  with the earlier history. Each target remains alive after its parent exits.
- The selected application's HTML bytes match its manifest-bound package, and
  the final target shuts down through its authenticated maintenance API.

Cleanup opens Linux pidfds only after matching each fixture executable and exact
launch profile. Signals target those process handles, never a recycled PID or an
unrelated process. A scan of the registered profiles also finds a target whose
launch acknowledgement was lost. Unconfirmed process or container cleanup fails
the overall check; it is not silently reported as success.

The fixture explicitly logs in through the normal host-key API and leaves
`openBrowser` false. It does not exercise graphical login, a native file chooser,
the generated shell launchers or browser service-worker navigation. Windows has
separate browser evidence for different-build switching and navigation.

## Different-build evidence

`artifacts/linux-handoff-versions-20260914-a/verified.json` records eight passing
native checks. The older application was rebuilt from the verified captured
`5b8f57794761d8abfad4c5bbbffd2b096690ee03` source snapshot; the newer application
uses the current application sources at `456b33b71b4b6fcfd66f584fa9f5fc2b00f55d4e`.
Exact input hashes, resolved dependency locks and both 373-file package manifests
are retained. The changed check runner and fixture are captured by source hash.

The actual older and newer Linux executables reported schema 5. Upgrade and
rollback both passed the ordinary reviewed restore/open APIs, exact exported
history comparisons and preservation of newer original edits. The prior lock
failure, CSRF and stale-review cases also passed. All four successful host
processes exited without cleanup signals, the owned container was removed, and
both published packages and build intermediates were removed. Zero live model
calls, workers, GPU requests or hosted Actions were needed. The running Windows
manual QA host and Luna bridge were preserved.

This qualifies the exercised revision pair on the native Linux process/API path.
It does not establish arbitrary version compatibility, schema migration,
graphical desktop behavior, signed updates or worker qualification.

## Earlier same-build evidence

`artifacts/linux-handoff-20260914-b/verified.json` records six passing native
checks, source/package verification and completed cleanup. All three successful
host processes exited without a cleanup signal. The deliberately failed target
was contained by the application's own handoff path. Zero live model calls,
worker starts, GPU use or GitHub Actions runs were needed.

The earlier `20260914-a` native workflow also passed, but its overall runner
failed because it compared three RID-resolved staged lockfiles against their
pre-publish hashes. Its container and build scratch were still removed. That
failed receipt remains unchanged. The corrected runner retains both original
and resolved lockfiles, verifies each against its appropriate hash, and passed
the complete command in `20260914-b`.
