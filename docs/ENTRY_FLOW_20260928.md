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
