# MVP release handoff

## September 23 release-prep checkpoint

The owner-study candidate is still F below; no new package has replaced it.
Its ZIP and manifest SHA-256 were rechecked and still match this ledger. A
separate fictional presentation study is running from the same package at
`http://localhost:5379`; its ignored local folder
`artifacts/presentation-study-20260923/` holds a guarded reopen command,
profile, Playwright UI screenshots and receipt for two saved To-dos, one Idea
and one successful fictional Luna High reply. It has no copied owner study,
Google connection or model key. The bridge ran one live call (18,312 charged
tokens) for demo readiness; this is not fresh Windows-user acceptance.

The source has a narrow Chrome interruption recovery change: when an unknown
interrupted model charge has exhausted a paused task's token allowance, Resume
is unavailable before dispatch and the card explains how to close and review a
new task. `BrowserConversationTests` passed 11/11 in Release, all 1,171 backend
tests passed with one opt-in browser fixture skipped, and the frontend production
build passed. These source checks do not make the unchanged F package pass this
new behavior; package and live acceptance remain open. Concurrent Organization
source edits have not been folded into a release candidate or a Git check-in.
The [presentation walkthrough](PRESENTATION_WALKTHROUGH.md) uses fictional data
and identifies the remaining demo-model setup. Publication remains paused.

## Current September 22 running candidate F

`artifacts/portable-portable-inbox-bound-20260922-f/thaddeus-win-x64` is
running the owner study at `http://localhost:5279`, PID 2056. Launch command
for the normal desktop is
`artifacts/preview-inbox-bound-20260922/Start-Preview.cmd`; Codex instead used
`Start-Thaddeus-Preview.ps1 -AllowAgentLaunch -NoBrowser` for this temporary
non-notification pass. Activation receipt
`artifacts/preview-inbox-bound-20260922/runs/20260922-232202-497ce026/result.json`
records the verified schema-12 backup
`.data-backups/20260922-232204-desktop-update-fbe27050a1134abd929e89b2154ce14a`.
Package E is the immediate rollback. ZIP SHA256:
`a985f91938da0e6faf6eb69d08d219250829255849e5c0ccf26d019aa6f74b1a`;
manifest SHA256:
`e74f76ed48e6da054613ef6747ac965a83ef8ad0706ff3eef47c369a8ef1bf69`.
Forty focused email/watch tests and 17 native Windows package checks pass;
`artifacts/portable-check-inbox-bound-20260922-f/verified.json` records the
extracted check and scratch cleanup. Only `InboxWatchConversation.cs` differs
in captured C# runtime source from E.

Controlled live acceptance on E passed one exact delayed self-email and
independent Inbox observation, one bounded Gmail/Calendar brief saved at 5:20 PM
and paused through Chat, and a read-only five-minute watch with one quiet poll
and one relevant unread alert. On F the watch's post-restart poll used a new
message ID rather than replaying the earlier alert; both test schedules are
now paused. A second natural-language watch proposal on F reached review with
host-bound activation time and was declined, so no duplicate job was made.
F also passed one live, bounded public Chrome read through Chat: the reviewed
scope was only `https://example.com/`, the receipt recorded one browser action
and two model calls, the reply reported the observed `Example Domain` heading,
and Close Chrome was used. A second live read-only task visibly entered Pause,
Take over and Resume, but its interrupted model reservation was retained as an
unknown charge and the fixed token allowance then blocked completion of the
IANA page. The browser was closed. Successful continuation after these controls
remains an open acceptance issue. Google-side revocation/reconnect and
fresh Windows-user setup remain owner/environment gates. Native notification
visual delivery/click is last, and agent-launch helper acceptance is not visual
acceptance.
Publication remains paused.

## Historical September 22 candidate E and live send check

The running owner study is now on
`artifacts/portable-email-time-20260922-e/thaddeus-win-x64` at
`http://localhost:5279`, PID 22908. Exact activation and verified backup:
`artifacts/preview-email-time-20260922/runs/20260922-224344-c6c6a09c/result.json`.
The guarded transition retained D as rollback. This temporary launch came from
Codex (`agentLaunch=true`), so it does not verify native notification delivery.
The owner had already confirmed the raven tray tooltip and Open path on C from
the normal desktop. One bounded live Calendar read passed on C.

E adds an explicit local date/time check after D's live review proposed 6:00 PM
for a 5:00 PM request. That incorrect review was denied without scheduling or
sending. E passed 14 focused email tests and 17 packaged native checks; receipt:
`artifacts/portable-check-email-time-20260922-e/verified.json`. ZIP SHA256 is
`0ea5456716ddf8624ef46ea3d551bfa205311b40a1b07778f9a7a3bdc4d41926`;
manifest SHA256 is
`6b4baa3b65db3f041a1a7fedfea3cf023e57a39a69ec8c233f5c57a101c4ea52`.
The exact controlled email to the owner's own account was reviewed and
scheduled once for September 22 at 5:00 PM America/Denver (`23:00Z`). Both
in-app browser tabs were closed before dispatch. The host claimed the sole
occurrence at `23:00:00.0005719Z`, and Gmail `messages.send` accepted it at
`23:00:01.3964889Z` with message ID `1a0cb58faa9aaf54`. An independent
read-only Inbox search (`connected-75b97ae38ae549a4ba8215514a119c66`)
found exactly that ID with `INBOX` and `UNREAD` labels at `23:00:01Z`. This
observes arrival in the owner's controlled self Inbox; no human reading or
universal exactly-once delivery is claimed. Recurring brief/watch, revocation,
fresh-user setup and native notification acceptance remain open. Publication
remains paused.

## Historical September 22 release acceptance checkpoint

The owner-confirmed normal-desktop preview currently runs
`artifacts/portable-tray-tip-20260922-c/thaddeus-win-x64` at
`http://localhost:5279`. Its activation and verified private backup are recorded
at `artifacts/preview-acceptance-20260922/runs/20260922-222141-8eb48d97/result.json`.
The owner confirmed tray tooltip and Open. A bounded live Calendar read completed
through Chat; see `MVP_DELEGATION_ACCEPTANCE.md` for its sanitized operation ID.
Native notification delivery/click, live recurring work, and delayed send remain
separate open gates.

The next **locally checked, not yet activated** candidate is
`artifacts/portable-email-intent-20260922-d/thaddeus-win-x64`, built from dirty
checkout `bf67d4e3c465c432c7264debe5f4f9a984b8a688`. It fixes the observed
delayed-email send-intent clarification loop without changing scheduler, OAuth,
credential, or notification code. ZIP SHA256:
`7defa0dcbc3ff537eb3765ceb6cd9cd43ae57a955a09f5b52f69799f0286686c`.
Manifest SHA256:
`48eeb9d15bb18b41af76529578218f559b5f43600e61941365b4b9fdc174be42`.
Thirteen focused email tests and 17 packaged native checks pass; receipt:
`artifacts/portable-check-email-intent-20260922-d/verified.json`. Its guarded
launcher is `artifacts/preview-email-intent-20260922/Start-Preview.cmd` and
retains package C as rollback. Publication remains paused.

## September 22 generated-app update - owner confirmed in correct window

The owner's Calorie & caffeine chat redesign was saved as app version
`4f8d500fda5e49b29fbf9b187ee01ce5`; page HTML/CSS/JavaScript changed and
one existing entry remains. Its stored page does not use `localStorage`. A clean
browser rendered the same revision without an error, and the owner then located
the correct window and confirmed that the updated app works. The repair banner
was in a different window; its exact cause is not established. The speculative
generated-page error filter was reverted before activation. Its disposable
package/source/archive were removed, leaving compact manifest and receipts under
`artifacts/portable-tray-app-error-20260922-d`. No fix for that banner is claimed.

The current owner host remains `portable-tray-icon-20260922-b` at localhost:5279.
The guarded launcher selects the narrower tooltip-only candidate
`portable-tray-tip-20260922-c` (manifest SHA256
`c109c88c8c65d0dd28d73de54fe3cf0653c7d7fe2c6c1b7a05810189b706280e`),
ZIP SHA256 `2c03578d961ceac7f6c57677a58d0b6f3c73d84952872f09e82f5fa476d701d0`),
not yet activated or visually accepted. The launcher `-CheckOnly` verified C and
its B rollback. The C package passed the focused native reopen, one-use login,
profile isolation and tray Exit fixture at
`artifacts/desktop-reopen-check-tray-tip-20260922-d/verified.json`; its fictional
study was removed. This does not prove the tooltip or browser login is visible on
the owner's normal desktop. Publication remains paused.

