# Configurable marketing agent: local integration contract

## Campaign workflow checkpoint (2026-09-24)

The standing Marketing assignment and its saved artifacts remain the authority
for internal work. A `runway_campaigns` record attaches a versioned brief and
experiment rule to the exact audience-note ID and digest in that assignment.
The owner Work form sends the current project and campaign versions; a stale
write conflicts. The note must cite two exact quotes in two saved source texts.
The brief records audience, customer problem, hypothesis, proposition, desired
behavior, channel, primary metric and definition, a conduct guardrail,
explicit review timing, and additional non-goals. The fixed $0/no-publish
boundary, project scope, and metered allowance come from the project ledger;
editing brief text cannot expand them. Work projects the next action,
worker wait reason, and review timing separately from the campaign stage. The
experiment rule is recorded before observations. `learning_only` has no
continuation threshold. The Work view shows source URLs and capture time when
known; older sources retain an unknown capture time, and publication dates are
unknown unless separately established.

The campaign stage is separate from the worker's execution status. Saving or
editing the brief returns it to `align`, so a past approval never authorizes a
changed brief. No campaign edit creates a model turn, enlarges a grant, or
publishes. The owner-authenticated HTTP route is
`POST /api/marketing/runway/{id}/campaign-brief`; the installed old host must
be restarted with the new build before this route is available. The host saves
an independent owner receipt in its private `marketing-chat.sqlite` and marks
the current brief `owner_verified` only when its exact version, source, brief,
and rule match that receipt. A direct CLI actor field is not proof of owner
identity. Do not treat an unverified CLI-written brief as authorization for a
live action; the live action path remains unavailable.

An **isolated fixture** exercises the later stages through the same SQLite
ledger, using `fixture-seed`, `campaign-action`, and `campaign-lessons`. It
requires both `MARKETING_CAMPAIGN_FIXTURE=ISOLATED_TEST_ONLY` and `HIRE_STATE`
under the OS temporary directory. The host can be pointed at that disposable
ledger only with the explicit `Marketing:FixtureLedger` and
`Marketing:FixtureRunwayScript` settings. Its owner-only fixture HTTP routes
are unavailable in the normal host. The fixture sources use `fixture://` URLs
and no network fetch. Its `fixture` campaign mode cannot be changed to
`internal`. Alignment requires a fresh approval of the exact asset after the
latest brief revision. Launch accepts only `fixture://publisher` and persists
`SIMULATED_ONLY` with `external_effect=false`; there is no network publisher.
Measurement requires source, capture and period times, timezone, matching
metric definition, attribution limit, actual/estimated type, and nonnegative
counts. Observation IDs deduplicate imports. A minimum-sample rule counts only
actual denominators and forces `collect_evidence` while insufficient. A
learning-only rule cannot silently become a continuation threshold. A lesson
records context, uncertainty, revisit condition, decision ID, and next action;
retrieval uses the brief revision that produced it. It does not change skills,
permissions, or product facts. All fixture receipts remain inspectable after
reopen and visibly simulated in Work when the fixture snapshot is supplied.

The normal host exposes neither fixture action routes nor a live publish route.
CLI-authored alignment is fixture-only; it does not gain owner authority from
an actor field. The live publication boundary is closed. Isolated HTTP tests
exercise authenticated fixture routes and ledger persistence; a disposable
host browser test also exercises the full Work flow against those real local
routes. These do not prove a real publisher, real analytics, or a two-human
shared campaign.

The existing bounded employee work products have these completion contracts;
they are logical capabilities, not separate agents or mandatory model calls:

| Work product | Inputs and permitted capability | Completion and stop |
| --- | --- | --- |
| Audience note | Two host-checked restricted source texts, owner goal, existing product brief; internal drafting only | One provisional audience/problem, two exact quotes from distinct saved sources, evidence limits. Stop at missing/invalid sources or the project allowance. |
| Three angles | Saved audience note and checked source texts; internal drafting only | Three distinct angles, each with source URL, rationale, and claim limit. Stop at unsupported claims or failed validation. |
| Review packet | Exact prior artifacts and checked source texts; internal drafting only | Unsupported claims, summary, owner decision, and bounded next-step proposal. Stop for owner review; no automatic grant. |
| Campaign fixture | Exact owner brief, approved asset, fake publisher, manual synthetic observations | Check versions and sample rule mechanically; stop at missing approval, insufficient evidence, stale writes, or a proposed lesson. No live capability is attached. |

This product checkout is based on Thaddeus 2.0 revision
`7b3dda5d12a8abc842c3920a8e5038d7365a9768`. The copied agent inputs under
`business/agent/` come from `raydeStar/marketing-hire` revision
`76afaafd0a36657f605b42ab8a854e52446e941e`. The original checkouts are
read-only inputs to this sprint. There is no push remote in this product repo.

