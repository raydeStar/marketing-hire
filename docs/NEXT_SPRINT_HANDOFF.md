# Marketing employee continuation — local handoff

## Current checkpoint: 2026-09-24 campaign loop sprint

This section supersedes the September 23 checkpoint below. Checkout:
`C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit`, branch
`business/marketing-hire`, starting commit `f2b5554`. The existing data,
Docker volumes, three historical pilot artifacts, and unknown newer request
were preserved. No model call, publication, spend, deployment, Plow send,
remote push, or Docker volume cleanup occurred in this sprint.

### What is usable now

- Owner Work has a campaign brief and experiment rule form tied to the exact
  saved audience note, with project/campaign version conflicts. The form
  records a hypothesis, primary metric, review timing, and additional non-goals
  without granting execution. Work now separates the next campaign action,
  worker wait reason, and next review timing from the campaign stage, while
  displaying the ledger's scope, $0/no-publish boundary, and worker allowance. The
  new host stores a private owner receipt and displays whether the current
  brief matches it; a direct CLI actor field does not verify ownership.
- The same `hire.sqlite` runway ledger stores brief revisions, source capture
  metadata (unknown for legacy sources), campaign stage, fixture actions and
  versioned receipts. The Work view shows source provenance and action history.
  A linked revision assignment now carries up to eight relevant source project
  inputs with original actor IDs and source-input links, so a recorded native
  collaborator constraint can reach the authorized revision worker packet.
- After an exact linked revision is approved, the owner can select it for the
  source campaign in Work. The ledger checks the released grant, predecessor,
  source and revision versions, exact approval, and internal-only campaign.
  The host records a private review and selection receipt. The original draft
  and both review histories remain inspectable; a later brief edit makes the
  selection historical until the owner reaffirms it. Selection cannot launch.
- A disposable fixture ledger can progress through fresh exact-draft alignment,
  an owner-requested simulated asset revision with predecessor and review
  lineage, a fresh exact approval,
  a fake publisher receipt, deduplicated observations, insufficient-sample
  waiting, a decision, and a contextual proposed lesson. Editing the brief
  returns it to alignment. Fixture lessons can be retrieved by a later brief
  with their original context and uncertainty. The fixture-only host mode
  exposes owner-authenticated seed/action/lesson routes against a temp ledger,
  and Work has controls for the entire simulated journey.
- Live publishing remains unavailable. A saved creative approval is still only
  approval for internal use. No external analytics or launch connector is
  claimed. Owner Work now has a validated manual observation path for an
  existing host-verified internal brief; it records an owner-attested source,
  period, counts, interpretation, and limitations without advancing launch or
  claiming causality. No actual owner observation has been entered. The owner
  brief and observation routes in the new Release build have not yet been
  exercised against the actual persistent host.
- A host-verified owner observation can now support an internal decision and
  proposed lesson in Work. The host chooses the verified observation IDs;
  the ledger applies the saved sample or learning-only rule, requires new
  evidence after collect-evidence, and records a decision rationale and
  contextual lesson. Continue does not release a worker or a launch. A later
  observation reopens alignment without erasing the prior decision.
- A later internal brief can read prior proposed lessons for its saved audience.
  Work shows the original context, decision, uncertainty, revisit condition,
  and observation references. The owner-only read checks host-private receipts
  for the lesson, decision, and source observations, excludes the current
  campaign, and never changes policy or starts a model turn.
- Work shows a blocked live-launch checklist for the internal campaign. The
  owner can record a capability request with the blocked task, exact
  destination/action scope, expected benefit, and cost status. It changes no
  capability, budget, or stage.

### Verification and limits

