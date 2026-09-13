# Portable Windows development launcher

Published development packages include `Start Thaddeus.cmd` and
`launch-host.ps1`. Open the command file to start the self-contained host and
open its browser with a one-use login link. A matching running host is reused;
the launcher does not stop processes or switch ports to resolve conflicts.

The package includes .NET and the built web client. Node, npm, an SDK, Docker
and a GPU are not needed to open the default study. A model and a supported
worker installation are separate setup choices. The Luna CLI bridge is a
development transport and is not silently installed or started by this launcher.

By default, private data lives outside the replaceable package in
`%LOCALAPPDATA%\Thaddeus2`. Existing development data is not moved automatically.
An operator can place a `launch.json` beside the launcher or pass `-LaunchProfile`:

```json
{
  "schemaVersion": 1,
  "dataDirectory": "C:\\Users\\example\\AppData\\Local\\Thaddeus2",
  "localOrigin": "http://localhost:5179",
  "workerPort": 5183
}
```

The optional `developmentWorkerInstallation` is an absolute path to the pinned
Windows preview configuration described in [HOST_SETUP_PREVIEW.md](HOST_SETUP_PREVIEW.md).
It does not itself enable research. Models, owner permissions and task allowances
remain under the product's existing controls. No model call or VM boot is part
of opening the study. For script-driven checks, `-NoBrowser` starts/verifies the
host without minting a link or opening a browser.

## Ownership and login

- The launcher keeps data outside the application package and uses a data-folder
  launch lock to serialize concurrent opens. It explicitly sets the content root.
- A saved instance record binds the process ID, OS process start time, executable
  location and effective launch profile. Stale IDs and changed profiles are not
  treated as a matching running host. Occupied ports belonging to another launch
  or process are refused; no process is terminated or replaced.
- Readiness compares the published index with the HTTP response as UTF-8. This
  avoids Windows PowerShell 5's different default decoding of `text/html`.
- The existing key is sent only to the verified local host. That host issues an
  in-memory ticket with a one-minute lifetime. Only one local browser can redeem
  it. At most eight unclaimed tickets are retained, and host restart invalidates
  them. The existing host-key and local-origin checks remain mandatory.
- The ticket travels in a URL fragment, which is removed before the browser's
  claim request. The durable key is never put in the URL. An already unlocked
  owner reuses its session. Opening the product does not add a model task.
- The launcher does not change PowerShell policy, register auto-start, elevate,
  install a service, configure a GPU, or restart Docker/WSL.

This is a local Windows development package. Download trust/signing, a consumer
installer, model-secret onboarding, managed upgrades/rollback and actual
consumer macOS/Linux installation remain open. The separate
[portable package workflow](PORTABLE_PACKAGES.md) adds native archives and
foreground Unix launch checks. It is not a finished nontechnical cross-platform
installer.

## Verification

`scripts/launcher-check.ps1` starts the actual self-contained published host
through Windows PowerShell 5, checks exact web readiness, reuses it, refuses a
second data owner and a changed profile, restarts it with exact seeded pages
preserved, and leaves an unrelated listener intact. Only its own test host is
stopped. The Windows CI job runs the same check.

Backend tests cover expiry, bounded ticket retention, concurrent single-use,
restart invalidation, key/origin/remote refusals, HttpOnly owner login and session
reuse. The browser test verifies fragment removal, durable reload and refused
replay without task creation. These are distinct from physically clicking the
command file in a new user's downloaded installation.

Private development receipts: `artifacts/portable-launch-20260913`. The first
attempt retains its PowerShell decoding failure. A later checker attempt retains
the captured-pipe wait with the host already ready; the corrected checker waits
on the launcher's process handle and keeps separate output files. Neither failed
attempt is overwritten.
