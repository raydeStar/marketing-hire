# Chat, Work, and company meetings

The business cockpit opens directly into Chat. Work contains searchable records,
the task board, draft decisions, team directory, and the marketing brief. The
right sidebar switches between At a glance, All members, and Department
conversations. Meeting notes remain accessible in Work and Conversations.

## Business workspace design

- Chat uses a broad conversation canvas, a fixed composer, neutral charcoal
  surfaces, and a compact company panel. A neutral light palette is also supported.
- Drag the divider on the panel's left edge to resize it. Width is saved locally
  and constrained to preserve usable chat space. The divider also supports arrow
  keys and Home/End. The top-right panel button collapses and reopens it.
- On narrow screens the panel opens over the workspace and can be dismissed with
  its close button or backdrop. Chat and the panel scroll independently.
- **Start meeting** opens a separate CEO–Marketing meeting with a title and
  agenda. Company ethos is available in a disclosure. The meeting has its own
  transcript; it does not copy private direct messages.
  Additional participants still need runtime support before they can be invited.
- Meetings show Discuss / Propose / Review / Assign, plan revision, CEO review,
  open questions, an exact owner grant, saved output, and the owner's veto.
  A return recap derives decisions, output, blockers, and next actions from
  saved meeting and task state without another model call.
- Work opens the Kanban board. Its other primary views are Records, Activity,
  and Approvals. Team setup and Brief & ethos sit under Manage. Resource-gated
  accepted meeting plans appear in Approvals alongside draft reviews, with links to the
  full meeting and its controls.
- `paused` is now a persisted task status. Paused and completed work have separate
  expandable sections; paused work is excluded from the decision queue. On this
  workspace, the three explicitly held Framewright tasks were changed to Paused
  through versioned hire operations, preserving all six tasks and their history.
- Activity reads the latest 100 saved hire events, newest first, with expandable
  receipts and related-task links. It is a bounded ledger view, not a claim that
  every recorded action succeeded. Reading it invokes no model.

The redesign was verified with frontend and host builds, five isolated hire CLI
tests, and two browser scenarios. Browser coverage includes pointer resizing,
keyboard sizing, width persistence after reload, collapse/reopen, unsent drafts,
paused work, record provenance, activity, meeting setup/review/veto, resource
approvals, and desktop/phone layouts. Screenshots also cover the light palette.
The browser scenarios use explicit fixtures and invoke no live agent. The live
workspace separately confirmed its activity ledger and correct decision count.
Theme preferences now load in the application module because the host's existing
Content Security Policy blocks inline scripts; that policy remains unchanged.

## Current roles

- Marketing is the existing direct-chat employee and owns the hire ledger.
- CEO chairs meetings, asks questions, challenges assumptions, and reviews the
  proposed plan against the meeting's saved company ethos.
- The Marketing planning role proposes actions in the meeting. CEO, planner,
  and restricted meeting worker have separate OpenClaw sessions with
  `tools.deny: ["*"]`. The host retrieves two exact approved public source URLs
  and writes only the resulting local artifacts and scoped hire task updates.
- All roles use the existing subscription route, `openai/gpt-5.6-luna`, with
  no model fallbacks. Additional directory entries are explicitly Setup needed;
  arbitrary department runtimes and extra meeting participants are not provisioned.

## Meeting lifecycle

1. The owner names the meeting, sets its agenda, and reviews the company ethos
   copied from the current brief's guardrails. These are saved meeting snapshots.
2. Start meeting asks the CEO for pointed questions. Sending a meeting message
   saves it and invokes one CEO response.
3. Develop plan & review invokes Marketing, validates a structured proposal, and
   asks the CEO to review the exact revision. Questions invalidate acceptance.
4. CEO acceptance is advice. It cannot authorize work, including work it calls
   routine. The owner reviews the exact revision and digest, selects two existing
   source records, and grants a restricted evidence-brief then local-draft job
   for the personal-brand content pilot. The founder audience is provisional;
   this approval does not edit the saved marketing brief. Legacy or unclassified
   plans cannot be approved into this profile. The pilot
   reader is intentionally limited to exact public Hacker News item URLs already
   present in the source ledger; expanding source types needs a separate review.
5. An owner grant stores the actor, source scope, local destination, model route,
   expiry, ten-minute execution deadline, maximum two assignments and two worker
   dispatches, consumption receipts, and revocation. The host checks it before
   assignment and each dispatch. At most eight top-level model turns are allowed
   across the meeting and worker, with one plan revision. A top-level turn may
   cause more than one provider request; the host does not claim a hard bill cap.
6. Approval closes the meeting and creates hire tasks with stable request IDs.
   One restricted worker turn runs per task, sequentially, only while its task
   remains Ready and Employee can act. Each worker turn has a 120-second runtime
   limit. The evidence brief is saved as a meeting artifact; the local draft
   remains `needs_you` until the owner reviews and accepts it. The host must stay
   running for the queue to advance. The host checks that every cited evidence
   excerpt occurs in the retrieved source text before saving the brief.

A veto revokes the grant and prevents pending dispatches. The host requests
cancellation of an active turn, but its remote outcome may still be unknown.
Interrupted or unconfirmed turns are marked unknown and remaining work pauses.
An inaccessible source pauses before model drafting; a confirmed but invalid
output is marked failed. A confirmed reply alone does not imply a delivered
artifact. The host validates and saves structured output before marking an
action produced, then reconciles the hire task update using a stable receipt.
Task status, produced artifact, and owner acceptance remain separate facts.

Meeting acceptance authorizes only internal research/drafting. External posting,
messaging, spending, and recurring schedules remain outside execution scope.

## Persistence and routes

- Organization: authenticated GET/owner PUT `/api/organization`; versioned
  settings envelope includes retry receipts and survives existing store backup.
- Meetings: authenticated GET `/api/meetings`; owner POST `/api/meetings` and
  `/api/meetings/{id}`. Persistent transcript, revision and digest, CEO advice,
  owner grant, dispatch receipts, local artifacts, acceptance, veto, and task
  associations share one ledger.
- Existing Origin, session, CSRF, and owner checks apply to mutations.
- Chat history: GET `/api/marketing/history?before=<request-id>` pages 100 saved
  assistant replies at a time, including older replies beyond the state window.
- Source/task/draft records use the authoritative hire snapshot. Its existing
  limits are 1,000 tasks, 500 sources, and 100 drafts; the UI displays the limit
  notice when reached. Meeting notes are a separate saved archive in Work.

Reading views, recap, and task status do not invoke models. Starting a meeting,
sending a message, developing/reviewing a plan, and dispatching granted work do.
There is no recurring meeting schedule or automatic outreach.

## Previous verification (before Sprint 02)

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
