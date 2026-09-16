# Windows delegation MVP acceptance

This is the acceptance ledger for the September 16 delegation finish-line
contract. `VERIFIED` means the current packaged Windows candidate has direct
evidence for the stated scope. Synthetic provider evidence is identified as
synthetic and is never presented as a live external action.

## Current candidate

- Source revision: `1d9f55be5ce505a1cfadc605271078d905a69f40`, clean at publication.
- Windows package:
  `artifacts/portable-local-delegation-release-20260916-d/thaddeus-win-x64`.
- Package manifest: runtime `win-x64`, 426 packaged files, unsigned development
  package, published September 16, 2026 at 20:49 local time.
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

## Acceptance matrix

| ID | Status | Current evidence | Remaining acceptance |
|---|---|---|---|
| G1 Schedule and send email | IN PROGRESS | Packaged Chat clarifies an exact recipient, presents the sender/recipient/subject/body/time/timezone review, schedules the durable action, and supports a reviewed replacement. Backend tests cover restart, denial, drift, unknown outcomes, and exactly-once host dispatch. Official MCP discovery and ten synthetic calls passed with zero external calls. | Connect an owner-authorized mail account to an owner-controlled test inbox and observe one synthetic delayed email plus its provider receipt. |
| G2 Recurring morning brief | IN PROGRESS | Packaged Chat clarifies the missing time, reviews bounded read-only email/calendar scope, creates the weekday brief, and supports pause, resume, time change, and message-count change. Backend tests cover DST, source unavailable versus empty, connector drift, recurrence after failure, and grant rotation. | Connect owner-authorized test mail/calendar data and observe one bounded occurrence with source receipts. |
| G3 Reminder delivery | IN PROGRESS | The durable reminder survives a packaged-host restart, dispatches once, leaves an unread in-app result, and records native notification refusal separately without replay. Windows Shell accepted the prior synchronized notification payload. | Human-observe one Windows toast from the current package; Shell acceptance alone does not prove it appeared. |
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
| R3 Freeze and handoff | IN PROGRESS | Exact source revision, package, manifest, package-gate receipt, 45-case browser receipt, and one rollback package are retained. | Finish bounded cleanup, write the manual QA/demo handoff, and capture the remaining human/live observations. Do not publish automatically. |

## External state still required

1. An owner-authorized test inbox and calendar for one delayed email, one bounded
   recurring brief, revocation, and readable provider receipts.
2. A human-observed Windows notification from the current package.
3. A fresh Windows user profile for installation/setup acceptance.
4. A readable official submission guide before making Astra-evidence or
   submission-field claims.

These are the remaining release observations. More synthetic benchmark passes
would not resolve them.
