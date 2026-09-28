# Shared workspaces: integration boundary

Checked against the [published Plow API](https://api.plow.co/openapi.json) on
September 28, 2026. This is a concrete integration design and support request,
not an implemented API contract.

## Current product gap

The landing page verifies a phone through Plow, lists the caller's agents and
requests an owner web ticket. Plow explicitly refuses a web ticket for another
account's agent. No workspace-member grant or member web-ticket API appears in
the published schema. Its agent-invite consent API concerns messaging invitations,
not granting access to a hosted cockpit.

HireZero already has role enforcement, browser pairing and campaign invitations.
The email invitations require a configured Google/Microsoft identity provider;
they do not accept Plow phone identities. They also cannot get a teammate past
Plow's current owner-only web proxy. The hosted adapter issues owner sessions
because that is the only identity the current platform gate admits. It must be
changed before any broader platform gate is enabled; newly admitted people must
never inherit owner authority automatically.

## Desired flow and ownership

1. An owner invites a verified person to the existing workspace and selects a
   role. Start with Reviewer unless the owner explicitly selects another role.
2. The recipient follows an expiring, one-use invitation and signs in as themself.
   Bind acceptance to the intended verified identity. A forwarded link grants
   nothing to another person.
3. The recipient sees the same business and shared work, without company
   onboarding, another agent installation, or a duplicate worker.
4. The host checks role and campaign scope on every API request. Owner approvals,
   account settings, usage and access management remain owner-only.
5. Revocation ends existing member access, including stale tabs and cached choices.
   Signing in again must not restore revoked membership.

## Two viable directions to qualify

**HireZero-managed entrance.** Keep Plow's supported agent integration, while
running the cockpit behind infrastructure HireZero controls. Store explicit
workspace memberships against validated identities; issue short-lived,
workspace-bound sessions; preserve per-workspace storage and worker credentials.
Use the supported self-hosted package path rather than tunnelling through an
owner web ticket or translating a teammate into the owner. Before deployment,
qualify server capacity, tenant isolation, persistent volume upgrade/rollback,
identity transport, credential custody and actual hosting cost. The existing
landing VM has not been qualified as a multi-workspace agent host.

**Plow-hosted entrance.** Keep the current deployment and obtain an upstream
workspace access grant and member ticket contract. Then bind that caller identity
to the host's explicit memberships instead of `IssuePlowOwner`. Platform access
and campaign roles remain separate checks. Do not guess an endpoint or turn an
owner bearer into a shared credential.

The owner has been asked which direction to take. No infrastructure migration,
new paid plan, guest access or invitation delivery has been performed.

## Ready-to-send request for Plow (not sent)

> HireZero has a campaign-review web cockpit alongside its Plow agent. Owners
> sign in through account OTP and `/v1/agents/{id}/web`. We need an invited second
> person to open that same cockpit under their own verified identity, while
> retaining owner-only approvals. The published web-launch contract currently
> refuses another account's agent. Is there a supported owner-issued workspace
> access grant, member workspace discovery and member web-ticket flow? What
> identity and revocation guarantees does the proxy provide? We will enforce
> business roles and campaign scope inside HireZero. We also need the supported
> way to update a running image while retaining the workspace volume and phone
> line; image promotion currently applies only to new installations. Finally,
> repeat web-ticket handoffs sometimes reach a browser `ERR_INVALID_RESPONSE`
> even when a first handoff and direct authenticated navigation work. Can we
> obtain the corresponding proxy response/trace without exposing credentials?

## Acceptance before calling multiplayer ready

Use two separate authenticated accounts and an isolated workspace with fictional
content. Prove invite acceptance, same-workspace continuity, private/unshared
content refusal, role boundaries, owner-only approval, logout/account switching,
expired/forwarded/reused invitations, immediate revocation, and persistence after
restart. Exercise a real hosted handoff for both people, not only mocked tickets.
No model call is required for the access-control checks. Actual group messaging
and competition-native multiplayer acceptance remain distinct from web roles.
