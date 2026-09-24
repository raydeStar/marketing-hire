# Marketing employee continuation — local handoff

Checkpoint: 2026-09-23, `business/marketing-hire` in `C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit`. Read [MULTIPLAYER_AUDIT.md](MULTIPLAYER_AUDIT.md) before claiming hackathon readiness. The owner deferred Plow and meetings. The product is one marketing employee for a personal brand selling configurable marketing agents. The three-step pilot is internal learning, with no continuation threshold or owner-time cap specified.

## Verdict

- **OpenClaw:** `2026.9.4 (3a9d69d)` in `marketing-business-hire`, image `marketing-hire:dev` at `sha256:6c4a3462a6f87f6f7dfaca3921aaba9629cb8c9ae47f3bdf8c07844fdfdb4e18`. Displayed route `openai/gpt-5.6-luna`; subscription/OAuth runtime auth usable; fallback model list empty. The installed `openai/*` subscription docs identify the Codex app-server runtime, but this pilot did not persist the worker's internal runtime ID independently.
- **Multiplayer: PARTIAL.** Native OpenClaw multi-user support is installed and documented. The branded ingress still uses a generic CLI Gateway participant, and no two-human shared conversation was demonstrated. Host device-session attribution and fixture permission tests are narrower evidence.
- **Continuing work: PASS for one bounded local project.** One owner assignment caused three dependent OpenClaw Gateway model runs and three saved, validated artifacts, then stopped at `needs_review` without another owner message. This is a single-user execution pilot, not multiplayer acceptance.

## Implemented checkpoint

- `business/agent/hire/bin/runway.py` adds a standing project and three linked tasks to the existing `hire.sqlite`, with measurable deliverable criteria, cached source excerpts, artifact hashes, idempotent request IDs, attributed project inputs, atomic execution claims, task/project version fences, receipts, pause/resume, bounded repairs, and honest unknown/rejected recovery. The current scope is `internal_research_draft`; this loop has no publishing, messaging, purchasing, or arbitrary research tools.
- `business/agent/boot-dev.mjs` pins a tool-denied `runway-worker` with `maxTokens:1800` and preserves the existing main agent. The local model route and empty fallback list remain intact. The pilot uses `openclaw gateway call agent` with `modelRun:true`, a stable execution/session/idempotency key, and no tool access. Observe/choose/execute/critique/checkpoint are logical work stages; they do not each trigger a model call.
- `src/Thaddeus.Host/MarketingRunway.cs` and `MarketingBackend.cs` bridge the host to the ledger and Gateway. A host timer checks the ledger every ten seconds using ordinary code. One shared in-process execution gate coordinates direct Chat with runway dispatch, and durable task versions fence stale results. The host immediately claims the next dependent step after a confirmed result. A restart changes an in-flight claim to `unknown`; it does not replay it. An uncertain transport or save outcome stops admission.
- Chat/Work: Work shows goal, steps, waiting reason, remaining admission allowance, artifacts, run receipts, and owner pause/resume. Signed-in people may add an attributed project constraint for the next eligible step; only the owner may start/pause/resume or approve. The generic main-agent Chat, Chat history, full task board, drafts, and decision receipts are owner-only. Nonowner Work state is projected to shared project fields. This local projection is not native Gateway multiplayer integration.

## Live pilot receipt (persistent state; do not replay)

