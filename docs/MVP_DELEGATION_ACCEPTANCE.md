# Windows delegation MVP acceptance

This is the single acceptance matrix for the September 16 delegation finish-line
contract. `VERIFIED` means the named behavior has current end-to-end evidence at
the scope of the requirement. Implementation, a mock-only result, or an older
partial receipt does not qualify. Update this file after each narrow slice so the
next action remains unambiguous.

## Environment checkpoint

- Checkout: `master`, commit `fc1ad81a7f05c338f42917fb1d5c6ebbad192695`,
  clean and equal to `origin/master` at the start of this audit.
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
| G4 Reading to real To-dos | IN PROGRESS | Supplied-link reading and editable To-dos exist separately. | Existing scoped website-reading and To-do CRUD/read-back checks; no combined extraction/approval/deduplication workflow. | Add a bounded batch To-do proposal from supported reading with source references, ambiguity reporting, one approval, idempotent read-back, and paraphrase tests. |
| G5 Conversational management | NOT STARTED | No automation list/edit/pause/cancel tools or commitment cards exist. | Existing run cancellation does not manage future jobs. | Add persisted lookup plus unambiguous cancel, reschedule, email edit, and recurring pause; bind changes to job/grant versions. |
| C1 Natural-language entry | IN PROGRESS | Ordinary Chat already supports model tools, clarification, background turns, and follow-up history. | Provider/tool tests cover frozen MCP schemas; none cover delegation paraphrases or commitment cards. | Add typed delegation tools to Chat, multiple formulations for G1-G5, necessary clarification, and accessible commitment cards. |
| C2 Durable execution | IN PROGRESS | Schema 9 persists versioned jobs, grants, occurrences, UTC instants, timezone semantics, dispatch intent, next run and missed state; one host pump owns claims. Upcoming shows host availability and the awake requirement. | Controlled two-hour restart, no stale catch-up, weekday/DST, interruption recovery and no-replay tests pass. The packaged one-minute fixture restarted before due and dispatched once after restart. | Verify schema upgrade and repeat the native check against the final synchronized package. |
| C3 Real verified actions | IN PROGRESS | Existing writes and MCP results produce receipts and some read-back. | To-do/page writes have scoped tests; no scheduled provider receipt, brief read-back, or native notification evidence. | Require provider/native receipts per occurrence and actionable setup states for missing credentials. |
| C4 Bounded delegation grant | IN PROGRESS | Reminder approval now creates a persisted typed grant bound to owner, Windows owner account, operation, exact payload hash/target, schedule version, one occurrence, expiry, and external/model budgets. The scheduler rechecks it immediately before dispatch and cancellation revokes it. | Approval/denial, cancellation-before-due, cancellation race, and exact persisted grant tests pass. | Extend the same broker-enforced grant contract to email and recurring brief scopes; add tamper tests at the conversation boundary. |
| C5 Visible/recoverable failure | IN PROGRESS | Jobs/occurrences expose scheduled, working, needs-approval, succeeded, failed, unknown, missed and cancelled; action and notification outcomes are separate. Upcoming renders state, reason and unknown/no-replay guidance. | Missed, failed/unknown, cancellation and notification receipt paths have deterministic tests. | Add provider reconnect/review controls with the email/calendar slices and prove native notification refusal is actionable. |
| C6 Duplicate-effect safety | IN PROGRESS | Stable occurrence/operation IDs, atomic claim plus pre-effect dispatch intent, authorization recheck, and UNKNOWN recovery are implemented. Successful/uncertain actions are never auto-replayed. | Duplicate tick, cancellation race, provider-uncertain outcome and crash-after-intent tests pass. | Add provider-specific idempotency/reconciliation for email and verify restored backups cannot rearm outbound jobs. |
| C7 Clean receipts | IN PROGRESS | Approval and scheduling events link the originating run to job, grant, exact schedule, target and capability receipt; occurrences retain provider and notification evidence and are included in export. | Conversation tests prove the persisted scheduling receipt returns to the final model turn; scheduler tests prove provider evidence survives restart. | Add a human-readable occurrence detail modal in the Activity Ledger and verify replay/reconnect remains read-only. |
| C8 Visible/controllable work | IN PROGRESS | Log → Upcoming now separates delegated work from planning check-ins and shows action, target time/timezone, host status, state, latest result, unread control and versioned cancellation. Exact reminder approval has a human-readable card with technical JSON behind disclosure. | Production TypeScript/Vite build passes; backend cancellation/read operations use version checks. | Add pause/edit/reschedule for recurring/email jobs, focused UI checks, and packaged responsive/manual QA. |
| R1 Preserve existing MVP | IN PROGRESS | Current live package includes Chat, apps, To-do, notes, search, Feed, uploads, history, backup/restore, and reviewed MCP. | `local-check-mcp-connectors-20260916-a`: 943 backend and 32 protocol tests plus production web build; current package UI was smoke-tested. | After delegation freezes, run normal release checks once and the established packaged/manual smoke without subjective redesign. |
| R2 Clean-user Windows path | BLOCKED | Current installer is an unsigned host-only preview; worker setup is separate and mail/calendar setup does not exist. | Prior native host installer evidence does not cover a clean Windows profile, included worker, or delegation. | Build synchronized candidate, document/setup actual dependencies, then verify from a fresh Windows profile with one real delegation. |
| R3 Freeze and handoff | IN PROGRESS | Current exact commit/package are known; no delegation candidate or submission bundle exists. | Baseline package and rollback are retained. | After G/C verification, create the exact release handoff, hashes, demo script/screenshots, verified limitations, and rules-grounded submission copy; do not publish. |

## Exact next implementation slice

Human-observe one reminder from the final synchronized package; Shell acceptance
alone is insufficient display proof. Continue independent work with G4: turn one
supported reading into a deduplicated batch of source-linked editable To-dos under
one exact approval, then record read-back evidence in the originating run.
