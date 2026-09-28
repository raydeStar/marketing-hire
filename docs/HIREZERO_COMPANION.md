# HireZero companion connection

September 28, 2026. The owner selected a hybrid: **Plow continues hosting the
competition agent. HireZero hosts the shared web entrance.** This supersedes the
earlier proposed choice between moving the whole runtime and waiting for a Plow
member web-ticket API. The existing Plow installations remain intact.

## Implemented so far — disabled, not deployed

`packaging/plow/companion-connector.mjs` implements a bounded outbound connection
to the HireZero service, using only Node's standard library. The companion service
lives in the separate landing repository under `cms/hzcms/companion*.py`.

The service persists explicit membership and expiring, one-use invitations bound
to a verified phone recipient. The subject comes from Plow's authenticated
`GET /v1/auth/owner-uid`, never a browser field. It uses separate origins, tickets
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

**This is a transport and access-registry foundation, not working multiplayer.**
The connector is not started by `boot.mjs`. The current host has no adapter that
accepts these assertions, and the website has no active workspace proxy or invite
screen. `HZ_COMPANION_DOMAIN` is unset in production. No live workspace, DNS,
hosting plan, credentials, agent image, or competition entry changed.

## Host adapter required before enabling the connection

The earlier user work split reserved `src/Thaddeus.Host/**` and
`tests/Thaddeus.Tests/**` for Claude. Permission to change these is pending.
Do not route a member through `IssuePlowOwner` to work around that boundary.

1. Add a separately configured companion ingress, preserving the existing Plow
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

## Remaining rollout

- Finish host adapter and actual host permission/replay/CSRF tests.
- Implement browser ticket exchange, authenticated streaming proxy and plain
  invitation UI. Test two separate browsers/accounts and actual host role checks.
- Add an owner-verified pairing action: verify ownership using the account-scoped
  Plow API, deliver the new per-workspace credential through agent settings, and
  read it at boot using `/v1/agents/me`. Never embed it in an image or browser.
  Define recovery for an uncertain settings update before enabling this action.
- Qualify wildcard DNS/TLS and nginx host routing for separate workspace origins.
  Browser cookies, service workers and storage must stay isolated by workspace.
- Publish a small overlay and establish the supported update/state-retention
  route. No in-place Plow image update is currently documented; do not delete an
  existing instance or claim a full state restore from the brief export.
- Verify repeat owner entry, a teammate's real hosted join, role changes,
  forwarding/replay refusal, revocation and reconnect behavior. Keep native
  OpenClaw group multiplayer/competition verification as separate acceptance.

## Checks and infrastructure observation

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
