# Multiplayer and runtime audit — 2026-09-24 V2 local checkpoint

## Native identity bridge addendum — 2026-09-24

The earlier V2 native hold below is historical. The current local host and
private shared Gateway now bind each host-authenticated device to a permanent
Gateway proxy identity slot and its observed `users.self` profile GUID. The
cockpit is checkpointed on the private `raydeStar/marketing-hire` repository's
`business/marketing-hire` branch; the existing `main` branch is separate and
untouched. This is source control, not a public release or Plow submission. The
owner's saved campaign session is verified against the Gateway creator actor;
collaborator suggestions are verified against the authenticated author and
recorded in the canonical version-linked campaign discussion and project
ledger. Revocation still gates host access. The Gateway is private and
tool-denied; legacy native writes stay disabled.

This is **implemented and fixture-proven device-to-profile routing**, not a
claim that two real people met or that the employee replied in a shared chat.
The real HTTPS collaborator ingress is not configured on the persistent host.
The local owner path uses the host's private LAN address as a trusted-proxy
address after loopback-only owner authentication; it does not assert that the
browser came from that LAN address. The paired device test used an isolated
HTTPS fixture and two independent device sessions on one machine. Its native
receipt had a collaborator profile distinct from the owner creator; the
canonical input was idempotent and used zero model calls. See the first section
of `NEXT_SPRINT_HANDOFF.md` for test results and the precise human acceptance
script.

## V2 update: verified boundary versus remaining native gap

The V2 implementation is in the local `business/marketing-hire` checkout.
The owner route is live at `http://localhost:5189/`; Docker was not restarted.
See `NEXT_SPRINT_HANDOFF.md` for the entry point, screenshot list, test commands,
and short two-person acceptance script. Earlier evidence below is historical.

| Boundary | Verifier and current result |
| --- | --- |
| Authenticated human principal | Windows host `Security` validates the session cookie, expiry, revocation, Origin, and CSRF. A fixture-only campaign-scoped collaborator session is a test principal. Real paired devices remain distinct host principals; no second human was tested. |
| Campaign access | Host `campaign_memberships` binds a device ID to one campaign. Reads and writes recheck active membership; past membership keeps the device in restricted scope after revocation. Owner grants/revokes from the real access control. Unshared campaigns, private Chat/state, worker controls, and event subscriptions are denied to campaign-scoped devices. |
| Shared discussion → revision request | `campaign_shared_inputs` records the host-authenticated actor, exact selected artifact/digest and project version, request ID, and linked ledger input ID. It is the single active campaign discussion. Owner authorization uses the existing `ReviewRunway` exact-version path and private `owner_runway_reviews` receipt. The new UI never treats CLI actor names or a static Gateway profile as human approval. |
| Revision request → artifact lineage | The existing `hire.sqlite` grant/claim/finish and linked source-input machinery remains authoritative. Isolated fixture proof preserves the original input ID, predecessor, linked runway, exact approval, and owner-verified adoption. Live inference stays off and a request alone creates no grant. |
| Host principal → native profile | **BLOCKED.** `shared-rpc.cjs` still forwards static `owner@cockpit.local` or `collaborator@cockpit.local` via a local trusted proxy. Its profile GUID does not prove which signed-in human made a request. No trusted identity-bearing external ingress or second real participant is configured. New native conversation/suggestion writes are held; historical native records can still be read/reconciled. |
| Native session → campaign | **PARTIAL design, unverified live.** The legacy host session table contains a project/session key, but static proxy roles and session ownership do not enforce per-human capability isolation. The constrained shared Gateway has tools denied and explicit ownership; it is not exposed to the browser. A verified per-human profile binding plus campaign membership check at every ingress/event is the missing next contract. |

