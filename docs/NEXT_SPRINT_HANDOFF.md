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
- A disposable fixture ledger can progress through fresh exact-draft alignment,
  a fake publisher receipt, deduplicated observations, insufficient-sample
  waiting, a decision, and a contextual proposed lesson. Editing the brief
  returns it to alignment. Fixture lessons can be retrieved by a later brief
  with their original context and uncertainty. The fixture-only host mode
  exposes owner-authenticated seed/action/lesson routes against a temp ledger,
  and Work has controls for the entire simulated journey.
- Live publishing remains unavailable. A saved creative approval is still only
  approval for internal use. No real observation or launch integration is
  claimed. The owner brief route in the new Release build has not yet been
  exercised against the actual persistent host.

### Verification and limits

| Gate | Result | Evidence or limit |
| --- | --- | --- |
| Ledger workflow tests | **PASS** | `python -m unittest discover -s business/agent/hire/tests -p 'test_runway.py' -q`: 41 tests, including isolated fixture full path, stale/duplicate guards, and additive migration of existing rows. |
| Release host build and focused tests | **PASS** | `dotnet build src/Thaddeus.Host/Thaddeus.Host.csproj -c Release --no-restore --nologo -v:q`: 0 errors; `dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MarketingRunwayTests --nologo -v:q`: 9 pass. One test drives the full owner-authenticated fixture HTTP path, Work state, persistence/reopen, and lesson retrieval; another rejects a forged brief without a private receipt. |
| Web build and browser fixture | **PASS** | `npm --prefix web run build`; `THADDEUS_TEST_ORIGIN=http://localhost:5189 npm --prefix web run test:e2e -- campaign-fixture-work.spec.ts first-customer-journey.spec.ts`: 2 intercepted-response UI tests pass. A separate disposable Release host on `localhost:5190` passed `campaign-fixture-live.spec.ts` (1 test) against actual fixture HTTP routes, from seed through lesson and refresh. |
| New route on loaded persistent host | **BLOCKED** | The old Windows `Thaddeus.Host.exe` process (PID 39712 at last read) still listens on loopback port 5189. Automatic approval review rejected Codex's stop/restart command as `blocked by policy`. The old process does not advertise `campaignBriefEnabled`; Work hides its new save control there. |
| Native shared gateway integration | **PARTIAL** | Earlier local routing and attribution controls remain; this sprint did not re-run native revision acceptance. Read `MULTIPLAYER_AUDIT.md`. |
| Two independent humans | **NOT RUN** | Needs secure ingress and a second real person; multiple tabs or fixture principals do not count. |
| New live inference / campaign publication | **NOT RUN / DISABLED** | Meter v5 was not ready at baseline; no fresh spending bound was established. Publication has no live route. |

The brief/Work follow-up checks on September 24 passed: 41 Python tests and
both intercepted-response browser tests. The new required brief fields are
covered by validation and the browser save path. The fixture journey still
needs an explicit revised-asset path after a revision request; editing the brief
invalidates the earlier review, but does not itself create a new asset or fresh
review. Do not call that revision acceptance complete.

The full fixture journey reaches owner-authenticated fixture HTTP routes and
the real Python ledger in a disposable temp directory. Work has the matching
fixture controls, browser tested both with intercepted responses and against
the disposable host's real HTTP routes. The normal campaign brief
uses an authenticated owner HTTP route in the new host build; the running host
still needs a permitted restart. Direct `runway.py` commands accept caller-supplied
actor fields, so their rows alone are not an authoritative owner receipt.
The new host projects its private exact-version brief receipt as
`owner_verified`. Future live align actions must also check a host-private
exact-review receipt at transition time. The fixture-only CLI action must
never become a live route by simply changing a feature flag.

### Short acceptance script

1. Once the old host has been closed by the owner, start the new Release host
   on loopback port 5189 using the existing local configuration. Verify
   `/api/marketing/state` advertises `campaignBriefEnabled=true`.
2. Sign in as the owner, open Work, and inspect the saved Marketing assignment.
   Check the source links, unknown publication dates, three prior artifacts,
   worker status, allowance, and receipt history.
3. Open the campaign brief, set one audience hypothesis, desired behavior,
   metric definition, review timing, non-goals, and learning rule, then save.
   Refresh and confirm the next action, wait reason, review timing, and the
   exact version and brief persist. Attempt a stale edit and confirm conflict.
4. Review the exact draft; confirm an edit to the brief returns it to align
   and the old review is historical only. Neither action may publish.
5. As a collaborator using a distinct authenticated device, verify owner-only
   brief/review controls are forbidden. The real shared Gateway conversation
   and revision attribution need separate two-human acceptance per
   `MULTIPLAYER_AUDIT.md`.
6. Run the isolated .NET fixture HTTP test and the two browser tests above.
   The HTTP test covers fake launch, insufficient and sufficient samples,
   persistence, and lesson retrieval. Every launch receipt must say
   `SIMULATED_ONLY`; no external action should occur.

**Highest-value next step:** load the new host after the owner closes the old
PID and verify the real owner brief save/reopen/conflict and private receipt
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
