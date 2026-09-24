# Marketing employee continuation — local handoff

## Native identity bridge checkpoint — 2026-09-24

This checkpoint supersedes the V2 native hold below. The Windows Release host
is running at `http://localhost:5189/`, and the persistent private
`marketing-shared-hire` Gateway has been recreated with its existing named
volume. `marketing-business-hire` and its `dev_state` volume stayed running.
The cockpit checkout tracks the private `raydeStar/marketing-hire` repository on
the separate `business/marketing-hire` branch. Its `main` branch remains the
original agent source and has separate Git history. This branch is a private
checkpoint for later repository shaping; it is not a public release.

### Implemented boundary

- A campaign grant assigns each paired device one permanent Gateway identity
  slot (`member-01` through `member-16`); revocation does not recycle a slot.
  OpenClaw's `users.self` supplies the profile GUID. The host pins the first
  observed profile to the device, rejects conflicts, and records each native
  suggestion ID/profile in the existing `campaign_shared_inputs` row. The
  authenticated host chooses the identity; browser payloads cannot choose it.
- The owner can connect one saved campaign in **Work → Campaigns → What changed
  → Campaign access → Connect native conversation**. A local owner browser is
  authenticated by the loopback-only host key; the host uses its private LAN
  address as the Gateway proxy address for that route. This is a host proxy
  address, not a claim about the browser's network source. Collaborator native
  input requires an actual HTTPS connection with a non-loopback address
  observed by the host. The Gateway has no published port and its shared agent
  has tools, heartbeat, and cron denied/disabled.
- The native session creator and each suggestion author must match the
  authenticated Gateway profile. The host then saves the same human text as a
  version-linked campaign discussion and project input, with `activate=false`.
  Startup marks unconfirmed native requests `unknown`; a stable marker permits
  read-only reconciliation without blindly resending. A recorded duplicate
  request is idempotent. Owner access changes and campaign input are serialized
  with native session creation. Legacy native suggestion writes remain held.
- The UI shows whether the session is connected, whether a collaborator's
  Gateway profile has been observed, and a short profile ID beside native
  receipts. This binds a **device session** to a Gateway profile; it does not
  prove the legal identity of the person holding the device. A suggestion is
  pending input, not an AI response or a worker execution.

### Verification and preserved state

| Check | Result |
| --- | --- |
| Real installed Gateway, isolated state | PASS: owner and member identities yielded distinct `users.self` profile GUIDs; the owner created one shared session and the member's `session.suggestions.add` receipt carried the member profile. No model turn was sent. |
| Isolated host plus HTTPS LAN fixture | PASS: `web/tests/campaign-native-fixture.spec.ts` used independent owner and paired collaborator sessions, real native RPCs, exact campaign membership, one native/ledger receipt, idempotent duplicate, profile distinction, owner-only route denial, revocation, zero model requests, and zero tokens. The HTTPS fixture used a local developer certificate and one machine's LAN address; this is not a two-person acceptance test. |
| Other local gates | PASS: 1,211 .NET tests, one existing skip; 49 Python runway tests; web production build; isolated campaign browser fixture; live owner review at 1440 and 1280 pixels. |
| Live pilot state after reload | Preserved: active runway `7cd4abc7581b455aab6ca95c82f12900` remains `unknown`, version 8, with execution `c75319bc5e0740d4b121fa3005f01639`, 25,000 tokens reserved and zero used. The persistent host still has zero native sessions, shared inputs, memberships, and native device bindings. No live campaign was connected during verification. |

