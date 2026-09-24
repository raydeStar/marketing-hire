# Overnight first-customer journey — September 24 local handoff

## Customer-visible result

Open [the local cockpit](http://localhost:5189/) and select **Work → Board → Marketing project**. The business brief, first assignment, current action, saved results, source links, project notes, and exact-version review now occupy one readable flow. The preserved pilot is still awaiting owner review; it was not replayed. New model work is visibly paused because the required model-request and total-token ceilings cannot yet be enforced on this OpenClaw route.

The branded Chat/Work navigation, company panel, records, wiki, task board, and historical meeting records remain. The one current employee is Marketing; the CEO and meeting workflows remain deferred. No publishing, outreach, purchase, account mutation, or Plow action occurred.

## Evidence and verdicts

| Question | Verdict | Evidence and limit |
| --- | --- | --- |
| Useful continuation | **PASS, historical single-owner pilot** | Project `91c4b1df1e6942e2a986936127b37742` ran three dependent OpenClaw Gateway steps, saved three checked artifacts, and stopped at `needs_review` without another owner prompt. This proves fixed-step continuation, not independent opportunity discovery or audience fit. |
| Review and revision | **PASS in deterministic tests; live revision NOT RUN** | Owner review binds an artifact ID, SHA-256 digest, project version, and idempotency key. A test requests a revision, claims the linked step, saves a changed artifact, and approves that version; a collaborator note alone does not reopen work. Stale revised output is held. The live route is paused, so no new revision inference was used. |
| Native two-person conversation | **NOT RUN** | OpenClaw 2026.9.4 supports profiles, participants, roles, and shared sessions. This container currently authenticates with one token and the host's CLI appears as a generic Gateway participant. No second verified profile, shared native session, or actual human transcript was available. Local device sessions and browser fixtures are not native Gateway identities. |
| Permissions | **PARTIAL** | Host routes deny nonowners start, pause, resume, approval, full-tool Chat, private history, drafts, and owner receipts. The runway worker is tool-denied. Native collaborator Gateway access remains unavailable because the token Gateway also exposes the full-tool `main` agent and no verified-profile role restriction is configured. A host UI permission check is not a Gateway security proof. |
| Budget enforcement | **BLOCKED for new live validation** | Host tracks six top-level claims, 15 minutes ledger active time, a 30-minute new-assignment deadline, 150,000-token admission allowance with 25,000 reserved per run, two repairs, and worker `maxTokens:1800`. The overnight ceiling additionally requires no more than 20 underlying model requests and 250,000 total input/output tokens. Installed documentation says the Codex runtime owns native network retries; the Gateway reply exposes aggregate usage after a turn, not an enforceable mid-turn request/token counter. New live assignments and revisions return 409 and the pump performs no claims by default. |
| Recovery and quiet state | **PASS in local ledger tests; crash path not rerun live** | Execution IDs, task-version fences, idempotent inputs/reviews, conservative missing-usage charges, and restart-to-unknown handling are tested. The real project is `needs_review`, with zero reserved tokens and no active execution. Pause stops new admission while an already active turn may settle. |

### Preserved live pilot receipt

The previous checkpoint recorded successful OpenClaw execution IDs `110325ba1fde4ca8a5dd112e887a16f1`, `379af7cad9f84f428031754d2f1c6563`, and `cd3df3348c094f808db4f8e61295ed04`. Their reported totals were 2,585, 2,901, and 3,132 tokens: **8,618** combined. Their saved artifacts are `7d6da97b402b4a229b86a768a8b55665`, `b5ec5b744b1847dab06a239c14712dfd`, and `2b2fc05d68bd410381ffaea7d9b938fd`. The fourth claim `ac2b12ebc39d47af8c4adb8aaff2ef6a` was rejected by Gateway `INVALID_REQUEST` before inference; its receipt remains, its 25,000-token reservation was released, and the project shows four claims with zero outstanding reservation. No provider HTTP request count or cash bill was established. The installed subscription route is `openai/gpt-5.6-luna`, with no fallback model. The installed `openai/*` documentation describes the Codex app-server runtime; an exact runtime ID was **not** independently persisted for those three receipts.

The two cached public sources are [HN item 47667504](https://news.ycombinator.com/item?id=47667504) and [HN item 49703771](https://news.ycombinator.com/item?id=49703771). They supply anecdotes and checked quotes, not proof of demand or return on investment. The existing review packet is historical and predates the newly structured next-step proposal. New review packets must contain one bounded hypothesis, evidence gap, intended audience, estimated work, and an observable continue-or-stop reason. Matching proposals in a later packet are rejected. A proposal remains pending owner authorization and does not create work.

## Native conversation integration boundary

The installed [multi-user contract](https://docs.openclaw.ai/concepts/multi-user) records creator and participants only when an authenticated Gateway profile or channel sender supplies identity. A shared token does not distinguish humans. The installed [Gateway integration contract](https://docs.openclaw.ai/gateway/external-apps) recommends WebSocket RPC/events and `sessions.*` for external apps. Its `gateway.roles` policy can restrict authenticated profiles to specified agents and session access, but the current Gateway has no identity-bearing local reverse proxy or Tailscale identity and no collaborator role. OpenClaw also documents that operator write scope includes Gateway-wide actions and that same-agent ownership is not isolation.

The next supported connection needs an identity-aware ingress that cannot be bypassed, a narrow authenticated collaborator role allowing only a tool-denied shared Marketing conversation agent/session, and owner-only access to `main` and private worker sessions. Gateway must supply authenticated sender/session/message IDs to the host; the host should validate membership, deduplicate the message ID, then record a project input in `hire.sqlite`. The host remains the only grant/dispatch authority. Verify direct RPC and agent-mediated attempts to change budget, approve, pause, or read owner-only material against protected state. Keep the native path unavailable until those checks pass. A second real person must join the **same** session and receive a reply before multiplayer acceptance can be called complete. No new external channel, tunnel, or deployment was enabled here.

## Five-minute owner walkthrough

1. Open the local app and go to **Work**. Read the offer, provisional audience, immediate goal, and saved assignment. The brief can be edited; changing it fences older grants before further claims.
2. Open each saved artifact. The audience note has two checked source links; the draft-angle set lists hooks and claim limits; the review packet lists unsupported claims and the next owner decision. Exact digests identify the version being judged.
3. Leave a project note if a constraint should be retained. A note is attributed to the signed-in host session but cannot reopen the finished project or grant spending. The **Request revision** action is disabled until the live limit gap is closed. **Approve exact draft** or **Reject idea** records a decision only; neither publishes.
4. Reopen Work to confirm the same project and record return. The sidebar and board remain available. The yellow pause notice explains why there is no new worker activity.

## Verification and repository state

- Base checkpoint: `699d888a343cb30d5e0926b618cca9b7b5bfcfe2`, branch `business/marketing-hire`; no Git remote configured. This handoff adds local changes on top of that checkpoint and does not create a remote or push.
- `py -3 -m unittest discover -s business/agent/hire/tests -p test_runway.py -v`: **12 passed**.
- `dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj --filter FullyQualifiedName~MarketingRunwayTests --no-restore -v q`: **2 passed**.
- `npm --prefix web run build`: **passed**. `THADDEUS_TEST_ORIGIN=http://localhost:5189 npm --prefix web run test:e2e -- tests/first-customer-journey.spec.ts`: **1 fixture journey passed**. The fixture creates an in-memory project and does not establish two real Gateway principals.
- Read-only authenticated live Work browser check: pause notice and three saved results visible; screenshot `artifacts/overnight-live-readonly.png`. Host API returned `runwayLiveEnabled=false`, project `needs_review`, four claims, three artifacts, zero reviews, zero reserved tokens. No new model request was made.
- Local service is intended to remain available at `http://localhost:5189/`, with the `marketing-business-hire` container and existing `dev_state` volume preserved. The worker is quiet. The previously blocked disposable build-directory cleanup was left alone.

## Submission work kept separate

Public repository-wide MIT release, official Agent Index listing/reporting, hosted connection, a recorded demo of at least 60 seconds, and two-human native participation remain separate unmet gates. Existing marketing additions have a local MIT license; no public submission is claimed. Do not publish, register, transmit usage, deploy, contact people, or spend from this handoff.
