# Windows delegation MVP acceptance

This is the acceptance ledger for the September 16 delegation finish-line
contract. `VERIFIED` means the current packaged Windows candidate has direct
evidence for the stated scope. Synthetic provider evidence is identified as
synthetic and is never presented as a live external action.

The compact current handoff is
[`NON_NOTIFICATION_MVP_HANDOFF.md`](NON_NOTIFICATION_MVP_HANDOFF.md).

## Release feature freeze

The final Release K follow-up scope adds one connector-neutral, read-only inbox
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
change before implementation. Publication remains paused. Native notification
delivery remains owned by the Astra handoff and is not implied by an in-app inbox
result.

## Current candidate

- Source revision: `553f4a7f0e5d8c34e2ea67cb19648cc3e8a17e62`, clean at publication.
- Windows package:
  `artifacts/portable-local-delegation-release-20260917-k/thaddeus-win-x64`.
- Package manifest: runtime `win-x64`, 738 packaged files, unsigned development
  package, published September 16, 2026 at 20:05 Mountain Time.
- Package gate:
  `artifacts/local-check-delegation-release-20260917-k/native/verified.json` reports
  `passed: true`, 17 extracted native checks, zero live model calls, zero GPU
  inference, native credential cleanup, and no worker qualification claim.
- Browser suite:
  `artifacts/local-check-delegation-release-20260917-k/browser/suite.json`
  reports 45/45 ordinary packaged workflows passed, with a fresh isolated study
  for every case. The current study was untouched and owned-process cleanup
  passed. Four opt-in cases remain outside this suite: live web/model research,
  the native folder picker, research, and study handoff.
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
  The owner visually confirmed the stable `raydeStar.Thaddeus` app-notification
  path. `artifacts/notification-final-package-g-20260916-a/receipt.json` binds
  the same implementation to release G: Windows App SDK notification 37539,
  setting `Enabled`, `activeCount: 2`, and `retainedInNotificationCenter: true`.
  Windows now registers the Thaddeus icon from the release G package.