The disposable Gateway container was stopped and removed, and fixture ports
5190/5191 and their active marker were cleared. Automatic policy rejected the
verified recursive cleanup command for this turn's four temporary fixture
directories and `artifacts/native-compile`/`artifacts/native-tests`; those
intermediates remain. No deletion retry was made. Their exact temporary
directory names are `marketing-campaign-browser-51e9e2e1fe164d06a7c14bd84ba5e553`,
`marketing-campaign-browser-8143450ccf574bd3b24d2cd76030727d`,
`marketing-campaign-browser-2672fb4385c9417da3de938405ff868c`, and
`marketing-campaign-browser-0c963b5568c24743ab51d9c3f1b5d6bd` under the
current user's Temp directory. These are disposable fixture data, not the live
Marketing state or Docker volumes.

### Hand back: real two-person test

1. **Provide a trusted HTTPS address for the collaborator.** The live host
   currently listens only on `localhost:5189`; a second device cannot use that
   address. Configure a LAN HTTPS origin with a certificate trusted by that
   device, or an authenticated Tailscale HTTPS proxy using the host's existing
   `Thaddeus:PhoneOrigin`/`PhoneMode` settings. Restrict the network ingress to
   intended devices. The shared Gateway itself must remain private. Do not
   reuse the fixture's `192.168.1.19:5191` developer-certificate endpoint as
   a durable or trusted deployment.
2. Owner signs into `http://localhost:5189/`, opens **Work → Campaigns**, and
   selects the saved campaign to test, not the newer held `unknown` runway.
   Review the exact draft and digest. In **Settings → Access**, start a pairing;
   the collaborator claims it on their own device over the HTTPS address, and
   the owner confirms it. Do not share the owner host key.
3. In **Work → Campaigns → What changed → Campaign access**, grant that paired
   device this campaign. The owner clicks **Connect native conversation**. The
   button must show connected and must not start a worker or model call.
4. The collaborator opens **Work → Shared campaigns**, checks the same draft
   digest, and posts a specific comment. Both views should show one saved note,
   the collaborator's device name, and a Gateway profile receipt. Reload both
   views: the note persists. The owner can inspect the exact project input.
5. As the collaborator, try owner Chat, worker controls, campaign authorization,
   and another unshared campaign; they must be denied. Owner revokes campaign
   access, then the collaborator refreshes and loses the shared view. Record
   sanitized device/profile/request IDs and any mismatch. Do not test by
   publishing content or releasing the held worker request.

An employee reply in that shared session, live event delivery, and a real
worker revision remain separate future acceptance work. Two actual people have
not run the above script yet.

## Enterprise multiplayer workspace V2 checkpoint — 2026-09-24

This section records the earlier, unpushed V2 checkpoint. The native identity
bridge and private Git checkpoint above supersede its publication status and
native hold. The
working checkout is `C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit`
on `business/marketing-hire`, starting from `93ec0da026a48556925c87ed6df4979da7e59eb5`.
The changes are local and uncommitted; there is no configured Git remote.

### What is running and where

- Open `http://localhost:5189/`, sign in with the existing host key, then use
  **Work → Campaigns**. The Windows host was restarted without restarting either
  Docker Gateway; its Release backend now serves the new review routes. The
  owner can open the saved pilot `91c4b1df1e6942e2a986936127b37742` and
  review its provisional brief, three draft angles, source limits, five separate
  assessments when a packet provides them, and exact internal decision actions.
  The existing pilot has no human creative decision; no approval was supplied
  on the owner's behalf. The newer active assignment is still visibly `unknown`.
- Owner Work has a compact campaign list, selected draft, version comparison,
  status/next-action block, access management, version-linked shared discussion,
  and collapsible context. A separately authenticated campaign-scoped device
  gets a full-width **Shared campaigns** view with only selected shared brief,
  draft, decision, discussion, and member names. Its generic owner Chat, private
  artifacts, worker controls, and event subscription are denied. A campaign
  membership continues to enforce restricted scope after revocation.
- Comments and change requests use `campaign_shared_inputs` for one host-side
  discussion, and the existing `hire.sqlite` project input path for worker
  context. The owner authorizes an exact saved request using the existing
  review receipt. This records **Authorized; execution unavailable** while live
  inference remains off; it does not create a grant or wake the worker. The
  fixture-only linked revision uses the existing grant, claim, finish, review,
  and adoption contracts with zero model calls. It preserves source input ID,
  predecessor, exact artifact digest, and owner receipts. This pilot supports
  one linked worker revision; further requested changes remain comments until
  a new authorized workflow exists.