Installed `marketing-shared-hire` reports `OpenClaw 2026.9.4 (3a9d69d)`.
Version-matched `/app/docs/concepts/multi-user.md` says creator, owner,
participant history, presence, and visibility are not security boundaries when
people share one tool-capable agent. `/app/docs/gateway/trusted-proxy-auth.md`
requires an authenticating proxy that overwrites forwarded headers and is the
only ingress. The present host adapter has neither a unique verified human
mapping nor a supported secure external ingress. We did not connect a new
native session, trigger a model turn, contact a second person, or expose a
Gateway to the internet to conceal that gap. The host-only shared comments are
useful for a local review contract, but are **not native multiplayer proof**.

The repository's fresh core gate passed at
`artifacts/local-check-enterprise-multiplayer-final-20260924/` with 1,211 .NET
tests passed, one existing skip, plus protocol and web checks. The isolated
two-browser fixture path passed; it uses deterministic zero-model artifacts,
not a fresh worker revision. Owner read-only browser checks passed at 1440×900
and 1280×800 on the saved pilot. The previous gate label failed two older
paired-device tests; that regression was repaired by giving campaign-scoped
devices a restricted role while retaining established unrelated paired-device
routes. The persistent unknown request and 25,000-token reservation stayed
unchanged across the host-only restart. No external posting, spending, Plow
send, deployment, push, or Docker restart took place.

## Historical September 23 checkpoint

