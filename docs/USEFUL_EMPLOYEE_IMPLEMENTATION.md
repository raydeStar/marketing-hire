# Useful employee implementation

## Accepted scope

One marketing employee, a large Chat surface, Work for deliverables and decisions,
and the right company panel. CEO and meetings remain parked. The business is the
owner's personal brand selling configurable marketing agents: "my first employee."
No publishing, outreach, purchases, Plow, or paid model fallback is authorized.

## Completion evidence required

- A real bounded assignment produces a source-backed draft; owner feedback yields
  a materially revised saved version. Fixture results stay labeled as fixtures.
- A versioned business brief records offer, audience, voice, claims, examples,
  goals and boundaries, and is included in chat and autonomous execution context.
- Research can use owner-selected current evidence rather than two hardcoded
  threads; proposed follow-ups state rationale, deliverable, limits and decision.
- The opening view shows saved accomplishments, decisions and the next permitted
  action, with an obvious path to each. Technical receipts remain inspectable.
- Person-based sign-in works across devices; campaign invitations are scoped,
  expiring and revocable. Public hosting requires a configured HTTPS deployment
  and identity provider. Private Tailscale transport is not customer authentication.

## Current constraints

The v6 post-response meter is deployed on the Luna subscription route, which
rejects max_output_tokens. Two earlier requests have terminal audit receipts
but unknown usage; their 25,000-token reservations remain separate from
reported consumption. A later single request produced a confirmed 2,380-token
receipt and a saved raw response. Its inexact source quote failed validation,
so no source-backed artifact or owner-directed revision exists yet. Execution
ownership is released and Chat is available. Do not invent zero usage for the
earlier requests or claim a hard per-response cap. The new verbatim-quote prompt
is tested locally and awaits the next normal host load for live evaluation.

Owner approved Google/Microsoft login through Auth0 Free. A real Google callback
and exact owner binding now work on the private Tailnet site; re-login after the
next restart, Microsoft and independent reviewer sign-in remain unchecked.
Existing browser pairing stays available during migration; it must not be
represented as a human account system.

## September 24 implementation checkpoint

Implemented:

- Claims and reference examples added to the existing profile, with additive
  migration, immutable revision snapshots, idempotency and stale-version refusal.
- The full saved brief is included in direct chat and worker claims. Previously
  worker prompts used the goal and sources without the saved profile content.
- Work reuses the complete brief editor; the company panel links directly to it.
- Chat has a compact current-assignment overview: saved results, decisions, next
  permitted action. An unresolved execution disables Send while retaining drafts.
  The host also refuses a new chat dispatch when execution ownership is held.
- Assignment source URLs are now explicit owner input. They remain restricted to
  two distinct public HN discussion pages, fetched by the existing bounded reader.
  Replaying an assignment request cannot change its source scope or brief version.
  This is source selection, not broader autonomous discovery.
- Removed the claim that the subscription endpoint enforces 1,800 output tokens.

Verification:

- Web production build passed.
- 45 runway ledger tests passed; all six task/profile tests passed after fixing
  the new test's Windows SQLite handle cleanup. Its retained temporary directory
  was then removed after process exit.
- 12 focused host tests passed in Release. A Debug test build first failed because
  the live host locks its assemblies; Release avoids that collision.
- The existing browser workflow passed with added claims/examples persistence,
  execution-hold Send refusal and retained-draft checks. No model calls were used.

Runtime and pending decisions:

- The exact historical agent.wait lookup returned status timeout; this supplies
  no terminal or usage receipt. The original unknown reservation remains intact.
- Automatic approval review rejected stopping/restarting Windows host PID 40396,
  reporting only blocked by policy. The tested new backend is not loaded yet.
  The frontend checks an explicit capability before enabling new brief fields;
  older servers retain the existing editable fields and the execution-hold UI.
