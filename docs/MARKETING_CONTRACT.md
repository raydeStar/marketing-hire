# Configurable marketing agent: local integration contract

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

Task evidence is stored as public HTTPS source links with notes and request IDs.
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