## September 22 tray icon visual follow-up - owner desktop launch completed

The Codex-launched tray control package registered a Windows tray icon, but the
owner could not see it or its tooltip. This is an open visual acceptance failure.
The replacement `artifacts/portable-tray-icon-20260922-b/thaddeus-win-x64`
contains a raven icon and passed the native packaged Open/Exit and retained-study
check at `artifacts/desktop-reopen-check-tray-icon-20260922-c/verified.json`.
ZIP SHA256: `9c674e37acaeedb9b84a5d96e4a5838a1c8cc0606db43542d9c9d1065d598078`.
Manifest SHA256: `04ab7de49ad0fe8c7b618e4c6203f0e0423b53a8b6b2c1efef33091e76757be4`.
The reviewed launcher selects this package with the first tray control package
as its immediate rollback; `-CheckOnly` passed. The last known-good pre-tray
polish package is also preserved. The owner launched
`artifacts/preview-acceptance-20260922/Start-Preview.cmd` outside Codex;
`artifacts/preview-acceptance-20260922/runs/20260922-213255-745b206c/result.json`
records PID 36952, the unchanged private study, verified schema-12 backup, and
readiness at localhost:5279. The owner saw the icon and used its menu to open a
browser link. The tooltip and completed browser login from that link remain
unverified. No notification or full tray acceptance is claimed, and publication
remains paused.

## September 22 tray control - active owner preview

Candidate: `artifacts/portable-tray-20260922-a/thaddeus-win-x64`, schema 12,
unsigned Windows x64, captured dirty source on
`bf67d4e3c465c432c7264debe5f4f9a984b8a688`. ZIP SHA256:
`c17bc686669302aab2e8f0e9f0a83e72a7f012fd9a59635a19c2263dc949d113`.
Manifest SHA256:
`843ace2f0041e1fff13f008bd69701571abab807adb6fa54ba03860b395c742d`.
The owner launcher remains
`C:\Users\Ayric\Documents\ChatGPT\Thaddeus 2.0\artifacts\preview-acceptance-20260922\Start-Preview.cmd`.
This was the launcher's selection at the first tray activation; see the newer
tray icon follow-up above for the current launcher selection.
The owner host is running at `http://localhost:5279` after a verified private
backup; the exact activation receipt is the latest `result.json` under
`artifacts/preview-acceptance-20260922/runs`.

The only changed packaged sources from the polish candidate are
`WindowsTray.cs`, `Program.cs`, `DESKTOP_REOPEN.md` and `PORTABLE_PACKAGES.md`.
`WindowsTrayTests` passes; the native fixture receipt at
`artifacts/desktop-reopen-check-tray-20260922-b/verified.json` confirms exact
host shutdown and preserved study data from the tray Exit command. Human menu
visibility remains open, as do the existing Google, Chrome, fresh-user and
notification acceptance gates. The separate Luna bridge is not owned by the
host tray. Whisper STT is assessment only. Publication remains paused.


## September 22 continuity and status polish - current candidate

Candidate: `artifacts/portable-polish-20260922-r4/thaddeus-win-x64`, schema 12,
unsigned Windows x64. Captured dirty source on `bf67d4e3c465c432c7264debe5f4f9a984b8a688`.
ZIP SHA256: `b4806d3a9739ff5a38c48c0fc08aaca548e901ed6d6b2d2859772fcfd1cbcfbf`.
Manifest SHA256: `c784f56028b0492875a832703e5b820576991c6bf68b01ea72ca95dfc9f639f8`.

Launch from a normal Windows Terminal:

```powershell
& 'C:\Users\Ayric\Documents\ChatGPT\Thaddeus 2.0\artifacts\preview-acceptance-20260922\Start-Preview.cmd'
```

It is already running at `http://localhost:5279` (PID 3024 at activation), with
`gpt-5.6-luna`, high reasoning. Save open work before refreshing the browser to
load the updated client. The existing `artifacts/Start-Thaddeus.cmd` also selects
this package. Framewright keeps port 5179. No PC shutdown or publication occurred.

Verified: form/scroll continuity between app and rail views; safe replacement of
unfinished app/design input; hidden-frame write refusal; retained drafts after a
refresh error; distinct task states and clear saved-connection status. Ten
packaged browser tests plus nine artifact API checks passed. Exact receipts,
source comparison and inspected desktop/mobile screenshots are under
`artifacts/polish-20260922`; start with `candidate.json` and
`verified-candidate-r4.json`. Fixtures are separate from live Google/Chrome checks.

Activation verified backup:
`.data-backups/20260922-193415-desktop-update-f37bc39c9b8b4002820559df60ca2192`.
`owner-runtime.json` confirms the exact served build and unchanged retained counts:
70 runs, 91 chat entries, 3 apps, 5 pages. Credentials remain in the existing host
vault. Same-schema rollback package: `portable-pinned-tabs-20260922-r1`; its ZIP
and source identity remain in the historical section below. Superseded trial
packages and synthetic studies were removed; their receipts remain.

**Release acceptance is open.** Remaining owner actions: use the normal product
review to approve an exact delayed test email to a controlled recipient; check
one bounded brief and selective inbox-watch pair, then revocation; try a real
Chrome task with its existing scope/action controls; launch/configure under a
separate Windows user. Native notification visual and cold-click checks remain
for a later normal-desktop session (`ASTRA_NOTIFICATION_HANDOFF.md`). The scheduler
and Google integration were not replaced; previous implementation and evidence
remain recorded below. Saved connection metadata is not live provider proof.

**Exact next action:** follow `MVP_DELEGATION_MANUAL_QA.md` when the owner is ready.
Until then hold this candidate for acceptance and fix only demonstrated blockers.
Do not mark the full MVP accepted/frozen or publish while mandatory gates remain.


## Previous September 22 pinned-page navigation candidate

Historical checkpoint, superseded by the current candidate above.

Package at this checkpoint: `artifacts/portable-pinned-tabs-20260922-r1/thaddeus-win-x64`,
schema 12, captured dirty source on `bf67d4e3c465c432c7264debe5f4f9a984b8a688`.
ZIP SHA256: `2f7de67c90a1b348d65ed538a43c0c5f29e2f776eb70a75d848b0f5054ccc45e`.
Manifest SHA256: `2cec6bf2b80e51cabe7b7a6bac586887571f486334e1b3252949cc8e0eeaf314`.
It is running at `http://localhost:5279` with Luna High. The existing
`artifacts/preview-acceptance-20260922/Start-Preview.cmd` and
`artifacts/Start-Thaddeus.cmd` now select this package. Normal desktop launch
remains required for the deferred native notification acceptance.

The owner reproduced a navigation problem: a pin replaced the right-hand tabs,
and Return to Today removed the saved pin. The corrected UI keeps Today and the
pinned app in the same tab strip, maintains the sidebar width, and provides
Back to Today without changing the pin. Remove pin is a separate explicit action.
Six affected packaged UI cases passed, including narrow-screen navigation, live
app updates, full screen, ordinary app editing, Profile and Chrome review controls.
Evidence and inspected screenshots: `artifacts/pinned-tabs-20260922/verified.json`
and its per-case folders. Fixtures made no live model or Google call; all six
fictional studies and packaging intermediates were removed after process checks.
`provenance.json` confirms only five frontend source files differ from R3;
unchanged backend/native evidence below remains applicable, not a new full run.

Activation used normal maintenance and a verified private backup:
`.data-backups/20260922-183538-desktop-update-86ad2e62f4f34be383d7697f0250a3c0`.
`artifacts/pinned-tabs-20260922/owner-runtime.json` records the exact host,
served client and preserved history. R3 remains available as rollback; the data
schema is unchanged. Live Google, fresh Windows-user, human Chrome and notification
acceptance stay open. Publication remains paused.

