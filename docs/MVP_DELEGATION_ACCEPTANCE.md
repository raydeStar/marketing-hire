# Windows delegation MVP acceptance

This is the single acceptance matrix for the September 16 delegation finish-line
contract. `VERIFIED` means the named behavior has current end-to-end evidence at
the scope of the requirement. Implementation, a mock-only result, or an older
partial receipt does not qualify. Update this file after each narrow slice so the
next action remains unambiguous.

## Environment checkpoint

- Checkout: `master`, commit `fc1ad81a7f05c338f42917fb1d5c6ebbad192695`,
  clean and equal to `origin/master` at the start of this audit. Durable reminder
  delegation was checkpointed and pushed as `47ac4fa` during the audit.
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
| G1 Schedule and send email | BLOCKED | No durable email job/grant/provider execution exists. Generic reviewed MCP calls are in `McpConnections` and `ConnectedToolConversation`. | Exact reviewed one-shot MCP calls have synthetic tests; no scheduled or live email was sent. | Implement durable jobs/grants/occurrences and an exact email action. Then connect an owner-controlled test inbox and run delayed live acceptance. |
| G2 Recurring morning brief | BLOCKED | No recurring delegation implementation exists. Existing website reading and MCP discovery are reusable inputs. | No recurring calendar/email occurrence or source-linked brief evidence. | Implement recurring reads, bounded selection, in-app delivery, pause/edit/cancel, then connect approved mail/calendar test data. |
| G3 Reminder delivery | IN PROGRESS | `DelegationConversation`, `DelegationScheduler`, `DelegationPump`, `WindowsDelegationDispatcher`, and the Upcoming log implement chat proposal, exact approval, durable one-time jobs, native Windows dispatch, and unread in-app occurrence results. | 59 focused runtime/migration/connected/delegation tests pass, including exact approval/denial, controlled two-hour restart, cancellation, missed run, DST, crash/unknown and no replay. `artifacts/delegation-native-20260916-a/inspection-b.json` records the pre-due packaged-host restart with no browser/model, one dispatch at 17:33:06Z, Windows Shell acceptance, and `ReadAt: null`. | Human-observe one synchronized-candidate notification because Shell acceptance does not prove Windows displayed it; final-package rerun must include the later ledger-link change. |
| G4 Reading to real To-dos | IN PROGRESS | `TodoBatchConversation` combines existing upload/public-page reading with Luna's bounded extraction, one exact batch approval, deterministic editable To-do writes, source retention, ambiguity notes, and ID/version read-back. `StoreTodoBatches` retains an idempotent operation receipt across restart. | Focused tests cover uploaded text, an actual brokered public-page read before proposal, denial, changed-source refusal, unresolved dates, deterministic replay, and recovery after interruption between item commits and the operation receipt. The compatible-provider test proves Luna receives only the frozen source/tool schema. | Add saved-note admission or explicitly constrain the first release to uploads/public links; run the packaged browser/manual workflow and paraphrase QA before marking verified. |
| G5 Conversational management | IN PROGRESS | `DelegationManagementConversation` gives Chat a bounded view of current jobs plus version-bound cancel and reminder-reschedule proposals. Listing is read-only. Exact approval is required before change; cancellation revokes the grant and rescheduling rotates its schedule authority. | Focused tests cover read-only listing, approval/denial, grant revocation, read-back, schedule/grant rotation, and refusal when a reviewed version becomes stale. | Add scheduled-email content editing and recurring-brief pause/edit after G1/G2 exist; add multi-match clarification/paraphrase and packaged UI checks. |
| C1 Natural-language entry | IN PROGRESS | Ordinary Chat advertises typed reminder, connected MCP, source-to-To-do, and current-job management tools from frozen host context; no workflow builder is required. | Provider/runtime tests cover exact frozen schemas, read-only job questions, cancellation, rescheduling, reminders, and reading-to-To-do entry. | Add multiple formulations and necessary clarification for email and recurring brief, including a short choice when more than one job plausibly matches, then packaged accessibility QA. |
| C2 Durable execution | IN PROGRESS | Schema 9 persists versioned jobs, grants, occurrences, UTC instants, timezone semantics, dispatch intent, next run and missed state; one host pump owns claims. Upcoming shows host availability and the awake requirement. | Controlled two-hour restart, no stale catch-up, weekday/DST, interruption recovery and no-replay tests pass. The packaged one-minute fixture restarted before due and dispatched once after restart. | Verify schema upgrade and repeat the native check against the final synchronized package. |
| C3 Real verified actions | IN PROGRESS | Approved source batches create actual library To-dos and verify every ID/version by read-back; connected calls and reminder occurrences retain provider/native receipts. | Combined public-page-to-To-do and upload-to-To-do tests prove the proposal itself is not treated as success and that changed sources cannot be approved. | Add scheduled email/provider receipts, brief read-back, and actionable credential setup states. |
| C4 Bounded delegation grant | IN PROGRESS | Reminder approval creates a persisted typed grant bound to owner, Windows account, payload, schedule, occurrence and budgets. Reading extraction has one exact source-version/item approval. Job changes bind the approval to ID, job version, schedule version and state; rescheduling rotates grant authority atomically. | Reminder approval/denial/race and management approval/denial/stale-version tests pass. To-do denial/source-change tests prove nothing is written outside the exact current approval. | Extend the durable grant contract to email and recurring brief scopes; add conversation-boundary tamper tests for those effects. |
| C5 Visible/recoverable failure | IN PROGRESS | Jobs/occurrences expose scheduled, working, needs-approval, succeeded, failed, unknown, missed and cancelled; action and notification outcomes are separate. Upcoming renders state, reason and unknown/no-replay guidance. | Missed, failed/unknown, cancellation and notification receipt paths have deterministic tests. | Add provider reconnect/review controls with the email/calendar slices and prove native notification refusal is actionable. |
| C6 Duplicate-effect safety | IN PROGRESS | Stable occurrence/operation IDs, atomic reminder claim plus pre-effect intent, authorization recheck, and UNKNOWN recovery prevent automatic replay. To-do item IDs derive from approval operation plus index; a missing batch receipt is recoverable only when every deterministic row still matches. | Duplicate reminder tick, cancellation race, uncertain outcome, crash-after-intent, To-do replay, mismatch refusal, and interrupted batch recovery tests pass. | Add provider-specific idempotency/reconciliation for email and verify restored backups cannot rearm outbound jobs. |
| C7 Clean receipts | IN PROGRESS | Approval and scheduling events link the originating run to job, grant, exact schedule, target and capability receipt. Occurrences retain provider/notification evidence. To-do batches retain source/item read-back evidence. Chat cancellation/rescheduling records canonical arguments, the changed job, and ID/version read-back; all are included in export. | Conversation tests prove reminder receipts survive restart, To-do execution is source-linked, and management changes produce no unreviewed mutation or credential exposure. | Add a human-readable occurrence detail modal in the Activity Ledger and verify replay/reconnect remains read-only. |
| C8 Visible/controllable work | IN PROGRESS | Log → Upcoming separates delegated work from planning check-ins and shows action, time/timezone, host status, state, latest result, unread control and versioned cancellation. Reminder, To-do, cancellation and reschedule approvals have human-readable cards with canonical JSON behind disclosure. | Production TypeScript/Vite build passes; controls and Chat management use version checks; review cards show the current and proposed time before approval. | Add pause/edit controls for recurring/email jobs, focused UI checks, and packaged responsive/manual QA. |
| R1 Preserve existing MVP | IN PROGRESS | Current live package includes Chat, apps, To-do, notes, search, Feed, uploads, history, backup/restore, and reviewed MCP. | `local-check-mcp-connectors-20260916-a`: 943 backend and 32 protocol tests plus production web build; current package UI was smoke-tested. | After delegation freezes, run normal release checks once and the established packaged/manual smoke without subjective redesign. |
| R2 Clean-user Windows path | BLOCKED | Current installer is an unsigned host-only preview; worker setup is separate and mail/calendar setup does not exist. | Prior native host installer evidence does not cover a clean Windows profile, included worker, or delegation. | Build synchronized candidate, document/setup actual dependencies, then verify from a fresh Windows profile with one real delegation. |
| R3 Freeze and handoff | IN PROGRESS | Current exact commit/package are known; no delegation candidate or submission bundle exists. | Baseline package and rollback are retained. | After G/C verification, create the exact release handoff, hashes, demo script/screenshots, verified limitations, and rules-grounded submission copy; do not publish. |

## Exact next implementation slice

Checkpoint G5 conversational list/cancel/reschedule, then implement G1's durable
exact-email job and provider boundary while leaving live delivery blocked on an
owner-authorized test account. Human-observe one reminder from the final
synchronized package; Shell acceptance alone is insufficient display proof.
