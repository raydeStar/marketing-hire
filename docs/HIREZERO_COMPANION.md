# HireZero companion connection

September 28, 2026. The owner selected a hybrid: **Plow continues hosting the
competition agent. HireZero hosts the shared web entrance.** This supersedes the
earlier proposed choice between moving the whole runtime and waiting for a Plow
member web-ticket API. The existing Plow installations remain intact.

## Implementation and rollout

`packaging/plow/companion-connector.mjs` implements a bounded outbound connection
to the HireZero service, using only Node's standard library. The companion service
lives in the separate landing repository under `cms/hzcms/companion*.py`.

The service persists explicit membership and expiring, one-use invitations bound
to a verified phone recipient. The subject comes from Plow's authenticated
account-scoped `GET /v1/auth/index-identity`, never a browser field. The assertion
is accepted only from that direct authenticated provider response. Phone account
tokens cannot use the agent-scoped `/v1/auth/owner-uid` route. It uses separate
origins, tickets
and browser sessions for each workspace. It stores token hashes, not raw login,
invitation or connector tokens. Revocation is checked at dispatch, response and
stream reads. A restored backup disables connectors and memberships rather than
reviving old access. No invitation messages are sent automatically.

The connector polls for requests using its own workspace credential, forwards
only to the colocated loopback host, and streams bounded response frames back.
It signs the individual caller identity, workspace, method, path, body digest,
request ID and short expiry. It strips owner/proxy headers supplied by a browser.
It never follows redirects or retries an uncertain host mutation. A missing reply
is unconfirmed, not success. `X-Plow-User` is never used for a companion member.

The host now verifies individual HMAC assertions, binds cookie sessions to the
portal grant, derives ownership from its pinned Plow identity, and applies native
roles and campaign access. Boot reads its own identity from Plow and its private
connection file from the persistent volume. It checks the private adapter
readiness route before polling. The companion website implements ticket exchange,
streaming, sign-out and phone-bound invitations. Owner phone sign-in, automatic
pairing, repeat entry and saved brief/objective writes passed on the live shared
workspace on September 28. A real teammate's hosted join remains unverified.

## Implemented host adapter

The owner authorized taking over the host and tests on September 28. A companion
member never goes through `IssuePlowOwner`.

1. A separately configured companion ingress preserves the existing Plow
   ingress. Pin workspace ID, HTTPS origin, owner UID and a private connector/host
   signing secret. Require loopback and constant-time signature verification;
   reject wrong origin/workspace, stale/replayed assertions, method/path/body
   changes, oversized bodies and ambiguous identity headers.
2. Derive ownership by comparing the verified subject to the pinned owner UID.
   Reuse stable accounts and normal cookie/CSRF sessions. Bootstrap on the session
   read only; a write without its existing session is refused. Keep each session
   bound to its asserted account. Preserve revoked account refusal.
3. Admit teammates with campaign-scoped Reviewer access. The existing host owns
   roles, campaign sharing and owner-only approvals. Bind the new identity to the
   existing campaign membership machinery explicitly; registry membership alone
   must not expose every campaign. Test real comment/change-request routes.
4. Advertise the qualified adapter version on a private readiness route. Start
   polling only after that probe succeeds. Polling by itself proves transport
   liveness, not host authentication readiness.

## Wire format v1

The agent uses `POST /api/companion/agent/poll` and `/reply` on the configured
HireZero broker origin with its workspace-specific bearer and a JSON `workspace`.
These endpoints refuse browser Origin headers. A poll waits at most 20 seconds.
The service bounds outstanding work at 32 requests per workspace and 256 total.

A claimed packet contains `protocol`, `id`, `workspace`, `origin`, `method`,
`path`, allowlisted `headers`, base64 `body`, and `identity` with `subject`, `name`,
`owner`, opaque browser `session` binding and `expires`. The connector checks the
owner flag against its pinned owner UID; the flag is not sent as authority to the
host. Request bodies are limited to 150,000 bytes. Large uploads are deliberately
unavailable until a separately bounded upload path is qualified.

`X-HireZero-Identity` is base64url UTF-8 JSON. Its fields are `version`,
`workspace`, `request`, `subject`, `name`, `session`, `issued`, `expires`, `method`,
`path`, and lowercase hex SHA-256 `bodyHash`. `X-HireZero-Signature` is lowercase
hex HMAC-SHA256 over the exact encoded header using the local ingress secret.
Assertions last at most 30 seconds. The host must verify before deserializing or
using the identity and retain consumed request IDs across that validity window.

