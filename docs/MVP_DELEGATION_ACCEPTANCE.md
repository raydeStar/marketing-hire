# Windows delegation MVP acceptance

This is the single acceptance matrix for the September 16 delegation finish-line
contract. `VERIFIED` means the named behavior has current end-to-end evidence at
the scope of the requirement. Implementation, a mock-only result, or an older
partial receipt does not qualify. Update this file after each narrow slice so the
next action remains unambiguous.

## Environment checkpoint

- Checkout: `master`. Durable reminder (`47ac4fa`), reading-to-To-do
  (`ed753aa`), conversational management (`8fd3e2f`), and scheduled email
  (`b239eaa`) checkpoints are pushed to `origin/master`. The recurring-brief
  slice described below is the current uncommitted candidate.
- Running host: Windows x64 package
  `artifacts/portable-mcp-connectors-20260916-a/thaddeus-win-x64`, PID 17476.
  Owner data remains in its existing study and was not modified by this audit.
- OpenClaw: the repository pins `2026.9.4` inside the isolated research worker.
  No host `openclaw` command or persistent OpenClaw service is installed. The
  current research coordinator owns a disposable, per-task worker and revokes its
  short-lived grant at stopping points. It cannot be the lifetime owner of a job
  due after that worker is retired.
- Scheduler decision: use one durable Thaddeus host scheduler and the existing
  broker boundary. Do not create a second agent loop. OpenClaw 2026.9.4 does have
  durable Gateway automations, but its scheduler lives in a continuously running
  Gateway; this product has no continuously running installed Gateway outside its
  disposable research worker.
- Connections: the owner study has no configured MCP connector, so it currently
  has no application credential for mail or calendar. The Luna model and Brave
  search connections are separate and do not grant mail/calendar access.
- Submission source: the official Product Hunt page identifies the GPT-6 Astra
  Challenge as September 18, 2026 and links to a submission guide, but the page's
  live countdown currently renders zero and the linked guide was unavailable to
  the read-only fetch. Timing and required Astra evidence remain unverified.
- Storage: approximately 92 GiB was free at audit start. Large tests remain
  subject to the repository's 10 GiB reserve and cleanup rules.

## Dependencies requiring owner or external state

1. **Mail and calendar:** G1, G2, and the live portion of R2 require one real,
   owner-authorized provider connection plus a test inbox/calendar. QA may send
   only synthetic content to an owner-controlled test inbox. A connector merely
   being present is not permission to send.
2. **Windows clean-user proof:** R2 needs a fresh Windows user profile or other
   clean Windows test environment. A new Thaddeus database alone is insufficient.
3. **Native notification acceptance:** Windows may require the owner to allow
   notifications when the packaged app first requests them. Missing permission
   must remain visible rather than being treated as delivery.
4. **Submission rules:** the official linked submission guide must become
   readable, or the owner must provide it, before claims about required Astra
   evidence or submission fields can be verified.

## Acceptance matrix

