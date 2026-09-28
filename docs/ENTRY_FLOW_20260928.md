# Entry flow and hosted invitations — September 28, 2026

The separate HireZero website now keeps signup and returning-user navigation in
one account flow. New customers request assisted setup in the owner's private
inbox; this does not provision a workspace or send email automatically. Existing
shared workspaces take priority over remembered owner-only installations, while
an explicit workspace selection remains available.

The cockpit previously offered `Create one-time code` remotely, although
`POST /api/pair/start` requires the local owner origin. The session response now
advertises `canPair` only for a local owner with trusted phone HTTPS configured.
Team waits for the actual invitation capabilities, offers email invitation only
when its provider is enabled, and offers the shared-workspace entrance when no
invitation method is configured. The server retains the local boundary and
returns an actionable error for unsupported pairing attempts.

Validation: TypeScript and the production web build passed. Twenty focused
CompanionIngress, PlowIngress and Network tests passed, including refusal of
remote pairing. The first build check raced the web asset build; rerunning after
the completed web build passed. No live model calls or real invitations were used.

Deployment boundaries: publishing a new image does not replace an existing
Plow-hosted installation. Preserve its identity, line, persistent volume and
history until a supported upgrade is available. The website can route users to
the existing connected shared workspace now. Campaign access still requires an
explicit owner sharing step after the invited member joins; role/campaign
selection during invitation is not implemented in this release. A real second
person's join, shared comment, refresh and revocation still need acceptance.

Published package: `ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.12`, immutable
digest `sha256:44522153ffd08de3e0bbd8703265f94acd478d740a2a5ea0700f088908c4a682`.
The host and web application were built from `ca86b0a` as a small overlay on
v11 (`sha256:4eae02aeb9651a3acb23f9590dded0d34caa58b44b46b6f14fe557e32e2209e4`),
preserving v11's worker and metering changes. Anonymous registry access returned
the exact published digest and a Linux amd64 manifest.

`check-plow-package.mjs` passed the actual packaged cockpit workflow (one browser
test), mutation-CSRF refusal and restart persistence for the owner, profile,
tasks, campaigns, direction and encrypted credential vault. This was a fictional
local fixture with no live inference or real invitations. Build staging and its
labelled test container, volume and network were removed; the released image and
compact receipts remain under `artifacts/plow-package-entry-20260928/` and
`artifacts/plow-check-entry-20260928/`. No existing Plow deployment was replaced.
