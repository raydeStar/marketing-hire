# Windows delegation MVP acceptance

## Current decision - September 17 final acceptance

**READY FOR OWNER ACCEPTANCE**, not ACCEPTED FOR WINDOWS PREVIEW.
Exact candidate: `0d0e463acac3b8621267eb8cbb1ee02ff790c00a`, schema 11, archive SHA256 `912b7ad53147c74a49cf7df08a6438031c1a477af7d716d854c51d091bf9b81f`.
Launch/path/rollback are in `NON_NOTIFICATION_MVP_HANDOFF.md`.
The owner study is running this candidate from the verified package; the prior
stable-Google package remains the launcher's rollback.

Current local evidence: `artifacts/local-check-gmail-empty-query-fix-r1` passes
1,104/1,104 backend tests, protocol tests, frontend production build,
notification build and the tracked-file secret scan. The exact package passes
17 extracted native checks and five Windows Credential Manager checks in
`artifacts/local-check-gmail-empty-query-package-r1`; its packaged browser suite
passes 52/52. The focused Google/connection set passes 44/44 and covers stable Gmail/Calendar
REST reads, PKCE, multiple permissions, old-catalog compatibility, revocation and
approved sending. These checks use fixtures. A live owner request on the preceding
candidate proved the model emits an empty optional Gmail query and that the old
adapter rejected it before contacting Google. The exact payload is now a passing
regression. A disposable copy of the verified pre-update study and the upgraded
owner study both completed the same live Gmail read through this exact package.

Astra coordination used the completed ChatGPT handoff **Define Thaddeus magic**,
as confirmed by the owner. No concurrent work was overwritten. Nine original
inbox failures were fixed minimally; the retained tenth repro failure proposed
a stricter quote policy and was withdrawn, not counted as a fixed bug.

Notification source hashes match Q's helper/host dispatch implementation; its
owner-observed scheduled display is reused only for that implementation. The
new helper binaries have different hashes; exact current-package human
observation is not inferred. `handoff-verified.json` records the distinction. The
frontend has since changed and is rechecked by the package suite. E2's callback
preceded sender exit by 328 ms, so cold activation is still OWNER ACTION.
No native probe or machine registration/settings change was made in this pass.

The owner then reproduced one release blocker: an explicit `right now` reminder
was converted to the frozen request timestamp and rejected after clarification.
The current candidate presents `Immediately after approval` and binds the real
due time at approval. A delayed-approval regression dispatches once, while past
non-immediate reminders remain invalid. Focused evidence is
`artifacts/immediate-reminder-20260917/immediate-reminder.trx` (24/24); the clean
package gate and all 52 packaged browser workflows pass.

The final UX blocker moved exact approval review into Chat and added narrowly
bound remembered Allow/Deny choices. Settings presents those choices as one
removable list; removing a rule restores ask-each-time. The focused packaged
workflow verifies both remembered decisions, connector/action scoping behavior,
chat-opened settings, removal, and 390-pixel layout with zero live model calls.

The matrix below is authoritative; historical receipts retain their original
candidate names. Publication and submission remain separately paused.

## Release feature freeze

The final feature-freeze scope adds one connector-neutral, read-only inbox
watch. It binds one exact mail connector and tool version, checks at a five-minute
interval while the host is running and awake, assesses at most 20 new messages,
and keeps one owner-editable importance instruction. Its recurring-read grant
expires after 30 days or 8,640 checks. Durable message and alert identities prevent
repeat announcements across restarts, including when a new message arrives in an
existing thread. Empty checks make no model call. Connector, authorization, or
classification failures remain visible and pause the watch.

This is the feature boundary until release acceptance is complete. Only fixes for
reproducible bugs, security or data-loss risks, failed acceptance criteria, and
confusing setup inside the agreed scope may enter this release. Other feature or
architecture ideas belong in `BACKLOG.md` and require an explicit owner scope
change before implementation. Publication remains paused. Astra's notification
correction is integrated and owner-observed display is retained. Final packaged
cold-click acceptance remains separate and is not implied by an in-app result.

## Previous broad-suite candidate (O)

The current package, focused evidence and remaining owner actions are recorded
in NON_NOTIFICATION_MVP_HANDOFF.md. The O receipts below retain their original
revision and have not been relabeled as Q results.

- Source revision: `96a7667b71184422ea8df7591cb6d610c99360d4`, clean at publication.
- Windows package:
  `artifacts/portable-local-mvp-release-candidate-20260917-o/thaddeus-win-x64`.