**DEFERRED BY OWNER: Astra handoff; visual acceptance remains open.** Preserve
the implementation and receipts above, but do not run more toast probes, change
Windows registration, or rebuild solely for notification debugging. Scheduler
and durable in-app result acceptance proceed independently.

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
| G1 Schedule and send email | IN PROGRESS | Chat clarifies an exact recipient, presents sender/recipient/subject/body/time/timezone review, persists one-send authority, and supports a reviewed replacement. The Google Gmail connector now exposes a narrow host-side `users.messages.send` adapter rather than treating a draft as delivery. Focused tests cover token refresh, revoked access, exact MIME content, provider acceptance versus recipient delivery, ambiguous transport outcomes, restart, drift, and no automatic resend. | Connect an owner-authorized Google test account to an owner-controlled recipient and observe one delayed send plus Gmail's message receipt. |
| G2 Recurring morning brief | IN PROGRESS | Packaged Chat clarifies the missing time, reviews bounded read-only email/calendar scope, creates the weekday brief, and supports pause, resume, time change, and message-count change. Backend tests cover DST, source unavailable versus empty, connector drift, recurrence after failure, and grant rotation. | Connect owner-authorized test mail/calendar data and observe one bounded occurrence with source receipts. |
| G3 Reminder delivery | DEFERRED BY OWNER | Astra handoff; visual acceptance remains open. Existing code, reproduction attempts and release G notification receipts are preserved. Scheduler and durable in-app results remain independently testable; neither counts as proof that someone who left the app was notified. | Astra verifies a visible native notification, then the coordinated final acceptance pass integrates that evidence without reopening broad notification experiments here. |
| G4 Reading to real To-dos | VERIFIED | The final package suite covers upload/public-page/saved-note admission and actual editable source-linked To-do creation. Host read-back, changed-source refusal, unresolved dates, deterministic replay, and interrupted-batch recovery are covered by backend and packaged tests. | A live model pass is optional release QA, not missing host behavior. |
| G5 Conversational management | VERIFIED | The final package suite covers read-only job listing, ambiguous references, ordinal choice, cancel, reminder reschedule, scheduled-email replacement, and recurring-brief pause/resume/edit. Every mutation remains version-bound and review-gated. | Live G1/G2 dispatch is tracked separately. |
| G6 Selective inbox watch | IN PROGRESS | Five focused fixtures cover connector-neutral Microsoft-style eligibility, refusal of mutating mail tools, exact recurring-read approval, important versus routine classification, quiet empty checks without a model call, durable no-duplicate restart behavior, new messages in an existing thread, and visible pause after revoked/unavailable access. The implementation accepts any eligible bounded read-only mail connector rather than binding the product to Gmail. | Connect an owner-authorized live mail account, approve the exact watch scope, observe one quiet check and one selective in-app result, then separately integrate Astra's native-notification evidence. |
| C1 Natural-language entry | VERIFIED | Ordinary packaged Chat accepts reminder, connected-action, source-to-To-do, and job-management requests. Host checks independently constrain recipient, time, tool, job identity, and mutation. | None for the packaged host contract. |
| C2 Durable execution | VERIFIED | Schema 11 persists versioned jobs, grants, occurrences, inbox-watch progress/alert identities, UTC time, timezone semantics, dispatch intent, next run, and missed state. Package/native checks cover startup, archive/restore, restart, and one-host ownership. | None for the Windows package contract. |
| C3 Real verified actions | IN PROGRESS | To-do writes are real and read back. Reminder/email/brief occurrences retain provider/native receipts, and proposals are not treated as success. | Live mail/calendar receipts are required for external-action acceptance. |
| C4 Bounded delegation grant | IN PROGRESS | Persisted typed grants bind owner, connection/tool fingerprints, target, schedule/version, occurrence count, expiry, external-call allowance, and model allowance. Package and backend tests cover drift, caps, rotation, pause/resume, races, stale versions, OAuth disconnect, partial consent, and revoked refresh credentials. | Exercise Google-side revocation once a live owner-authorized test connector exists. |
| C5 Visible and recoverable failure | VERIFIED | The final package exposes scheduled, paused, working, needs-approval, succeeded, failed, unknown, missed, cancelled, and notification-failed states. Review in Chat preserves drafts; unknown outcomes cannot retry or cancel; notification failure retains the successful unread result. | Live connector recovery remains useful QA but is not needed to prove the UI/state contract. |
| C6 Duplicate-effect safety | VERIFIED | Stable occurrence/operation IDs, claim-before-effect, authorization recheck, UNKNOWN/manual-review recovery, backup revocation, deterministic To-do IDs, and notification no-replay are covered. Package restart/restore checks passed. | Provider-native idempotency may be added when a chosen mail provider supports it. |
| C7 Clean receipts | IN PROGRESS | Package tests verify readable summaries with disclosed canonical arguments and technical receipts. Briefs retain safe source hashes/status, provider/token accounting, and `sourceMutation=false`; credentials are not exposed. | Verify one live provider receipt and reconnect/replay path. |
| C8 Visible and controllable work | VERIFIED | Log -> Upcoming shows action, recurrence/timezone, host state, pause state, result, unread state, and versioned controls. Human-readable review cards keep canonical JSON behind disclosure. Desktop and mobile packaged cases passed. | None for the packaged UI contract. |
| R1 Preserve existing MVP | VERIFIED | All 45 ordinary packaged workflows passed, including Chat, apps, artifacts, notes, To-do, Ideas, Feed, uploads, search, settings, history, backup/restore, token UI, MCP connection UI, and responsive navigation. | Opt-in live/native workflows remain separately scoped. |
| R2 Clean-user Windows path | BLOCKED | The current candidate is an unsigned portable development package. It does not bundle a continuously running OpenClaw gateway or preconfigure mail/calendar credentials. | Verify setup from a fresh Windows user profile with owner-authorized test connectors. |
| R3 Freeze and handoff | IN PROGRESS | Exact source revision, clean package, manifest, native package receipt, cleanup receipt, verified owner-study activation, focused manual QA/demo handoff, Product Hunt submission draft, five visually reviewed `1270×760` fictional-data gallery exports with per-file hashes, exact `240×240` packaged-raven thumbnail, a self-contained launch page, and refreshed binary-only publication handoff `publication-handoff-20260916-d` are retained. Release K is 101,609,834 bytes with SHA256 `836daa8ed907ec0ab456a8a939ff8d2cdc22671b4c976fe88953a76674e837fc`; Release H and its schema 10 backup remain available for rollback. | Keep publication paused. Complete the owner-authorized Google pass, fresh-profile pass, and coordinated Astra notification acceptance before submission preparation. |

## External state still required

1. An owner-authorized test inbox and calendar for one delayed email, one bounded
   recurring brief, revocation, and readable provider receipts. The current
   owner study has no MCP connector configured.
2. Astra's native-notification handoff and a coordinated visual acceptance pass.
   Do not resume notification debugging from this workstream automatically.
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