## Previous September 22 owner preview candidate (R3)

Candidate: `artifacts/portable-invited-preview-20260922-r3/thaddeus-win-x64`,
captured working tree on `bf67d4e3c465c432c7264debe5f4f9a984b8a688`, schema 12.
ZIP SHA256: `d62450dd3bf2c22e9749871f5a672f254894c00d7c09c82175ee1ce4c151b091`.
Manifest SHA256: `4bac0e385e6112a801a76e9648b8089595172a7d5c49255f16d4b1653125de42`.
Its manifest lists exact source and payload hashes. This is an unsigned local
preview, not a frozen release, public upload, or acceptance of outstanding gates.

From a normal Windows Terminal (outside Codex/ChatGPT), run:

```powershell
& 'C:\Users\Ayric\Documents\ChatGPT\Thaddeus 2.0\artifacts\preview-acceptance-20260922\Start-Preview.cmd'
```

This starts the Luna bridge, verifies the candidate and baseline packages, makes
and verifies a private backup before upgrading, then opens the existing study at
`http://localhost:5279`. Saved model: `gpt-5.6-luna`, high reasoning. It does not
load LM Studio or stop Framewright. At the owner's explicit request, the preview
was launched from Codex with the helper's opt-in `-AllowAgentLaunch` switch.
Native notification acceptance remains open; ordinary launches retain the
normal-desktop guard. `artifacts/Start-Thaddeus.cmd` now selects this candidate.

The live host is PID 36372 on 5279/5283, serving the exact candidate client with
schema 12 and Luna High. Read-back retained 70 runs, 91 chat entries, 3 apps and
5 pages; no inference was performed. Evidence:
`artifacts/preview-acceptance-20260922/owner-runtime.json` and
`runs/20260922-174002-d2427fbe/result.json` under that same directory. The verified
schema-11 backup is `.data-backups/20260922-174003-desktop-update-e861f730e9bc45af85773c786bf74f6f`,
manifest SHA256 `4b20aa314e04d18f237214ab3e1ba10e8e3f14a65cc1775628194d918795ecec`.
The preserved baseline launcher below is historical; do not point a schema-11
app at the upgraded original study.

Local evidence: 1,153 ordinary backend passes plus 60 focused restore checks;
17 native package checks on this candidate; real baseline-to-candidate migration
and separate rollback in `artifacts/preview-upgrade-20260922-r4/verified.json`.
All 54 ordinary packaged UI cases pass in
`artifacts/preview-browser-suite-20260922-r9/suite.json`, reusing valid cases on
this same immutable R3 package. These used synthetic providers and isolated data. Prior My page, real MCP/Chrome
fictional-page checks and their limits are in `MVP_DELEGATION_ACCEPTANCE.md`.

A final read-only audit (`artifacts/preview-acceptance-20260922/acceptance-audit.json`)
confirms all 230 runtime source files match the captured candidate, the two
post-core restore files have focused/native coverage, and the selected Google,
scheduler and notification source matches the earlier live-Gmail package.
The saved Google catalog still reports read/send/calendar ready. Refresh and live
dispatch were not exercised today, and no delayed outbound test has been queued.
The concise remaining owner session is at the top of `MVP_DELEGATION_MANUAL_QA.md`.

The Google audience remains the owner's existing account only. Saved app setup
is reused through the Windows credential vault; no client secrets or account
tokens are bundled. Google's actual Desktop endpoint rejected a client-ID-only
negative control, so onboarding additional testers is still open.

Remaining owner gates: live Google workflows on the candidate, human Chrome
takeover/review, native notification visibility/cold click, and a fresh Windows
user setup. Fresh application folders on the developer account do not pass the
last gate. Follow `MVP_DELEGATION_MANUAL_QA.md`; publication remains paused.

For schema rollback, use the preserved baseline executable's `--study-restore`
with the pre-upgrade backup recorded by the preview launcher, then launch that
separate copy. The original newer study stays intact. A restore performed by the
newer app upgrades the copy and cannot then be opened by the schema-11 baseline.

## September 22 baseline and preview work

The preserved owner package is now `bf67d4e3c465c432c7264debe5f4f9a984b8a688`,
`artifacts/portable-local-daily-token-usage-package-r1/thaddeus-win-x64`.
Archive SHA256: `ff7f458b5f93bb8d6c32f1facd6c51568f9191c0ebe619111545dec9e5355c78`.
Rollback: the September 18 Qwen package below. The guarded launcher verifies both
package manifests and keeps the original `.data` study and credential boundary.

From a normal Windows Terminal in this checkout:

```powershell
powershell.exe -NoProfile -File artifacts/profile-rail-20260917-owner/Start-Thaddeus-Test.ps1
```

Address: `http://localhost:5279`; worker port 5283. The old 5179/5183 ports belong
to Framewright. `-CheckOnly` passed without launching, logging in, or touching
the study. The old launch profile is backed up at
`artifacts/tester-preview-20260922-baseline/launch-profile-before.json`.

Historical My page-only fixture package (superseded binaries pruned):
`artifacts/portable-my-page-20260922-r1/thaddeus-win-x64`, archive SHA256
`9ba1e908bf8f5f5189f3d3e83d2b9da67edc1aca3da7dd4065176c01f48237eb`.
Its retained manifest/source describe a captured working-tree build. The combined
candidate above now includes Chrome; existing real-device/Google/setup gates remain open.
The current [acceptance ledger](MVP_DELEGATION_ACCEPTANCE.md) records their exact
status and receipts. Publication remains paused.

## Historical candidate - September 18 Qwen owner review

- Source: `8fa2a51fb8f70982df7edb5eee3283998233bd83`, schema 11, unsigned Windows x64.
- ZIP: `artifacts/portable-local-qwen-staged-package-r1/thaddeus-win-x64.zip`
  (102,400,531 bytes).