- Package manifest: runtime `win-x64`, 738 packaged files, unsigned development
  package, published September 17, 2026 at 06:26 Mountain Time. The ZIP is
  101,624,222 bytes with SHA256
  `238b19bc324547bee071ba73197d690f1fe0c4b592f6777b930cb6f293983f6b`.
- Package gate:
  `artifacts/local-check-mvp-release-candidate-20260917-o/verified.json` reports
  `passed: true` for publish, 17 extracted native checks, native credential
  cleanup, locked MCP fixture restore/build, and the browser suite, with zero live
  model calls, zero GPU inference, and no worker qualification claim.
- Browser suite:
  `artifacts/local-check-mvp-release-candidate-20260917-o/browser/suite.json`
  reports 46/46 ordinary packaged workflows passed, with a fresh isolated study
  for every case. The current study was untouched and owned-process cleanup
  passed. Five opt-in cases remain outside this suite: live web/model research,
  the native folder picker, native notification, research, and study handoff.
- Connected delegation fixture: the official .NET MCP Streamable HTTP SDK
  advertised `send_email`, `search_email`, and `list_calendar_events`. Packaged
  Chat completed ten synthetic provider calls while the fixture recorded zero
  external calls.
- Owner-study activation:
  `artifacts/activation-delegation-release-20260917-k/activation.json` records a
  verified schema 10 backup, the schema 11 migration, Release K process identity
  on ports 5179/5183, and an exact served client hash. The owner study retained
  36 runs, 38 chats, two artifacts, its existing delegation, Luna provider
  selection, Brave credential, and 999-query allowance. Activation started no
  model, worker, notification probe, or publication action. Release H and the
  pre-migration backup remain the rollback pair.
- Cleanup:
  `artifacts/storage-cleanup-delegation-20260916-b/cleanup.json` records removal
  of five superseded packages and 91 disposable fictional studies. The final
  candidate, rollback, active study, compact evidence, and pinned worker/VM
  inputs remain.
  A later attempt to remove only superseded Candidate M/N package copies and
  their leftover fictional studies was rejected by automatic approval review;
  nothing was removed and no alternate route was attempted. Exact paths and the
  `blocked by policy` outcome are in
  `artifacts/storage-cleanup-mvp-release-20260917-a/blocked.json`.
- External-state audit:
  `artifacts/external-acceptance-state-20260916-a/receipt.json` records the live
  owner-study UI reporting zero MCP connectors, the exact final-package host
  still serving on port 5179, and the current Windows Sandbox boundary. No
  settings, credentials, owner data, model calls, or external services were
  touched. Windows Sandbox is disabled and its executable is absent; this shell
  is not elevated, so a fresh-profile pass cannot be created silently from this
  session.
- Live reminder occurrence:
  `artifacts/reminder-notification-acceptance-20260916-a/receipt.json` records an
  exact reviewed one-shot reminder through the owner study. Occurrence
  `7124f7ca2935f92da75db7ce46d6be419353af9d7312df431f73d841e6583c23`
  dispatched once at 15:42:06 Mountain Time, succeeded, and retained provider
  receipt `windows-shell:31096:1` with `Shell_NotifyIcon` accepted. The result
  remains unread. Human visual confirmation of the toast is still deliberately
  separate from Shell acceptance. The adjacent `diagnostic.json` records that
  this candidate uses a classic `Shell_NotifyIcon` tray balloon without an
  AppUserModelID or WinRT toast registration. A missing per-app registry entry
  is therefore expected, and Windows may suppress the visible balloon even
  after the shell accepts it.
- Modern notification replacement:
  `artifacts/notification-browser-release-20260917-d/screenshots/native-notification-receipt.json`
  binds the final runtime to a normal Chat review, a one-shot reminder, browser
  close before dispatch, and exactly one occurrence. Windows App SDK notification
  37558 reports setting `Enabled`, `activeCount: 7`, and
  `retainedInNotificationCenter: true`; the unread in-app result also remains.
  Windows registers the Thaddeus icon and activation target from Candidate O.

**Historical September 17 notification investigation status.** The original **DEFERRED BY OWNER: Astra handoff; visual acceptance remains
open** boundary is retained as history; the later owner observation above
supersedes display status, but does not close the final cold-click gate. Preserve prior
receipts; scheduler and durable in-app acceptance remain separate gates.

