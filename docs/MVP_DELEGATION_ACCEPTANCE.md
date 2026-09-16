# Windows delegation MVP acceptance

This is the acceptance ledger for the September 16 delegation finish-line
contract. `VERIFIED` means the current packaged Windows candidate has direct
evidence for the stated scope. Synthetic provider evidence is identified as
synthetic and is never presented as a live external action.

## Current candidate

- Source revision: `d1a96c6128f626aa5757cd3531ee6aa2dc09cba3`, clean at publication.
- Windows package:
  `artifacts/portable-local-delegation-release-20260916-f/thaddeus-win-x64`.
- Package manifest: runtime `win-x64`, 736 packaged files, unsigned development
  package, published September 16, 2026 at 16:52 Mountain Time.
- Package gate:
  `artifacts/local-check-delegation-release-20260916-d/verified.json` reports
  `passed: true`, 553 source files, six required steps, zero GitHub Actions,
  zero live model calls, and no worker start.
- Browser suite:
  `artifacts/local-check-delegation-release-20260916-d/browser/suite.json`
  reports 45/45 ordinary packaged workflows passed, with a fresh isolated study
  for every case. The current study was untouched and owned-process cleanup
  passed. Four opt-in cases remain outside this suite: live web/model research,
  the native folder picker, research, and study handoff.
- Connected delegation fixture: the official .NET MCP Streamable HTTP SDK
  advertised `send_email`, `search_email`, and `list_calendar_events`. Packaged
  Chat completed ten synthetic provider calls while the fixture recorded zero
  external calls.
- Owner-study activation:
  `artifacts/activation-delegation-release-20260916-a/activation.json` records a
  verified pre-migration backup, additive schema 8 to 10 migration, matching
  preexisting table counts, final-package process identity, and an exact served
  client hash. It started no model or worker task and did not restart Luna.
- Cleanup:
  `artifacts/storage-cleanup-delegation-20260916-b/cleanup.json` records removal
  of five superseded packages and 91 disposable fictional studies. The final
  candidate, rollback, active study, compact evidence, and pinned worker/VM
  inputs remain.
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
  `artifacts/notification-final-20260916-a/receipt.json` records the exact clean
  package, SHA256 `752432581de0f04de2ecd8c4dc5cee521e1b98e4b49d99de1e34ad511c0ab252`,
  Windows App SDK notification 37535, setting `Enabled`, `activeCount: 1`, and
  `retainedInNotificationCenter: true`. Windows' Push Notification Platform log
  records the notification as delivered to active session 1. Human observation
  remains separate.

## Acceptance matrix

