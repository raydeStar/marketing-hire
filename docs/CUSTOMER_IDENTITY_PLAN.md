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
  access. Scoped invitation links are implemented in the following checkpoint.
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
  customer launch. Microsoft was subsequently configured as described below.
- Current Auth0 dashboard showed the 22-day extra-feature trial. Official pricing
  says it falls back to Free automatically: 25,000 monthly active users, social
  connections and one enterprise connection are included. No paid upgrade selected.
  See https://auth0.com/pricing (checked September 24, 2026).
- Owner approved official Auth0 CLI setup access. The application's existing secret
  was transferred directly to the private local config without printing it. CLI
  is signed out at the end of this setup checkpoint.
- The earlier Microsoft access denial was resolved after the owner signed into
  Entra with the personal administrator. Device-code login showed AADSTS530035
  (Security Defaults); normal interactive CLI login succeeded. Security Defaults
  remain enabled. Work-directory resources and its default CLI profile are unchanged.
- Owner approved registration of **First Employee Auth0** in the personal tenant.
  Application `be78b29e-b373-487b-94cb-8a2a51883a32` allows Microsoft work/personal
  identities, with only `https://marketing-hire-dev.us.auth0.com/login/callback`.
  The default Graph User.Read permission was removed; Graph permission count is zero.
  Auth0 connection `microsoft` uses v2/common endpoint and `sub` (Auth0 rejects
  common endpoint with `oid`), requesting openid/profile/email. Extended profile,
  groups and directory lookup are disabled. Upstream emails are not assumed verified.
  Its six-month secret expires **March 24, 2027**; renew before then. The only local
  setup receipt now contains IDs and expiry; the secret was transferred to Auth0.
- Auth0 and the isolated personal Azure CLI were signed out after setup.
- Owner restarted successfully: PID 48968 reports Google enabled on port 5189.
  Real HTTPS Google flow reaches the signed-in account's identity consent page;
  consent/owner binding is pending. Microsoft is enabled in Auth0 and in the
  protected local config, but that provider change requires the next host restart.
  No complete real Google/Microsoft callback has been claimed yet.
- At this setup checkpoint, OwnerSubject was intentionally empty. The first
  customer login therefore received no owner access. The later live binding is
  recorded below. Email alone must never grant ownership.

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

## September 24: scoped campaign invitations

- Owner opens Work → campaign → What changed → Invite a reviewer. The form names
  the exact review/comment/change-request scope, intended email/provider and expiry
  (24 hours, 3 days or 7 days). It creates a link for the owner to copy and send;
  the application sends no email or message. Only saved campaigns can be invited.
- Each link has 256 random bits. Only its hash is stored; the token travels in a
  URL fragment and is retained in same-tab session storage across sign-in. The
  recipient sees the scope before explicitly accepting. Acceptance is a CSRF-
  protected transaction that consumes the link and grants exactly one campaign to
  the stable account. Concurrent/replayed acceptance cannot re-grant access.
- Email invitations require a validated identity with matching issuer, provider
  and verified email. An unverified Microsoft email cannot consume an email
  invitation. The exact-account option below supports that case without trusting
  arbitrary enterprise email.
- Owner can revoke unused invitations. Accepted links remain audit records; removing
  campaign access revokes all browsers for that account and closes other pending
  invitations for its matching identity. Account membership remains visible and
  revocable after all browser sessions expire. Revocation needs no running ledger.
- 22 real middleware/backend tests passed (synthetic identity provider and campaign
  ledger read; actual authorization, SQLite membership and invitation writes).
  Coverage includes owner-only creation, expiry bounds, nonsaved campaigns, issuer/
  provider/email mismatch, unverified email, expiry/revoke, concurrent replay, exact
  campaign scope, private-route refusal, and revocation with no browser or ledger.
- Five browser checks passed using mocked invitation APIs: sign-in providers/error,
  mobile invitation retention and explicit acceptance, account-switch error, and
  owner create/revoke controls. These are not live customer acceptance evidence.
- Production web build and Release host build passed. No employee/model request ran.
- The fallback setup helper now uses a DACL-only `icacls` operation because
  `Set-Acl` requested an unavailable audit privilege on this machine. A disposable
  probe verified an inherited-permission-free file with only the current user's
  access, then removed that probe. The script parser also passed.
- The checksum-verified Auth0 CLI download is retained temporarily for the first
  Microsoft callback check; it is signed out and ignored by Git.

## September 24: invitations to an existing account

- The reviewer signs in once, then the owner chooses **Invite by → Existing
  sign-in account**. The owner-only selector shows the known account's name,
  provider, email label and account ID prefix. Refresh loads new sign-ins.
- Links bind the validated issuer, subject and stable account ID. Email is only a
  label for these invitations: another account with the same email cannot preview
  or accept them. The intended account can accept after changing its email, or in
  another browser. Ownership is never granted by an invitation.
- Expiry, single-use acceptance, campaign scope and revocation apply to both
  invitation types. Removing campaign access also closes unused account-targeted
  invitations even when the account's email changed. Existing email invitations
  retain their verified-email requirement through the additive SQLite migration.
- Verified with 24 focused middleware/backend tests and seven browser checks,
  including unverified Microsoft, same-email impersonation refusal, email changes,
  second-browser membership, replay, and both owner create/revoke flows. Production
  web build passed. Browser/provider replies were fixtures; real customer callback
  acceptance was outstanding at that checkpoint. The restarted host serves the
  new owner-only known-account endpoint and both configured login providers.

## Worker usage decision (separate from customer login)

The owner authorized a very small pilot through existing OpenClaw ChatGPT OAuth:
Luna, one request first, inspect usage, then at most three requests/15 minutes.
Tokens are measured after replies and an individual response has no enforceable
output-token cap on this subscription route. Do not remove v5's guard without a
new explicit accounting mode, conservative stop conditions, durable per-request
receipts, and recovery of the historical unknown execution. No paid fallback.

## September 24: live Google owner binding

- The real Google callback completed through private Tailscale Serve. The first
  validated sign-in appeared as a collaborator, as designed; email alone did not
  confer ownership.
- At the owner's explicit direction, `scripts/bind-customer-owner.py` matched the
  unique verified account by issuer, exact subject, email and displayed account
  prefix, then bound that subject in the restricted private configuration and
  persisted its owner role. The live session displayed **Mark · Owner**. No
  client secret or full subject was printed or committed.
- Repeating the binding returned `alreadyBound=true`; a wrong account prefix was
  rejected. The private credential file still grants only the current Windows
  user access. The local host key remains the recovery administrator.
- The running host had loaded its OIDC settings before the binding. The next
  ordinary restart must load the OwnerSubject, followed by a real Google
  sign-out/sign-in check to prove the new session remains Owner. A real Microsoft
  callback and independent collaborator invitation acceptance are still open.
