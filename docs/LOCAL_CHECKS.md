# Development without hosted Actions

## Storage and cleanup

The owner requires bounded storage and cleanup after testing. Local core/package
checks and the primary image/product runners check expected allocation plus a
10 GiB reserve before starting large work. This is an admission check, not a disk
quota; concurrent writers can still reduce available space. Do not run several
large checks concurrently or repeatedly republish an unchanged package.

Routine removal of our disposable test fixtures and their exact test
registrations/shortcuts is explicitly authorized by the owner (September 15).
Do not request that permission repeatedly. Verify paths, inventory and process
exit as usual; report a tool rejection separately from user authorization.

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

For app continuity and connection/task status changes, use packaged
`app-continuity.spec.ts` and `status-clarity.spec.ts` with the existing browser
runner. The first checks real synthetic app saves, draft and scroll retention,
hidden-frame write refusal (including a raw bridge request), failed-refresh
recovery, and explicit app/design replacement. The second uses fictional task
and connector responses; it cannot prove live OAuth or provider access. Keep
screenshots, package hashes and results; remove each study after process exit.
Related generated-page, My page and background-chat cases cover affected callers.

For My page changes, run the focused `MyPageTests` / `ArtifactApiTests` backend
filters and packaged `my-page.spec.ts`. The browser fixture uses synthetic saved
items and advances only Playwright's page clock across midnight. It does not
change Windows time, call a model, or touch the owner study. Keep screenshots and
receipts; remove the fictional study after the runner verifies process exit.

The browser adapter fixture is explicitly opt-in. Prepare the pinned runtime
with `node scripts/prepare-browser-runtime.mjs . artifacts/FRESH-RUNTIME`, then
set `THADDEUS_BROWSER_RUNTIME` to that absolute path and run the
`ManagedBrowserSessionTests` filter. It launches installed Chrome headlessly in
a fresh disposable profile, intercepts all requests with fictional page data,
and uses the actual pinned MCP server. It exercises neither the owner profile
nor external websites nor a model. `BrowserTaskPolicyTests` require no browser.
The runtime is connected to the existing chat/approval flow. The standalone
adapter fixture now also exercises the real Runtime's two approvals and saved
result with scripted model replies. It is not live browser-assistant acceptance.
`BrowserConversationTests` uses a fake browser for host lifecycle checks;
`browser-task-card.spec.ts` uses synthetic responses for packaged UI checks.
Keep these separate from real Runtime/MCP integration and live owner acceptance.
Browser controls require the owner session and CSRF protection. Native Chrome
fixtures close the browser before stdio and wait briefly for crashpad files to
release, then remove their own profile. Keep an explicitly needed runtime for a
bounded follow-up; otherwise remove it after owned processes exit.

For an explicitly requested Windows notification investigation, use the focused
`NotificationRegistrationTests` and `DelegationSchedulerTests` filters. A real
native acceptance run is opt-in: set `THADDEUS_NATIVE_NOTIFICATION=1`, then run
`node scripts/browser-check.mjs HOST_PACKAGE FRESH-EVIDENCE native-notification.spec.ts`.
This uses a fictional study and synthetic planning provider, obtains approval
through the chat UI, closes the browser, and checks one real scheduled dispatch
after 30 seconds. It creates a Windows notification; ordinary suites skip it.
Its receipt deliberately leaves human visual acceptance unset. Check the named
notification in Windows Notification Center separately. After stopping owned
processes, remove the fictional study and record cleanup. Restore the production
helper's activation registration before deleting a tested candidate package.

Run native visual/click acceptance from a normal Windows desktop terminal or
File Explorer, **not a terminal descended from an MSIX development app**.
Codex's children were observed writing notification registrations and shortcuts
into its private registry/AppData view while reporting successful notification
delivery. `GetCurrentPackageFullName == APPMODEL_ERROR_NO_PACKAGE` does not rule
out inherited redirection. The September 17 D3 owner-launched check displayed the
unchanged helper's notification after the same binary's agent-launched D1/D2
checks failed visually. See `ASTRA_NOTIFICATION_HANDOFF.md`. Retained API results,
on-screen delivery, and an actual notification click are three distinct checks.

For a changed activation handler, run `node scripts/notification-click-check.mjs
HOST_PACKAGE FRESH-EVIDENCE` from that normal desktop terminal. It verifies package
hashes, sends one labeled diagnostic notification, waits for the sending helper
to exit, and then asks for one click. Its temporary loopback page records the
actual callback destination without touching an owner study or credential. The
two-minute timeout fails the check; it never substitutes an API receipt for a
click. The result belongs to the recorded package, current user and machine.