| ID | Status | Current evidence | Remaining acceptance |
|---|---|---|---|
| G1 Schedule and send email | IN PROGRESS | Packaged Chat clarifies an exact recipient, presents the sender/recipient/subject/body/time/timezone review, schedules the durable action, and supports a reviewed replacement. Backend tests cover restart, denial, drift, unknown outcomes, and exactly-once host dispatch. Official MCP discovery and ten synthetic calls passed with zero external calls. | Connect an owner-authorized mail account to an owner-controlled test inbox and observe one synthetic delayed email plus its provider receipt. |
| G2 Recurring morning brief | IN PROGRESS | Packaged Chat clarifies the missing time, reviews bounded read-only email/calendar scope, creates the weekday brief, and supports pause, resume, time change, and message-count change. Backend tests cover DST, source unavailable versus empty, connector drift, recurrence after failure, and grant rotation. | Connect owner-authorized test mail/calendar data and observe one bounded occurrence with source receipts. |
| G3 Reminder delivery | IN PROGRESS | The owner checked Windows Notification Center and confirmed that the classic `Shell_NotifyIcon` result was not visible. The replacement clean package now requires Windows to report the notification as retained in Notification Center before it records success. Notification 37535 returned `activeCount: 1`, `retainedInNotificationCenter: true`, and the platform event log records delivery to active session 1. Focused tests pass, while the durable reminder still records one unread result and never replays an uncertain presentation. | Owner visually confirms **Thaddeus final package test**, then one reviewed scheduled occurrence is observed through the host. Platform retention is strong machine evidence, but human observation remains separate. |
| G4 Reading to real To-dos | VERIFIED | The final package suite covers upload/public-page/saved-note admission and actual editable source-linked To-do creation. Host read-back, changed-source refusal, unresolved dates, deterministic replay, and interrupted-batch recovery are covered by backend and packaged tests. | A live model pass is optional release QA, not missing host behavior. |
| G5 Conversational management | VERIFIED | The final package suite covers read-only job listing, ambiguous references, ordinal choice, cancel, reminder reschedule, scheduled-email replacement, and recurring-brief pause/resume/edit. Every mutation remains version-bound and review-gated. | Live G1/G2 dispatch is tracked separately. |
| C1 Natural-language entry | VERIFIED | Ordinary packaged Chat accepts reminder, connected-action, source-to-To-do, and job-management requests. Host checks independently constrain recipient, time, tool, job identity, and mutation. | None for the packaged host contract. |
| C2 Durable execution | VERIFIED | Schema 10 persists versioned jobs, grants, occurrences, UTC time, timezone semantics, dispatch intent, next run, and missed state. Package/native checks cover startup, archive/restore, restart, and one-host ownership. | None for the Windows package contract. |
| C3 Real verified actions | IN PROGRESS | To-do writes are real and read back. Reminder/email/brief occurrences retain provider/native receipts, and proposals are not treated as success. | Live mail/calendar receipts are required for external-action acceptance. |
| C4 Bounded delegation grant | IN PROGRESS | Persisted typed grants bind owner, connection/tool fingerprints, target, schedule/version, occurrence count, expiry, external-call allowance, and model allowance. Package and backend tests cover drift, caps, rotation, pause/resume, races, and stale versions. | Exercise connection revocation once a live owner-authorized test connector exists. |
| C5 Visible and recoverable failure | VERIFIED | The final package exposes scheduled, paused, working, needs-approval, succeeded, failed, unknown, missed, cancelled, and notification-failed states. Review in Chat preserves drafts; unknown outcomes cannot retry or cancel; notification failure retains the successful unread result. | Live connector recovery remains useful QA but is not needed to prove the UI/state contract. |
| C6 Duplicate-effect safety | VERIFIED | Stable occurrence/operation IDs, claim-before-effect, authorization recheck, UNKNOWN/manual-review recovery, backup revocation, deterministic To-do IDs, and notification no-replay are covered. Package restart/restore checks passed. | Provider-native idempotency may be added when a chosen mail provider supports it. |
| C7 Clean receipts | IN PROGRESS | Package tests verify readable summaries with disclosed canonical arguments and technical receipts. Briefs retain safe source hashes/status, provider/token accounting, and `sourceMutation=false`; credentials are not exposed. | Verify one live provider receipt and reconnect/replay path. |
| C8 Visible and controllable work | VERIFIED | Log -> Upcoming shows action, recurrence/timezone, host state, pause state, result, unread state, and versioned controls. Human-readable review cards keep canonical JSON behind disclosure. Desktop and mobile packaged cases passed. | None for the packaged UI contract. |
| R1 Preserve existing MVP | VERIFIED | All 45 ordinary packaged workflows passed, including Chat, apps, artifacts, notes, To-do, Ideas, Feed, uploads, search, settings, history, backup/restore, token UI, MCP connection UI, and responsive navigation. | Opt-in live/native workflows remain separately scoped. |
| R2 Clean-user Windows path | BLOCKED | The current candidate is an unsigned portable development package. It does not bundle a continuously running OpenClaw gateway or preconfigure mail/calendar credentials. | Verify setup from a fresh Windows user profile with owner-authorized test connectors. |
| R3 Freeze and handoff | IN PROGRESS | Exact source revision, clean package, manifest, package-gate receipt, 45-case browser receipt, one rollback package, cleanup receipt, verified owner-study activation, focused manual QA/demo handoff, Product Hunt submission draft, five visually reviewed `1270×760` fictional-data gallery exports with per-file hashes, exact `240×240` packaged-raven thumbnail, a self-contained launch page, and refreshed binary-only publication handoff `publication-handoff-20260916-c` are retained. The final archive is 101,530,936 bytes with SHA256 `752432581de0f04de2ecd8c4dc5cee521e1b98e4b49d99de1e34ad511c0ab252`. | With owner authorization, create the proposed public repository and release, verify the public Pages/download URLs, optionally record the demo video, and capture the remaining human/live observations. |

## External state still required

1. An owner-authorized test inbox and calendar for one delayed email, one bounded
   recurring brief, revocation, and readable provider receipts. The current
   owner study has no MCP connector configured.
2. Owner confirmation that the modern **Thaddeus final package test** notification is
   visible in Windows Notification Center, followed by one reviewed scheduled
   occurrence through the packaged host. The original classic balloon was not
   visible and is not accepted as release evidence.
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