- New native conversation writes are held. The older static `owner` and
  `collaborator` Gateway proxy profiles do not prove which human signed in.
  Historical native receipts remain readable/reconcilable; new native sessions
  and suggestions return a clear hold instead of creating a second editable
  transcript. This is a native identity/routing gap, not a claimed multiplayer
  verification.

### Evidence and verdicts

| Layer | Result |
| --- | --- |
| Campaign regression | **PASS locally.** Python runway tests 44/44; focused Marketing, feed, and guidance .NET tests 16/16; final core gate `node scripts/check-local.mjs core enterprise-multiplayer-final-20260924` passed 1,211 .NET tests, one previously skipped browser-session test, protocol checks, and web build. Evidence: `artifacts/local-check-enterprise-multiplayer-final-20260924/`. The first gate label `enterprise-multiplayer-20260924` failed two paired-device regressions; both were fixed before the final passing gate. |
| Enterprise browser review | **PASS for local appearance and saved reads.** `web/tests/campaign-review-owner.spec.ts` passed 2/2 at 1440×900 and 1280×800 against the running owner pilot. `web/tests/campaign-shared-fixture.spec.ts` passed against an isolated host with independent browser contexts. No page-wide horizontal overflow observed at either target width. This is a visual/usability check, not accessibility certification. |
| Automated multi-principal path | **PASS for isolated contract.** Fixture sessions exercise pre-share denial, scope/private route denial, forgery resistance, idempotent duplicate input and dispatch, stale 409, owner authorization, zero model calls, linked input attribution, exact approval/adoption, two-browser refresh, and revocation. These are test principals, not two humans. |
| Native identity/routing | **PARTIAL/BLOCKED.** Installed OpenClaw reports `2026.9.4 (3a9d69d)`. Its version-matched multi-user and trusted-proxy docs say Gateway profile/presence/owner labels are not isolation and require a trusted authenticated ingress. The local adapter still maps static roles rather than unique verified people. No native live conversation was asserted. |
| Two real humans | **NOT RUN.** A second person and trusted identity-bearing ingress are not provisioned. A paired campaign-only device and access grant are the supported host-side path, but they do not by themselves establish native identity. |
| Fresh live worker revision | **BLOCKED.** The persistent host reports `runwayLiveEnabled=false`; no metered revision grant, new model request, or publication was authorized. Fixture revisions are deterministic and marked simulated. |

Screenshots: `artifacts/enterprise-review/before-owner-1440.png`,
`owner-1440.png`, `owner-1280.png`, `collaborator-1440-fixture.png`,
`collaborator-1280-fixture.png`, `owner-waiting-fixture.png`,
`owner-comparison-fixture.png`, and `collaborator-revision-fixture.png`.
Owner images use the read-only saved pilot; collaborator/comparison/waiting
images use isolated simulated campaign data. The fixture host on port 5190 was
stopped and its active marker cleared after browser testing. Retained prior
temporary fixture directories were not deleted after earlier approval review
rejected their recursive cleanup.

### Short acceptance script and next action

1. **Available now:** Owner signs into `http://localhost:5189/`, opens Work →
   Campaigns, reads the provisional brief and exact draft, and personally
   chooses approve, reject, or request change. An approval is internal only.
2. **Requires trusted access setup:** On a separately paired device, sign in as
   a real collaborator through an authorized trusted ingress. Owner grants only
   the chosen campaign in Work → What changed → Campaign access. Confirm that
   this device sees the same goal/digest, can comment, and cannot read private
   Chat or owner controls. Revoke it and verify access disappears. Do not use a
   public tunnel or invented invitation link to satisfy this step.
