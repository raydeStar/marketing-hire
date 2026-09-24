# Marketing manager: durable autonomy contract

**Decision, September 23, 2026:** The MVP has one Marketing employee. The owner is its manager for authority purposes, but the employee should show managerial initiative: find opportunities, choose useful internal work, keep a queue, challenge weak ideas, and bring reviewable proposals to the owner. The CEO meeting experiment is archived and paused.

## Current gap

The local app sends one OpenClaw turn only when the owner sends a chat message. A task marked `agent_ready` is a saved suggestion, not an automatic dispatch. The old meeting worker could run at most two sequential approved tasks, with a ten-minute deadline. No general four-hour marketing work window exists in this product. A long timeout cannot make one agent turn invent its own next job. Stopping after the first brief is expected from the present control flow, not evidence that four hours of useful work were attempted.

## Operating loop

The owner starts a **runway** with a goal, a time ceiling (up to four hours for the first pilot), a model-turn ceiling, and an allowed action scope. The host persists this grant before dispatching work. Marketing then repeats short, separately recorded cycles:

1. **Observe:** Read the current owner brief, approved context, task board, prior proposals, and bounded public evidence. Identify what changed and what is still unknown.
2. **Choose:** Select one useful next internal step from a persistent backlog, or propose a new idea with a hypothesis, audience, expected learning, and evidence needed. Deduplicate against prior attempts.
3. **Do:** Carry out one bounded internal step, such as a source check, evidence brief, local draft, or comparison. Save the artifact and task state in the existing `hire` ledger. A reply alone is not a completed task.
4. **Critique:** Check the result against the brief, source quality, claim limits, and previous work. Record whether to continue, revise, park, or bring a decision to the owner.
5. **Checkpoint:** Save the action receipt, artifact links, next step, model outcome, and next wake time. Release the current turn. The host wakes the next cycle while the runway remains valid.

The host owns scheduling and durable state; OpenClaw owns each model/tool turn. A browser tab staying open is not the scheduler. A host restart marks an in-flight turn **unknown** until it is reconciled; it must not blindly reissue that turn. Only one manager cycle runs at a time. Stale profile or scope changes pause the run for review.

## Authority and stop rules

The default internal scope is public reading, local analysis, task updates, and local drafts. It never includes posting, DMs, email, purchases, account changes, calendar scheduling, or contacting people. Those require a separate exact owner grant. The owner can pause or veto a runway; an active remote turn may still finish, so the result must be shown as possibly late rather than silently discarded.

Time is a ceiling, not a success target. The run stops or asks for direction when the brief is too vague, source access is blocked, evidence is exhausted, proposals repeat, the turn/budget ceiling is reached, the owner changes the brief, or the deadline arrives. It should not produce filler merely to occupy the rest of four hours.

## Owner experience

Work shows the current runway, queue, active step, elapsed time, next wake, latest evidence, local drafts, proposals, and exact reason for any pause. The sidebar shows one concise status and decisions that actually need the owner. Chat remains a normal direct conversation with Marketing; scheduled manager turns are labeled as autonomous work, never as messages from the owner. The owner can revise the goal, pause/resume after reconciliation, reject an idea, or veto pending work.

## Acceptance gate

First use a fake clock and scripted model/ledger fixtures to prove multiple cycles over a four-hour simulated window, a restart between cycles, deduplication, owner pause/veto, no external actions, and truthful unknown-turn recovery. Then run a bounded live local pilot on the existing OAuth route with no publication or spending. Count saved evidence, distinct ideas, usable drafts, and honest stop reasons. Do not call the feature autonomous on the basis of a long timeout or one finished turn.

**Implementation status:** Contract only. The durable runway controller, autonomous dispatch, and owner controls are not built or running yet. The existing direct chat and task board remain manual.
