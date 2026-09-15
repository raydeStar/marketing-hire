# Development status — 2026-09-14

The Windows build is available for [manual QA](MANUAL_QA.md) now. The owner
narrowed this cycle to the MVP on 2026-09-14: follow the finite
[MVP checklist](MVP_CHECKLIST.md). Broad benchmarks and additional sandbox backend
qualification are deferred. Historical architecture-gate entries below do not
override this scope correction. Installation/distribution and truthful native
platform support remain launch work.

## Current Windows QA checkpoint: desktop reopening installed

`portable-desktop-reopen-20260914-a` is now running at http://localhost:5179/.
It retains the monthly search guard and adds native desktop reopening: another
launch of the same package/profile returns to the current study through a bounded,
same-account IPC channel. The permanent host key is neither read nor sent to
discover an HTTP listener. Different profiles and unrelated occupied ports remain
refused without creating another study or stopping a process.

37 focused launch/login checks pass. Actual Windows package evidence verifies
repeated launch, a one-use IPC-issued owner session and refusal cases. Eight
native installer cases pass, including the installed entry's repeated launch and
study-preserving uninstall. The first installer fixture did not capture the fast
child's process handle before observing its exit code; its reuse output and clean
failure receipt remain. The corrected observer passes against the same installer
and application bytes. No broad regression, benchmark, model/search, GPU or VM run
was used. Opening the browser via the OS shell and visual wizard review are not
claimed by these `--no-browser` fixtures; native Mac/Linux acceptance stays open.

Evidence is `artifacts/desktop-reopen-20260914-a`,
`artifacts/desktop-reopen-check-20260914-a` and
`artifacts/windows-installer-check-desktop-reopen-20260914-b`. The current installer
is under `artifacts/windows-installer-desktop-reopen-20260914-a`. It remains
unsigned and host-only. Activation verified a fresh backup, unchanged study
tables and the owner session; the Luna bridge was preserved. Temporary build
files and the superseded guidance package/old installer are removed with receipts.
MVP-search-A remains the one rollback. The checkpoints below are historical.

## Previous Windows QA checkpoint: search spending guard installed

`portable-mvp-search-20260914-a` runs at http://localhost:5179/. Settings and the
research composer show the study's monthly search count, cap and remaining
allowance. Default 100; zero pauses new searches. Shared admission commits each
intent before dispatch. Historical attempts, unknown outcomes and restart are
accounted for; changing a cap/key or replaying a result does not refund usage.
The full ledger is counted, including beyond the replay UI's 2,000-event page.
The calendar resets in UTC. Account billing and other apps/studies remain outside
this count, so the provider's spending controls are still needed.

43 focused backend checks and one packaged browser flow pass. Desktop/narrow
screenshots were reviewed, package/source hashes and generated notices checked.
An initial test-analyzer failure and a browser test that omitted reopening
Settings after reload were corrected; the package's application inputs did not
change during the browser correction. No broad regression/benchmark campaign,
live search/model request, GPU, VM or hosted Actions ran.

Evidence: `artifacts/mvp-search-budget-20260914-a` and
`artifacts/mvp-search-browser-20260914-b`. Activation verified a fresh backup,
all study-table fingerprints and the owner session, retaining the existing Luna
bridge. Guidance-A is the one rollback; the preceding notices-B package and
temporary build/browser scratch are removed with cleanup receipts. This is the
current QA baseline; the checkpoints below are historical.

## Worker module paths classified from the pinned disk

Read-only inspection confirms that all 32 module paths previously reported as
missing metadata or dangling links are symlinks with absent targets. The scanner
mislabeled 28 scoped links because it skipped their package-root check. That
reporting bug is corrected and 13 parser tests pass. Actual paths/inodes/targets
are retained in `third-party/worker/module-layout.json`; no worker file changed.

Evidence is `artifacts/worker-module-layout-20260914-b`. Both disk hashes match,
the owned diagnostic container was removed, and no model, guest execution, image
copy or app rebuild occurred. The prior resolver source and failed first probe
are preserved. The full frozen inventory was not re-scanned or re-pinned; its
60 unsupplemented findings now have a clearer breakdown: 32 dangling links and
28 named packages lacking a supplied notice. Runtime/bundled-code coverage and
distribution requirements remain open. The Windows QA build stays guidance-A.

## Legacy worker notices recovered without an image build

The archive notice collector now accepts missing registry `unpackedSize` while
enforcing a fixed unpacked/capture limit. Seven focused checks pass. Five legacy
archives were re-acquired with their exact locked integrity and previous hashes;
all five now pass inspection and their downloads were removed. Original README
notices from isarray and strictdom add two verified supplements. A separate
Standard Webhooks source-version mismatch remains unresolved and adds no binding.

The [worker reference bundle](WORKER_DISTRIBUTION.md) now contains 94 supplemented
package instances and 1,091 original texts. Independent verification passes all
1,938 local links and source/output hashes. All 154 findings remain, with 60
without supplements. Evidence is `artifacts/worker-notice-legacy-20260914-a` and
`artifacts/worker-notice-bundle-20260914-c`; prior failures are preserved. Complete
worker redistribution/source coverage remains open. No app/image build, container,
worker, live model, GPU or hosted Actions ran; the Windows QA package is unchanged.

## Linux different-build upgrade and rollback verified

The [Linux study handoff check](LINUX_STUDY_HANDOFF.md) now accepts a prior
verified source snapshot and exercises different application revisions through
the normal review/restore/open APIs. The earlier captured application and current
application both ran natively under an unprivileged Linux account. Eight checks
pass, including upgrade, rollback of newer-build data, exact history preservation,
failed-start recovery and process survival after the prior host exits.

Evidence is `artifacts/linux-handoff-versions-20260914-a`. Both versions use
schema 5; other revision pairs, schema migrations, desktop browser/chooser and
signed distribution remain unverified. All four successful hosts exited normally,
the owned container and both disposable builds were removed, and no live model,
worker, GPU or hosted Actions ran. The Windows QA package remains guidance-A,
with its host and Luna bridge preserved. Measured free space was 133.60 GiB.

## Current Windows QA checkpoint: composer guidance installed

`portable-guidance-20260914-a` is running at http://localhost:5179/ and includes
[durable composer guidance](RESEARCH_GUIDANCE.md) in Conversation and task detail.
Guidance reaches the next worker model request while retaining the task's
execution identity, time/token allowance and durable receipt. The broker now
allows guidance and cancellation during inference without admitting a concurrent
model call. Ambiguous delivery remains visible and is not automatically replayed.

The source checkpoint passes 849 backend tests, the actual browser research flow
and direct native steering/cancellation checks. Desktop/mobile screenshots were
reviewed. The published Windows package passes 17 extracted-package checks and
includes its verified dependency notices. Evidence is in
`artifacts/guidance-20260914-d`, `artifacts/guidance-research-20260914-c`,
`artifacts/native-guidance-controls-20260914-b`,
`artifacts/native-guidance-package-20260914-a` and
`artifacts/guidance-delivery-20260914-a`. Earlier failed guidance fixtures are
retained as compact evidence; their disposable workspaces were removed.

Activation made a verified backup and preserved all study-table fingerprints,
the owner key/session and the existing Luna bridge. Notices-B remains as one
rollback. Test overlays/build intermediates and the superseded handoff-C package
were removed; measured free space afterward was 133.66 GiB. No live model calls,
GPU inference or hosted Actions ran. Manual QA can begin now; wider platform,
distribution/security, Lab evidence and final UI acceptance remain open. Physical
phone setup remains deferred. The checkpoints below are historical.

## Previous checkpoint: native control ownership and research regression verified

The [native control check](NATIVE_EXECUTION_CONTROLS.md) now passes active and
queued cancellation using one persistent public Gateway connection with only
read/write scopes. Start, steering and inspection share that caller. Native
checks also refuse a live caller reset and refuse to recreate/replay a lost
controller. The real browser research flow passes question, worker restart,
continuation, bounded correction, exact approved import and reviewed removal.
The transport is embedded in the host; no new guest image or dependency is needed.
The final source check passes 830 backend tests and 29 protocol checks; evidence
is `artifacts/native-control-progress-20260914-j`. The TLS fixture now launches
the host built alongside its test and fails promptly if startup does not succeed.
The Windows manual QA build remains unchanged and available now. Every owned
test workspace/overlay is removed; no live model, GPU or hosted Actions ran.
Durable composer steering and broader platform/release qualification remain open.
The new transport is verified in source and disposable native fixtures; it has
not replaced the running QA package.

## Worker notice detection and bundle reconciliation

The worker scanner now recognizes prefixed notice filenames. Read-only inspection
of the same pinned image found Panzoom's `MIT-License.txt` and Rolldown's
`THIRD-PARTY-LICENSE`; package metadata and all previously collected notices are
unchanged. Six reviewed original READMEs from already verified archives also
supply missing notices. The current bundle has 92 supplemented entries and
62 findings without supplements, down from 69. Full distribution/source coverage
remains open. The running Windows manual QA build and Luna bridge stay unchanged.