- SHA256: `7ae9a7937e46b48bacb005f3de733af5c84fba37792817b2f5a1c78512d3c7df`.
- Manifest SHA256: `1aca39cf5230692438d5e08c9aeb266e878357ad1dadf8689ecfbf64bcfa1b1f`.
- Package evidence: `artifacts/local-check-qwen-staged-package-r1/verified.json`.
- Core evidence: `artifacts/local-check-qwen-staged-release-r1/verified.json`.
- Live LM Studio evidence: `artifacts/live-space-invaders-qwen-staged-r2/verified.json`.
- Private GitHub prerelease: [`v0.1.0-preview`](https://github.com/raydeStar/sir-thaddeus-2/releases/tag/v0.1.0-preview).

The owner study still runs the preceding Gmail-empty-query package. Its saved
provider now targets LM Studio `qwen3.8-27b` with `low` reasoning. The guarded
desktop launcher verifies this candidate, backs up the study, replaces the host,
and retains that running package as rollback when the owner next launches it from
a normal desktop terminal. Public publication and Product Hunt submission remain
paused. The authoritative acceptance state is in `MVP_DELEGATION_ACCEPTANCE.md`.

## Superseded September 17 package record

**HISTORICAL RECORD; not the current package.** Astra's completed
ChatGPT conversation, **Define Thaddeus magic**, was the coordination handoff;
the owner confirmed it was completed work. No competing app update was started.

- Source: `ea911352eaac74277caf9ad8c98350bebc574630` (clean at packaging); schema 11, unsigned Windows x64.
- ZIP: `artifacts/portable-local-stable-google-package-r1/thaddeus-win-x64.zip` (102,389,425 bytes).
- SHA256: `83e25a8f6fee8981bce488bd42cfda75e6d1201a8ee9f675bb35a68f49a3b235`.
- Manifest SHA256: `2c9e28b91d54d2e8fa8295b23343fe144f51d7541d8805d80181d1ea85988818`.
- Package evidence: `artifacts/local-check-stable-google-package-r1/native/verified.json`
  (17 native checks), `artifacts/local-check-stable-google-package-r1/credentials/verified.json`
  (5 credential checks), and `artifacts/stable-google-package-browser-r2/suite.json`
  (52/52 isolated browser workflows). Live model calls and GPU inference remained zero.
- Focused Google/connection regression: 40 passed, none failed/skipped. It covers
  omitted token scopes, multi-permission consent, partial consent, stable Gmail and
  Calendar API reads, old-catalog compatibility, revocation and approved sending.
- Current core evidence: `artifacts/local-check-stable-google-read-r2`, with
  1,103/1,103 backend tests plus protocols, frontend production build,
  notification build and tracked-file secret scan.
- The guarded owner launcher is pinned to this manifest and accepts the currently
  running `portable-local-final-google-r5` manifest as its rollback source. Its
  read-only `-CheckOnly` preflight passed. Earlier handoff and archive receipts
  retain provenance for their original candidates. Tested environment: Windows 11
  Pro 10.0.26200, current Windows user with fresh fictional app data.

At the time of this record, the app observed at `localhost:5179` still ran
`portable-local-final-google-r5`, not this candidate. The owner study/vault were
not modified during packaging. That host has the saved Google app setup and
connected Gmail read, Gmail send and Calendar grants. The preceding package,
prior tested candidates and owner backups remain.
Do not downgrade the database in place. The owner update helper makes and verifies
a new private backup before replacement and refuses active work/unrelated hosts.

Run from **normal Windows Terminal or File Explorer**, outside Codex:

```powershell
& "C:\Users\Ayric\Documents\ChatGPT\Thaddeus 2.0\artifacts\Start-Thaddeus.cmd"
```

Refresh existing browser tabs afterward. This temporary owner-study wrapper also
reuses the existing Luna development bridge; it is not part of the public ZIP.
Fresh-user testing uses the extracted ZIP's `Start Thaddeus.cmd` and a separately
configured compatible model, following `MODEL_CONNECTIONS.md`. No SDK, Node or
GPU is needed to open the host. Model/network access and an isolated research
worker are separate prerequisites, not silently supplied by the archive.

## Fixed blockers and disclosed limits

- Exact action reviews now render directly in Chat. Owner controls are **Deny
  once**, **Always deny this type**, **Allow once**, and **Always allow this
  type**. Remembered choices are bound to the reviewed action category and, for
  connectors, the connector/tool version. A changed scope asks again.
- Settings -> Permissions & devices contains one plain removable list. Removing
  a choice restores ask-each-time. “Show my approval settings” opens that list
  locally without a model call.
- Focused packaged evidence is
  `artifacts/browser-inline-approval-focused-r2/verified.json` and
  `screenshots/approval-rules-check.json`: inline exact review, 390-pixel layout,
  remembered allow/deny, automatic matching reuse, chat-opened settings, and
  removal restoring review all passed with five synthetic and zero live model
  calls.

The owner-observed Google callback failure was reproduced. OAuth permits a token
response to omit `scope` when the granted scope is identical to the request;
Thaddeus previously treated that omission as zero grants and discarded the
connection. The candidate now retains the exact requested grant in that case,
keeps an explicit partial grant authoritative, and reports the signed-in account.
Chat answers missing Gmail/Calendar capability requests locally, says the
capability is not connected, and offers a dismissible Connect card without using
the model. One consent card can select Gmail read, Gmail send and Calendar
together; successful child capabilities remain independently visible and
disconnectable. The callback page now says verification is still in progress
instead of claiming connection success prematurely. These behaviors passed
focused and packaged fixture tests. A real Google account is still required to
close live consent/read/send acceptance.

Reproduced unreadable/partial inbox data becoming quiet success, loss of earlier
new thread replies, full-batch progress loss, `sender` excluding valid read tools,
and missing assessment usage receipts. Before/after TRX files are retained. The
first repro run had ten failures; one proposed unknown-quote rejection was
withdrawn because it changed the existing provider contract, rather than fixing
a regression. Unknown usage is now explicit, never claimed as zero cost.

Supported empty collections stay quiet without inference. Malformed results,
pagination and full batches pause without advancing progress. The stable Gmail
message adapter supplies precise provider timestamps and message IDs; correct-account
links remain a live acceptance check. No pagination engine, scheduler replacement,
new connector framework or notification transport changes were added. A reproduced
release blocker in the preceding candidate rejected an
explicit `right now` reminder because its frozen request timestamp was no longer
in the future when approved. The corrected review says `Immediately after
approval`; the scheduler binds the actual due time only when that exact review is
approved. Ordinary past reminders remain rejected. Focused reminder, scheduler
and notification tests pass 24/24, including one dispatch exactly once after a
deliberately delayed approval. Evidence is retained in
`artifacts/immediate-reminder-20260917/immediate-reminder.trx`.

Fixtures additionally prove profile persistence/correction/forgetting and that
Identity/Soul/User text cannot bypass connected-tool review. They do not prove
subjective live-model personality behavior. No live Google/model/GPU call was made.

## Consolidated remaining owner acceptance

1. Launch the command above, refresh, and confirm the candidate opens the existing
   study. Allow the safe backup/upgrade to finish; retain its receipt.
2. Use the already-consented Google account with the stable Gmail and Calendar API
   adapters in the next candidate. The Desktop client, test user, Gmail API,
   Calendar API and all three bounded product grants are configured. No Developer
   Preview enrollment or reconnection is required. Use only the owner's approved
   account, controlled recipient and agreed model allowance. Account consent is
   distinct from exact task approval.
3. Through the product, observe one exact delayed send with the browser closed,
   bounded email/calendar brief, quiet inbox check, important-message result and
   correct original-email link. Record provider acceptance separately from
   recipient delivery. Revoke access last and confirm queued work cannot dispatch.
4. Click a scheduled notification from this package only after its sending helper
   has exited, keeping the host running. Display is already owner-confirmed for
   the matching helper implementation; Q's E2 was a warm click, not a cold pass.
   Notification sources/build inputs match Q; newly compiled executable/DLL
   hashes differ. Prior display is implementation evidence, not a claim that
   the owner has seen this exact new binary.
   No standalone notification probe, registration or Windows setting was changed.
5. Test actual new Windows-user setup with the exact ZIP: configure model, get a
   real reply/useful task, close/reopen and safely restart the host with a pending
   reminder. Fresh app data under the current account does not satisfy this gate.

Use `MVP_DELEGATION_MANUAL_QA.md` for exact steps, including cancellation, denied
edits, source-linked To-dos and real host restart. Once these agreed checks pass,
record ACCEPTED FOR WINDOWS PREVIEW, freeze this candidate and stop development.
Until then the mandatory gates stay open; another synthetic suite cannot close them.

## Submission state

`artifacts/publication-final-acceptance-20260917` is now a historical local-only
payload for the preceding candidate. Do not publish it or its checksum. Refresh
the publication payload only after owner acceptance of this candidate. Existing
reviewed fictional gallery images remain usable with their original provenance.
See `PUBLICATION_HANDOFF.md` and `PRODUCT_HUNT_SUBMISSION_DRAFT.md`.
No public repository, release, listing, scheduling or visibility change was made.

## Check commands, retained failures and cleanup

The focused command was `dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj
--no-restore --configuration Release --filter
'FullyQualifiedName~InboxWatchTests|FullyQualifiedName~Delegation|FullyQualifiedName~UserTests|FullyQualifiedName~SoulTests|FullyQualifiedName~IdentityTests|FullyQualifiedName~ContextTests|FullyQualifiedName~ConnectedToolConversationTests|FullyQualifiedName~Google'
--logger 'trx;LogFileName=focused-final.trx' --results-directory
artifacts/final-acceptance-20260917`. It passed 116/116.

`node scripts/check-local.mjs package final-acceptance-20260917` passed publication,
17 extracted native checks, native credential-store cleanup, MCP fixture build
and 51/51 isolated browser cases. Every step exit code is zero. The earlier
`inbox-before.trx` (10 failed/12 passed), `inbox-after.trx` (22/22) and
`focused.trx` (115/115) are retained; the last additional case covers nested
thread pagination. `node scripts/scan-secrets.mjs` and `git diff --check` passed.

No live model/search/Google, GPU, worker, hosted CI or native notification probe
ran. The browser suite deliberately excludes its opt-in live research, native
picker, notification and study-handoff tests; prior evidence remains scoped to
its original inputs. A non-fatal Vite chunk-size warning remains; this is not an
invitation for an optimization cycle.

The archive's initial generic secret-pattern scan matched a marker inside
`System.Private.CoreLib.dll`. The file is byte-identical to installed .NET 10.0.7
and carries a valid Microsoft signature; no owner credential was found. A local
handoff script initially used Windows' default text decoding; the check caught
it and the draft was regenerated as UTF-8 before verification. Neither issue
changed product code or the accepted test inputs.

Free space was 96.60 GiB before the 2 GiB package budget plus 10 GiB reserve,
96.24 GiB after checks. Publisher `scratch-cleanup.json` records removal of staging
bin/obj/node_modules. Native `scratch-cleanup.json` confirms extracted scratch
removed and zero owned processes; browser cases removed their disposable studies.
Compact receipts, captured source, package, rollback, owner data and backups are
retained. Earlier policy-rejected legacy cleanup was not retried or bypassed.

## Historical review fixes and evidence (identities below are not current)


- **September 17 ordinary chat outage recovered:** the owner study still ran U,
  with model endpoint `http://127.0.0.1:5184/v1` saved correctly, but no process
  listening on that port. The host-only temporary launcher omitted the separate
  Luna development bridge. Restored the existing bridge at the unchanged endpoint
  using the already signed-in Codex CLI; no provider setting or credential was
  changed, and the host was not restarted. Nine synthetic bridge/protocol tests
  passed. One targeted live retry through the product UI completed successfully:
  `3180918c5a8245fd985d907f5be15249` replied to the owner's swallow question.
  Exact receipt: `artifacts/chat-recovery-20260917-z/verified.json`.
  The temporary `artifacts/Start-Thaddeus.cmd` now runs its existing safe app
  launcher followed by `chat-recovery-20260917-z/Ensure-Luna.ps1`. The latter only
  starts the recorded Luna configuration, checks captured script hashes and CLI
  sign-in, uses a hidden process, refuses other port owners, and reuses its own
  running bridge. Re-entry preserved the same PID/start time; see
  `reopen-check.json`. Current bridge PID is 38456; retain that folder's `bridge`
  scripts and manifest as active runtime inputs. This is an owner development
  launcher repair, not bundled model access or a new production startup service.
  Ordinary chat works on U without rebuilding; the pending Y2 desktop upgrade
  remains necessary for the inline-card UI and Google consent-start repair.

- **September 17 inline connection card:** Google/service setup now belongs to
  the relevant assistant reply and scrolls with chat instead of occupying the
  composer. The compact rounded card keeps permission review, secure import,
  consent status/errors and one-time setup. New cards leave typing focus in the
  composer; closing and reopening a card preserves its message association and
  provides keyboard focus on explicit reopen. Credentials remain host-only.
  Candidate Y2 source `5b45d934f915e128b254dccb55e217ae78815ee4`, package
  `artifacts/portable-mvp-inline-connection-20260917-y2`, ZIP SHA-256
  `f4e40235cf8285bb2275264a6f9b1851b8ceab28331a49bf8a45fb5dbe26c042`.
  Both packaged connection browser tests pass in
  `inline-connection-20260917-y2-browser/verified.json`, with desktop/mobile
  screenshots. Checks cover message association while chat continues, close/
  reopen, focus, secure import/reload, a simulated consent denial and secret-free
  exports. No live Google, external model, GPU or notification call was made.
  Earlier Y passed these checks, then screenshot review prompted focus polish.
  Build intermediates and owned browser/host processes were cleaned. Automatic
  approval review rejected the subsequent disposable-file removal command as
  "blocked by policy"; no alternate deletion was attempted. Exact retained paths
  are in `artifacts/inline-connection-20260917-y2-owner/CLEANUP.md`.
  `artifacts/Start-Thaddeus.cmd` now targets Y2; read-only preflight against U
  passed in `inline-connection-20260917-y2-owner/preflight.json`. It did not restart
  or authenticate to the owner host. Run that command in normal Windows Terminal,
  refresh the app, and reopen Connect Google from its chat reply. Saved study and
  Google app registration are reused. Live Google acceptance remains open.

- **September 17 Google consent startup:** the owner's downloaded Desktop app
  setup was imported successfully into the existing host vault. A live connection
  attempt exposed anonymous MCP discovery skipping OAuth entirely. Candidate X
  starts Google's maintained PKCE flow explicitly before catalogue discovery;
  it preserves scope review, verified account identity and host credential custody.
  Source `aff90ed`, package `artifacts/portable-mvp-google-signin-20260917-x`, ZIP
  SHA-256 `ab045359a76666edeac8d87503c57076e0c0039721c45596948746d2abd2e60b`.
  Forty-one focused Google/connection checks pass in
  `google-explicit-signin-20260917-x/google-verified.trx`; seventeen extracted
  Windows package checks pass in `google-explicit-signin-20260917-x-native/verified.json`.
  Publisher intermediates and extracted scratch were removed with cleanup receipts.
  These use the current Windows user with fresh fictional app data, not a fresh
  Windows user or a clean machine. No native notification probe or live mail call
  was made. At this stage, `artifacts/Start-Thaddeus.cmd` targeted X and passed read-only
  preflight against the running U owner study; the prior verified update logic is
  unchanged. Run it from normal Windows Terminal, refresh the app, then choose
  Continue with Google. The one-time app setup is already saved. Live consent and
  service enablement/terms approval remain open; this is not a connected-account pass.

- **September 17 reminder presentation:** the owner's clipped screenshot was a
  chat confirmation beneath a fixed heading. The heading now scrolls with the
  transcript; Latest messages stays anchored in the visible history area. The
  saved reminder dialog leads with its title, exact reminder message and time,
  keeps delivery errors visible, and collapses technical receipts. Mark result
  read updates the open dialog without dispatching again. Completed replaces
  the misleading generic Delivered label; provider acceptance still does not
  establish visual Windows delivery or recipient delivery.
  Candidate W is `artifacts/portable-mvp-reminder-presentation-20260917-w`, source
  `cb10ef28cf92f5f35bbfce8f49f8eb698aa36ed4`, ZIP SHA-256
  `0922d38cf0b84c2bf3a5b55c887b701cb0f4187b8161ce8829ce2309316fbb6c`.
  Existing delegated-work and notification-link checks passed in
  `reminder-presentation-20260917-w-browser-r3/browser-results.json`; both new
  presentation checks passed in `reminder-presentation-20260917-w-browser-r4/verified.json`.
  Desktop/mobile screenshots are beside that receipt. Earlier failed test
  attempts are retained: their selectors needed the new exact heading and the
  existing mobile log navigation. Runtime package W was unchanged between runs.
  All four browser fixture studies were cleaned after owned processes exited.
  These are synthetic UI checks, with no native toast, external model call or
  owner data mutation.
  The temporary `artifacts/Start-Thaddeus.cmd` then targeted W; the unchanged safe
  upgrade flow passed against U in `desktop-test-launcher-20260917-w/verified.json`.
  It preserves the owner study and makes a verified backup. Refresh existing
  tabs after running it: changing the host does not replace already-loaded JS.
  Latest action: run that command from the normal desktop, then inspect the
  reminder under Activity log > Upcoming. Native acceptance gates below remain
  separate; no registration or notification helper changes were made here.

- **Notification cause confirmed:** agent-launched D1/D2 wrote registration inside
  Codex's private Windows environment and were invisible. The owner launched the
  unchanged Candidate O helper from File Explorer and confirmed **D3 appeared**.
  Its receipt shows no desktop registration existed beforehand. Evidence:
  `artifacts/notification-review-20260917-e/owner-observed.json`, Windows ID 37565.
  No Windows notification preference was changed.
- **Click handling fixed:** the helper registers its event before COM registration,
  waits for the actual invocation, and opens the sending study's Upcoming results.
  It no longer launches a host against a guessed default data directory, and has
  no console window. Only local origins are allowed; normal browser authentication
  remains required and the host must still be running.
- **Desktop follow-up:** Q's real scheduled test passed with the browser closed
  (`notification-review-20260917-q-desktop/verified.json`, notification 37570).
  The owner reported visible delivery and a redirect to the sign-in page. E1
  received no callback; its failed receipt is preserved. E2 reached the exact
  confirmation URL and the owner confirmed it, but arrived 328 ms before the
  sender exited. `notification-review-20260917-q-click-e2/receipt.json` correctly
  leaves cold activation failed/unverified. Do not relabel it as a cold-click pass.
  The owner then launched Q against the existing study. The in-app browser was
  also unlocked through a one-use launch ticket; reload retained sign-in.
  Windows opens links in the default browser, whose cookies are separate from
  the Codex in-app browser. This is host/browser authentication, not OpenClaw.
- **Google permission display fixed:** receipts retain all scopes Google returned,
  rather than hiding an additional grant. Requested scopes and permitted tools
  remain restricted. The failing reproducer is `scope-before.trx`; seven Google
  safety fixtures pass in `google-review.trx` in the same evidence directory.
  These are synthetic tests, not live email evidence.
- **Focused regression checks:** `notification-final.trx` records 21 passing
  notification/scheduler tests. Three packaged browser workflows passed for
  notification links, delegation controls/results and navigation. Their receipt
  is `artifacts/notification-review-20260917-p-browser/verified.json`.
  `notification-review-20260917-e/reused-browser-evidence.json` binds unchanged
  notification source and byte-identical served client assets to Q. The temporary
  P binaries and fictional browser study were then removed.
- **Final package:** `artifacts/notification-review-20260917-q-native/verified.json`
  records 17 passing extracted-package checks on Windows 11 Pro 10.0.26200, current
  user with fresh isolated app data. Owned processes/extracted scratch were removed.
- Candidate O's previous 1,017-test backend and 46-workflow browser evidence remains
  preserved for unchanged behavior; it is not relabeled as a new full Q run.
- The unpublished download page had a stale checksum and incorrect bare-executable
  launch instructions. Its README, archive checksum and manifest now agree.

## Existing scheduler

`src/Thaddeus.Infrastructure/StoreDelegations.cs` persists jobs, exact grants,
versions, occurrences, cancellation/replacement and inbox progress/alert IDs.
`DelegationScheduler.cs` resolves and claims due work. The existing host
`src/Thaddeus.Host/DelegationPump.cs` checks independently of browser tabs, and
`HostDelegationDispatcher.cs` routes approved actions.

Existing fixtures and real-clock receipts cover one-shot execution, recurring
triggers, explicit UTC/timezone, restart recovery, persisted cancellation and
browser-independent dispatch. Late time-sensitive one-shots become missed;
unknown external outcomes are retained without automatic retries. No replacement
scheduler was introduced. Native visibility remains a separate check.

## Google and inbox watch

Desktop OAuth, loopback callback, PKCE/state validation, credential custody,
refresh/reconnect/disconnect, stable read-only Gmail/Calendar adapters and a narrow
Gmail send adapter are implemented. Briefs and connector-neutral inbox watches
reuse bounded grants and the scheduler. Fixtures do not establish real sends,
recipient delivery, or controlled live use of the new package.

The earlier owner-study snapshot had schema 11, 36 runs, 38 chats and
**zero MCP connectors**. A subsequent normal chat request opened the read-only
Google connection card without a model call; it did not connect an account.
The owner authorized a dedicated Google test project and signed in on September
17. `Thaddeus MVP Test` (`thaddeus-mvp-test-20260917`) now exists. The Google Auth
Platform app is named Thaddeus, External/Testing, with the owner as its sole test
user. The owner explicitly approved Google's API Services User Data Policy;
Google confirmed OAuth configuration creation. The Gmail API is enabled, and
the `Thaddeus Windows test` Desktop OAuth client was created. Scope declarations
match the implemented sign-in identity, Gmail read/send and Calendar read/freebusy
products; Google confirmed the save. Declaring scopes is not an account grant,
and read/send remain separate product authorizations.

The owner downloaded the Desktop setup file, and the secure import succeeded in
the running candidate: the UI confirmed saved setup and enabled Continue with
Google. No file content was copied into chat, source control or worker inputs by
the import. The first real Connect attempt failed before opening a browser with
"Google authorization completed without a reusable token."
Google's public MCP catalogue had returned successfully without an OAuth challenge.
The fix starts Google's maintained PKCE flow explicitly, validates single-use
state/issuer, and requires a refresh credential and the selected scopes before
committing tools. Canonical Google identity scope URLs count as the matching OIDC
scope without hiding the actual grant in the receipt.

Evidence: `artifacts/google-explicit-signin-20260917-x/anonymous-discovery-before.trx`
reproduces the exact failure against the old implementation. Later controlled
consent connected Gmail send and granted Gmail read and Calendar scopes, but actual
reads failed because Google's remote MCP servers require Workspace Developer Preview
enrollment. The stable REST adapters remove that release dependency without changing
the provider-neutral broker or stored grants. Focused Google/connection tests cover
PKCE, callback replay, denial, partial grants, stable read schemas, revocation and
approved sending. The new package still needs controlled live Gmail/Calendar reads.
Keep the downloaded setup file out of chat, source control and worker inputs. Approve
one exact delayed send to an owner-controlled recipient, one bounded brief/watch,
and a revocation check. Record Gmail acceptance separately from recipient-observed
delivery. Developer success is not public verification or unrestricted availability.

## Package and preservation

- Source: `e36a2c58add6e1f0d33544d36f17a68acf2b4475`, clean at capture.
- Package: `artifacts/portable-mvp-reviewed-20260917-q/thaddeus-win-x64`.
- ZIP: 101,627,076 bytes; SHA256
  `804a518920dacaafa36b74ecd5a3a564093061eeea0673b179ac9acd8a24e0a3`.
- Prepared publication materials: `artifacts/publication-handoff-20260917-f`.
  Nothing was published. Archive, checksum, site README and manifest agree.
- Owner data/credentials, Candidate O, Release H and its rollback backup remain.
  Publisher staging and native extractions were cleaned. This review removed its
  unsuccessful private shortcut, intermediate P binaries/ZIP and fictional browser
  study, retaining manifests and receipts. See `notification-review-20260917-e/cleanup.json`.
  Earlier policy-rejected cleanup targets were not retried.

## September 17 raven polish

The owner requested more expressive, child-friendly behavior while keeping major
features and architecture frozen. Candidate R changes the existing SVG/CSS bird:
a clearer eye, curious tilt while composing, a brief greeting wave, thinking dots,
and a one-time completion hop with two small sparks. Resting/error states stay
calm. Reduced motion and the existing log/detail click remain intact. No model,
GPU, sound, dependency, integration or task behavior was added.

- R source: `f63703ad237fdbd5d7d7a25a475247d14a80501b`, clean at package capture.
- Package: `artifacts/portable-mvp-raven-20260917-r/thaddeus-win-x64`.
- ZIP: 101,629,025 bytes; SHA256
  `2b21178b089cef6332baab0b1cf928026761a5b10580024129aedde9cc005469`.
- Frontend typecheck/build passed during publication. The packaged raven workflow
  passed at `artifacts/raven-polish-20260917-r/browser-recheck/verified.json`:
  composing state, work poses, keyboard task opening, reduced motion, task-state
  precedence, 390/1440 layouts, offline state and no unexpected writes/history
  changes. It used a fictional study and synthetic provider. The first run's
  failed locator targeted the intentionally hidden header; it was corrected to
  the visible activity companion. Its failed receipt is retained.
- `artifacts/raven-polish-20260917-r/index.html` is an interactive appearance
  preview rendered from the real component and stylesheet. The preview's
  expression selection and pause/resume controls were checked in the in-app
  browser. These controls are preview-only, not new product features.
- R has not replaced the running owner Q process. Q's native/Google acceptance
  boundaries still apply; this focused browser check is not fresh-user acceptance
  or a new full native package run. Keep Q and the existing rollback available.
- Publisher staging was removed (`scratch-cleanup.json`). Automatic approval
  review rejected deletion of the two new fictional browser studies with
  "blocked by policy". They remain at the exact paths in
  `artifacts/raven-polish-20260917-r/cleanup-blocked.json`; no alternate deletion
  was attempted. The previously blocked Q desktop fixture also remains.

## Exact next actions

### September 17 Google setup simplification (candidate U)

- Asking to connect Gmail or Calendar opens a focused card with the requested
  permission and **Continue with Google**. The activity drawer closes so it does
  not dim setup. Asking for a different service refreshes the card's permission.
- One-time developer setup imports Google's Desktop-app credentials JSON directly
  into the host credential vault. Normal sign-in sends only the selected product
  and settings version; the host supplies the saved client details. Setup survives
  restart and is reusable across Google services. Imported URLs cannot redirect
  credentials, and web/service-account files are refused. System-browser launch
  is restricted to Google's HTTPS account host; failure offers an explicit link.
- Evidence: 34 focused backend checks in
  `artifacts/google-setup-20260917-t-tests/google-setup.trx`; the changed chat copy
  was rechecked in `setup-copy.trx`. Two packaged browser tests passed against U:
  `artifacts/google-connect-20260917-u-browser/verified.json`. They cover missing
  setup, invalid file rejection, real fixture-vault import/removal, reload/reopen,
  prompted permission changes, denied-consent recovery, no credentials in exports,
  and a narrow viewport. Google consent/status are simulated; no live account,
  Google API, model, worker or GPU was used. This is fresh app data under the
  existing Windows user, not fresh-user or clean-machine acceptance.
- Source: `b40526da8df7d05dc1b687ad639aede66f611a25`, clean at capture.
  Package: `artifacts/portable-mvp-google-connect-20260917-u/thaddeus-win-x64`.
  ZIP SHA-256: `5256ad6156e71400e255dbf2e1c66333d264e4fcf4985b3657ed244d1d83143b`.
  U includes the prior retry correction and supersedes S/T as the activation
  candidate. The owner host remains Q (PID 17820, localhost 5179 when checked).
- Both browser fixtures exited and removed their vault credentials. Package
  staging intermediates were cleaned, and `dotnet clean` removed root test-build
  outputs. Automatic approval review blocked deletion of the T and U disposable
  browser studies with **blocked by policy**; each has `cleanup-blocked.json`.
  No alternate deletion was attempted. Owner-removable paths, after any fixture
  review: `artifacts/google-setup-20260917-t-browser/study` and
  `artifacts/google-connect-20260917-u-browser/study`. The intermediate T package
  is retained pending owner cleanup; it is not the active host or rollback.
- Activation now has an owner-requested temporary helper:
  `artifacts/Start-Thaddeus.cmd`, run from a normal Windows terminal after saving
  unsaved note edits. It verifies the pinned packages and running host/profile,
  closes Q through the product's maintenance API, makes an offline verified
  backup with the product CLI, then starts U against the same `.data` profile and
  opens the signed-in browser. It refuses busy work and unrelated port owners,
  and reuses U if already running. No maintenance-menu steps are required.
  It has been prepared, not executed against the owner study.
- Temporary helper verification:
  `artifacts/desktop-test-launcher-20260917-v5/verified.json` binds script SHA-256
  `aa5a66c0ebb86a454bca2e0ad381f2cb72f43ad62136026df709d9d14869440a`.
  The actual Q-to-U fixture transition preserved chats, runs and pages; backup,
  repeat-launch reuse, stopped-host startup, unrelated-port refusal and output
  credential checks passed. All owned fixture hosts exited and disposable V
  studies/backups were removed with cleanup receipts. Browser opening and visible
  notification delivery were not exercised by this `-NoBrowser` fixture.
  The owner host remained Q, PID 17820 when checked. No package rebuild, GPU,
  model call or notification probe was required.
- This helper is explicitly disposable after notification acceptance and the
  normal release launch path are settled. It is not a permanent updater, new
  development workstream or exception to the feature freeze. Preserve backups
  and compact receipts when retiring the helper.
- Superseded Google status: the dedicated test project, Desktop app registration,
  test user, Gmail API, Calendar API and product consents are complete. Live use
  proved Google's remote Gmail/Calendar MCP servers additionally require Workspace
  Developer Preview enrollment, so the release candidate replaces only those
  built-in reads with narrow stable REST adapters. Generic MCP support remains.
  Controlled live read/send/watch acceptance, native cold-click and actual
  fresh-Windows-user gates remain open. No production client is bundled;
  publication and shutdown stay paused.
- Exact next action: activate the newer stable-Google candidate recorded below,
  then run the controlled live Gmail read and Calendar read before the remaining
  delayed-send, watch, fresh-user and notification acceptance cases.

### September 17 connection-retry correction

Candidate S fixes an owner-observed dead end in a saved Google connection request.
The deterministic connection reply had already opened the host-owned secure form,
but ordinary chat controls still offered model Retry. Two such legacy attempts
were recorded and the last stopped at its model-call limit. The UI now offers
**Continue connection setup** for the whole reply family, including those existing
failed attempts, and explains that no model retry is needed. The server also
rejects direct retry requests for connection-setup families, so a stale page or
API client cannot spend more model calls on them.

- Source: `96da8e7b38accb30c640fee930797015ae987975`, clean at package capture.
- Package: `artifacts/portable-mvp-connection-retry-20260917-s/thaddeus-win-x64`.
- ZIP SHA256: `80bab740d23b13ac5715524ea591e2083264267910653c3d219857c13a78a501`.
- Twelve focused retry/API tests, frontend typecheck/build, tracked-file secret
  scan, and packaged `connection-chat.spec.ts` passed. The browser receipt is
  `artifacts/connection-retry-20260917-s-browser/verified.json`; its fictional
  study did not touch owner data and its owned processes exited.
- Automatic approval review rejected deletion of that disposable browser study
  with "blocked by policy" after its path and process state were checked. It is
  retained and recorded in the adjacent `cleanup-blocked.json`; no workaround
  was attempted.
- Candidate Q remains the running known-good host. To activate S without repeating
  the Codex-launched Windows notification identity problem, close Q through
  Settings > Storage & backups > Review maintenance, then run
  `artifacts/connection-retry-20260917-s-owner/Open-fixed-study.cmd` from File
  Explorer. The wrapper verifies the package and existing launch profile and
  refuses while Q still owns its ports. It preserves Q as rollback.

1. Retain the passing Q scheduled test and E2 warm-click observation. For the
   remaining cold-click gate, use a fresh label/evidence directory with the
   existing click runner from a normal desktop terminal and click only after
   **Click now**. Do not rerun the already-passing scheduled test.
2. The existing owner study is running from Q. To reopen it, use
   `artifacts/notification-review-20260917-e/Open-existing-study.cmd` from File
   Explorer. It validates Q and the existing `.data` launch profile, then opens
   localhost 5179 without creating a replacement study. The owner ran it
   successfully. Earlier automatic review rejected an agent owner-host launch;
   the rejection remains recorded in the Astra handoff.
3. Create the owner-authorized Google test project/client, then complete the
   controlled-recipient pass above. Microsoft/GitHub browser-consent adapters
   are not implemented by the generic bearer-token MCP connection form.
4. Use an actual fresh Windows account for setup, useful work, history,
   close/reopen and scheduled dispatch. Fresh app-data tests under the existing
   user do not pass that gate. No account, password, OS policy or VM was changed.

See `ASTRA_NOTIFICATION_HANDOFF.md` for the precise failures and confirmed desktop
environment diagnosis. The computer stays on; publication stays paused.

## September 17 stable Google read correction and frozen candidate

The owner completed Gmail read, Gmail send and Calendar consent. The preceding
host recorded the correct account and scopes, but its first real Gmail read proved
that Google's remote Workspace MCP endpoint requires separate Developer Preview
enrollment. That release blocker is removed with the smallest provider-specific
change: built-in Google reads now use stable official Gmail and Calendar REST APIs
behind the existing `IConnectedToolBroker`. Generic remote Streamable HTTP MCP,
host credential custody, exact review, scheduler and approval architecture remain
unchanged. Previously stored Google accounts and refresh grants are reused after
restart; their old remote catalog is exposed as the new bounded stable tools, and
the connection version invalidates obsolete reviewed tool bindings.

- Frozen source: `ea911352eaac74277caf9ad8c98350bebc574630`.
- Package: `artifacts/portable-local-stable-google-package-r1/thaddeus-win-x64`.
- ZIP: `artifacts/portable-local-stable-google-package-r1/thaddeus-win-x64.zip`,
  102,389,425 bytes, SHA-256
  `83e25a8f6fee8981bce488bd42cfda75e6d1201a8ee9f675bb35a68f49a3b235`.
- Package manifest SHA-256:
  `2c9e28b91d54d2e8fa8295b23343fe144f51d7541d8805d80181d1ea85988818`;
  it records clean `win-x64` source `ea91135` and 746 files.
- Exact normal-desktop launch command from this repository:
  `artifacts\Start-Thaddeus.cmd`. Its owner launcher validates every package file,
  the existing launch profile and current rollback, then uses the product's normal
  maintenance API and verified backup before starting the candidate. `-CheckOnly`
  passed without login, shutdown, backup or launch. The owner host was deliberately
  left on `portable-local-final-google-r5` pending this action.
- `artifacts/local-check-stable-google-read-r2` passes the secret scan, locked
  restores, notification Release build, 1,103/1,103 backend tests, protocols and
  frontend production build. The focused Google/connection subset passes 40/40.
- `artifacts/local-check-stable-google-package-r1/native/verified.json` passes all
  17 extracted native checks; `credentials/verified.json` passes all five Windows
  Credential Manager checks and removes its fictional entries. The initial package
  browser run retained one unrelated 15-second background-task timing failure after
  four passes. Its exact fresh rerun passed, and
  `artifacts/stable-google-package-browser-r2/suite.json` passes all 52/52 ordinary
  packaged workflows with isolated studies and cleanup.
- Publisher staging and build intermediates were removed by the established
  publisher. Automatic approval review rejected deletion of two remaining
  fictional fixture studies with `blocked by policy`; no workaround was attempted.
  Owner-removable paths are
  `artifacts/local-check-stable-google-package-r1/browser/05-background-chat-10/study`
  and `artifacts/stable-google-package-background-retry-r1/study`.
- Live status remains honest: consent and account grants passed on the preceding
  host; controlled Gmail and Calendar reads through this exact candidate have not
  yet been observed. Delayed send, brief/watch, revocation, exact-package cold
  notification click and fresh-Windows-user setup remain owner actions. No live
  model/GPU call, owner-study mutation, publication, submission or shutdown occurred
  in this correction pass.
- Exact next action: from a normal Windows Terminal or File Explorer, run
  `artifacts\Start-Thaddeus.cmd`; refresh Thaddeus, ask it to read the latest Gmail
  message and today's Calendar, then continue the remaining checklist in
  `MVP_DELEGATION_MANUAL_QA.md`. Stop development unless that acceptance reveals a
  reproducible agreed-scope release blocker.

## September 17 live Gmail empty-query correction

The owner activated the stable-Google candidate and requested one latest Gmail
message. Receipt `375b33fbe64c4a22b3b2b544ed493277` proves that the model chose the bounded
`gmail.messages.search` tool with `{"query":"","maxResults":1,"unreadOnly":false}`,
the remembered owner approval dispatched it, and the host rejected it with an
`ArgumentException` before any Gmail response was accepted. The cause was the
adapter treating an empty optional string as invalid. Optional empty strings now
normalize to omission; required strings still fail through the existing required
field check.

- Frozen source: `0d0e463acac3b8621267eb8cbb1ee02ff790c00a`.
- Package: `artifacts/portable-local-gmail-empty-query-package-r1/thaddeus-win-x64`.
- ZIP: `artifacts/portable-local-gmail-empty-query-package-r1/thaddeus-win-x64.zip`,
  102,389,554 bytes, SHA-256
  `912b7ad53147c74a49cf7df08a6438031c1a477af7d716d854c51d091bf9b81f`.
- Package manifest SHA-256:
  `1dc830726d8b1d6bcef2e4870111ec9d2eb444a8748de2cbfa993d6b5a9ebf3a`;
  it records clean `win-x64` source `0d0e463` and 746 files.
- `artifacts/local-check-gmail-empty-query-fix-r1` passes the secret scan,
  locked restores, notification Release build, 1,104/1,104 backend tests,
  protocols and frontend production build. The focused Google/OAuth/connected-tool
  set passes 44/44, including the exact live argument shape.
- `artifacts/local-check-gmail-empty-query-package-r1` passes publication,
  17 extracted native checks, five Windows Credential Manager checks, and all
  52/52 ordinary packaged browser workflows. It made no live model or Google call.
- `artifacts\Start-Thaddeus.cmd` now validates this package and retains the
  stable-Google package as rollback. `-CheckOnly` passed without login, backup,
  shutdown or launch. The existing owner host and data were left untouched.
- Owner-study activation remains open until the owner launches this candidate
  from a normal desktop terminal. The exact-package live Gmail read passed in the
  isolated acceptance below. Publication remains paused; unchanged notification
  and broader acceptance evidence is not relabeled.

### Exact-package live Gmail acceptance

The same candidate was launched on alternate loopback ports against a disposable
copy of the verified pre-update owner study. The prior exact request was approved
once, the saved Google authorization refreshed successfully, and run
`fe8e0ef443b94f9daadb0d42c0b8861d` reached `succeeded` with a non-error
`gmail.messages.search` receipt. The returned envelope contained the documented
`messages`, `nextPageToken`, `resultSizeEstimate` and `readOnly` fields. Sanitized
evidence is retained at `artifacts/live-gmail-empty-query-acceptance-r2.json`; it
contains no email content, account address or credential. This proves the bounded
live Gmail read for the exact packaged binary. It does not prove Calendar, delayed
send, recurring brief/watch, revocation, notifications or fresh-user setup.

The isolated host exited and ports 5279/5283 were released. Automatic approval
review rejected recursive deletion of the two disposable folders, so no alternate
deletion method was used. The owner should manually delete
`artifacts/live-gmail-empty-query-acceptance-r2`, which contains the private study
copy, and `artifacts/live-gmail-empty-query-acceptance-r1`, which contains the empty
failed setup. The compact sanitized JSON outside those folders should remain.

### Owner-study activation and live Gmail acceptance

At the owner's request, notification testing was skipped and the verified
replacement was activated from the Codex environment. The launcher closed the
old host through its maintenance API, verified backup
`.data-backups/20260917-232041-desktop-update-f9af247c9f8649e18de83593e9a918fc`
(schema 11, 10 files, 1,911,757 bytes, manifest SHA-256
`cf034c9e037f475f52351e5c6a203a8f6ea6591d700722824afb82e216d530c2`), and
started the exact candidate against the unchanged `.data` study. PID 38768 served
the expected package on port 5179 when verified.

Owner-study run `b8ebccc6b56647f789e6c732c4e3eb2b` repeated the exact latest-Gmail
request. The remembered owner approval dispatched automatically, the run reached
`succeeded`, and `gmail.messages.search` returned a non-error tool receipt. No
email content, account address or credential was printed or copied into acceptance
documentation. The temporary Codex-launch override was removed immediately after
activation; the launcher's ordinary desktop-environment guard and `-CheckOnly`
verification both remain in force. Native notification delivery was not exercised
or inferred from this run.

### Last-minute read-only release audit

The active candidate also completed owner-study run
`cfb030d1f04a43e082099e66b373e71d` through normal Chat. The exact one-time review
bound `calendar.events.list` to the primary calendar, the interval
`2026-09-17T00:00:00-06:00` through `2026-09-18T00:00:00-06:00`, and at most 50
results. The run reached `succeeded` with a non-error Google Calendar receipt; no
event details were copied into acceptance documentation.

The final non-mutating audit found all three Google products connected, two
persisted delegation jobs and their two occurrences in `succeeded`, zero failed
jobs, zero failed occurrences and zero unknown occurrences. Core and exact-package
receipts remain passed; the package manifest and ZIP still hash to
`1dc830726d8b1d6bcef2e4870111ec9d2eb444a8748de2cbfa993d6b5a9ebf3a` and
`912b7ad53147c74a49cf7df08a6438031c1a477af7d716d854c51d091bf9b81f`.
Git `main` and `origin/main` matched before this documentation update. No email
was sent, no recurring brief/watch was created, no external data was changed and
notification delivery was deliberately skipped at the owner's direction.