| Gate | Result | Evidence or limit |
| --- | --- | --- |
| Ledger workflow tests | **PASS** | `python -m unittest discover -s business/agent/hire/tests -p 'test_runway.py' -q`: 43 tests, including historical internal lesson retrieval with audience and campaign exclusion, owner-only decision/lesson, request-only capability record, insufficient sample, new-evidence waiting, linked revision selection, stale/duplicate guards, and additive migration. |
| Release host build and focused tests | **PASS** | `dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MarketingRunwayTests --nologo -v:q`: 10 pass. Isolated HTTP retrieves a prior lesson, filters an unreceipted forged row, and consumes zero new model tokens. |
| Web build and browser fixture | **PARTIAL** | `npm --prefix web run build` passes. Earlier intercepted-response browser tests passed. The expanded observation/decision/lesson browser test now includes prior-lesson display and is discovered by Playwright, but cannot run while the local host is absent. A separate disposable Release host on `localhost:5190` previously passed `campaign-fixture-live.spec.ts` (1 test) against actual fixture HTTP routes; that run predates the newer internal paths. |
| New route on loaded persistent host | **WAITING FOR OWNER START** | Port 5189 is free after the owner ran the stop script. Codex built the current Release host, but automatic approval review rejected a background `Start-Process` launch as `blocked by policy`. The owner was asked to run `./scripts/start-marketing.ps1` from this checkout. The new persistent-host routes have not yet been exercised. |
| Native shared gateway integration | **PARTIAL** | Earlier local routing and attribution controls remain. Linked revision claims now carry source-input provenance, but no new live Gateway revision was run. Read `MULTIPLAYER_AUDIT.md`. |
| Two independent humans | **NOT RUN** | Needs secure ingress and a second real person; multiple tabs or fixture principals do not count. |
| New live inference / campaign publication | **NOT RUN / DISABLED** | Meter v5 was not ready at baseline; no fresh spending bound was established. Publication has no live route. |

The September 24 linked-adoption seam adds owner-only
`POST /api/marketing/runway/{id}/campaign-adopt-revision`, exact linked
artifact checks in `hire.sqlite`, and a host-private review and selection
receipt. It does not grant a model turn, publishing, or launch. The isolated
ledger test and host receipt tests pass; the browser test is authored but
has not run while port 5189 is empty.

The internal decision seam adds owner-only
`POST /api/marketing/runway/{id}/campaign-internal-action` for a decision and
contextual proposed lesson. Its host-private receipts mark exact saved
actions; the ledger preserves source observation IDs, saved rule, actual sample,
asset/brief versions, no-launch status, and the lesson's decision ID. A new
observation after completion reopens alignment. This is local implementation
and isolated HTTP evidence, not a claim that a real campaign produced an
outcome. The browser interaction is authored but not yet executed.

The contextual retrieval seam adds owner-only
`GET /api/marketing/campaign-lessons?audience=...&excludeCampaignId=...`.
The ledger reads the historical brief revision, exact decision, and observation
references. The host checks its private receipts for all three kinds of action
before showing the proposed lesson in a later Work brief. It does not import
fixture learning, alter company policy, or authorize a new worker grant.

The blocked launch checklist names missing live link/tracking,
destination/rollback, and publisher capability even when a creative draft has
internal approval. An owner-only capability request records the task, scope,
expected benefit, and cost status with a private receipt. It grants nothing.

The September 24 manual-observation seam adds the owner-only
`POST /api/marketing/runway/{id}/campaign-observation` route, an additive
host-private exact-action receipt, and the Work form. The 42 Python tests pass
including internal observation validation, deduplication, and non-progression;
10 focused .NET tests pass including a successful isolated internal-mode HTTP
save/reopen, fixture-route denial, and private receipt projection; the new
intercepted-response browser test passes the Work save/reopen path. A successful
observation against the persistent normal host is still **NOT RUN** while the
new host is not started. Do not treat a CLI actor field or an owner-entered
source reference as independently verified real-world evidence.

The brief/Work follow-up checks on September 24 passed: 42 Python tests,
9 focused .NET tests, and both intercepted-response browser tests. Required
brief fields are covered by validation and browser save. The fixture journey
now includes an explicit revised asset and fresh owner review; the asset action
is simulated and consumes zero model requests. Real native shared revision
acceptance remains unproven. The earlier disposable-host browser run predates
the revision addition; current revision coverage is in the .NET HTTP fixture
test and intercepted-response browser test.

