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

The subscription endpoint rejected max_output_tokens. The v5 meter refuses new
worker calls. The earlier run has now been reconciled against the Gateway's
persisted failure audit: execution ownership is released and Chat is available.
Its usage is still unknown and its 25,000-token reservation remains intact.
Do not invent zero usage or declare a hard token cap without provider evidence.
A live end-to-end result remains outstanding.

Owner approved Google/Microsoft login through Auth0 Free. Existing browser pairing
stays available during migration; it must not be represented as a human account system.

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

Still required: implement the approved post-response usage policy for a fresh
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
