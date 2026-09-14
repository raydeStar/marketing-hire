# Development without hosted Actions

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

Completed hosted run artifacts can still be downloaded without dispatching a new
job. They prove the revision recorded in their manifests, not subsequent changes.
Local receipts replace automatic hosted checks for daily development; signing,
worker qualification and physical-device acceptance remain separate requirements.
