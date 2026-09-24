# Customer identity: small-budget MVP

Owner approved Google/Microsoft sign-in on September 24. Use a managed provider
via OpenID Connect. Auth0's free development signup is the first candidate; no
paid plan is authorized. Provider setup and app implementation remain pending.

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