The preserved notification implementation, evidence paths, reproduction boundary,
and coordinated final-pass instructions are in
[`ASTRA_NOTIFICATION_HANDOFF.md`](ASTRA_NOTIFICATION_HANDOFF.md).

## Scheduler implementation map

- `src/Thaddeus.Infrastructure/DelegationScheduler.cs` creates reviewed reminder,
  email, and weekday-brief jobs and claims due occurrences for one dispatch.
- `src/Thaddeus.Host/DelegationPump.cs` is the hosted one-second due-work loop. It
  runs with the host and does not depend on an open browser.
- `src/Thaddeus.Infrastructure/StoreDelegations.cs` persists jobs, grants,
  occurrences, next-run UTC, local timezone semantics, versions, cancellation,
  edits, restart recovery, missed-time decisions, and terminal outcomes.
- `src/Thaddeus.Host/HostDelegationDispatcher.cs` routes the persisted action to
  its approved reminder, Gmail, or bounded-brief dispatcher.

Focused tests cover one-shot execution, a short real-clock host-pump dispatch,
host restart, interrupted/unknown dispatch, cancellation races, stale missed
work, daylight-saving recurrence, connection drift, exact payload preservation,
and authority rotation after editing. Missed one-shot work is recorded rather
than sent late. Unknown external outcomes are retained for inspection and are
never retried automatically.

## Acceptance matrix