## One employee, one task authority

The selected employee is OpenClaw's `main` agent in the existing local Docker
state volume. Main Chat uses `agent:main:marketing-business-main`. A task
discussion uses `agent:main:marketing-task-<task-id>`, with a bounded summary of
that task. Switching views or selecting a task never sends an agent turn. The
browser never talks to the Gateway or SQLite directly.

The copied `hire` command owns marketing tasks in its existing
`/var/lib/plow/hire/hire.sqlite` database. Its `task create`, `task update`,
`task get`, and `task list` commands return JSON. The app host calls these
commands through fixed `docker exec` argument arrays. Agent turns call the same
commands using OpenClaw's allowed exec tool. The existing v1 activity feed is
retained, with additive `task` events. A successful mutation is committed before
the command returns JSON. Each mutation takes a caller-generated `request_id`;
repeating that ID returns the saved result without repeating the write.

Task fields: `id` (stable), `title`, `status` (`ready`, `working`, `needs_you`,
`done`), `priority` (`high`, `normal`, `low`), `next_action`, `action_state`
(`agent_ready`, `user_waiting`, `blocked`, `none`), optional `blocker`,
`conversation_key`, `version` (increasing integer), and `updated_at` (Unix
seconds). `next_action` and `action_state` drive the next-steps queue; the queue
has no separate store or automatic scheduler. Manual UI priority/status changes
use the same CLI operation. A single versioned marketing profile supplies the
working name, product summary, audience, goals, voice, channels and guardrails
to new agent turns. Other departments are future scope; this release does not
create employees for them.

Task evidence is stored as HTTPS source links with notes and request IDs.
The owner can attach a link from task detail in Work; the agent can use the same
ledger command. A stored link records a reference, not independent source
verification. The `pulse items` command exposes bounded stored candidates with
dates and snippets; they are leads to inspect, not evidence or demand counts.
Drafts expose their full text, destination, rationale, rules URL, revision and a
SHA-256 digest over the reviewable fields. The owner-only Work action records an
exact approve/reject decision; it never posts. The host saves an owner-session
receipt in its own private database before calling `hire draft decide`, then
confirms the receipt after the ledger returns. If transport outcome is unknown,
the same request ID can reconcile it. A draft status without a matching confirmed
host receipt is displayed as unverified.

## Browser API

All endpoints are under Thaddeus's existing authenticated, CSRF-protected
`/api/marketing` path.

| Method and path | Request / response |
| --- | --- |
| `GET /state` | `{employee, connection, canConfigure, taskStoreAvailable, profile, tasks, drafts, evidence, ownerDecisions, messages, requests}` snapshot. `employee` has `name`, `model`, `sessionKey`; `connection` has `status` (`connected`, `disconnected`, `auth_required`, `busy`, `failed`) and optional `detail`. `taskStoreAvailable` is false if the durable task command cannot be read. `ownerDecisions` comes from the host's private receipt database. The server reads the task authority and persisted chat mirror. No inference. |
| `POST /tasks` | Task create fields plus `requestId`; returns committed task. No inference. |
| `PUT /tasks/{id}` | Changed task fields, `version`, `requestId`; returns committed task or 409 on stale version. No inference. |
| `PUT /profile` | Owner-only complete brief fields, `version`, `requestId`; returns the committed profile or 409 on stale version. No inference. |
| `POST /tasks/{id}/evidence` | Owner-only source URL, title, note, query, source and `requestId`; returns stored evidence. No inference. |
| `POST /drafts/{id}/decision` | Owner-only `approved` or `rejected`, exact `revision`, `digest` and `requestId`; returns the recorded decision or a conflict. No inference or outbound send. |
| `POST /chat` | `{requestId, content, taskId?}`; invokes a real OpenClaw turn and returns `{requestId,status,reply,sessionKey}` only after a confirmed reply. The caller keeps the same request ID on an uncertain transport result and reconciles state before any retry. |

The app host records chat requests and confirmed replies in its private
`.data/marketing-chat.sqlite` for browser refresh. OpenClaw's own durable
session remains the model context authority. The host does not infer task writes
from reply text. Both Chat and Work consume the same `/state` task snapshot and
poll at a modest interval, replacing local state from the snapshot after a
reconnect. A failed agent call remains `failed` or `unknown`; it is never shown
as complete. Agent-unavailable state is explicit. One in-flight turn per
session is allowed. No UI read, view switch, task selection or manual task
mutation calls the model.

The local agent container is pinned to OpenClaw `2026.9.4 (3a9d69d)` and the
existing `marketing-hire:dev` image. Its startup template regenerates
`openclaw.json` and the workspace prompt; lasting prompt changes belong in the
copied `business/agent/prompt/AGENTS.md`, mounted at boot. Container and host
must both use loopback listeners. OAuth and model route are separately verified
in the sprint handoff.