- After clarification, the owner approved the small Luna subscription pilot,
  conditional on preserving usage limits. The route is confirmed as
  openai/gpt-5.6-luna. Codex account usage currently reports 80% used in the weekly
  window; no secondary window was returned. Start with ONE request and check
  impact before continuing; at most three requests and 15 minutes. No request has
  been sent yet. Quota identity equivalence with OpenClaw still needs verification.
- The owner approved Google/Microsoft login and a small-budget MVP. The subsequent
  identity checkpoint is recorded in [CUSTOMER_IDENTITY_PLAN.md](CUSTOMER_IDENTITY_PLAN.md):
  OIDC/person-account code is tested, Auth0 and Google development login configured,
  secret stored privately. The owner restarted the host and the real Google flow
  reached identity consent. Scoped email invitations are implemented and tested;
  live owner sign-in/binding and independent Microsoft email verification remain.
  Microsoft registration and Auth0 connection are now configured after the normal
  interactive CLI login succeeded. A further restart loads the Microsoft option and
  latest invitation controls. Auth0 Free is the ceiling; compare alternatives
  before any paid upgrade. No public exposure or new model subscription.

Still required: deploy and exercise the approved post-response policy for a fresh
bounded grant; a real feedback/revision loop; broader authorized discovery; customer
identity/invitations and an agreed HTTPS deployment. The goal is not complete.

## September 24 terminal recovery and response accounting

- The Gateway's read-only `audit_events` table contains a matching start and
  `agent.run.finished` failure for execution `c75319bc5e0740d4b121fa3005f01639`.
  The recovery command validates the exact derived worker session, event ordering,
  timestamps and one unambiguous start. Timeouts and caller-supplied status cannot
  release ownership. The evidence and its hash are saved in the hire ledger.
- Applied recovery to the live assignment: version 9, `needs_review`, no active
  execution, 25,000 tokens still reserved, reported usage still unknown. Its
  failed task cannot automatically retry. The browser shows Chat available and
  the failure/retained-reservation explanation in Work.
- Added streaming provider-usage receipts bound to the reserved request digest.
  Valid terminal usage is persisted before forwarding completion to OpenClaw;
  malformed usage, HTTP errors, disconnects and cancellation retain uncertainty.
  Receipt writes and request settlement share one SQLite transaction. No raw
  response text or credentials are stored in these accounting receipts.
- Verification: 49 Python ledger checks, 17 local fetch/stream checks and 9
  installed-OpenClaw checks passed; web production build passed. Installed tests
  ran in a disposable `--network none` container, removed on exit. Python fixture
  directories were cleaned. No new images, live model calls or host restart.
- Deployment boundary: the recovery/UI are live. The response-capture adapter is
  tested source and still requires a Gateway image rebuild together with the
  explicit new grant policy. The running v5 Gateway remains unready for worker
  inference. The approved first-request checkpoint and three-request/15-minute
  ceiling must be enforced before starting a fresh grant.

## September 24 fresh grant and token tracker

Owner renewed the Luna grant and requested the day/week/month token tracker used
in Thaddeus 2. The worker model remains `openai/gpt-5.6-luna`.

Implemented and verified in source:

- A new `post_response` grant explicitly accepts the subscription route's lack
  of a hard output cap. Legacy grants retain their old policy and cannot silently
  gain this permission. The v6 transport omits the rejected field only for an
  exact, current, unexpired post-response claim.
- Each grant allows at most three physical requests, three worker turns and
  fifteen minutes from creation. It starts with ONE request permitted. A durable
  usage checkpoint requires a confirmed provider receipt, the current version,
  owner review and remaining allowance before releasing the other two requests.
  This never extends the deadline. Unknown usage or a reservation overrun blocks
  continuation. Retries cannot reuse a physical-send reservation.
- The transport persists provider usage before forwarding the terminal SSE
  event. The host reads that digest-bound receipt instead of substituting a turn
  aggregate. A late receipt can improve unknown usage without releasing an
  unresolved execution. Deadline cancellation retains unknown usage when needed.