Evidence is `artifacts/worker-notices-20260914-d`,
`artifacts/worker-notice-bundle-20260914-b` and
`artifacts/worker-notice-detection-20260914-a`. Ten parser checks, actual image
inspection and independent bundle verification pass. Prior assembler checks are
reused only after matching their unchanged source hashes. Both owned diagnostic
containers were removed, including the failed first attempt. No new archive,
disk copy, app rebuild, live model, GPU or worker VM was used. See
[worker distribution preparation](WORKER_DISTRIBUTION.md#corrected-notice-detection-and-current-bundle).

## Windows installer preview; manual QA remains available

The [per-user Windows installer](WINDOWS_INSTALLER.md) now builds from the exact
existing host package, adds a native Start menu entry and supports removal through
Windows Installed apps. Eight contract checks and seven actual native cases pass,
including startup, refusal of a running/linked application, cleanup after a real
write failure, and uninstall that preserves study bytes and extra files. The
51.3 MB executable remains unsigned and host-only. Wizard review, publisher trust,
signed upgrades and broader platform/worker qualification remain open.

Evidence is `artifacts/windows-installer-20260914-e` and
`artifacts/windows-installer-check-20260914-d`. Test processes and disposable
payloads are removed; compact evidence and one final installer remain. The pinned
7.1 MB compiler is retained for reuse. No app rebuild, worker VM, live model, GPU
or hosted Actions ran. The existing app and Luna bridge continue unchanged at
http://localhost:5179/. Start with the [manual QA guide](MANUAL_QA.md); installing
this preview is optional and is not a prerequisite for testing the current UI.

## Windows manual QA checkpoint: packaged notices

The Windows manual QA baseline remains unchanged and available now. Separate
release preparation produced `artifacts/worker-notice-bundle-20260914-a`: an
offline guest reference bundle with 1,802 component entries and 1,081 preserved
notice texts (4.4 MB). Verified catalog bindings supply upstream text for 86
package instances; all 155 inventory findings remain, including 69 without a
supplement. Twelve focused assembly checks passed. This is progress toward
worker distribution, not certification of complete notice/source coverage, and
it does not require waiting to begin the [manual QA pass](MANUAL_QA.md).

Separate release preparation now has a verified read-only worker inventory at
`artifacts/worker-notices-20260914-b`: 577 dpkg packages, 1,225 npm instances and
155 explicit metadata/notice findings. Eight parser tests and full pinned-input
checks passed. Its 8.1 MB of guest metadata/notice bytes remain as compact evidence;
the diagnostic container was removed. No VM, model, GPU, application rebuild or
disk copy ran. [Worker distribution preparation](WORKER_DISTRIBUTION.md) describes
the findings and remaining source/notice coverage. This does not certify worker
redistribution or replace the manual QA baseline below.

The owner is ready to begin manual QA. The current Windows package is
`artifacts/portable-notices-20260914-b/thaddeus-win-x64`, running at
http://localhost:5179/. The [short manual QA guide](MANUAL_QA.md) identifies this
baseline and the remaining release work. Use disposable candidate studies for
further development while this build is being reviewed.

The native publisher now generates notices for the published dependency graph
before sealing the package: 14 NuGet/runtime components plus 89 npm entries,
including Vite's emitted browser helper, with 41 unchanged license/notice texts.
Pinned upstream files supply licenses omitted from NuGet distributions. Missing
text, changed identities, modified pinned notices and mismatched archive entries
refuse publication. NuGet restore content hashes and signed-archive checksums
are distinct identities and are recorded/checked separately. This is a host
notice bundle; worker distribution/source requirements remain open.

The final core check passes 825 backend tests, protocol checks, secret scan and
web build. The new package passes all 17 extracted Windows checks, including
credentials, startup, backup/restore and its generated launcher. Independent
verification matches all 425 package files, 200 captured source inputs, both
dependency graphs and every included text hash. Evidence:
`artifacts/local-check-notices-20260914-b`,
`artifacts/native-notices-20260914-b`, and
`artifacts/notices-delivery-20260914-a`.

The first publication failed because the new helper conflated a signed archive's
checksum with NuGet's content hash. That failure remains recorded; its unsealed
package and staging build output were removed. Regression coverage now checks
both identities, archive-to-extracted notice equality, missing text, version
mismatch, changed upstream pins and refusal to overwrite sealed output.

Activation verified a new backup and preserved all study-table fingerprints,
the owner key and owner session. The Luna bridge was not restarted. Cleanup
removed 19 checkout build directories and the superseded picker-C package/archive;
notices-B is active and handoff-C is the one rollback. The final observed free
space after cleanup was 134.6 GiB. No live model, GPU, worker VM or hosted Actions
was used for this checkpoint. Existing product/UI evidence is reused where the
code is unchanged; user UI acceptance, Mac qualification and phone setup remain.

## Current qualification: native Linux study handoff

The new [bounded Linux handoff check](LINUX_STUDY_HANDOFF.md) passes six checks
against real packaged Linux hosts running as UID 1100 in an existing pinned
container. It exercises backup and restore, a real `flock` startup failure,
recovery without another backup, both in-app Open actions, target survival after
the previous host exits and exact old/new study histories. The final target
closes through its owner API; no cleanup signal was needed for the three
successful hosts. No model endpoint, GPU, worker VM or GitHub Actions ran.

Evidence is `artifacts/linux-handoff-20260914-b/verified.json`. It verifies the
373-file package, captured sources and both original/RID-resolved lockfiles,
then removes its owned container, published fixture and build intermediates.
The first attempt passed the native workflow but failed its final lockfile
bookkeeping check; its failed overall receipt and successful cleanup remain
recorded in `artifacts/linux-handoff-20260914-a`. Application code is unchanged;
the earlier 816-test core result remains applicable rather than being rerun.

This closes the missing Linux process-level handoff check for two directories
of the same build. Linux desktop chooser/browser launch, different-build native
upgrade/rollback and distribution still need qualification. Mac hardware access,
release trust/signing, broader security/Lab evidence and final UI acceptance
remain open. Phone setup remains deferred. The active Windows handoff-C app and
Luna bridge are unchanged.

## Current development: repeatable Lab checks for the current file workflow

The independent native Lab now compares the current captured-file workflow through
`register-artifact` / `run-artifact`. Its candidate uses the unchanged production
version-4 profile. A named trusted test control omits quotation checking and repair,
with capture, source versions, credential custody, budgets and exact owner approval
still enforced. No browser setting or request-body switch exposes this control.

All six Windows/OpenClaw/QEMU cases passed their declared protocol: two reversed
repetitions per arm and a wrong-conclusion negative in both arms. Both repairs
reproduced; the independent scorer retained four false successes. There were 28
synthetic calls and 3,640 synthetic tokens, zero live model/GPU calls. All six
overlays were removed and all grants revoked, without copying the immutable base.
The three earlier campaigns were regraded separately without changing their
original reports, outcomes or recorded usage.

Evidence: `artifacts/native-artifact-protocol-20260914-a/report.json`,
`post-run-verification.json`, and
`artifacts/native-artifact-protocol-plan-20260914/legacy-grade-check.json`.
The core check at `artifacts/local-check-artifact-lab-20260914-a` passes 816 tests
plus protocol, secret-scan and web checks. The native registration preserves all
397 sources and five loaded assembly hashes; compact snapshots total 4.36 MiB.
See [Native Lab](NATIVE_LAB.md#september-14-captured-file-protocol-evidence).

This closes the missing repeated-protocol coverage for the current import format.
It does not establish model capacity, generalized efficacy, resource-comparison
claims or a release security qualification. The user's running handoff-C app and
Luna bridge stay unchanged; this checkpoint adds Lab capability rather than a new
default application policy. Native Mac access remains an outstanding verification
prerequisite; the owner has been asked which hardware is available.

## Current study: open a verified restored study from maintenance

The Windows package is `artifacts/portable-handoff-20260914-c/thaddeus-win-x64`.
After restoring a selected backup, **Open restored study** transfers to its
verified application, and **Open original study** returns to the original app and
newer edits. Saved launchers remain available. The initiating local owner and
CSRF checks apply; stale reviews and changed launcher/package bytes refuse launch.
The current host closes its listener before starting the selected app through its
existing desktop interface, so a compatible older build can participate.

If startup fails, only the newly created target process is stopped. Maintenance
reopens after exit is confirmed, preserving both studies without repeating a
backup or launch. Unknown exit leaves maintenance closed. Each attempt has a
durable intent/result and is never replayed automatically. Reconnection uses a
fresh navigation to avoid the previous app's cached offline shell.

The final core check passes 804 backend tests, CPU protocol checks, secret scan
and web build. The actual Windows browser workflow tests a locked target's failed
startup, recovery, both Open buttons, different compatible host assemblies,
survival after the original process exits and both expected histories. It checks
the selected script actually loaded in the browser. Default-browser opening is
unchecked in this fixture; its one-use login mechanism is shared with the existing
desktop launcher. The desktop and 390-pixel layouts were inspected.

Evidence: `artifacts/local-check-handoff-20260914-final-b`,
`artifacts/browser-handoff-20260914-d`, and
`artifacts/handoff-delivery-20260914-a`. The separate leftover-process check is
`artifacts/browser-handoff-20260914-b/leftover-cleanup-verified.json`. An early run
passed its product test but exposed a Windows PowerShell array-handling error in
cleanup; the corrected helper confirmed cleanup afterward. A later run exposed
the stale offline-shell navigation and cleaned its remaining target. Neither
failed overall run replaces the final acceptance receipt.

The delivery launch/activation records identify the live host and verified main
backup. Cleanup retains the active handoff-C app and picker-C rollback, compact
receipts, pinned worker inputs and user data/backups. No model requests, GPU
inference, worker VM starts or GitHub Actions runs were needed.

Remaining delivery work includes signed installation/trust and app downloads,
native Mac/Linux handoff and chooser qualification, different-schema migration
evidence, broader worker/security qualification, Lab evidence and final user UI
acceptance. Physical-phone setup remains explicitly deferred.

## Earlier checkpoint: native application-folder selection

This checkpoint used
`artifacts/portable-picker-20260914-c/thaddeus-win-x64` at
`http://localhost:5179`. **Browse for application folder** opens the system folder
chooser from the local-owner maintenance screen. Manual path entry remains
available. Selection supplies a path; the existing manifest, compatibility and
confirmation checks still decide whether that app can open a separate restored
study. No folder is uploaded from a phone or another browser device.

Only one chooser can be open, with cancellation from the desktop or maintenance
screen, a five-minute deadline and cancellation during host shutdown. Windows
owns its helper in a kill-on-close job. Opening another chooser invalidates the
previous restore review. Reloading recovers a pending chooser; a dropped status
request is retried without leaving maintenance stuck. Mac and Linux desktop
adapters exist but await native qualification; headless/missing adapters retain
manual path entry. These are not claims of Mac worker or phone verification.

The final local core check passes all 798 backend tests, protocol checks, secret
scan and web build. Actual Windows desktop acceptance selects the package in the
native Common Item Dialog and verifies the returned field, package review,
reload, cancellation, stale-operation/refused-close boundaries and recovery from
one deliberately dropped status request. Desktop and 390-pixel screens were
inspected. Opening the chooser leaves the package's inventory unchanged. The
existing packaged backup/restore browser workflow also passes on the final build.

Evidence: `artifacts/local-check-picker-20260914-final`,
`artifacts/browser-picker-20260914-d`,
`artifacts/browser-maintenance-picker-20260914-b` and
`artifacts/picker-delivery-20260914-a`. All 381 package files and 178 captured
publication inputs match their hashes. An earlier interactive invocation skipped
its test because the runner filtered its opt-in flag; that invocation is not
native evidence. The runner now passes this explicit flag to the browser only
and refuses to report success when every selected test was skipped.

Activation created and verified
`.data-backups/20260914-180037-189a1f549d774e0f945df632828ea222`, preserved all
study-table fingerprints, the owner key and existing owner session, and left
the Luna bridge running unchanged. Schema remains 5. The launch record is
`artifacts/picker-delivery-20260914-a/launch.json`. No model requests, GPU
inference, worker VMs or GitHub Actions runs were needed.

Cleanup removed both superseded picker app/archive pairs, the old Feed
app/archive pair and 18 checkout build/test directories after process checks.
The current picker app and versions-B rollback app remain, alongside their
archives, immutable worker inputs, the combined worker archive, private data,
backups and compact proof. Final cleanup observed 134.64 GiB free. Removed
candidates require rebuilding their captured sources to replay them.

Remaining delivery work includes signed installation/trust, automatic app
downloads/switching, Mac/Linux native upgrade and chooser qualification, broader
worker/security qualification, Lab evidence and final user UI acceptance.
Physical-phone setup remains explicitly deferred.

## Earlier checkpoint: guided application versions and return launchers

This checkpoint used `artifacts/portable-versions-20260914-b/thaddeus-win-x64` at
`http://localhost:5179`. Maintenance can select another extracted portable app,
verify its complete manifest and payload, check its declared study compatibility,
and restore the selected backup into a separate study. Its generated launcher
rechecks the reviewed package before starting it. An independent return launcher
opens the original study directly with its existing app and newer edits. This
avoids retaining a chain of still older verifier packages across repeated changes.
See [guided upgrade and rollback](STUDY_BACKUPS.md#upgrade-and-rollback).

Portable publication now obtains compatibility metadata from the just-published
executable without starting the product. Packages lacking that declaration,
platform mismatches, incompatible backups, path/link violations, changed manifests
and modified payloads are refused. Package integrity is distinct from publisher
trust: these remain unsigned private development packages. Backup and restore now
budget their full logical payload plus metadata and a 10 GiB reserve, and check
remaining space while copying.

The core check passes 790 backend tests plus protocol/storage/cleanup, secret-scan
and web checks. The final native Windows check passes 20 checks. It selects a copy
of the earlier `portable-versions-20260914-a` build through the final B build's
real owner API, confirms that the host assemblies differ, refuses launch after a
fixture payload change, then launches the selected app and the original-study
return shortcut. Both exports match their expected histories, including the newer
original edits. These are two compatible schema-5 builds; this does not prove a
different-schema migration, another native OS, publisher signing or automatic
process switching.

The packaged maintenance browser test passes, including reload, version review,
separate restore, both launcher locations and unchanged original history. Review
and result layouts were inspected at desktop and 390 pixels. Initial visual QA
caught a checkbox layout issue, fixed before the final package. These narrow-width
screenshots are not physical-phone evidence; maintenance remains local-owner-only.

Evidence: `artifacts/local-check-versions-20260914-b`,
`artifacts/native-versions-20260914-b`, `artifacts/browser-versions-20260914-b` and
`artifacts/version-delivery-20260914-a`. All 381 final package files and 175 captured
inputs matched their declared hashes. The only subsequent core-input change was
the native-check driver, which was executed against the final package. Both native
attempts removed their extracted app copies after process and credential cleanup.

Activation first verified a backup at
`.data-backups/20260914-173120-d5676dd0db89420c9f67984708ce1590`, then preserved every
study-table fingerprint, the owner key and owner session. Schema remains 5. The
Luna bridge was not restarted; no model call, GPU use, worker VM or hosted Actions
job was needed. The final launch record is
`artifacts/version-delivery-20260914-a/launch.json`.

Cleanup removed 25 checkout build/test directories, the earlier version candidate
and the superseded Raven rollback app/archive. Their manifests and evidence remain;
replaying a removed package requires rebuilding its recorded inputs. The active B
package, the Feed package as one rollback, the combined worker archive, pinned
worker inputs and all private data/backups remain. The final cleanup reading was
135.93 GiB free.

This is a usable separate-copy version workflow. Native folder selection was
added in the checkpoint above. Automatic downloading/switching, signed consumer installation, native Mac/Linux
version-transition qualification, broader worker/security qualification and final
UI acceptance remain open. Earlier packages without compatibility declarations
need the documented operator procedure; their support is not inferred.

## Combined Windows host and worker archive

`scripts/package-with-worker.mjs` now packs an existing checked host and pinned
worker directly into one ZIP. It avoids staging another guest disk, budgets
incompressible output with a 10 GiB reserve, verifies all archived contents before
publishing, and cleans incomplete output and builder scratch. The combined
manifest covers the complete payload and preserves the original host provenance.
See [the packaging commands and limits](PORTABLE_PACKAGES.md#one-archive-containing-the-host-and-worker).

The current Windows candidate is
`artifacts/portable-combined-worker-20260914-a/thaddeus-win-x64.zip`: 2,321,534,539
compressed bytes, 9,987,904,565 logical bytes and 3,776 entries. Its SHA-256 is
`1b144d4a1612c08ebc08aa2d8653d99f27a3438a37c5bcba98c5af265a17e4de`.
A separate Python ZIP reader verified every entry and the original worker pins,
including the 8 GiB guest file. Windows Expand-Archive extracted the real payload;
all 18 native package checks passed. The relocated app discovered its worker
without an operator path, kept research disabled pending checking/enrollment, and
passed the existing login, native credential, restart, backup and restore checks.

The core check passes 772 backend tests plus protocols, storage/cleanup contracts,
secret scan and web build. The 14 new archive cases cover exact inventory,
relocation, Windows/Linux archive modes, changed inputs, path refusal,
cancellation, storage admission/failure and malformed or valid-but-tampered ZIPs.
Linux executable metadata is tested; actual Linux combined extraction and macOS
workers are not qualified by this Windows result.

Evidence: `artifacts/local-check-worker-archive-20260914-a`, the combined
publication's packaging/independent receipts, and
`artifacts/combined-native-20260914-a/recovered-summary.json`. The native check removed its
full extracted package after confirming zero owned processes and fictional
credential cleanup. The compressed archive is retained as the deliverable.

The native command's exit-0 result and valid receipt were observed before the
computer restarted at 10:47. After restart, that one summary file contained only
zero bytes. Its separate recovery summary records the observed tool output and
preserved cleanup/launcher evidence; the damaged original remains for inspection.
The archive's full SHA-256 was checked again and is unchanged. No large native
test was repeated to recreate a summary. Future native cleanup/final receipts now
flush to disk before completion; the small failure/cleanup contract passes.

The running Feed app and Luna bridge were not rebuilt for this packaging change.
After an interruption left both recorded processes and ports absent, they were
restarted from the same package/profile. All study-table fingerprints and the
owner key were unchanged. The new launch record is
`artifacts/combined-delivery-20260914-a/launch.json`.
Eighteen checkout build directories were removed, leaving 136.11 GiB free at the
final reading, with the active app, one rollback, worker inputs and private data
preserved. The combined delivery receipt records the final source checks and
exact live web assets.
No model, GPU, VM or GitHub Actions run was used. This remains
an unsigned private development archive; signing, complete redistribution
notices, consumer installation/upgrades, broader platform qualification and the
other delivery gates remain open. Its archive verification is not new worker
execution or confinement evidence.

## Current study: subscriptions plus saved links

The active package is `artifacts/portable-feeds-20260914-a/thaddeus-win-x64`
at `http://localhost:5179`. Feed now supports explicit RSS 2.0/Atom subscriptions,
website feed discovery, hourly background checks with backoff, unread/source
filters, read state, pause/resume/removal, and durable copies in Saved links.
Preview and refresh use the restricted public-web transport, independently of
models and workers. No subscriptions are installed by default. See
[Feed behavior and boundaries](FEED_SUBSCRIPTIONS.md).

The final core check passes all 758 backend tests, plus protocol, secret-scan and
web checks. The package passes 22 browser checks and 17 native Windows package
checks; the opt-in VM browser test is skipped. Desktop and 390-pixel layouts were
inspected. The Feed browser fixture simulates source/API responses; backend
integration tests independently exercise parsing, request limits, storage, API
authority, cancellation, migration and backup/restore. This is not evidence of a
live third-party feed, physical phone or another native operating system.

All 381 package files and 132 application inputs were verified against the
checkout and final core proof. Activation backed up the study to
`.data-backups/20260914-161836-2832adccafa54ce8b5e9435b732aa58b`, added the schema-5
feed tables, and preserved every existing content table, including 21 runs,
238 events, 14 chats, five pages and 18 revisions. The existing owner session and
Luna bridge were preserved; the exact served index/JS/CSS match the package.

Both failed and successful native checks removed their extracted package after
confirming process/credential cleanup. Publication removed staging dependencies
and intermediates. The superseded Settings app/archive and 25 checkout build/test
directories were removed, leaving 138.46 GiB free at the final cleanup reading.
The active Feed package, one Raven rollback package, immutable worker inputs,
user data/backups and compact proof receipts remain. The Raven package requires
the pre-upgrade backup because it cannot open a schema-5 database.

No live model call, GPU inference, worker VM or GitHub Actions job was started.
Evidence: `artifacts/local-check-feeds-20260914-final`,
`artifacts/browser-feeds-20260914-c`, `artifacts/native-feeds-20260914-b` and
`artifacts/resumed-20260914-g`. The wider delivery gates, user artwork/design
acceptance and deferred physical-phone setup remain open.

## Previous checkpoint: expressive raven and accurate task captions

The active package is
`artifacts/portable-raven-20260914-a/thaddeus-win-x64` at
`http://localhost:5179`. The raven has new sixteen-ink pixel artwork and discrete
wing poses, blinking, idle movements, attentive posture and a completion bow.
Every animated part honors reduced motion. Desktop and mobile captions follow
the selected task before other pending work, and correctly report disconnection.
Idle portraits are labeled images rather than buttons with no action.

All 21 browser checks pass, with the new presentation fixture covering state
selection, keyboard access, folded/open poses, reduced motion and unchanged data
during viewing. Status variants in that fixture are simulated UI inputs. The 94
backend source inputs remain unchanged; all 381 package files were verified and
124 application inputs match the checkout. Actual worker execution evidence is
reused from the previous native workflow, not rerun for this art/UI change.

Activation backed up the current study at
`.data-backups/20260914-154900-907de44a90d74fbb9d715f16c5afec29` and preserved all
tables, including the owner's newer conversation: 21 runs, 238 events, 14 chats,
five pages and 18 revisions. The existing owner session and Luna bridge were
preserved. Staging dependencies/intermediates and the superseded worker-progress
app/archive were removed; the Settings package is the one retained rollback.
No live model call, VM, GPU inference or hosted job was started for this work.
Evidence: `artifacts/raven-art-20260914-a`, `artifacts/browser-raven-20260914-a`
and `artifacts/resumed-20260914-f`. Artwork acceptance and the broader delivery
gates remain open. The owner selected subscriptions plus saved links for Feed;
that implementation is next, with fetching separate from model dispatch.

## Previous checkpoint: organized Settings

The preceding package was
`artifacts/portable-ui-settings-20260914-b/thaddeus-win-x64` at
`http://localhost:5179`. Settings is divided into Connections, Research worker,
Permissions & devices, and Storage & backups. Unsaved forms survive section
switches, repeated owner sessions are expandable, and storage/backup controls
are together. Token usage stays visible above the workspace. This is another
implemented UI improvement, not user acceptance of the final design or raven.

All 21 browser checks pass, including the new keyboard/mobile navigation check
and existing connection, maintenance/restore, approval, collection and token
checks. Exactly one settings section is visible; navigation makes no mutating
API requests and preserves exported data. All 381 packaged files were verified;
94 backend inputs are identical to the preceding package and 123 application
inputs match the checkout. The earlier native research evidence is reused for
unchanged execution code; the opt-in VM case was not rerun for navigation changes.

Activation verified a backup at
`.data-backups/20260914-153322-fb9a425b07544d179770d3868f766d7b`, preserved every
study table and the existing owner session, and left the Luna bridge running.
The active package and previous worker-progress package are retained. Three
superseded app/archive pairs were removed after checking for live processes;
their source snapshots, manifests, hashes and receipts remain. Both package
builds removed staging dependencies and intermediates automatically. No VM,
GPU inference, live model call or hosted Actions job was started. Evidence:
`artifacts/browser-ui-settings-20260914-c` and `artifacts/resumed-20260914-e`.

## Storage cleanup and test retention

The owner interrupted testing to reclaim disk space and explicitly requires
cleanup after future tests. Twenty redundant large disks/exports/inspection
copies were removed, along with build intermediates from 61 captured build trees.
Free space rose from about 4 GB to over 130 GB; the exact observations, removed
file hashes and cleanup records are under `artifacts/storage-cleanup-20260914-a`.
Original diagnostic roots, the active package, current worker inputs, reusable
Linux assets, study data/backups and unrelated benchmark/model files remain.
The main host and Luna bridge remain running; the study serves HTTP 200.

Local core/package checks and the primary VM/image runners now check an expected
allocation plus 10 GiB of free-space reserve. Package staging intermediates and
disposable Linux product disks are removed after execution; retaining a product
fixture for a specific repeat requires `--retain-fixture`. Cleanup failures are
reported, and a process that is not confirmed stopped keeps its disks. See
[local checks and retention](LOCAL_CHECKS.md) and the repository `AGENTS.md`.
Three small storage contract tests and script syntax checks pass. No large VM
or package test was restarted for this scripting change. Legacy runners still
require a storage audit and explicit scratch cleanup before use.

The native package runner now also removes its extracted package after confirmed
process/credential cleanup, including failed checks. It keeps the tested manifest
and small study/backup evidence, refuses deletion after uncertain launcher exit,
and reports cleanup in the overall result. All 17 native Windows checks passed
against the existing package without rebuilding it; its extracted copy was
removed automatically (`artifacts/portable-cleanup-20260914-a`). A tiny damaged
inventory fixture verifies failure cleanup and preservation of the original
publication. No VM, GPU, live model request or hosted job was used.

## Worker check progress and independent Windows bundle

The preceding package was activated at `http://localhost:5179`, from
`artifacts/portable-local-worker-progress-delivery-20260914-a/thaddeus-win-x64`.
Activation made a verified backup at
`.data-backups/20260914-151119-1382bc4ea12c4f03a5aa331092c2e7a3`, preserved every
study table and the existing owner session, and left the Luna bridge running.
The exact served web shell matches the package. No model request was made.
Private activation/launch evidence is under `artifacts/resumed-20260914-d`.

Worker setup now displays the actual verification phase, verified runtime-file
count and elapsed time. The owner can cancel the exact active check. Starting a
recheck invalidates old positive admission; cancellation or a late result cannot
enable research. The API remains owner/CSRF protected and starts no VM or model.

The pinned Windows QEMU build mishandled Unicode input paths, including its
default firmware lookup. Existing compatible Windows short paths now carry
native file arguments; canonical paths and hashes remain the installation
identity. Setup explains an unavailable alias instead of enabling a worker
that cannot start. No filenames or Windows settings are changed. Native research
from the relocated path passes with seven synthetic replies, 910 fixture tokens,
exact approved import and verified worker/workspace removal. The complete core
check passes 729 backend and ten protocol/storage tests; the delivery check
passes 17 package, five credential and 20 browser checks. All 154 packaged
application/source inputs match the core receipt; four additional guides match
the checkout. See [bundle proof and limitations](BUNDLED_WORKER.md).

Full independent preparation verifies 3,394 files and about 9.87 GB logical bytes.
The new copy passed desktop discovery/owner enablement and required fresh
verification after relocation. Those checks took 68.04 and 9.77 seconds; the
earlier 60-second fixture deadline was insufficient for the first observation.
A damaged manifest pin was refused. The original image hash is unchanged, all
test hosts exited, and the copied app/worker and diagnostic scratch were removed.
The cleanup helper's absent-process exit-code mistake and its verified follow-up
remain explicit in `artifacts/bundled-worker-progress-20260914-a/completion.json`.
No native execution was claimed for this admission-only copy. The main study
uses its original verified worker inputs; release qualification remains open.

## Earlier readiness-package activation after the owner's return

The owner explicitly requested continuation. The updated Windows package and Luna
High bridge are running at `http://localhost:5179`. The startup and diagnostic
changes below are active in the main study. Its maintenance flow made a verified
seven-file backup before replacement, and every study table matches its
pre-replacement fingerprint: 20 runs, 232 events, five pages, 18 revisions and
12 chats, including unchanged provider, enrollment and session settings.
The existing owner session still works; notes and the host key are unchanged.
The running executable path, packaged application hashes and served web shell
match `artifacts/portable-local-readiness-delivery-20260914-a/thaddeus-win-x64`.
Private launch/activation evidence is under `artifacts/resumed-20260914-b`.
The backup is `.data-backups/20260914-132626-b4e217b3bb5b4012ae32d5e4e197e2ae`.
The model bridge was not restarted during replacement, and activation made no
model request. Both GitHub workflows remain disabled.

The small-disk fixture now works. The retained root contained a `/.dockerenv`
marker and systemd identified the VM as Docker, so its container-mode command-line
reader ignored the kernel mount option. A new temporary copy removes the marker
and becomes a standalone compressed QEMU root, about 235 MiB. The original raw
root's hash is unchanged. The native run now identifies KVM, observes the fourth
disk mounted read-only and passes all seven workflow checks, including restart,
exact approved import and reviewed workspace removal. Evidence:
`artifacts/linux-product-shared-images-20260914-c/verified.json`.
Its six replies and 780 tokens are synthetic; no live model or GPU was used.
The shared repeat runner is also verified below.

Gateway readiness now has a measured 60-second window, two seconds between
completed unsuccessful probes and a maximum of 30 probes. The process is started
once. The deadline cancels a stalled probe, owner cancellation remains distinct,
and a response after the deadline cannot admit the worker. Private diagnostics
and exact workspace inventory now support all 30 bounded replies. The full local
suite passes 693 backend tests, seven protocol checks and the web build in
`artifacts/local-check-readiness-window-20260914-a`. The new policy passes all
seven native Linux checks in `artifacts/linux-product-readiness-window-20260914-a`,
with three Gateway boots, 14 health replies and six synthetic model replies
(780 fixture tokens). All 143 captured application sources match the full suite.
The Windows delivery package also passes 17 native package checks, five credential
checks and 19 browser checks. Its 149 application/tested source inputs match the
core receipt; four additional packaged documentation files match the checkout.
The unchanged Linux repeat also passes all seven checks in
`artifacts/linux-product-repeat-readiness-window-20260914-b`, using the same
compressed root and captured package without a rebuild or large disk copy.
Both readiness-policy runs use six synthetic replies each. The Windows VM
browser research check also passes: a durable question, restart, one correction,
exact approved import and reviewed workspace removal. It uses seven synthetic
replies (910 fixture tokens), with evidence under
`artifacts/readiness-windows-20260914-a`. Across the shared-image qualification,
two readiness-policy Linux runs and Windows research check, there are 25
synthetic replies and 3,250 fixture tokens, with no live model, GPU inference or
hosted Actions. All test-owned VMs/containers have exited. These passes support
the changed startup policy and tested workflows, not universal startup
reliability or production qualification. Original failure images remain intact.

## Earlier shutdown checkpoint

The study closed through its maintenance API on September 14 at 12:08 UTC,
after verifying a seven-file backup under
`.data-backups/20260914-120843-300bb8305c2246c48422a59b9b951c07`.
The Windows host and Luna bridge have exited, ports 5179/5181/5183 have no
listeners, and no fixture containers remain. The private checkpoint is
`artifacts/stopping-20260914-a/checkpoint.json`. No model requests, GPU work or
hosted Actions were started for this shutdown.

The shared-image fixture work is an **unfinished experiment**. Both attempts,
`artifacts/linux-product-shared-images-20260914-a` and
`artifacts/linux-product-shared-images-20260914-b`, failed package admission
before starting a worker or producing any synthetic model replies. The second
receipt proves that the expected `/opt/probe/worker-image/worker-base.ext4`
did not exist and the fourth disk was absent from the guest mount inventory,
although the requested mount appeared in the kernel command line. The cause
of the missing mount is not yet established. Its completed build and zero-exit
outer boot are not a passing product result. Original images remain intact.

At that shutdown, the next steps were to fix the shared-image mount, implement
bounded Gateway readiness and prove native restart and cleanup. Those steps are
now verified in the resume section above. The shutdown's 688-test core receipt
and failed fixture attempts are historical evidence, not current instructions
to pause or repeat that work. GitHub Actions and GPU work remain excluded.

## Earlier September 14 resume

The owner requested continuation on September 14. The Windows study and Luna High
bridge were restarted. Every study table matched its pre-start fingerprint,
and the exact saved web package was verified over HTTP. Startup made no model
request. Private startup evidence is under `artifacts/resumed-20260914-a`.
Before shutdown the study had closed through its maintenance API after a verified
backup. The shutdown checkpoint is
`artifacts/stopping-20260913-a/checkpoint.json`; the backup is
`.data-backups/20260914-030628-51f39e8afeae474381c3de35b707e1f4`.

The latest diagnostic evidence distinguishes the Linux restart failure from a VM
transport failure: all ten resumed Gateway health commands completed with
`ECONNREFUSED`. Offline inspection preserved the original disks but recovered no
useful second-startup log. A bounded private log/process snapshot is now implemented
before failure cleanup. All 682 backend tests, seven protocol checks and the web
build pass in `artifacts/local-check-startup-snapshot-20260913`. The native fixture
`artifacts/linux-product-startup-snapshot-20260913-a` was deliberately interrupted
for shutdown, before a product result; its `interruption.json` records that fact.
It does not qualify the new snapshot or establish a fix. No live model, GPU or
hosted Actions was used. The diagnostic code has not been installed in the main
study.

The fresh, unchanged-package attempt in
`artifacts/linux-product-repeat-startup-resumed-20260914-a` completed research,
restart, bounded correction and exact approved import with six synthetic replies
(780 fixture tokens). It then exposed a diagnostic integration regression:
workspace removal's old inventory rejected the new health-reply files. A regression
test fails before the fix; all 30 focused workspace/diagnostic checks pass after it.
The fix recognizes only the exact bounded diagnostic names and keeps drift,
unknown-file, credential and ownership refusals. The full local suite now passes
688 backend tests, seven protocol checks and the web build; its inputs match all
142 source files in the captured native package.

The updated native attempt, `artifacts/linux-product-cleanup-diagnostics-20260914-a`,
reproduced the earlier startup failure before reaching workspace removal, so native
confirmation of the cleanup fix is still pending. This time the private startup
snapshot succeeded: the resumed `openclaw-gatewa` process was in runnable state,
and its latest log said `starting HTTP server...` shortly before the observation.
All ten health probes had returned connection refused. The process was still
initializing at the cutoff; the next change must give the single Gateway process
a measured readiness window, preserve cancellation and prove eventual readiness
without restarting it or replaying a task. This observation does not itself
establish that a longer window fixes every startup failure.

The two September 14 native attempts used eight synthetic replies and 1,040
fixture tokens, with no live model or GPU requests and no hosted Actions. Original
failure disks are retained. The main Windows package has not been upgraded with
these diagnostic changes. Disk headroom is limited; reuse captured read-only
fixtures where possible instead of repeatedly copying the worker base.

## Earlier checkpoints

The latest [Linux resume investigation](LINUX_PRODUCT_PREVIEW.md#identifying-a-failed-resume-operation)
reproduced the failure with the corrected guest image and now identifies Gateway
health checking as the failing step after boot, grant refresh and launch. The
first worker had closed its filesystem cleanly. A private first-transport-error
receipt is now implemented; its diagnostic build and one unchanged-build repeat
both pass all seven Linux product checks, so the underlying error remains
unresolved. Across the failed and passing attempts there were 14 synthetic replies
and 1,820 fixture tokens, with no live inference, GPU or hosted Actions. Original
failed disks are preserved. The existing Windows study and its model bridge were
not restarted or updated by this diagnostic change.
The final local suite passes 678 backend tests, seven protocol checks and the
web build; all source fingerprints match their captured inputs.

The [guest shutdown fix](GUEST_SHUTDOWN.md) is active in the main Windows study.
The old image reproducibly left journal recovery pending after a successful
poweroff. The rebuilt image closes the filesystem cleanly, preserves all 32
acknowledged test files across restart, and refuses successful poweroff for a
deliberately damaged copy. Actual OpenClaw/WHPX integration and the full research
browser workflow also pass with that image: 11 synthetic replies, 1,430 fixture
tokens, no live model or GPU use. A verified backup and before/after fingerprints
preserve all study history, provider settings and saved sessions; only the worker
enrollment changed. Usage remains 116,213 reported. Private activation evidence is
under `artifacts/guest-shutdown-20260913`. This fixes the demonstrated shutdown
defect, but does not establish the cause of the earlier Linux continuation failures.

The owner can now [check this computer](HOST_REQUIREMENTS.md) before configuring
a worker. The read-only report covers native platform support, virtualization,
and Linux service/resource prerequisites, with specific next steps. Local core
validation passed 669 backend tests, seven protocol checks and the web build.
Packaged Windows checks and desktop/phone-width browser coverage pass. The Linux
fixture passes all seven product checks in
`artifacts/linux-product-requirements-20260913-d/verified.json`, including the
new prerequisite endpoint and full continuation/import. Two preceding Linux
attempts failed during continuation after application restart; their cause is
unresolved. Additional private boot/recovery diagnostics are implemented, but
the later pass does not establish a fix for those intermittent failures.

Two further unchanged-package Linux repeats also pass all seven checks with six
synthetic replies each. The [frozen fixture runner](LINUX_PRODUCT_PREVIEW.md#repeat-the-packaged-workflow-without-rebuilding)
retains each attempt without another application build or large base-disk copy.
Offline inspection of failure C now includes journal recovery on a separate
diagnostic copy: the second VM reached its ready observation, and the first
guest logged ext4 write failures during shutdown. This narrows the investigation;
the failing continuation operation and the cause of those write errors remain
unproven. No original failed disk was repaired, booted or used to replay a task.

This prerequisite UI is now active in the main Windows study. A verified backup
and before/after fingerprints preserve all 20 runs, 232 events, five pages,
18 revisions, 12 chats, provider/enrollment settings and saved sessions. Token
usage remains 116,213 reported. The final package passes 17 native checks,
five credential checks and 19 browser checks (the opt-in native worker browser
case is separate). Packaging also exposed a Windows PowerShell limit on the
generated restore launcher's path. Compact sibling launcher names fix the
observed case without shortening the restored data path or changing execution
policy; excessively long parent/package paths remain an explicit limitation.
Private delivery evidence is under `artifacts/host-requirements-20260913` and
`artifacts/local-check-host-requirements-delivery-20260913-b`.

The earlier [packaged Linux product preview](LINUX_PRODUCT_PREVIEW.md) connects the
native supervisor, worker factory and explicit owner setup. Six native product
checks passed: malformed service refusal, installation-bound enrollment, observed
VM resources, saved question/context/usage across application restart, one bounded
quotation correction with exact approved import, and reviewed workspace removal.
The fixture made six synthetic replies accounting for 780 tokens; no live model,
GPU or hosted Actions was used. The same backend passed 639 tests plus seven
protocol checks. Consumer provisioning, signing, macOS/Arm worker qualification,
final UI acceptance and physical-phone verification remain open.

The [Linux worker backend lifecycle](LINUX_WORKER_LIFECYCLE.md) now passes native
creation, stopping-point recovery after host-owner loss, restart, disk-writer
exclusion and retired workspace cleanup. Eight Linux checks and seven Windows
checks pass, including damaged-disk retention without repair or boot. The local
suite passes 639 backend tests, seven protocol tests and the web build. Linux
application factory/setup and packaged supervisor are connected by the subsequent
product check above; the main installed study is unchanged. Active-worker crash evidence is separately limited:
the retained Linux experiment detected leaked clusters and refused recovery.

The shared [QEMU session now runs on native Linux KVM](LINUX_QEMU_SESSION.md).
Six real session checks pass on Linux and Windows: private overlay, actual
OpenClaw 2026.9.4 guest, TLS 1.3 channels, guest file/device inventory, fixed
broker routing with a denied path, and independently observed shutdown. Linux
records actual cgroup limits and every executable mapping against its read-only
runtime bundle. The full local suite passes 630 backend tests, seven protocol
tests and the web build. Evidence is under `artifacts/linux-qemu-session-20260913-b`
and `artifacts/windows-qemu-session-20260913-b`. The installed app is unchanged.
The lifecycle and product evidence above extend that session checkpoint. Consumer
admission remains open; the expired QEMU signer is recorded, not
treated as release trust.

The [Linux process owner](LINUX_PROCESS_OWNERSHIP.md) passes nine real-process
checks in a disposable Linux x64 VM, including detached descendants, owner and
supervisor crashes, observed CPU throttling and a service deadline while its
owner is paused. Sixteen deterministic contract checks pass, and the full local
suite passes 624 backend tests, seven protocol tests and the web build. The installed main
package was unchanged. The shared Linux session now has the evidence above;
product integration is covered by the later packaged check above. The process receipts are under
`artifacts/linux-process-ownership-20260913-c/boot-deadline`.

The owner's Actions allowance is exhausted. Both hosted workflows are disabled
and now have manual-only triggers. [Local checks](LOCAL_CHECKS.md) replace hosted
runs for daily development; no new runner job should start without the owner's
explicit decision. The completed native restore matrix remains valid for its
recorded application revision.

The [QEMU launch planner](QEMU_MANAGED_BACKEND.md) now separates VM configuration
from Windows process ownership. Local checks passed 608 backend tests, seven
protocol tests and the web build, with no hosted jobs, live model requests or VM
starts. The previous Windows argument contract is preserved; Linux/macOS plans
remain unexposed until their process/resource boundaries, runtime packages and
native execution are verified. Evidence is under
`artifacts/local-check-qemu-plan-20260913`. The installed main package is unchanged.

[Guided restore](STUDY_BACKUPS.md#restore-through-the-maintenance-screen) selects a
recorded backup, fixes confirmation to its reviewed manifest, and verifies a new
sibling study without overwriting later original edits. Published hosts prepare
a separate launcher; the current host must close before that launcher is opened.
Intent/result receipts are retained without automatic replay. The backend suite
passes 591 tests, including tampering, stale review, repeated confirmation,
long paths and permission checks. Eighteen packaged browser checks and seventeen
native Windows package checks pass. The actual generated launcher opens the
restored history, makes its next backup and closes cleanly. Packaging tests exposed
and fixed missing Windows long-path opt-in and copying of active launcher logs;
the original logs remain in place. Private evidence under
`artifacts/guided-restore-20260913` records final package and main-instance delivery.
Different-version package selection, interrupted
attempt UI recovery, signed updates and consumer rollback remain open.

[Public search](SEARCH_CONNECTIONS.md) adds an optional host-brokered Brave
connection, explicit task query allowances, separately granted result-page
retrieval and visible search attempt receipts beside model usage. Local validation
passes 578 backend tests, including 32 search and worker-compatibility cases. The
ordinary packaged browser suite and a real OpenClaw/QEMU search workflow pass:
one simulated provider response, real public-page retrieval, a durable question,
native continuation, one bounded source correction, exact approved import and
reviewed workspace removal. Eight synthetic model replies account for 1,040
fixture tokens; no live search-provider or model request was made. Fifteen native
Windows package checks include search-key storage, restart and removal. Private
results are under `artifacts/public-search-20260913`; its checkpoint records final
delivery state. Real Brave account acceptance and search quality remain unverified.

Owner [maintenance](STUDY_BACKUPS.md) now has an application flow: review the
locations, close an idle study, see the verified backup, and reopen the same study
or finish shutdown. New work is refused during the transition; open event streams
close cleanly. The temporary local screen has no model, worker or product-store
services and accepts only its initiating owner. It does not yet select an
upgrade package; the guided separate-study restore above extends this screen.

The packaged host now includes offline [study backup and restore](STUDY_BACKUPS.md).
It takes an exclusive source lease, makes a standalone SQLite snapshot, preserves
file bytes/timestamps, and validates a new restore before installing its directory.
Existing studies and later edits are never overwritten. Seventeen focused tests
cover journal recovery, active ownership, paths, tampering and version refusal.
The native package matrix now exercises the actual restored product. Guided
consumer upgrade/rollback remains an open part of distribution.

The [model connection form](MODEL_CONNECTIONS.md) adds explicit native or
session-only credential storage, endpoint binding, removal and setup checks that
do not generate text. Keys stay outside application data and the worker. Local
validation passed 515 backend tests and sixteen ordinary browser checks;
native package receipts separately establish each operating system's support.
Signing and consumer credential prompts across upgrades remain open.

Credential/restart validation also exposed unsafe store disposal during an
admitted write. A deterministic regression fails before the fix; disposal now
uses the same operation lock, and the single owned SQLite connection does not
leave a pooled handle behind after its lease ends. Earlier failed test receipts
remain under `artifacts/model-onboarding-20260913`.

The [portable package workflow](PORTABLE_PACKAGES.md) now builds native Windows
x64, Linux x64 and both Mac architectures from captured sources, with the runtime
and PWA included. An extracted Windows package passed local startup, owner-ticket,
conflict and data-preserving restart checks. Native CI executes each target;
the retained run receipts determine which archives are verified. This adds a
foreground Unix launcher, not a signed installer or a macOS/Linux VM worker.
Main-study data and its running Windows instance are not changed by packaging.

The current worker qualification work exercises the actual [transport boundaries](TRANSPORT_BOUNDARIES.md):
37 focused checks plus a real VM routing/size-limit/request-exhaustion workflow.
It exposed and fixed post-stop command admission before the asynchronous process
receipt finished. This is bounded Windows development evidence, not a claim of
universal confinement or a qualified default installation.

The prior [checkpoint recovery](WORKER_CRASH_RECOVERY.md#restoring-a-saved-stopping-point-in-the-product)
update is active in the main study: exact owner inspection and restoration,
435 backend checks, fifteen browser checks and the real VM workflow with synthetic
replies. Its closed backup and byte-level preservation receipts are under
`artifacts/checkpoint-recovery-20260913`. Historical builds below retain their own
evidence cutoffs; use the latest private `checkpoint.json` for current process,
package and CI identities.

The first [workspace redesign](UI_REDESIGN.md) is implemented: conversation in
front, separate saved collections, a collapsible activity log and an animated
pixel raven. Collection schema 4 preserves existing history; To-do completion
is explicit. Local validation passed 410 backend tests, fifteen ordinary
browser checks and the full native research path with synthetic replies.
Feed is saved reading for now; subscriptions and final user design acceptance
remain open. Upgrade and current-instance receipts for this pass are retained
under `artifacts/ui-workspace-20260913`; the instance history below describes the
previous builds, not a claim that the entire architecture is finished.

The [guided Windows preview setup](HOST_SETUP_PREVIEW.md) now uses the actual
product worker factory and an owner-enabled, installation-bound configuration.
Its complete native browser workflow passed with separate guest/host relay
ports, seven synthetic calls, one bounded repair and reviewed workspace removal.
All 391 backend tests and twelve ordinary browser checks passed locally. This
is an explicit development option; default and production qualification remain
separate, open requirements.

Published Windows development packages also include a [portable launcher](WINDOWS_LAUNCHER.md):
it keeps data outside the package, reuses an exactly recorded running host,
refuses conflicts and opens the browser with a one-minute, single-use owner
handoff. Signing, downloaded-package trust and consumer installation remain
unqualified. The main study uses this launcher with its existing data directory;
the default launch profile reuses the current owned host.

| Area | Implemented | Still required |
|---|---|---|
| Product | Conversation, scoped plans, approvals/history/export; research composer, durable coordinator, native continuation and reviewed import; visible token usage, per-message limits and reviewed workspace removal | Qualified default worker admission and broader interruption recovery |
| Isolation | `ISandboxBackend`, pinned Docker CLI adapter; explicit Windows QEMU backend with owned processes, queried host resource caps, mutual TLS, private overlays, bounded transfer and explicit crash reconciliation | Resolve Docker image-service failure; complete production confinement/resource and broader crash qualification |
| OpenClaw | Pinned image, public Gateway adapter, bootstrap/context hooks; native integration with Luna High; product-host research orchestration verified through a real VM with scripted replies | Qualified worker network path and production admission; broader outcome reconciliation |
| Brokers | Official MCP SDK, short-lived task grants, scoped tools, exact import proposals, model destination/budget enforcement; bounded public-page retrieval with task host grants and source receipts | Public search and research admission, qualified network path, native effect reconciliation |
| V1 reuse | Declarative personality, frozen source context and native hooks; explicit source-linked memory and scoped delivery; native quotation checks and bounded repair with retained attempts | Broader evidence validation and independent model/product-quality evaluation |
| Lab | Independent native runner uses the product API/coordinator; frozen repeated controls, actual repair delivery, separate scorecards and false-success negatives verified with real OpenClaw/QEMU and scripted replies; one live Luna captured-file task passed with complete usage | Live native comparisons, controlled model context/sampling, held-out quality evaluation and worker/provider resource measurements |
| Distribution | Native portable package pipeline, bundled PWA/runtime, Windows launcher and foreground Unix launch path; extracted archive checks | Signed supported releases, consumer lifecycle, macOS/Linux worker qualification and wider OS coverage; physical phone last |

## Development instance history

The host uses `http://localhost:5179` and the existing private `.data` directory.
The Luna bridge uses loopback port 5181, model `gpt-5.6-luna`, high reasoning.
After the user's shutdown, both ports were confirmed stopped. A complete closed
data-directory backup was copied and hash-verified before starting the tested
`ba60e17` package. The main browser now shows the token bar and remains unlocked.
Its 19 tasks, five pages, ten chat entries and 103,309 reported live tokens were
observed after that initial startup. Recovery receipts:
`artifacts/recovery-20260913-0554`.

The subsequent guided-setup update runs the self-contained `607d9b9` package,
whose exact revision passed CI run `34763653178`. A fresh closed-data backup and
row/file comparison preserved all 20 tasks, 232 events, five pages, 18 revisions
and 12 chat entries present at this update. The existing owner browser remained
unlocked and visibly reported 116,213 retained live tokens. This total excludes
separate Lab/CLI use and is not a bill or account quota.

The operator-configured Windows preview is now checked and explicitly enabled;
its host worker listener is loopback port 5183. The model remains Luna High via
5181. No model call or VM boot was used to enable this main instance. Package
and state receipts are under `artifacts/host-setup-20260913`; the active web asset
is `index-TTT9Ciq3.js`. The first launch used the repository as content root and
returned 404 for the web shell; it was corrected to the package working
directory. Both launch records and the prior immutable package are retained.
Default installation and production admission still await qualification.
Glimmer remains loaded elsewhere for shared benchmarks; no local inference,
reload, eviction or benchmark mutation was performed for this checkpoint.
The worker model gate refuses direct local GPU requests until a resource lease
adapter is implemented. The caller's existing benchmark queue must be respected.

The user has requested a UI redesign after the functional work. See
[UI_REDESIGN.md](UI_REDESIGN.md) for the observed problems and acceptance scope;
the current interface is not an accepted final design.

Docker Sandboxes 0.42.1 is installed per user and now authenticated. After the
user signed into Docker, the supported credential helper supplied the existing
Docker Hub login to `sbx login --password-stdin`, without printing its value.
The free account is sufficient. The new Sandboxes policy was initialized to
`deny-all`; no existing sandbox policy or Docker Desktop setting was replaced.

Sandboxes inventory and all 12 generic diagnostics pass, including Windows
hypervisor support. However, template listing, local import and registry pull
fail because its image backend cannot connect to its own Windows Unix socket.
The observed symptom matches [Docker issue #157](https://github.com/docker/sbx-releases/issues/157),
reported against an older version; this run reproduces the symptom on 0.42.1.
No VM was created. Empty inventories reconciled both failed creation IDs, with
receipts under `artifacts/worker-qualification-20260912`. No reboot, UAC feature
change, Docker Desktop restart or automatic weaker backend fallback occurred.
The app's newer inspection distinguishes this image-service failure from sign-in.
An upstream refresh on September 12 confirmed current reports of the same failure
on 0.42.1 and the tested nightly; resets and reboots had not fixed those reports.
Docker's maintainers are investigating. No verified workaround was available in
the issue thread, so those disruptive steps were not repeated on this host.

A separate [QEMU feasibility probe](QEMU_FEASIBILITY.md) then booted Alpine Linux
through the existing Windows hypervisor with one CPU, 512 MiB RAM, no guest NIC
and no host filesystem shares. A dedicated virtual serial channel returned
65,536 random bytes exactly, and QMP observed pause, resume and clean guest
shutdown. This establishes a candidate second VM backend without a Docker account
in its execution path. It is not registered in the product; OpenClaw, authorized
broker transport, persistence, recovery and production confinement remain open.

The later [native VM integration](NATIVE_VM.md) passed selected-note and public
retrieval workflows with the pinned OpenClaw engine inside QEMU/WHPX. It used the
shared host brokers and execution control, shut the entire VM down at a durable
question, booted a distinct process on the same private overlay, and continued to
an exact approved import. Direct public/host-app access and unauthorized broker
routes were denied in the observed negative cases. These scripted integration
results strengthen the native boundary evidence; a production backend, admission,
hostile-input/resource bounds and crash recovery remain required.

The next [Windows ownership primitive](WORKER_PROCESS_OWNERSHIP.md) assigns a trusted
worker to a kill-on-close job atomically at process creation. Real helper-process
checks and a pinned QEMU/WHPX check passed abrupt owner death, descendant cleanup,
cancellation, lifetime/output limits and unrelated-process preservation. Cancelled
job termination can return OS exit code zero; the typed completion record retains
the stop reason and refuses to classify it as success.

The later [managed QEMU backend](QEMU_MANAGED_BACKEND.md) connects that owner and
fresh mutually authenticated TLS channels to the real `ISandboxBackend`. The
NativeCheck host now owns the VM directly. Its public-source/question/whole-VM
restart/import case passed with TLS 1.3 on both channels, separate per-boot
certificates and independent shutdown receipts. Input files remain pinned and
read-locked; private keys were cleaned up and the base disk was unchanged. This
explicit development path remains outside product admission. The subsequent
[crash recovery checkpoint](WORKER_CRASH_RECOVERY.md) adds a backend ownership
lease, read-only overlay checking, abandoned TLS-key retirement and task-grant
rotation. A real host kill exposed unflushed bootstrap files; native quiescence
now also requires a guest filesystem checkpoint. The full public-source workflow
passed after this fix, preserving its question and interrupted-write marker.
Corrupt, missing and locked images were refused without automatic repair or boot.
Broader crash-point and adversarial qualification, production orchestration and
release packaging remain open.

## Evidence

- [Full QEMU runtime package](QEMU_RUNTIME_PACKAGE.md): new launches require a
  pinned manifest of the complete vendor tree, including libraries and firmware.
  A fresh portable extraction produced 3,389 verified files; changed, missing,
  unexpected, linked and case-colliding inputs are refused. The real native
  question/shutdown/restart/import workflow passed with that bundle at `3ad2211`;
  post-run hashes and exact saved bytes matched. Receipts:
  `artifacts/qemu-package-integrity-20260913` and
  `artifacts/qemu-managed-scripted-web-1789308260562`. All 379 backend tests pass.
  Five synthetic responses, zero live inference/GPU. Publisher signing, updates,
  guided distribution and broader worker qualification remain open.
- [Windows VM host resource limits](QEMU_HOST_RESOURCES.md): committed-memory, CPU
  and host-process limits are applied and queried before QEMU starts. Six added
  real Windows resource controls passed; the complete ownership fixture has 16
  checks and the backend suite has 359 passing tests. A new native public-source/
  question/whole-VM restart/exact-import workflow passed at source `af15ab3`, with
  limits unchanged across both boots and shutdowns. Five synthetic responses,
  zero live inference/GPU; pinned inputs unchanged and no VM remaining. Receipts:
  `artifacts/qemu-host-resource-20260913` and
  `artifacts/qemu-managed-scripted-web-1789307315188`. Broader qualification remains open.
- [Live captured-file pilot](NATIVE_LAB.md#september-13-captured-file-pilot): one
  registered Luna High task passed the native question/shutdown/resume/capture/
  exact-import workflow and independent document checks, without repair. Four
  calls reported 70,874 input + 1,174 output = 72,048 tokens, with no unknown usage
  or remaining reservations. The campaign's `usage.md` makes its accounting
  directly visible. All 227 source/assembly/VM pins were unchanged; the grant was
  revoked and the owned worker removed. Receipt:
  `artifacts/native-luna-artifact-20260913-a`, source `91b1a09`.
  Protocol `PASSED`, efficacy `INCONCLUSIVE`; no GPU, repeat or release claim.
  The backend suite has 359 passing tests. Default worker admission remains open.
- [Captured-file import](ARTIFACT_IMPORT.md): new managed tasks use contract 2;
  approval is built from the paused worker's file instead of a second model-authored
  content copy. Source failures retain the file and can request one native correction
  inside the original budgets. The real OpenClaw/QEMU product/browser case passed
  with seven synthetic replies / 910 test tokens, exact import and reviewed removal;
  all VM pins were unchanged and no VM process remained. Full backend suite: 345;
  ordinary browser suite: 12. Receipt: `artifacts/research-artifact-reference-20260913-a`.
  The separate live check above passed; default worker qualification remains open. Source changes
  are verified separately from the main app's running `ba60e17` package.
- [Artifact review and retirement](ARTIFACT_REVIEW.md): failed readback now retains
  expected/observed hashes and an actionable classification, visible after cancellation.
  The original failed Luna workspace was retired without new inference or deletion;
  four calls / 73,160 tokens and its original capture remain unchanged. The full
  backend suite has 318 passing tests, plus a focused browser check. These changes
  are in source; the running main app remains the tested `ba60e17` package.
- [Live native Lab pilot](NATIVE_LAB.md#september-13-live-pilot): Luna High completed
  source read, durable question and native continuation, then produced a proposal
  differing from the written artifact. Import was refused and the campaign stopped
  before its second arm. Four calls consumed 73,160 reported tokens; no GPU or
  automatic repeat. The failed capture and workspace remain available. A separate
  Unicode-serialization fix corrected a Lab response-hash false alarm using saved
  evidence only. Verdict remains `INCOMPLETE_OR_FAILED` / `INCONCLUSIVE`; 313 tests pass.
- [Independent native Lab](NATIVE_LAB.md): six frozen native cases completed,
  reversed-order repeats agreed, and both valid-quotation/wrong-conclusion negatives
  were independently caught. All six exact imports were verified, with four false
  successes retained in content scoring. Twenty-eight scripted replies; no live
  inference or GPU. Private campaign: `artifacts/native-lab-protocol-20260912-a`.
  Protocol verdict `PASSED`, efficacy `INCONCLUSIVE`; 307 backend tests pass,
  including deterministic reproduction of a CI-exposed cancellation-ordering race
  and preservation of approved imports awaiting reconciliation.
- [Native evidence repair](NATIVE_EVIDENCE_REPAIR.md): versioned quotation contracts,
  captured source checks, one correction within original limits, fail-closed
  validation and exact approval/review binding. The backend suite has 286 passing
  tests and the ordinary browser suite has 11. A real OpenClaw/QEMU browser case
  delivered failed-check feedback, retained the failed draft, corrected it, verified
  exact approved import and removed its workspace: `artifacts/research-browser-evidence-20260912-a`.
  Seven synthetic requests, no live model/GPU; no research-quality improvement claimed.
- [Source-linked memory](SOURCE_LINKED_MEMORY.md): explicit notebook entries,
  source-version checks, correction/forgetting and scoped native context. The
  255-test backend suite verifies stale-context refusal and retained usage for
  in-flight revocation. Eleven browser cases pass across the ordinary checks;
  a separate native VM case observed the selected entry and excluded unselected
  text in all five model requests. Receipt: `artifacts/research-browser-memory-20260912-b`.
  These are synthetic transport/activation checks, not model-quality results.
- [Workspace maintenance](WORKSPACE_MAINTENANCE.md): owner-reviewed removal is
  available independently of worker startup. A fresh real OpenClaw/QEMU browser
  case verified removal after exact import, preserved the note and receipts,
  revoked the grant and confirmed workspace absence. Receipt:
  `artifacts/research-browser-removal-20260912-b`. The current backend suite has
  237 passing tests; ten ordinary browser tests and one explicit native VM browser
  test pass. These maintenance checks used no live inference or GPU.
- [Research workflow](RESEARCH_WORKFLOW.md): the real product host and browser
  completed native note/public-page research, saved question, reload, continuation,
  artifact readback, exact approval/import, export and retired workspace. Five
  synthetic responses; no inference. Receipt: `artifacts/research-browser-20260912-d`.
- This checkpoint adds coordinator/API coverage for admission, stopped-worker
  review, grant rotation, interruption/cancellation, incomplete cleanup, committed
  answer/decision recovery and caller-supplied chat budgets. The backend suite is
  214 tests; the default browser suite is ten tests, plus one explicit native VM
  browser test. [Token accounting](TOKEN_USAGE.md) is visible on every screen.

- Previous backend suite: 194 passing tests, including the official MCP client over the
  test HTTP transport, scoped authorization, exact import/replay, model admission,
  unknown usage, migration preservation, interrupted-dispatch recovery, uncertain
  native command recovery, duplicate continuation refusal and cumulative active time;
  six TLS cases cover authenticated transport and bounded credential cleanup;
  stop-adapter cases refuse an absent filesystem checkpoint without a retry.
- Luna bridge protocol: four passing CPU-only contract tests.
- Worker configuration: three additional CPU-only tests cover task-bound broker
  routes, refusal of direct host/model endpoints, altered context, and session
  mismatch. A disposable, network-disabled container ran the real OpenClaw schema
  validator without warnings and loaded both typed context hooks with the required
  permissions. Exclusive bootstrap and private file-mode checks passed. No model
  call occurred. See [worker package](../workers/openclaw/README.md) for the exact
  test command and its evidence limits.
- Real Luna High general-function probe: one `thaddeus_ask_user` proposal,
  12,747 reported input tokens and 141 output tokens; zero tool executions.
  Private receipt: `artifacts/model-probes/luna-tool-proposal-1789245180957.json`.
  This verifies inference transport, not native OpenClaw behavior or efficacy.
- The later [native integration](NATIVE_INTEGRATION.md) passed with Luna High:
  scoped MCP read, one durable question, verified Gateway process restart,
  native continuation, worker artifact, exact fixture approval and readback.
  The passing case used three calls and 55,929 reported tokens. An earlier failed
  case exposed an unclear artifact filename contract and retained all 111,144
  reported tokens. These are integration observations, not a benchmark gain.
  The container had no network or host mounts; a test-only stdio relay reached
  the real host brokers. The production VM/network boundary remains unqualified.
- The native fixture now uses shared durable execution control. Its later scripted
  run passed start/quiesce/resume/quiesce, a real Gateway process restart and exact
  import: `artifacts/native-integration-scripted-1789249593075`. Command intents
  precede RPC, lost acknowledgements cannot be replayed, and broker time allowance
  carries across answers. Native tool/process budgets remain a separate open gate.
- A later [public-research check](PUBLIC_RESEARCH.md) passed native selected-note
  reading, real brokered HTTPS retrieval, durable question, Gateway restart,
  continuation and approved import with five scripted model responses. The complete
  source text and URL were observed in native model input. The worker had no
  network; public HTTP ran outside it. No live model or benchmark work was used.
- Browser suite: eight tests passed against a disposable host, including the new
  setup screen at 1440 and 390 pixels and export schema 3. These are browser
  viewport tests, not physical-phone evidence.
- Worker image built from official pinned base manifests. Python 3.14.4,
  Node 24.19.0 and OpenClaw 2026.9.4 were observed. A disposable Docker command
  with network disabled, two CPUs, 4 GiB RAM, capabilities dropped and
  no-new-privileges successfully ran `openclaw --version`.
- Initial image digest before adding the native bootstrap/context package:
  `thaddeus-openclaw@sha256:061f69f26d8abff7d615f2224a6f8ca7f95c4af0958130f75ab88b969d3dee89`.
  Exported as `artifacts/thaddeus-openclaw-2026.9.4-dev.tar`; import failed at
  Docker's local image service. It has not executed inside Docker Sandboxes.
- A self-contained Windows publish passed the fictional plan/approval/import
  smoke from its own output directory, including bundled fixtures. The updated
  development host runs from `artifacts/dev-host-20260912-control`; authenticated
  checks retained all 18 existing runs and the Luna High provider after migration
  to database schema 2. The prior data snapshot is private under
  `artifacts/data-backup-before-schema2-20260912`.
  The control checkpoint preserved all 18 run IDs/states, five pages and the exact
  provider profile. Its published binary path and source provenance were checked
  against the listening process, with a stopped-host snapshot under
  `artifacts/data-backup-before-control-20260912`. A separate package smoke verified
  the served client asset, bundled fixtures, approval/import and export schema.
- Locked restore passes; npm audit reported zero vulnerabilities. The development
  publisher restores RID-specific packages inside a separate source staging tree,
  preserving the source checkout's normal lockfiles and the running host's files.

No model-quality improvement, secure VM boundary, production readiness,
cross-platform host qualification or physical-phone success is asserted.

Protocol references: [MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk),
[Chat Completions](https://developers.openai.com/api/reference/resources/chat),
[OpenClaw hooks](https://docs.openclaw.ai/plugins/hooks),
[Docker isolation](https://docs.docker.com/ai/sandboxes/security/isolation/).
