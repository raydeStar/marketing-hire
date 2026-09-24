# Customer identity: small-budget MVP

Owner approved Google/Microsoft sign-in on September 24. Use a managed provider
via OpenID Connect. Auth0 Free is the spending ceiling. The owner explicitly wants
to compare other providers before paying if Free stops meeting the product's needs.
No paid plan is authorized. Keep workspace permissions in this app and use standard
OIDC. A future provider change still needs an explicit issuer/subject migration;
never automatically merge identities by matching email addresses.

## Implemented September 24: login foundation

- Server authorization-code + PKCE integration using the standard ASP.NET Core
  OIDC middleware. Validates state, correlation, nonce, signature, issuer, audience
  and the configured provider's subject prefix. Requests openid/profile/email only.
- Persistent customer accounts keyed by exact issuer and subject, separate from
  revocable seven-day browser sessions. Tokens are not saved to the browser or
  workspace session records. Google/Microsoft accounts with the same email are not
  merged. Owner role requires an explicitly configured OwnerSubject.
- Campaign membership, native identity binding, request ownership and receipts
  use the persistent person ID for customer accounts. Old browser IDs remain valid.
  Signing out revokes only that browser session; campaign revocation reaches all
  browsers belonging to that person.
- Login page displays only configured providers, generic failure messages and
  local recovery. New customer accounts are campaign-only nonmembers, with an
  access-needed screen. Existing owner campaign controls can grant their account
  access; expiring invitation links are NOT implemented yet.
- Optional `.data/customer-login.json` imports only CustomerLogin fields and
  preserves explicit environment/command-line values. This file and temporary CLI
  credentials are ignored by Git. The configured credential file has inheritance
  disabled and grants access only to the current Windows user.
- `scripts/set-customer-login.ps1` is a fallback setup helper with a hidden secret
  prompt; it refuses to overwrite an existing config. Do not paste secrets into chat.

## Provider and runtime checkpoint

- Auth0 tenant `marketing-hire-dev.us.auth0.com`; existing Default App reused as
  **First Employee — development**, type Regular Web Application.
- Exact callback `https://hope.tail47397a.ts.net/signin-oidc` and logout origin
  `https://hope.tail47397a.ts.net/`. No wildcards, embedded cross-origin auth,
  password connection for this app, implicit flow, refresh-token or client-credentials
  grant. Only authorization_code is enabled.
- Google identity-only connection is enabled with Auth0 development keys. Own
  Google OAuth credentials and production consent setup remain required before
  customer launch. Microsoft is intentionally absent from the login page.
- Current Auth0 dashboard showed the 22-day extra-feature trial. Official pricing
  says it falls back to Free automatically: 25,000 monthly active users, social
  connections and one enterprise connection are included. No paid upgrade selected.
  See https://auth0.com/pricing (checked September 24, 2026).
- Owner approved official Auth0 CLI setup access. The application's existing secret
  was transferred directly to the private local config without printing it. CLI
  is signed out at the end of this setup checkpoint.
- Microsoft registration in the owner's confirmed personal directory was rejected
  with Authorization_RequestDenied / insufficient privileges. A separate personal
  CLI device login was also denied and cancelled. No Microsoft app/secret/connection
  was created, and no work-directory resource was changed. Obtain the exact sign-in
  error / correct administrator access before retrying.
- Google is configured locally but NOT live-tested: old Windows host PID 40396
  still serves port 5189. Earlier automatic approval review rejected stopping it.
  Owner said they will restart later. No bypass or alternate production host used.
- OwnerSubject is empty intentionally. First successful customer login gets no
  owner access. After real sign-in, bind the exact validated Auth0 subject belonging
  to the owner in the private config, then load the configuration. Email alone
  must not grant ownership.

## Verification for this checkpoint

- 13 isolated real OIDC middleware tests passed, including malformed callbacks,
  PKCE exchange, provider mismatch, two browsers/one person, independent logout,
  explicit owner identity, campaign isolation/revocation and private config bounds.
- Existing SecurityTests (3) and MarketingRunwayTests (12) also passed with the
  identity changes. Latest additional config test brought OIDC total from 12 to 13.
- Web build passed. Two mocked sign-in browser tests and two existing owner access
  layout tests passed. Two campaign screenshot tests and the HTTPS pairing test
  were skipped by their environment gates; they are not new live-login evidence.
- Full solution locked restore passed after updating only the affected transitive
  locks for the OIDC dependency. Test fixture directories were removed by teardown.
- No model inference, publishing, outreach, paid fallback or public deployment.

## First release

- One workspace for this pilot, invite-only. Do not expose the existing global
  store as a multi-tenant customer service or grant every new login ownership.
- Explicitly bind the workspace owner to a verified issuer and subject.
- Persist a person ID separately from browser session IDs. Same person on a
  phone gets the same workspace membership, with a separate revocable session.
- Use standard authorization-code flow with PKCE, state, nonce and issuer/audience
  validation. Keep tokens server-side; use Secure/HttpOnly cookies and existing
  CSRF protection. Configure exact callback and logout URLs.
- Separate authentication from membership. An authenticated user with no
  membership sees an invitation-required screen and cannot read owner records.
- Campaign invites bind the intended verified identity to one campaign, expire,
  can be revoked and can be consumed once. Show the inviter the exact scope.
  Invite links open the intended campaign after successful sign-in.
- Preserve the local owner recovery path and existing browser memberships while
  transitioning. Never merge accounts solely because display names match.
- Keep Google/Microsoft login distinct from access to mail, Drive or calendars;
  request identity scopes only. No background access to those services.

## Deployment boundary

The current URL is private Tailscale Serve. Account login does not make that URL
reachable outside the tailnet. A public MVP needs an agreed domain, HTTPS host,
storage/backup plan and monthly budget. Existing loopback administration and
worker endpoints must remain private. No Funnel or public tunnel is authorized.

## Required checks

Provider flow with state/nonce failure and issuer/audience mismatch; same person
on two browsers; nonmember denial; exact-campaign invite/expiry/replay/revocation;
owner-private Chat denial; logout and session revocation; no account-secret output.
Use an isolated fake provider first, then real owner sign-in once configured.

## Worker usage decision (separate from customer login)

The owner authorized a very small pilot through existing OpenClaw ChatGPT OAuth:
Luna, one request first, inspect usage, then at most three requests/15 minutes.
Tokens are measured after replies and an individual response has no enforceable
output-token cap on this subscription route. Do not remove v5's guard without a
new explicit accounting mode, conservative stop conditions, durable per-request
receipts, and recovery of the historical unknown execution. No paid fallback.