Project `91c4b1df1e6942e2a986936127b37742`: personal-brand campaign packet for configurable marketing agents. Provisional audience: founder-led small businesses / solo technical founders. Two restricted, cached public sources were [HN item 47667504](https://news.ycombinator.com/item?id=47667504) and [HN item 49703771](https://news.ycombinator.com/item?id=49703771). They are anecdotal evidence, not demand or ROI proof.

| Step | OpenClaw execution ID | Reported total tokens | Saved artifact ID | Result |
| --- | --- | ---: | --- | --- |
| Audience/problem note | `110325ba1fde4ca8a5dd112e887a16f1` | 2,585 | `7d6da97b402b4a229b86a768a8b55665` | Provisional audience/problem, two checked short quotes, evidence limits. |
| Three draft angles | `379af7cad9f84f428031754d2f1c6563` | 2,901 | `b5ec5b744b1847dab06a239c14712dfd` | Control, post-launch follow-up, and avoiding generic automation; each linked to a source and claim limit. |
| Owner review packet | `cd3df3348c094f808db4f8e61295ed04` | 3,132 | `2b2fc05d68bd410381ffaea7d9b938fd` | Unsupported claims and next decision: choose or revise the provisional audience and one angle. |

Total reported by OpenClaw `agentMeta.usage`: **8,618 tokens** over **three confirmed successful Gateway model runs**, roughly 25.2 seconds of their recorded wall time. A fourth claimed execution, `ac2b12ebc39d47af8c4adb8aaff2ef6a`, was rejected by Gateway `INVALID_REQUEST` before inference because an explicit provider/model override was unauthorized. Gateway logs and zero worker sessions supported reconciliation as pre-admission; its 25,000-token reservation was released, and the rejected receipt remains. Its ledger elapsed time includes manual investigation and is not model runtime. Project `run_count=4`, `token_reserved=0`, `token_used=8618`, three artifacts, status `needs_review`, reason “All deliverables saved; owner review needed.” No output was posted or sent externally.

Budget: six **top-level** admitted runs; at most 900 seconds summed ledger execution time; two repair attempts after the first failure per step; 150,000-token aggregate **admission** allowance with 25,000 reserved before each run. Reported usage replaces the reservation after a confirmed outcome. If usage is missing, the reservation is charged conservatively and further admission stops. The worker's `maxTokens:1800` limits generated output. These are not a metered hard cap on underlying provider HTTP requests/tokens mid-turn: internal retries and exact provider request count are not independently exposed in this receipt. No planner or CEO model calls were used.

## Verification and remaining risks

- Synthetic Python ledger tests cover dependent advancement/quiet state, duplicate claim, task-version fencing, pause, restart unknown, failed deliverables, missing usage, attributed input, waiting input wake, and exact source URL shape. C# fixture tests cover denied nonowner controls and deliverable validation. Frontend production build compiles. The fixture principals are **not** real multiplayer users.
- Pause prevents new admission. It currently does not send a Gateway cancellation request for a turn already running; the UI says that active work may still finish. A host-level gate does not cover a separate native Control UI/channel writer; connect those paths through Gateway session claims or keep the runway worker separate and tool-denied. A physical host crash during a model run remains `unknown` until the original Gateway/session receipt is reconciled; no blind retry.
- The fixed source reader checks HTTPS HN URLs, DNS, response size, and redirects. Sources are cached per project. The validator checks format, quote substrings, source URLs, and distinct angle titles; subjective quality and audience fit remain for owner review. There is no measured continuation threshold or validated market demand.
- `docs/MULTIPLAYER_AUDIT.md` contains the exact two-person acceptance script. No identity-bearing Gateway endpoint/shared channel, second participant, or real-human transcript is available in this checkpoint.
- Routine cleanup of disposable `artifacts/runway-build` (~63 MB) and `artifacts/runway-test-build` (~83 MB) was attempted after verifying the targets were inside this checkout's `artifacts` directory. Automatic approval review rejected the recursive deletion. The directories remain; no alternate deletion path was used.

## Next highest-priority action

Make **one native shared conversation** for the one marketing employee. Use OpenClaw 2026.9.4's documented identity-bearing Gateway profile connection or an authenticated shared channel, not a second owner-key browser. Preserve the tool-denied worker and owner-only authority boundary. Map the shared session to this project; verify owner and collaborator as distinct native participants, one reply destination, attributed project constraint, and denied collaborator budget/approval controls. Only then change multiplayer to VERIFIED. The short acceptance script is in the audit.

## Submission gates at this checkpoint

| Gate | Status |
| --- | --- |
| Required native multiplayer with two real humans | **PARTIAL / acceptance NOT RUN** |
| Real startup work by OpenClaw | **LOCAL PASS** for one bounded draft/research project; no adoption evidence |
| Public MIT code | **NOT DONE**; marketing addition has `business/agent/hire/LICENSE`, no public remote/release or repo-wide MIT audit |
| Agent Index listing and official client usage reporting | **NOT DONE**; local usage receipts are not official reporting |
| Demo video at least 60 seconds | **NOT DONE** |

Do not deploy to Plow, publish, register, transmit reporting, or make external contact from this handoff alone.