| ID | Status | Current evidence | Remaining acceptance |
|---|---|---|---|
| G1 Schedule and send email | OWNER ACTION | Chat clarifies an exact recipient, presents sender/recipient/subject/body/time/timezone review, persists one-send authority, and supports a reviewed replacement. The connected Google Gmail account exposes a narrow host-side `users.messages.send` adapter rather than treating a draft as delivery. Focused tests cover token refresh, revoked access, exact MIME content, provider acceptance versus recipient delivery, ambiguous transport outcomes, restart, drift, and no automatic resend. | Reuse the owner-authorized Google test account with an owner-controlled recipient and observe one delayed send plus Gmail's message receipt. |
| G2 Recurring morning brief | OWNER ACTION | Packaged Chat clarifies the missing time, reviews bounded read-only email/calendar scope, creates the weekday brief, and supports pause, resume, time change, and message-count change. Backend tests cover DST, source unavailable versus empty, connector drift, recurrence after failure, and grant rotation. Exact-candidate owner-study acceptance proves the bounded Gmail search path with the saved owner grant; Calendar and scheduled composition were not exercised. | Observe one bounded recurring occurrence with both Gmail and Calendar source receipts. |
| G3 Reminder delivery | OWNER ACTION | Q's owner-launched scheduled test passed with the browser closed (notification 37570); the owner confirmed visible delivery. E2 reached the exact confirmation URL and was owner-confirmed, but its callback arrived 328 ms before the sender exited. The current candidate also fixes explicit `right now` reminders by scheduling them at approval time; focused tests prove exactly one dispatch after delayed approval. Warm-click evidence and the failed cold-click receipt remain distinct. | Run the current candidate's normal Chat `right now` reminder once and complete one click after the helper exits. |
| G4 Reading to real To-dos | PASS | The final package suite covers upload/public-page/saved-note admission and actual editable source-linked To-do creation. Host read-back, changed-source refusal, unresolved dates, deterministic replay, and interrupted-batch recovery are covered by backend and packaged tests. | A live model pass is optional release QA, not missing host behavior. |
| G5 Conversational management | PASS | The final package suite covers read-only job listing, ambiguous references, ordinal choice, cancel, reminder reschedule, scheduled-email replacement, and recurring-brief pause/resume/edit. Every mutation remains version-bound and review-gated. | Live G1/G2 dispatch is tracked separately. |
| G6 Selective inbox watch | OWNER ACTION | Focused fixtures cover connector-neutral Microsoft-style eligibility, refusal of mutating mail tools, exact recurring-read approval, important versus routine classification, quiet empty checks without a model call, durable no-duplicate restart behavior, new messages in an existing thread, stable Gmail message envelopes with precise timestamps, incomplete/full-page refusal, and visible pause after revoked/unavailable access. The implementation accepts any eligible bounded read-only mail connector rather than binding the product to Gmail. Relevant results use the same notification interface; empty and routine checks stay quiet. | Reuse the owner-authorized live mail account, approve the exact watch scope, observe one quiet check and one selective in-app attention result, then human-observe notification delivery separately. |
| C1 Natural-language entry | PASS | Ordinary packaged Chat accepts reminder, connected-action, source-to-To-do, and job-management requests. Host checks independently constrain recipient, time, tool, job identity, and mutation. | None for the packaged host contract. |
| C2 Durable execution | PASS | Schema 11 persists versioned jobs, grants, occurrences, inbox-watch progress/alert identities, UTC time, timezone semantics, dispatch intent, next run, and missed state. Package/native checks cover startup, archive/restore, restart, and one-host ownership. | None for the Windows package contract. |
| C3 Real verified actions | OWNER ACTION | To-do writes are real and read back. Reminder/email/brief occurrences retain provider/native receipts, and proposals are not treated as success. | Live mail/calendar receipts are required for external-action acceptance. |
| C4 Bounded delegation grant | OWNER ACTION | Persisted typed grants bind owner, connection/tool fingerprints, target, schedule/version, occurrence count, expiry, external-call allowance, and model allowance. Package and backend tests cover drift, caps, rotation, pause/resume, races, stale versions, OAuth disconnect, partial consent, and revoked refresh credentials. | Exercise Google-side revocation once a live owner-authorized test connector exists. |
| C5 Visible and recoverable failure | PASS | The final package exposes scheduled, paused, working, needs-approval, succeeded, failed, unknown, missed, cancelled, and notification-failed states. Review in Chat preserves drafts; unknown outcomes cannot retry or cancel; notification failure retains the successful unread result. | Live connector recovery remains useful QA but is not needed to prove the UI/state contract. |
| C6 Duplicate-effect safety | PASS | Stable occurrence/operation IDs, claim-before-effect, authorization recheck, UNKNOWN/manual-review recovery, backup revocation, deterministic To-do IDs, and notification no-replay are covered. Package restart/restore checks passed. | No universal exactly-once delivery claim; ambiguous sends require inspection, not an automatic retry. |
| C7 Clean receipts | OWNER ACTION | Package tests verify readable summaries with disclosed canonical arguments and technical receipts. Briefs retain safe source hashes/status, provider/token accounting, and `sourceMutation=false`; credentials are not exposed. | Verify one live provider receipt and reconnect/replay path. |
| C8 Visible and controllable work | PASS | Log -> Upcoming shows action, recurrence/timezone, host state, pause state, result, unread state, and versioned controls. Human-readable review cards keep canonical JSON behind disclosure. Desktop and mobile packaged cases passed. | None for the packaged UI contract. |
| R1 Preserve existing MVP | PASS | All 52 ordinary packaged workflows passed, including inline exact approval and removable remembered choices, Chat, apps, artifacts, notes, To-do, Ideas, Feed, uploads, search, settings, history, backup/restore, token UI, MCP connection UI, and responsive navigation. | Opt-in live/native workflows remain separately scoped. |
| R2 Clean-user Windows path | OWNER ACTION | The current candidate is an unsigned portable development package. It does not bundle a continuously running OpenClaw gateway or preconfigure mail/calendar credentials. | Verify setup from a fresh Windows user profile with owner-authorized test connectors. |
| R3 Freeze and handoff | OWNER ACTION | The current exact source/checksum, focused and packaged receipts, sanitized live Gmail receipt, corrected unpublished publication handoff, and rollback are recorded in NON_NOTIFICATION_MVP_HANDOFF.md. | Publication stays paused; delayed send, Calendar/brief/watch, revocation, fresh Windows user and final scheduled/click acceptance remain open. |

## External state still required

1. The current candidate is active and has passed a live bounded Gmail search in
   the owner study with the saved owner grant. Continue with one delayed email,
   one Calendar-backed recurring brief, one quiet/important watch pair, revocation,
   and readable provider receipts.
2. One normal-desktop click after the notification helper exits. Q scheduled
   dispatch, visible delivery and warm activation have evidence; E2 was clicked
   just before sender exit and does not prove cold activation.
3. A fresh Windows user profile for installation/setup acceptance. Windows
   Sandbox is not currently available, so this requires either an owner-created
   local profile or an owner-enabled Sandbox.

The [official Product Hunt launch
guide](https://producthunt.s.gy/forum-astra-launch-guide) and [challenge
page](https://www.producthunt.com/contests/gpt-6-astra-challenge) were read on
September 16, 2026. The resulting field limits, launch checklist, honest Astra
attribution, gallery plan, and demo route are recorded in
`docs/PRODUCT_HUNT_SUBMISSION_DRAFT.md`.

These are the remaining release observations. More synthetic benchmark passes
would not resolve them.