- The right company panel has **Token usage**: Today, 7 days and 30 days, plus
  expandable Day/Week/Month charts. Dates use the viewer's local calendar days.
  It reuses the existing Thaddeus chart and refreshes through ordinary read APIs,
  with zero model calls. The owner-only endpoint combines autonomous receipts
  and Chat usage without counting a worker execution twice. Fixture campaigns
  are excluded from live worker history.
- Chat dispatches now save usage separately from their content. Valid Gateway
  turn totals are retained; pending, failed or historical unmeasured attempts
  stay explicitly unreported. Physical request counts are not inferred from
  Chat turn totals. Reservations are displayed separately from consumption.
  This tracker does not estimate the account's remaining subscription quota.

Verification: 56 Python ledger checks, 20 focused host checks, 19 local transport
and response checks, ten installed
OpenClaw transport/plugin checks in a disposable network-disabled container,
and the focused browser checkpoint/chart test passed. Production web build
passed. Tests used synthetic replies; no model request was sent. The compact
v6 candidate image is built; no data volume was copied or removed.

Runtime boundary at this checkpoint: Windows host PID 48968 still runs the older
backend. Owner was asked to restart with `start-marketing.ps1 -Tailnet -ShortPilot`
because automatic approval review previously rejected our stop/restart action.
That script rebuilds/reloads the business Gateway and host. After it is listening,
verify the v6 meter and new owner-only usage API, then create the approved fresh
assignment, inspect ONE real request's usage, and decide whether to release more.
Do not describe this as a completed live pilot before those receipts exist.

## September 24 owner-feedback revision path

While the owner was away from the PC, completed the revision path independently
of the pending restart:

- Work now shows **Run this saved change request** beside the selected assignment,
  including the exact feedback and a separate measured-usage authorization.
  Authorizing it prepares and releases one linked revision; saving feedback alone
  still does not dispatch work. A release retry reuses the held grant.
- The live revision grant uses the v6 post-response policy: one request, one turn,
  a 25,000-token reservation, five active minutes and a ten-minute UI deadline.
  A single response may exceed the reservation. It cannot open the initial
  research pilot's two-request continuation checkpoint.
- Host receipts now verify the exact owner feedback as well as the selected
  artifact, actor and decision. A ledger row without the matching host receipt
  cannot authorize a live revision. Shared change-request authorization already
  uses this same host review path. Grants also bind an instruction digest.
- Expired grants can be replaced through another explicit owner authorization.
  The previous grant remains in history as expired. A transactional migration
  replaces the old permanent review uniqueness constraint with uniqueness for
  current/released grants; existing grant identities and payload hashes survive.
- The transport's effective deadline is now the earlier of the grant deadline
  and remaining active-time allowance. No model calls are needed for this check.

Verification: 58 ledger checks, 21 focused host checks, the pilot accounting/chart
browser test and the saved-revision browser test passed. The latter exercises a
failed release followed by retry of the same grant, and confirms a one-request
revision cannot offer additional pilot requests. The production frontend built.
The initial revision browser test used the wrong synthetic artifact field and
was corrected from `draft` to the actual `hook` contract before passing.
All replies were fixtures, test sessions were revoked and temporary ledger/host
fixtures were removed by their test lifecycle. No owner feedback or live revision
has been fabricated; live acceptance still awaits restart and owner feedback.

## September 24 current discussion discovery

- New assignments now offer **Find current discussions**. An owner-entered public
  query searches recent HN story records, shows publication date/comment count,
  and lets the owner fill the two source slots directly. Results are candidates;
  assignment startup still fetches and validates the chosen discussion pages.