The linked-revision fixture test now also saves a collaborator-labeled source
input after the original asset and verifies its actor, content, and original
input ID survive into the released revision claim. This is ledger lineage
coverage, not proof of a second human or Gateway identity in this run.

The full fixture journey reaches owner-authenticated fixture HTTP routes and
the real Python ledger in a disposable temp directory. Work has the matching
fixture controls, browser tested both with intercepted responses and against
the disposable host's real HTTP routes. The normal campaign brief
uses an authenticated owner HTTP route in the new host build; the rebuilt host
still needs an owner launch on port 5189. Direct `runway.py` commands accept caller-supplied
actor fields, so their rows alone are not an authoritative owner receipt.
The new host projects its private exact-version brief receipt as
`owner_verified`. Future live align actions must also check a host-private
exact-review receipt at transition time. The fixture-only CLI action must
never become a live route by simply changing a feature flag.

### Short acceptance script

1. From this checkout, run `./scripts/start-marketing.ps1` in PowerShell to
   start the new host on loopback port 5189. Verify
   `/api/marketing/state` advertises `campaignBriefEnabled=true`.
2. Sign in as the owner, open Work, and inspect the saved Marketing assignment.
   Check the source links, unknown publication dates, three prior artifacts,
   worker status, allowance, and receipt history.
3. Open the campaign brief, set one audience hypothesis, desired behavior,
   metric definition, review timing, non-goals, and learning rule, then save.
   Refresh and confirm the next action, wait reason, review timing, and the
   exact version and brief persist. Attempt a stale edit and confirm conflict.
4. Request a revision of the exact fixture draft, create the simulated revised
   asset, inspect its predecessor and QA record, approve that new exact version,
   and align it. Confirm a brief edit makes an earlier approval historical.
   None of these actions may publish.
   For a real linked revision after a metered grant, approve its exact artifact,
   select it for the source internal campaign, and verify the owner receipt,
   predecessor, and preserved original draft. No live revision is authorized
   by this acceptance script.
   If you have an actual measurement record for the internal brief, use
   **Owner-reported observations** to enter its source, period, counts, and
   attribution limits; refresh and check the verified receipt. In **Decision
   and learning**, record an internal decision and proposed lesson; reopen and
   inspect their evidence IDs and owner receipts. None of these actions moves
   the campaign to launch or establishes that the draft caused the result.
   On a later internal brief for the same audience, expand **Relevant prior
   proposed learning** and verify the earlier context and source references
   appear while the current campaign's own lesson is excluded.
   Expand **Launch readiness** to inspect the blocked checks. A capability
   request should retain its task, scope, benefit, and cost status while
   leaving the campaign stage, budget, and external capability unchanged.
5. As a collaborator using a distinct authenticated device, verify owner-only
   brief/review controls are forbidden. The real shared Gateway conversation
   and revision attribution need separate two-human acceptance per
   `MULTIPLAYER_AUDIT.md`.
6. Run the isolated .NET fixture HTTP test and the four targeted browser specs
   once the host is available; the two newer UI specs have not run yet.
   The HTTP test covers fake launch, insufficient and sufficient samples,
   persistence, and lesson retrieval. Every launch receipt must say
   `SIMULATED_ONLY`; no external action should occur.

**Highest-value next step:** have the owner start the rebuilt host on free
port 5189, then verify the real owner brief save/reopen/conflict and private receipt
path. Genuine two-human shared revision remains separate unfinished
acceptance work. The disposable fixture host was stopped after the browser
test; recursive cleanup of
`C:\Users\Ayric\AppData\Local\Temp\marketing-campaign-browser-20260924`
was rejected by automatic approval review as `blocked by policy`, so the
fixture directory remains. No alternate deletion method was used.

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
