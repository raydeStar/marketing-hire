# Marketing manager: durable autonomy contract

**Decision, September 23, 2026:** The MVP has one Marketing employee. The owner is its manager for authority purposes, but the employee should show managerial initiative: find opportunities, choose useful internal work, keep a queue, challenge weak ideas, and bring reviewable proposals to the owner. The CEO meeting experiment is archived and paused.

## Current boundary after the bounded pilot

The local host now advances one explicitly enabled three-deliverable marketing project without further owner messages. It does not autonomously discover unlimited new assignments or run a general four-hour work window. A task merely marked `agent_ready` outside that project is still a saved suggestion, not an automatic dispatch. The CEO meeting experiment remains paused. See `docs/NEXT_SPRINT_HANDOFF.md` for the live receipt and `docs/MULTIPLAYER_AUDIT.md` for the separate multiplayer gap.

## Operating loop

The owner starts a **runway** with a goal, measurable deliverables, a fifteen-minute active-time ceiling, six admitted agent runs, a numeric token allowance, and an internal research/drafting scope. The host persists this grant before dispatching work. Marketing then repeats short, separately recorded steps:

1. **Observe and choose:** Ordinary code finds the first eligible dependency in the owner's three-step assignment; it never asks a model whether idle work exists.
2. **Do:** One bounded OpenClaw run produces one internal deliverable. The worker has no tools or external action surface.
3. **Critique:** The host checks the required shape, exact source quotes/URLs where applicable, and budget. Subjective quality stays for owner review.
4. **Checkpoint:** Save the run receipt, usage, artifact, task version, and next eligible step or stop reason in the existing `hire` ledger. A reply alone is not a completed task.

These are logical stages, not mandatory separate model calls. The host owns scheduling and durable state; OpenClaw owns each agent run. A browser tab staying open is not the scheduler. A host restart marks an in-flight turn **unknown** until it is reconciled; it must not blindly reissue that turn. Direct host Chat and runway dispatch share an execution gate, and task-version checks prevent stale results from overwriting the board. Stale profile or scope changes stop the run for review.

## Authority and stop rules

The default internal scope is public reading, local analysis, task updates, and local drafts. It never includes posting, DMs, email, purchases, account changes, calendar scheduling, or contacting people. Those require a separate exact owner grant. The owner can pause or veto a runway; an active remote turn may still finish, so the result must be shown as possibly late rather than silently discarded.

Time is a ceiling, not a success target. The run stops or asks for direction when the brief is too vague, source access is blocked, evidence is exhausted, proposals repeat, the turn/budget ceiling is reached, the owner changes the brief, or the deadline arrives. It should not produce filler merely to occupy the rest of four hours.

## Owner experience

Work shows the current runway, queue, active step, elapsed time, next wake, latest evidence, local drafts, proposals, and exact reason for any pause. The sidebar shows one concise status and decisions that actually need the owner. Chat remains a normal direct conversation with Marketing; scheduled manager turns are labeled as autonomous work, never as messages from the owner. The owner can revise the goal, pause/resume after reconciliation, reject an idea, or veto pending work.

## Acceptance evidence and remaining scope

Synthetic ledger and host fixture tests cover multiple steps, duplicate claims, stale task versions, pause, waiting input, missing usage, bounded failure, and restart unknown. One bounded live local pilot on the existing OAuth route saved three real artifacts and stopped for owner review with 8,618 reported tokens. This demonstrates continuation for one assigned project, not open-ended managerial initiative, native multiplayer, a quality threshold, or market demand. The next priority is a native identity-bearing shared conversation; see the audit.

**Implementation status:** One three-step pilot runway is built and running locally; generic direct Chat and work outside that assignment remain manual. No general four-hour autonomous campaign mode is promised.
