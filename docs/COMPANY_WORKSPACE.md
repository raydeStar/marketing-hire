# Chat, Work, and company meetings

The business cockpit opens directly into Chat. Work contains searchable records,
the task board, draft decisions, team directory, and the marketing brief. The
right sidebar switches between At a glance, All members, and Department
conversations. Meeting notes remain accessible in Work and Conversations.

## Current roles

- Marketing is the existing executing employee and owns the existing hire ledger.
- CEO chairs meetings, asks questions, challenges assumptions, and reviews the
  proposed plan against the meeting's saved company ethos.
- The Marketing planning role proposes actions in the meeting. CEO and planner
  have separate durable OpenClaw sessions and `tools.deny: ["*"]`.
- All three roles use the existing subscription route, `openai/gpt-5.6-luna`, with
  no model fallbacks. Additional directory entries are explicitly Setup needed;
  arbitrary department runtimes and extra meeting participants are not provisioned.

## Meeting lifecycle

1. The owner names the meeting, sets its agenda, and reviews the company ethos
   copied from the current brief's guardrails. These are saved meeting snapshots.
2. Start meeting asks the CEO for pointed questions. Sending a meeting message
   saves it and invokes one CEO response.
3. Develop plan & review invokes Marketing, validates a structured proposal, and
   asks the CEO to review the exact revision. Questions invalidate acceptance.
4. The CEO may approve routine internal work. The conservative initial policy is
   at most three small research/drafting actions using the existing subscription,
   with ten minutes estimated total work. Both roles must classify the plan as
   routine; missing or uncertain resource assessment requires owner approval.
   Larger plans, spending, new services, substantial compute, bulk work, and
   external actions require owner review. These are planning gates, not a billing
   meter or a mechanism for authorizing purchases.
5. Accepted routine plans wait two minutes for a veto, then close and release
   assignments. Resource-gated plans wait for **Approve plan & close meeting**.
   Changing the discussion clears prior approval. The owner can veto during
   discussion, review, the waiting window, or execution.
6. Closing creates real hire tasks with stable request IDs. The host dispatches
   one bounded turn per task, sequentially. Assignment dispatch checks that the
   task is still Ready and Employee can act; changing its status on the board
   prevents a pending turn. Each turn has the existing 600-second deadline.
   The host must stay running for the queue to advance.

A veto prevents pending dispatches. It cannot retract an already-running
OpenClaw turn; the UI says that the turn may still finish. Interrupted or
unconfirmed turns are marked unknown and remaining work pauses. A successful
turn is labelled `turn_complete`, not task completion: the hire task ledger and
saved evidence determine the actual outcome. Paused/unknown work can be inspected
and continued explicitly from its task conversation; it is not blindly retried.

Meeting acceptance authorizes only internal research/drafting. External posting,
messaging, spending, and recurring schedules remain outside execution scope.

## Persistence and routes

- Organization: authenticated GET/owner PUT `/api/organization`; versioned
  settings envelope includes retry receipts and survives existing store backup.
- Meetings: authenticated GET `/api/meetings`; owner POST `/api/meetings` and
  `/api/meetings/{id}`. Persistent transcript, plan revision, resource assessment,
  CEO decision, approval actor, veto, and task associations share one ledger.
- Existing Origin, session, CSRF, and owner checks apply to mutations.
- Chat history: GET `/api/marketing/history?before=<request-id>` pages 100 saved
  assistant replies at a time, including older replies beyond the state window.
- Source/task/draft records use the authoritative hire snapshot. Its existing
  limits are 1,000 tasks, 500 sources, and 100 drafts; the UI displays the limit
  notice when reached. Meeting notes are a separate saved archive in Work.

Reading views and changing task status do not invoke models. Starting a meeting,
sending a message, developing/reviewing a plan, and dispatching ratified work do.
There is no recurring meeting schedule or automatic outreach.

## Verification (2026-09-23)

- Frontend production build passed.
- Nine targeted backend tests cover directory persistence/validation, meeting
  retry receipts, CEO versus owner approval, veto windows and active turns,
  board pauses, resource assessment defaults, and restart/unknown recovery.
- Two Playwright tests cover record filtering and provenance, task controls,
  exact draft decisions, meeting creation/review/veto/archive, and phone width.
  UI tests use route fixtures against a disposable local host; no inference.
- The live agent container's configuration validates. Both planning roles show
  a usable subscription route, zero fallbacks, and wildcard tool denial.
- Container restart preserved six tasks and nine source records. No live
  meeting/model completion was run as a test; live end-to-end meeting generation
  remains unverified until the owner starts a real meeting.

The disposable host on port 5191 was stopped. Automatic approval review rejected
the cleanup command as "blocked by policy"; no deletion occurred. Its test store,
Playwright results, and test build outputs remain in place. Screenshot receipts,
the compact browser report, backend TRX, and cleanup receipt are retained under
`artifacts/` (gitignored). The live application on port 5189 remains running.