| ID | Status | Implementation location | Actual evidence | Remaining blocker / exact next action |
|---|---|---|---|---|
| G1 Schedule and send email | IN PROGRESS | `DelegationEmailConversation`, `DelegationScheduler.CreateEmail`, `ConnectedEmailDelegationDispatcher`, and `HostDelegationDispatcher` implement a chat-originated exact email review, durable one-time job/grant, unattended MCP dispatch, and retained provider evidence. The sender connection, actual recipient, subject, body, time, timezone, and one-send authority are visible before approval. | Focused tests prove no job/call before approval, denial, exact two-hour due time, persistence across host restart, one dispatch only, provider result retention, connector-version refusal, credential custody, failed/unknown outcomes without replay, and connector-removal protection. No live email was sent. | Connect an owner-authorized email MCP to an owner-controlled test inbox, verify its send tool shape, then run one synthetic delayed live acceptance. Keep this row short of VERIFIED until that provider receipt exists. |
| G2 Recurring morning brief | IN PROGRESS | `DelegationBriefConversation`, `DelegationScheduler.CreateBrief`, and `ConnectedBriefDelegationDispatcher` create a weekday, timezone-aware, read-only email/calendar grant with a frozen provider, bounded email rule, unread in-app result, and per-source evidence. Chat and Upcoming support versioned pause/resume/cancel; Chat can replace the reviewed schedule and read arguments without changing accounts/tools. | 13 focused brief/management tests and 31 broad delegation tests pass. They prove approval/denial, no pre-approval reads, connector drift refusal, exact occurrence-time arguments, source-linked unread output, unavailable-versus-empty wording, recurrence after model failure, and grant rotation for pause/resume/edit. `local-check-delegation-brief-20260916-a` passes 981 backend and 32 protocol tests plus the production web build. No live mail/calendar data was read. | Connect owner-authorized mail/calendar test data, verify the selected read tool shapes, then run one bounded live occurrence and packaged UI check. Keep short of VERIFIED until those receipts exist. |
| G3 Reminder delivery | IN PROGRESS | `DelegationConversation`, `DelegationScheduler`, `DelegationPump`, `WindowsDelegationDispatcher`, and the Upcoming log implement chat proposal, exact approval, durable one-time jobs, native Windows dispatch, and unread in-app occurrence results. | 59 focused runtime/migration/connected/delegation tests pass, including exact approval/denial, controlled two-hour restart, cancellation, missed run, DST, crash/unknown and no replay. `artifacts/delegation-native-20260916-a/inspection-b.json` records the pre-due packaged-host restart with no browser/model, one dispatch at 17:33:06Z, Windows Shell acceptance, and `ReadAt: null`. | Human-observe one synchronized-candidate notification because Shell acceptance does not prove Windows displayed it; final-package rerun must include the later ledger-link change. |
| G4 Reading to real To-dos | IN PROGRESS | `TodoBatchConversation` combines existing upload/public-page reading with Luna's bounded extraction, one exact batch approval, deterministic editable To-do writes, source retention, ambiguity notes, and ID/version read-back. `StoreTodoBatches` retains an idempotent operation receipt across restart. | Focused tests cover uploaded text, an actual brokered public-page read before proposal, denial, changed-source refusal, unresolved dates, deterministic replay, and recovery after interruption between item commits and the operation receipt. The compatible-provider test proves Luna receives only the frozen source/tool schema. | Add saved-note admission or explicitly constrain the first release to uploads/public links; run the packaged browser/manual workflow and paraphrase QA before marking verified. |
| G5 Conversational management | IN PROGRESS | `DelegationManagementConversation` gives Chat a bounded view of current jobs plus version-bound cancel, reminder-reschedule, scheduled-email replacement, and recurring-brief pause/resume/edit proposals. Listing is read-only. Exact approval is required before every Chat mutation; each change revokes or rotates schedule/payload authority. | Focused tests cover read-only listing, approval/denial, grant revocation, read-back, reminder schedule rotation, stale-version refusal, email replacement dispatch, and recurring-brief pause/resume/edit authority rotation. | Add multi-match clarification/paraphrase and packaged UI checks. |
| C1 Natural-language entry | IN PROGRESS | Ordinary Chat advertises typed reminder, connected MCP, source-to-To-do, and current-job management tools from frozen host context; no workflow builder is required. | Provider/runtime tests cover exact frozen schemas, read-only job questions, cancellation, rescheduling, reminders, and reading-to-To-do entry. | Add multiple formulations and necessary clarification for email and recurring brief, including a short choice when more than one job plausibly matches, then packaged accessibility QA. |
| C2 Durable execution | IN PROGRESS | Schema 9 persists versioned jobs, grants, occurrences, UTC instants, timezone semantics, dispatch intent, next run and missed state; one host pump owns claims. Upcoming shows host availability and the awake requirement. | Controlled two-hour restart, no stale catch-up, weekday/DST, interruption recovery and no-replay tests pass. The packaged one-minute fixture restarted before due and dispatched once after restart. | Verify schema upgrade and repeat the native check against the final synchronized package. |
| C3 Real verified actions | IN PROGRESS | Approved source batches create actual library To-dos and verify every ID/version by read-back; connected calls, reminders, scheduled emails, and recurring briefs retain provider/native receipts. A brief is only delivered from persisted occurrence evidence after both broker reads and bounded composition. | Combined reading-to-To-do tests prove proposals are not treated as success. Email tests retain provider response/message identifiers. Brief tests read the persisted unread occurrence back and verify its source-linked output and source statuses. | Add actionable credential setup states and run real owner-controlled email plus calendar/email brief acceptance. |
| C4 Bounded delegation grant | IN PROGRESS | Reminder, email, and recurring-brief approvals create persisted typed grants bound to owner, account/tool fingerprints, target/scope, schedule/version, occurrence counts, expiry, external-call allowance, and model allowance. Brief edits preserve accounts/tools/provider and atomically rotate schedule/payload authority. | Approval/denial, connector drift, payload caps, schedule/payload rotation, pause/resume, race, and stale-version tests pass. Brief source data is supplied only as untrusted model input and cannot widen authority or select mutation tools. | Add live provider revocation acceptance and remaining packaged tamper checks. |
| C5 Visible/recoverable failure | IN PROGRESS | Jobs/occurrences expose scheduled, paused, working, needs-approval, succeeded, failed, unknown, missed and cancelled; action and notification outcomes are separate. Brief sources record unavailable separately from available-empty, and a failed occurrence leaves the recurring schedule active. | Missed, failed/unknown, email uncertainty, cancellation, notification, all-sources-unavailable, and brief-model-failure paths have deterministic tests. | Add provider reconnect/review guidance and prove native notification refusal is actionable. |
| C6 Duplicate-effect safety | IN PROGRESS | Stable occurrence/operation IDs, atomic claim plus pre-effect intent, authorization recheck, and UNKNOWN recovery prevent automatic replay. Email dispatch stops UNKNOWN after an inconclusive provider call. Restoring a backup now revokes every unfinished delegation grant and clears its next run instead of rearming outbound work. To-do item IDs derive from approval operation plus index. | Duplicate reminder/email tick, email host restart, cancellation race, uncertain outcome, crash-after-intent, restored-email disarming, To-do replay, mismatch refusal, and interrupted batch recovery tests pass. | Add provider-native idempotency/reconciliation where the selected live email provider supports it; otherwise retain the current UNKNOWN/manual-review boundary. Repeat restore/package acceptance on the final candidate. |
| C7 Clean receipts | IN PROGRESS | Approval and scheduling events link the originating run to job, grant, exact schedule, target and capability receipt. Brief occurrences retain the generated brief, safe source hashes/status, provider/token use, and `sourceMutation=false`. Upcoming opens a readable result modal; canonical arguments and the complete technical receipt remain behind disclosure and export. | Conversation tests prove reminder/email receipts survive restart, To-do execution is source-linked, and brief/management changes produce no unreviewed mutation or credential exposure. Production web build proves the occurrence modal compiles. | Verify the modal and read-only replay/reconnect behavior in the packaged browser workflow. |
| C8 Visible/controllable work | IN PROGRESS | Log → Upcoming shows action, recurrence/timezone, host status, pause state, latest result, unread control, and versioned pause/resume/cancel. Chat supports bounded recurring-brief edit. Reminder, email, brief, To-do, and management approvals use human-readable cards with canonical JSON behind disclosure. | Production TypeScript/Vite build passes; 31 delegation tests prove all controls use version checks and rotate/revoke authority. | Add focused browser interaction checks and packaged responsive/manual QA. |
| R1 Preserve existing MVP | IN PROGRESS | Current live package includes Chat, apps, To-do, notes, search, Feed, uploads, history, backup/restore, and reviewed MCP. | `local-check-delegation-brief-20260916-a`: secret scan, locked restore, 981 backend and 32 protocol tests, and production web build all pass with zero live model calls, worker starts, hosted Actions, or GPU use. | After delegation freezes, run the established packaged/manual smoke without subjective redesign. |
| R2 Clean-user Windows path | BLOCKED | Current installer is an unsigned host-only preview; worker setup is separate and mail/calendar setup does not exist. | Prior native host installer evidence does not cover a clean Windows profile, included worker, or delegation. | Build synchronized candidate, document/setup actual dependencies, then verify from a fresh Windows profile with one real delegation. |
| R3 Freeze and handoff | IN PROGRESS | Current exact commit/package are known; no delegation candidate or submission bundle exists. | Baseline package and rollback are retained. | After G/C verification, create the exact release handoff, hashes, demo script/screenshots, verified limitations, and rules-grounded submission copy; do not publish. |

## Exact next implementation slice

Checkpoint G2's recurring read-only morning brief and controls, then finish G4
saved-note admission and the remaining packaged interaction checks. Live
mail/calendar acceptance remains blocked on owner-authorized test accounts.
Human-observe one reminder from the final synchronized package; Shell acceptance
alone is insufficient display proof.