For chat-created data apps, use the focused tests and packaged browser workflow
in [Artifact apps](ARTIFACT_APPS.md#focused-verification). It uses a small local
synthetic provider and no worker, GPU or external model/search calls. After the
browser runner has closed its owned processes, clean its fictional study and
retain the compact receipts and screenshots.

For a changed Windows startup failure UI, run `powershell.exe -NoProfile
-NonInteractive -File scripts/desktop-failure-check.ps1 -Package HOST_PACKAGE
-Name FRESH-NAME`. It reuses the package, admits 32 MiB plus the 10 GiB reserve,
and observes/dismisses its own native dialogs. Occupied-port and invalid-profile
failures must remain visible, while `--no-browser` must exit without a dialog.
It preserves its unrelated listener and refuses before creating a study. Every
owned process is closed on success or failure; only compact fictional profiles,
logs and receipts remain. No installer, worker, VM or provider call is involved.

For a changed native desktop reopening contract, use `node
scripts/desktop-reopen-check.mjs HOST_PACKAGE FRESH-NAME` on Windows. It verifies
actual repeated launch, one-use IPC authentication and refusal of another study
or unrelated web listener, with no worker or model. It needs 32 MiB plus the
reserve, reuses the supplied host package and removes its own small fictional
study after owned processes exit. Retain the source hashes, logs and receipt.
The installer check also verifies reopening the installed entry. Do not rerun
broad VM or benchmark suites for this launcher-only change.

The [Linux handoff check](LINUX_STUDY_HANDOFF.md) can take a prior successful
evidence directory as its second argument to verify different-build upgrade and
rollback. It admits 4 GiB plus the reserve, rebuilds the verified old source in a
fresh directory, and removes both published packages and staging dependencies
after owned process/container cleanup. Use this only for a changed Linux
application-transition contract or an unverified revision pair; no worker or
model is involved. The default one-argument mode retains its 2 GiB allowance.

The opt-in [composer guidance check](RESEARCH_GUIDANCE.md) runs the ordinary
`Thaddeus.ResearchCheck` fixture with its `guidance` argument and the browser's
`THADDEUS_GUIDANCE=1` flag. It holds one synthetic inference until the browser
has saved guidance, then requires that exact text in the next model request.
Use it for a changed control/accounting contract, with the pinned base and a
fresh overlay. Its ordinary browser workflow removes the workspace; retain
compact requests, screenshots and failed/successful receipts, then remove the
separate build intermediates after all owned processes exit.

The opt-in [native execution-control check](NATIVE_EXECUTION_CONTROLS.md) uses
one fresh overlay of the existing pinned worker and two synthetic requests.
It verifies steering, active and queued cancellation, live-refresh refusal and
lost-controller refusal. Do not repeat it without a concrete caller/control
change; it is not part of core checks. It removes its overlay after process exit on
success or failure. Remove its separate build intermediates after the bounded
follow-up; keep the compact failed/successful observations and source hashes.
The small Node transport tests are included in core checks. The transport is an
embedded host resource; testing a change does not require rebuilding the guest.

The [Windows installer preview](WINDOWS_INSTALLER.md) reuses a checked host-only
package and a pinned NSIS directory. Run `node --test
scripts/windows-installer.test.mjs` for its small contract checks, then
`node scripts/windows-installer.mjs HOST_PACKAGE PINNED_NSIS_DIRECTORY FRESH-NAME`
to publish. `scripts/windows-installer-check.ps1` installs into a fresh artifact
fixture, verifies the actual app/uninstaller and removes owned test registrations
and files after exit. Publication cleans its input copy; neither operation
rebuilds the host or starts a model/worker. See the guide for full commands,
storage budgets and the unsigned-preview boundary.

For worker distribution preparation, use `node scripts/worker-notices.mjs
PINNED_INSTALLATION FRESH-NAME`. It runs parser tests and reads the existing guest
disk through `debugfs` in a bounded, network-disabled diagnostic container,
checking its pin before and after. It also verifies the pinned Windows runtime
inventory and boot files. It creates no VM, filesystem export or disk copy and
removes its owned container. The retained output lists installed packages,
candidate notices and missing coverage; it does not certify redistribution.
See [worker distribution preparation](WORKER_DISTRIBUTION.md).

The read-only inspector selects `test_collect.py` explicitly; the separate
assembler's temporary-file tests run through the local command below. Candidate
notice names include prefixed forms such as `MIT-License.txt`. A corrected
inventory must be reconciled against the prior package identities and text
hashes before rebinding the supplement catalog; a newer inventory is not an
automatic replacement for its existing pin.

Once an inventory exists, `python -B tools/worker-notices/assemble.py
GUEST_INVENTORY_JSON third-party/worker/catalog.json FRESH_OUTPUT_DIRECTORY`
assembles a small offline guest reference bundle. Its focused checks are
`python -B -m unittest discover -s tools/worker-notices -p test_assemble.py`.
No image inspection or source download is repeated. The assembler verifies every
referenced text and binding, refuses existing outputs and insufficient free
space, and removes its own partial output after a write failure. Tests remove
their temporary fixture directories, including the Windows junction fixture.
Retain the compact bundle and evidence; unresolved distribution/source coverage
remains explicit.

For a separately acquired, integrity-verified npm archive, use
`python -B tools/worker-notices/inspect_archive.py ARCHIVE NEW_DIRECTORY
[REGISTRY_UNPACKED_BYTES]`. Omit the optional size only when the registry omits
it; the collector still caps total unpacked bytes at 64 MiB. It never extracts
archive paths or runs package scripts. Focused tests are
`python -B -m unittest discover -s tools/worker-notices -p test_inspect_archive.py`.
The caller must check the archive's locked integrity before inspection and remove
its owned download afterward; inspection by itself does not authenticate an
archive. Small captured candidates remain for review, including incomplete
failed attempts. Existing outputs are never overwritten.

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

## Browser batches and the request limit

The host allows 600 ordinary requests a minute per address, and 12 sign-ins. A browser batch loads pages far faster than a person does. So the disposable fixture (`scripts/start-campaign-fixture.ps1`) sets `Thaddeus__ApiRequestsPerMinute=3000` and `Thaddeus__AuthRequestsPerMinute=120`. The product default is unchanged. Specs that change fixture state (objectives, shifts) expect a fresh fixture; run a batch on a new one.

A fixture stopped hard (its launcher killed) leaves its marker behind. `-ResumeFixture <folder>` reopens that same kept fixture once the old launcher is gone, and still refuses any other marker.

## Plow worker meter

For changes to the Plow worker meter, run `node scripts/check-plow-meter.mjs
hirezero-marketing:plow-package-CANDIDATE plow-meter-check-FRESH-NAME` with an
existing pinned package. It uses a fresh, network-disabled container, fictional
ledger and synthetic provider response, while exercising the installed SDK and
actual Gateway. It checks source hashes, reserves 512 MiB plus the 10 GiB floor,
and removes only its labelled container after process exit. Retain the compact
receipt and logs. This does not prove live provider billing or phone delivery.

## Demo videos


`web/tools/record-demo.mjs` records a narrated demo of a workspace you own. The employee writes the storyboard as a Library document with a fenced JSON block: `{title, subtitle, scenes:[{scene, caption, narration, seconds}]}`. The scenes come from a fixed menu (intro, chat, cockpit, shifts, research, wedge, blog, calendar, listening, scorecard, weekly, golive, library, outro).

**Your voice.** On the storyboard document, **Record narration** opens a teleprompter:
- It shows one line at a time; press Space (or Next line) before the next one. Each press marks the cut.
- Each line becomes its own clip, trimmed of the quiet at both ends. You can redo any single line.
- Saving uploads the clips to Library → Media as WAV and writes each clip's ID into its scene (`audio`).

The tool then:
1. uses each scene's recorded clip. Scenes without one are captions only; `--voice` fills them with the Windows speech engine instead, which is off unless asked for;
2. records the real app with Playwright at 1280×720, with title cards and a caption bar;
3. muxes the narration and video with ffmpeg into H.264/AAC MP4;
4. with `--upload`, adds the video to the Library (Media).

```bash
node web/tools/record-demo.mjs --data <fixture>/host --storyboard <wiki id or file.json> --out <scratch folder> --ffmpeg <ffmpeg.exe> --ffprobe <ffprobe.exe> --upload
```

It writes nothing to the workspace but the uploaded video, and deletes the raw recording after muxing. Keep the output folder in scratch space and remove the per-scene WAVs afterwards. Playwright's bundled Chromium has no H.264 decoder, so check playback with the `msedge` or `chrome` channel.