This is an audit of the running local checkout, not a hackathon compliance claim. The [organizer rules](https://luma.com/zhkhsnpa) require OpenClaw 2.0 multiplayer, a real job, public MIT code, official Agent Index usage reporting, and a demo of at least 60 seconds. No Plow deployment, public submission, Index registration, or external messaging occurred in this sprint.

## Evidence table

| Question | Verdict | Observed evidence and limit |
| --- | --- | --- |
| Running source/runtime | **VERIFIED locally** | `C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit`, branch `business/marketing-hire`, baseline commit `7cc6b0573fd29eda1afca9dbf2af03df4d858728` before the uncommitted sprint changes; no Git remote configured. `business/agent/compose.yml` runs `marketing-hire:dev` as `marketing-business-hire` with external `dev_state`, digest `sha256:6c4a3462a6f87f6f7dfaca3921aaba9629cb8c9ae47f3bdf8c07844fdfdb4e18`. Mounted `business/agent/boot-dev.mjs` is the lasting boot source. The installed CLI reports OpenClaw `2026.9.4 (3a9d69d)`; the Gateway and local host both responded during the pilot. |
| Model route | **PARTIAL** | OpenClaw resolves `openai/gpt-5.6-luna`, with a usable subscription/OAuth route and no configured fallback models. The installed `openai/*` subscription runtime documentation describes a Codex app-server harness, distinct from the provider label and model name. Older main-session records carry `agentRuntime.id=codex`; the three new worker receipts report OpenClaw `agentMeta.usage`, but do not independently persist the internal runtime ID or count provider HTTP retries. No paid API-key route was introduced. |
| Actual message path | **VERIFIED for local owner, PARTIAL for human attribution** | Branded Chat: `web/src/components/MarketingPanels.tsx` → `POST /api/marketing/chat` → `MarketingBackend.Chat` → container `openclaw agent --agent main` → Gateway/agent reply → `chat_requests` and UI. The host authenticates a device session and records actor ID/name locally. Its OpenClaw CLI call is a generic `gateway-client` participant; it does not carry the signed-in person's identity as a verified Gateway profile or channel sender. Runway dispatch is separate: `RunwayTick` claims an execution ID in the same `hire.sqlite`, then calls Gateway `agent` with `modelRun:true`, `agentId:runway-worker`, a stable idempotency key/session key, and stores a validated artifact. The three successful execution IDs are in the handoff. These are OpenClaw executions, not direct calls to an OpenAI model API from the host. |
| Installed multiplayer capability | **VERIFIED as available, PARTIAL as integrated** | Installed version-matched OpenClaw `docs/concepts/multi-user.md` describes creator, assignable owner, participant history, presence, and shared sessions; the [upstream multi-user documentation](https://docs.openclaw.ai/concepts/multi-user) agrees. A verified Gateway profile or authenticated channel sender must carry human identity. The same documentation warns that everyone who can operate one agent can use all of its capabilities: ownership and sidebar visibility are not permission boundaries. `agents.ownership="explicit"` is now set in boot config, but that setting alone does not create distinct humans or a shared conversation. |
| Two real people in one intended conversation | **BLOCKED as an acceptance demonstration** | No second authenticated human, identity-bearing Gateway ingress, or authorized shared channel was available locally. No two-human run was conducted. The host has local fixture tests for a nonowner session, signed-in project input, and owner-only controls. These prove the host API boundary under fixtures; they do not prove OpenClaw's participant history or two-person reply delivery. |
| Branded UI preservation | **PARTIAL** | Work exposes a shared project note surface with session-derived attribution; ordinary participants cannot start/pause/resume the runway, control owner approvals, use the generic full-tool Marketing Chat, read private Chat history, or see owner drafts/decision receipts through Marketing endpoints. The branded app still does not implement identity-bearing Gateway shared-session ingress, event streaming, or membership synchronization. The current local owner Chat cannot be counted as multiplayer. |
| Submission gates | **PARTIAL/BLOCKED** | Internal OpenClaw run and saved marketing work: verified. `business/agent/hire/LICENSE` covers listed marketing additions under MIT; there is no repository-wide MIT release/public remote. `business/agent/HIRE.md` has distribution notes, but no public runnable package/release has been verified. Agent Index client reporting is absent from this local Docker route and no usage was transmitted. No 60-second demo video or public Index listing exists. These are distinct outstanding gates. |

## Why the current mode remains PARTIAL

One owner key shared between browsers would still identify one principal. Adding local actor names to the host ledger improves auditability but does not register OpenClaw participants. Giving a collaborator the current `main` agent would also expose its tool authority; the [native trust model](https://docs.openclaw.ai/concepts/multi-user) explicitly says ownership is not isolation. The next integration must use a supported identity-bearing Gateway profile endpoint or an authenticated shared channel, with a constrained agent/tool scope for participants. The [Gateway integration guide](https://docs.openclaw.ai/gateway/external-apps) identifies WebSocket Gateway RPC plus `sessions.*`, `agent`, `agent.wait`, and events as the supported external-app path. Pin and verify against this installed package before adapting the branded UI. A Plow group thread is another potential channel surface but was deliberately deferred by the owner and was not enabled.

## Exact local acceptance script for two people (NOT RUN)

1. On a supported identity-bearing Gateway/Control UI or authenticated shared channel, sign in as the owner and as a separate collaborator. Record the two Gateway profile IDs or channel sender IDs without publishing credentials.
2. Owner opens a shared Marketing employee session and delegates one small campaign constraint, referencing the saved runway/project ID. Verify the Gateway records the owner's creator/participant identity and the employee's reply in that same session.
3. Collaborator opens that same session under their own identity, adds a concrete revision constraint, and asks the employee to incorporate it. Verify the Gateway participant history has two distinct human identities, the reply lands in the same session, and the project input/next eligible artifact retains the collaborator's attribution.
4. Attempt pause, budget change, approval, and access to owner-only material as collaborator. Each must be denied by the relevant host or agent/tool API. Verify owner controls still work.
5. Capture sanitized session/run IDs and transcript excerpts as a receipt. Until these checks run, report multiplayer **PARTIAL**, not VERIFIED.

## Read-only evidence references

- Local boot and boundaries: `business/agent/compose.yml`, `business/agent/boot-dev.mjs`, `src/Thaddeus.Host/MarketingBackend.cs`, `src/Thaddeus.Host/MarketingRunway.cs`, `src/Thaddeus.Host/MarketingEndpoints.cs`.
- Version-matched installed OpenClaw docs: `/app/docs/concepts/multi-user.md` and `/app/docs/concepts/agent-runtimes.md` inside `marketing-business-hire`.
- Official [organizer rules](https://luma.com/zhkhsnpa), [OpenClaw multi-user mode](https://docs.openclaw.ai/concepts/multi-user), [Gateway external-app integration](https://docs.openclaw.ai/gateway/external-apps), and [Agent Index reporting client](https://github.com/plow-pbc/agent-index-client).