3. **Requires native identity binding:** Map each authenticated human to a
   distinct verified Gateway profile or supported authenticated channel sender;
   then bind permitted shared session and campaign membership. Only after that
   can a two-human native session/event test be reported as passed.
4. **Requires separate owner grant and metering gate:** If a real employee
   revision is desired, set a bounded model-use allowance, release an exact
   authorized grant through the existing path, inspect the saved revision,
   approve its exact version, and select it for the source campaign. No fixture
   receipt substitutes for this live proof.

**Highest-value remaining action:** establish a trusted identity-bearing
ingress for a second real human and verify its Gateway profile binding. Until
then the polished host-side review can be used locally, but native multiplayer
and two-human acceptance remain blocked. The unknown request
`7cd4abc7581b455aab6ca95c82f12900` remained at version 8, status `unknown`,
active execution `c75319bc5e0740d4b121fa3005f01639`, and 25,000 reserved
tokens before and after the host-only restart. Both Docker Gateways stayed up;
there was no outreach, spend, publishing, deployment, push, or new model use.

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
  records a hypothesis, priority rationale, primary metric, review timing, and additional non-goals
  without granting execution. Work now separates the next campaign action,
  worker wait reason, and next review timing from the campaign stage, while
  displaying the ledger's scope, $0/no-publish boundary, and worker allowance. The
  new host stores a private owner receipt and displays whether the current
  brief matches it; a direct CLI actor field does not verify ownership.
  Older briefs remain readable, exact legacy request retries retain their
  receipt, and a new edit requires a stated priority rationale.
- On the persistent host, the saved three-artifact pilot
  `91c4b1df1e6942e2a986936127b37742` now has an assistant-prepared,
  owner-session-recorded **provisional internal brief** at campaign version 1.
  It uses the two saved anecdotal sources and the owner's learning-only rule;
  it sets no continuation threshold, spend, external channel, or worker grant.
  The host reopened it at `align` with an exact private receipt and rejected a
  competing version-0 edit with HTTP 409. This verifies the local owner route;
  it does not mean the human owner ratified the audience or creative. The newer
  active runway remains `unknown` and was not retried or modified.
- The same `hire.sqlite` runway ledger stores brief revisions, source capture
  metadata (unknown for legacy sources), campaign stage, fixture actions and
  versioned receipts. The Work view shows source provenance and action history.
  A linked revision assignment now carries up to eight relevant source project
  inputs with original actor IDs and source-input links, so a recorded native
  collaborator constraint can reach the authorized revision worker packet.
- Future review packets must give five separate qualitative assessments:
  audience fit, clarity, product truth, channel suitability, and desired
  action. Work labels them as employee assessments pending the owner's exact
  artifact decision. Older saved packets remain readable without invented
  scores or retroactive assessments.
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
  with their original decision, observations, context, and uncertainty. A later
  fixture Work brief now shows this simulated prior learning and excludes its
  own campaign; the fixture-only host mode
  exposes owner-authenticated seed/action/lesson routes against a temp ledger,
  and Work has controls for the entire simulated journey.
- Live publishing remains unavailable. A saved creative approval is still only
  approval for internal use. No external analytics or launch connector is
  claimed. Owner Work now has a validated manual observation path for an
  existing host-verified internal brief; it records an owner-attested source,
  period, counts, interpretation, and limitations without advancing launch or
  claiming causality. No actual owner observation has been entered. The brief
  route is now exercised on the persistent host; the observation route is not.
- A host-verified owner observation can now support an internal decision and
  proposed lesson in Work. The host chooses the verified observation IDs;
  the ledger applies the saved sample or learning-only rule, requires new
  evidence after collect-evidence, and records a decision rationale and
  contextual lesson. Continue does not release a worker or a launch. A later
  observation reopens alignment without erasing the prior decision.