- Search is an owner-only read endpoint using the fixed
  [HN Search API](https://hn.algolia.com/api), whose indexed fields are documented
  in the [service source](https://github.com/algolia/hn-search). The request returns
  at most 20 records from the last 90 days. The reader pins the public host,
  rejects redirects/private IPv4 destinations, caps response bytes and duration,
  ignores supplied external URLs, and constructs only the existing approved HN
  discussion URLs. Invalid, future, stale and duplicate results are discarded.
- Queries run only when requested by the owner. No business brief is sent as a
  search query automatically. There are no model calls, automatic polling,
  publishing or outreach in this discovery path.
- Verified one public read returned a current story record with the expected
  fields. Synthetic parser/access checks and the source-selection browser flow
  passed; the production frontend built. Latest focused verification totals:
  58 ledger checks, 22 host checks and two browser checks. The new host endpoint
  itself remains staged until the pending restart.

This closes the manual URL-finding gap within the existing HN scope. Autonomous
multi-source discovery, a real draft/feedback/revision acceptance run, final
customer identity acceptance and an agreed public deployment remain outstanding.

## September 24 account invitations and restart verification

- Added **Invite by → Existing sign-in account** for known reviewers. This binds
  the invitation to validated issuer/subject/account ID, allowing Microsoft
  accounts without verified email claims to join the intended campaign. Email
  invitations keep their verified-email rule. Expiry, one-time acceptance and
  account-wide campaign revocation apply to both paths.
- Verified 24 focused backend/middleware checks and seven browser checks. Coverage
  includes a different subject sharing the same email, changed email, another
  browser, replay, and pending-link revocation. Frontend production build passed.
  The tests do not represent a real Google/Microsoft callback acceptance.
- The owner restarted the host: PID 15836 serves the usage API, known-account
  endpoint and both configured sign-in providers. The business Gateway reports
  `marketing-meter-v6`, ready, post-response accounting, durable response receipts,
  and `openai/gpt-5.6-luna`. Shared Gateway remained running.
- This restart omitted short-pilot mode: `runwayLiveEnabled=false`. No fresh grant
  or model request was created. Requested the same startup script with `-Tailnet
  -ShortPilot`; previous automatic review still prevents our own stop/restart.
  Account-wide weekly usage was 94% consumed at the preflight, so retain the
  one-request checkpoint and reassess before any further request.

### Short-pilot restart and dispatch boundary

Owner restarted with `-Tailnet -ShortPilot`. Host PID 53828 now reports
`runwayLiveEnabled=true`; v6 meter readiness and Luna route were rechecked. The
next shell action, which would have created and submitted the fresh assignment,
was rejected by automatic approval review with only `blocked by policy`. It did
not execute. Do not bypass the rejection through another dispatch path.

Prepared the exact assignment in the existing localhost browser's **Work → New
assignment** form, including the two source URLs and accepted measured-usage
checkbox. Left **Start bounded work** for the owner to click. No fresh grant or
live request has been created by this preparation. The selected source records
were inspected through the public HN Search item API: discussion 49826029 provides
criticism of automated marketing quality; 49328818 is a builder's own account of
repetitive SEO work. The goal explicitly forbids treating these as buyer-demand
validation. Startup still fetches the checked pages before admitting a grant.

## September 24 first v6 request: receipt failure and repair

- Owner clicked **Start bounded work**. Project
  `b752befb01e74ba8aa011c4e7024bf91` admitted exactly one request/execution,
  `2c280d5ba63a4485827a74d633dc3472`. The durable request receipt records HTTP 200
  with `unsupported_response`, no confirmed tokens. Gateway audit events 179/180
  record the exact worker/session/run starting and ending successfully. The app
  therefore held the execution before saving a deliverable. No second request ran.
- The observer rejected successful responses before reading their body unless
  their MIME header matched SSE. The original header/body was not retained, so
  its exact value cannot be recovered from this receipt. Offline reproduction
  confirmed that the installed native client consumes valid SSE without that MIME
  header while our observer previously discarded its usage. The repair observes
  bounded SSE bytes regardless of MIME label, still requiring a valid terminal
  event and consistent provider counts. Non-SSE/error/truncated bodies remain
  unknown. This repair has not yet been verified by another live request.
- Terminal reconciliation now accepts an exact persisted Gateway completion as
  proof that execution ended. It releases Chat ownership while marking the app
  attempt failed, preserving unknown usage and its full reservation, blocking
  the step, and scheduling no retry. It does not turn Gateway success into a
  verified app deliverable or manufacture token counts.
- Applied reconciliation using the real audit receipt. Live API now reports
  `needs_review`, no active execution, no Chat blocker, and 25,000 tokens reserved
  for this attempt. Usage history lists it as unknown. Total reservations are
  50,000 including the older unknown request; this is not reported consumption.
- Verification: 59 ledger tests, 20 local meter/response tests and seven installed
  transport checks passed. Installed checks ran network-disabled with fake OAuth
  and synthetic replies. Rebuilt only the small meter overlay and recreated the
  business Gateway, preserving `dev_state` and the shared Gateway. Readiness is
  green; deployed parser SHA-256 matches source:
  `66d5eccaf0c4ad77850ea09e8b00b876465a9cde3437f6b20b9895498481721d`.
- The new fixture test initially used the wrong receipt field and left an SQLite
  connection open. Both were corrected; the 59-test rerun passed and cleaned its
  fixtures. Automatic review rejected cleanup of the original 8-KiB disposable
  `C:\Users\Ayric\AppData\Local\Temp\tmp7p9k_pn9\state\openclaw.sqlite`
  with only `blocked by policy`; that specific fixture remains. No cleanup bypass.

At the first-request check, shared weekly Codex usage was 95% consumed. That is
account-wide and cannot be attributed to this request. Keep this attempt stopped;
a fresh owner grant and a further live receipt check are still required before
claiming the pilot or token capture works end to end. No usable draft or actual
owner feedback/revision acceptance has been produced by this attempt.

## September 24 durable returned-work records

- The host now saves returned Gateway text before checking provider usage or
  validating the deliverable. Its private SQLite record binds project, step and
  execution, records configured model/time, and hashes the original reply.
  Repeated identical storage is idempotent; changed text or binding cannot replace
  an earlier record. Storage failure cannot produce a successful deliverable.
- Work exposes collapsed **Employee response records** for current and archived
  assignments. These are plain-text audit records, distinct from saved artifacts,
  confirmed usage and owner approvals. A maximum 12,000-character preview is saved;
  oversized replies are explicitly labeled truncated with their original length
  and full-reply digest. No reasoning trace or raw Gateway envelope is stored.
- The host supplies these records from its own database, replacing any same-named
  field from the worker ledger. Existing owner-only assignment APIs enforce access;
  collaborator campaign views continue to return their explicit shared projection.
- Verified 24 focused host tests, the production web build, and one browser test
  covering current/archive records, escaped output, truncation labeling and absent
  approval/continuation controls. The first build attempt overlapped frontend asset
  generation with .NET static-asset compression; rerunning after the web build
  passed. Future web and host builds must run sequentially. The initial browser
  selector treated a generic archive container as a region; corrected it and moved
  session cleanup to the independent afterEach hook. The passing test revoked its
  session; the failed run timed out before revocation and its closed-browser session
  will expire normally. No live model request was made for these checks.
- The frontend is built; the new host capture code loads on the next normal host
  restart. No restart was attempted or requested during this change. This cannot
  reconstruct the already-lost reply from the earlier pilot.

### Remaining acceptance audit

| Requirement | Current evidence | State |
| --- | --- | --- |
| Real source-backed draft and owner-directed revision | Second live attempt confirmed 2,380 tokens and saved its response; an inexact quote rejected the artifact; revision flow passed fixture checks | Incomplete: fresh live draft and actual owner feedback needed |
| Versioned business brief in Chat and worker context | Profile v6 read from live host; immutable profile tests and worker-context implementation | Implemented; unused voice/claims/examples await owner content |
| Current selected sources and bounded proposals | Live current-discussion search and source retrieval; proposal schema and fixture validation | Selected-source flow implemented; broader autonomous discovery remains deferred |
| Useful opening view and inspectable records | Current-assignment overview and Work views; usage tracker and saved failed-response record live | Live usability acceptance remains |
| Person login and scoped invitations | Both providers configured; Google callback and exact validated owner binding verified on Tailnet | Owner re-login, Microsoft callback and independent collaborator acceptance remain |
| Public MVP access | Private Tailscale Serve only | Requires agreed domain/hosting/storage budget before deployment |

The goal remains active. Fixtures and configured providers do not satisfy live
employee or customer-login acceptance.

## September 24 shared allowance history

Added owner-only account allowance history beside the existing employee token
tracker in the right company panel. Five-minute read-only Codex app-server checks
persist remaining percentages and reset windows, with account isolation, explicit
staleness and a 30-day JSON download. These checks start no model turns and expose
no reset action. See [USAGE_TRACKING.md](USAGE_TRACKING.md) for boundaries and checks.

The first real snapshot saved 0% used / 100% remaining after the authorized reset
had already completed. The owner's later instruction to wait until exhaustion
arrived after redemption; no further reset is authorized. Earlier unknown
employee receipts stay unknown and have not been overwritten by account data.

At this checkpoint, the host had not been restarted: the new allowance
endpoint/collector and previously staged response capture awaited its next
normal load. No fresh Luna dispatch was made during that work. Live
draft/revision and login acceptance remained incomplete.

## September 24 live owner login and first measured reply

- After the owner restarted the host, the allowance panel sampled the shared
  account and displayed 99% remaining. This is account-wide history and does not
  attribute usage to the employee.
- The owner clicked **Start bounded work** for project
  `3f08abb320724f768289448ff83c0e33`. Exactly one Luna request returned and
  the meter recorded **2,380 reported provider tokens**. The host saved the raw
  employee text to its private response record before validation. The step is
  `needs_review`; no artifact was accepted and no later step or retry ran.
- The second evidence quote changed the source's contraction `He's` to `He is`.
  The exact-source validator correctly rejected it. The new work packet provides
  a short verbatim excerpt from each checked page and explicitly confines
  paraphrase to the inference field. Both host and ledger exact-quote checks
  remain in force. The historical failed reply remains unchanged. A fresh bounded
  assignment after the next host load is needed to test this prompt repair live.
- The owner's validated Google account completed the real Auth0 callback on the
  private Tailnet site. At the owner's direction, the existing private account
  record and restricted local login configuration were bound to the same exact
  issuer/subject pair. The live site displayed **Mark · Owner**. The binding was
  checked for a unique verified account, exact subject match and private file
  ACL; `scripts/bind-customer-owner.py` is idempotent and rejects the wrong
  account prefix. The running host loaded login settings before
  the binding, so sign-out/sign-in durability awaits the next ordinary host
  restart and callback test. Microsoft and independent collaborator callbacks
  remain unverified.

### Next live acceptance sequence

1. At the owner's desk, stop the existing Windows host, then run
   `scripts/start-marketing.ps1 -Tailnet -ShortPilot`. The new host must load the
   quote packet and private owner configuration. Do not stop the Docker Gateways
   separately or clear their persisted state.
2. Sign out and sign back in with the same Google identity on the private HTTPS
   address. Confirm that the new session still says **Owner**. This tests owner
   binding after startup rather than only the existing live session.
3. Prepare a fresh bounded assignment with the two already reviewed source URLs.
   The previous failed response remains an audit record; do not relabel it as a
   successful draft. The owner reviews the allowance and starts one request.
4. Check the request receipt and exact quotes. Only if a source-backed artifact
   is accepted should the owner direct a material revision and inspect its
   version, receipts and final summary. Do not auto-publish or contact anyone.