Response frames have `id`, monotonically increasing `sequence` starting at zero,
base64 `body` (at most 64 KiB decoded), and boolean `end`. Only the first frame has
`status` and allowlisted `headers`. At most eight chunks wait per request. Response
redirects and cookies other than a host-only `thaddeus-session` are refused.
Requests time out after 120 seconds; streams can reconnect using their normal
client behavior. Writes are not requeued if a connection is lost.

## Live deployment and pairing

Public image `ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.7`:

```
ghcr.io/raydestar/hirezero-marketing@sha256:363f68426c9dafc10769fe3e129a062ddf8c308c4d0d762303fade528958ab24
```

It runs on Plow; the shared browser origin is
`https://3943820d59d372193c165ae8f5483edb.work.hirezero.app`.
Enter through [HireZero sign-in](https://hirezero.app/account/). The landing
service uses `HZ_COMPANION_DOMAIN=work.hirezero.app` and an exact qualified image
allowlist in `HZ_COMPANION_IMAGES`. Each workspace has its own ordinary CNAME and
exe.dev domain registration. New workspace DNS/TLS provisioning is still an
operator step; this is not automatic public tenant provisioning.

Plow rejected undeclared catalog settings, so pairing does **not** use agent
settings. After checking account ownership and the qualified image, the service
obtains the owner's short-lived Plow web ticket. A separate cookie jar visits
only that exact agent origin. Its owner-protected `/_hirezero/companion` endpoint
requires a boot nonce and writes the connection atomically to a mode-0600 file in
the persistent volume. No credential reaches browser JavaScript. An uncertain
write is reconciled by reading its credential hash, without replaying it. Tests
cover origin, owner, nonce, file permissions and redirect boundaries.

The previous installations were retained because no supported in-place Plow
image replacement was available. The reviewed HireZero brief and objectives were
copied into the new workspace. This is not a complete VM or campaign-history
migration; the original installations and private exports remain available.

Open **Team → Invite → Invite and manage teammates** to create a phone-bound
invitation. The recipient signs in with their own phone. They initially have
Reviewer access; choose which campaigns to share in the native cockpit. Creating
an invitation does not send a message automatically.

Remaining acceptance: a real second person's hosted join, live phone/model work,
native OpenClaw group multiplayer, and organizer verification/one-click admission.
The Index recorded a successful installation after this image booted, but that
is not proof of real usage or those remaining checks. Large uploads above the
150,000-byte request limit and public campaign-page delivery through the companion
are not qualified. No paid plan or resource was added.

## Checks and infrastructure observation

The focused `CompanionIngressTests` exercise the actual host middleware and
campaign ledger: replay and body/path tampering, independent accounts, swapped
cookies, CSRF, promotion/demotion, explicit campaign sharing, comments, change
requests and revocation. They make no model calls. The browser fixture is
`node scripts/check-companion.mjs LANDING_REPO companion-check-FRESH-NAME`.
It runs the actual Python service, connector and .NET host with fictional phone
verification, substituted DNS/TLS and a synthetic browser event heartbeat.
Receipts and desktop/phone screenshots stay under `artifacts`; fixture data is
removed only after its owned processes exit.

`scripts/build-plow-overlay.mjs PINNED_BASE plow-package-FRESH-NAME` builds a
small application layer over the existing immutable Plow image. It captures
source hashes and removes its staging/build files. It neither pushes nor deploys.
Each workspace hostname needs an ordinary CNAME and an exe.dev domain registration;
wildcard TLS is optional and is not a prerequisite for this deployment.

`node --test packaging/plow/companion-connector.test.mjs` exercises config and
identity boundaries, actual loopback HTTP streaming, redirect refusal and a lost
response without replaying the write. Its host is a fictional HTTP fixture, not
the .NET permission implementation. The landing repository has registry and real
HTTP API tests with fictional Plow identities. No model or SMS request is used.

Read-only observation of the existing landing VM on September 28: one visible
CPU, zero load averages, 2,008,140 KiB total RAM, 1,851,628 KiB available, no swap,
4.71 GiB filesystem free; `hirezero-cms` active, 22,577,152 bytes memory, one task.
This supports a lightweight relay proof but does not establish production
capacity or account-wide quota. Do not build or copy large agent images there;
the repository's 10 GiB reserve would not fit. No paid resources were added.