- An owner decision no longer depends on using the exact device session that
  authored the brief. The host checks the saved private brief receipt and the
  current authenticated owner session; the ledger retains that acting session
  on each decision, lesson, or capability request. A focused host test first
  reproduced HTTP 409 on a second owner session, then passed after the fix.
  The running container reads `runway.py` through a bind mount; its SHA-256
  matched this checkout after the change, so no host restart was needed.
- A later internal brief can read prior proposed lessons for its saved audience.
  Work shows the original context, decision, uncertainty, revisit condition,
  and observation references. The owner-only read checks host-private receipts
  for the historical brief, lesson, decision, and source observations, excludes the current
  campaign, and never changes policy or starts a model turn.
- Work shows a blocked live-launch checklist for the internal campaign. The
  owner can record a capability request with the blocked task, exact
  destination/action scope, expected benefit, and cost status. It changes no
  capability, budget, or stage.
- The mounted `campaign-desk` skill and direct-chat role prompt now describe
  this versioned Work campaign and its saved review timing. The old skill's
  generic campaign/checkpoint events and automatic weekly cadence no longer
  compete with the authoritative ledger in source. The running container sees
  both mounted file updates, but its generated workspace `AGENTS.md` still has
  the old weekly sentence until a controlled restart. No running conversation
  or model turn was restarted to validate prompt behavior.

### Verification and limits

| Gate | Result | Evidence or limit |
| --- | --- | --- |
| Ledger workflow tests | **PASS** | `python -m unittest discover -s business/agent/hire/tests -p 'test_runway.py' -q`: 44 tests, including a second owner device acting on the saved brief, fixture review criteria, priority rationale, a later fixture brief retrieving its predecessor's decision and observations while excluding itself, stale/duplicate guards, and additive migration. |
| Release host build and focused tests | **PASS** | `dotnet test tests/Thaddeus.Tests/Thaddeus.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MarketingRunwayTests --nologo -v:q`: 10 pass. The second owner session's decision has its own verified receipt; the test failed with HTTP 409 before the fix. Isolated HTTP also filters forged lessons and changed historical briefs, and now returns fixture decision/observations with a validated current-campaign exclusion. Zero new model tokens were consumed. |
| Local core gate | **PASS** | `node scripts/check-local.mjs core campaign-loop-verified-20260924` passed on clean source HEAD `08692a1`; receipt in `artifacts/local-check-campaign-loop-verified-20260924/verified.json`. Locked restore, notification build, 1,211 backend tests, CPU protocol checks, and web build passed; no live model call, worker start, or GitHub Actions dispatch. Its first attempt stopped at the secret scanner because that scanner classified the three tracked `business/agent/hire/bin/*.py` source files as generated output. Commit `08692a1` permits only those exact three files, and the clean rerun passed. |
| Web build and browser fixture | **PASS for the isolated journey** | `npm --prefix web run build` passes. Four targeted Playwright specs passed against intercepted Marketing responses on port 5189; the fixture Work spec was rerun after prior simulated learning was added. The read-only persistent Work spec also passed. The updated `campaign-fixture-live.spec.ts` passed against the real owner-authenticated fixture HTTP routes on a fresh port-5190 Release host: exact asset revision and approval, fake launch, insufficient-sample wait, fresh observation, decision, lesson, refresh, and a second fixture brief reading the first campaign's evidence. The first attempt reached its final assertion but failed because two valid observations matched one strict locator; after asserting both rows and starting a fresh fixture, the complete test passed (1/1). The fixture launcher first exposed an empty phone-origin startup bug, which was fixed before the passing run. Main port 5189 was left running. |
| New route on loaded persistent host | **PASS for brief; other owner writes untested** | The owner retried foreground `start-marketing.ps1`; it built web and .NET, confirmed the Docker services, and started `Thaddeus.Host` on `localhost:5189`. Unauthenticated Marketing state returned 401. A signed-in owner read returned `campaignBriefEnabled=true`, `runwayLiveEnabled=false`, and `fixtureCampaignEnabled=false`. The archived real pilot brief saved and reopened at version 1 with `owner_verified=true`, `stage=align`, three untouched artifacts, and unchanged `needs_review` worker status; a stale new request at version 0 received HTTP 409. The launcher uses checked native exit codes so nonfatal Vite/Docker stderr warnings no longer abort startup, though Windows PowerShell still displays their `NativeCommandError` records. |
| Native shared gateway integration | **PARTIAL** | The running owner state advertises `sharedGatewayEnabled=true` and `deferredRevisionEnabled=true`, while `runwayLiveEnabled=false`. Earlier local routing and attribution controls remain. Linked revision claims carry source-input provenance, but no new live Gateway revision was run. Read `MULTIPLAYER_AUDIT.md`. |
| Two independent humans | **NOT RUN** | Needs secure ingress and a second real person; multiple tabs or fixture principals do not count. |
| New live inference / campaign publication | **NOT RUN / DISABLED** | Meter v5 was not ready at baseline; no fresh spending bound was established. Publication has no live route. |

The September 24 linked-adoption seam adds owner-only
`POST /api/marketing/runway/{id}/campaign-adopt-revision`, exact linked
artifact checks in `hire.sqlite`, and a host-private review and selection
receipt. It does not grant a model turn, publishing, or launch. The isolated
ledger test, host receipt tests, and intercepted-response browser test pass.
No linked revision was selected on the persistent campaign.

The internal decision seam adds owner-only
`POST /api/marketing/runway/{id}/campaign-internal-action` for a decision and
contextual proposed lesson. Its host-private receipts mark exact saved
actions; the ledger preserves source observation IDs, saved rule, actual sample,
asset/brief versions, no-launch status, and the lesson's decision ID. A new
observation after completion reopens alignment. This is local implementation
and isolated HTTP evidence, not a claim that a real campaign produced an
outcome. The intercepted-response browser interaction passed; no real owner
observation or decision has been submitted.

The contextual retrieval seam adds owner-only
`GET /api/marketing/campaign-lessons?audience=...&excludeCampaignId=...`.
The ledger reads the historical brief revision, exact decision, and observation
references. The host checks its private receipts for the historical brief and
all three kinds of action
before showing the proposed lesson in a later Work brief. It does not import
fixture learning, alter company policy, or authorize a new worker grant.

The blocked launch checklist names missing live link/tracking,
destination/rollback, and publisher capability even when a creative draft has
internal approval. An owner-only capability request records the task, scope,
expected benefit, and cost status with a private receipt. It grants nothing.

The September 24 manual-observation seam adds the owner-only
`POST /api/marketing/runway/{id}/campaign-observation` route, an additive
host-private exact-action receipt, and the Work form. The 44 Python tests pass
including internal observation validation, deduplication, and non-progression;
10 focused .NET tests pass including a successful isolated internal-mode HTTP
save/reopen, fixture-route denial, and private receipt projection; the new
intercepted-response browser test passes the Work save/reopen path. A successful
observation against the running persistent normal host is still **NOT RUN**;
no real measurement was supplied. Do not treat a CLI actor field or an owner-entered
source reference as independently verified real-world evidence.

The brief/Work follow-up checks on September 24 passed: 44 Python tests,
10 focused .NET tests, four targeted intercepted-response browser tests, and
one opt-in persistent read-only browser test. Required
brief fields are covered by validation and browser save. The fixture journey
now includes an explicit revised asset and fresh owner review; the asset action
is simulated and consumes zero model requests. Real native shared revision
acceptance remains unproven. The expanded real-route browser spec now passes
on a fresh disposable host, including exact revision, insufficient evidence,
and later contextual learning; this is simulated workflow evidence only.

The linked-revision fixture test now also saves a collaborator-labeled source
input after the original asset and verifies its actor, content, and original
input ID survive into the released revision claim. This is ledger lineage
coverage, not proof of a second human or Gateway identity in this run.

The full fixture journey reaches owner-authenticated fixture HTTP routes and
the real Python ledger in a disposable temp directory. Work has the matching
fixture controls, browser tested both with intercepted responses and against
the disposable host's real HTTP routes. The passing fixture ledger has two
projects, the first with eight actions from revision through lesson, zero
reported or reserved model tokens, and one launch receipt with
`SIMULATED_ONLY` and `external_effect=false`. The normal campaign brief
uses an authenticated owner HTTP route on the running host and passed an exact
save/reopen/stale conflict check. Direct `runway.py` commands accept caller-supplied
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
3. In **Previous assignments**, open the saved three-artifact pilot and inspect
   its provisional campaign brief. Check the reason to prioritize, desired
   behavior, metric definition, review timing, non-goals, and learning-only
   rule. The assistant prepared this draft through an owner session; the human
   owner should edit it if the audience or proposition is wrong. Refresh and
   confirm the next action, wait reason, review timing, version, and brief
   persist. A second edit based on version 0 must conflict.
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
6. Run the isolated .NET fixture HTTP test and the four targeted intercepted
   browser specs. To verify the saved real pilot without modifying it, set
   `THADDEUS_TEST_ORIGIN=http://localhost:5189` and
   `MARKETING_PERSISTENT_PROJECT_ID=91c4b1df1e6942e2a986936127b37742`,
   then run `playwright test tests/campaign-persistent-readonly.spec.ts` from
   `web`. It passed after the saved brief was added.
   The HTTP test covers fake launch, insufficient and sufficient samples,
   persistence, and lesson retrieval. In the fixture browser journey, save a
   second founder brief and inspect **Relevant prior simulated learning** for
   the first campaign's decision, sourced observations, and uncertainty. The
   first campaign must not list its own lesson as prior learning. Every launch
   receipt must say
   `SIMULATED_ONLY`; no external action should occur.
   To repeat the full browser journey against current real fixture HTTP routes,
   start `scripts/start-campaign-fixture.ps1` in a foreground PowerShell window
   or a managed foreground command session. The latter worked without another
   owner action; an earlier background `Start-Process` request had been rejected
   by automatic approval review. The launcher checks port 5190, uses a fresh
   temp ledger and host data, sets a
   nonexistent Marketing container, and writes only a nonsecret active-path
   marker to `artifacts/campaign-fixture-active.json`. While it is listening,
   set `THADDEUS_TEST_ORIGIN=http://localhost:5190` and `THADDEUS_TEST_DATA`
   to the marker's `dataRoot`, then run
   `playwright test tests/campaign-fixture-live.spec.ts` from `web`. Stop the
   fixture session after the receipt is captured. The disposable directory is
   retained explicitly until its follow-up review because earlier automatic
   approval review rejected recursive fixture cleanup; do not use alternate
   deletion methods to bypass that rejection.

**Highest-value next step:** the human owner reviews the provisional brief and
the three saved angles in Work, choosing an exact draft to approve or revise.
That decision is still pending; no launch or new model work follows from the
saved brief. Genuine two-human shared revision remains separate unfinished
acceptance work. The disposable fixture host was stopped after the passing
browser test; port 5190 and its active marker are gone. Four small temporary
fixture directories remain under `C:\Users\Ayric\AppData\Local\Temp`:
`marketing-campaign-browser-20260924` (0.49 MiB),
`marketing-campaign-browser-7ec086e9420f413ea46184ed3876da0b` (failed
launch, near empty), `marketing-campaign-browser-413209445f9b4d76ada3599908464a1b`
(first browser run, 0.57 MiB), and
`marketing-campaign-browser-4edf6c67055346ea8d782cbf76d4e4df` (passing
receipt, 0.57 MiB). Recursive cleanup of the first directory was rejected by
automatic approval review as `blocked by policy`; no alternate deletion method
was used.

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
