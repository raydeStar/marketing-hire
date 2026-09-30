# v19: production signup and installation routing

Verified September 29, 2026 (America/Denver), following Claude's owner-workspace
replacement. Production now selects:

`ghcr.io/raydestar/hirezero-marketing@sha256:e1f05fec62a2e1113e0944ff18cfb31ee87b8258c425aeeed2fce3803d4f3145`

Tag: `v0.1.0-plow.19`. Application source: `0f69d1a`.

## Live changes and checks

- Changed only `HZ_PLOW_SIGNUP_IMAGE` on the landing VM, from v18 to v19.
  Verified the running service's environment, active service state and public
  session response (`selfServeEnabled: true`, `Cache-Control: no-store`).
- `HZ_SIGNUP_CAP=0` remains unchanged. New customer installations request v19;
  existing workspaces are not silently migrated.
- Plow's enabled `hirezero-marketing` one-click record already selected this
  v19 digest; it was read back and did not need another promotion.
- The exact registry manifest was fetched anonymously and its SHA-256 matched
  the configured digest. Manual installation instructions now use the same pin.
- The owner's v19 workspace is `54b8a70c8d5ffc849de1932fa2c275d7`. An
  owner-authenticated launch and marketing-state request succeeded. It reported
  a connected `plow/z-ai/glm-5.2` runtime and five saved drafts.
- Retired only failed v20 workspace `d99bae1c68fa9d54136cdbed33e4db1d`, as
  requested. The official API confirmed deletion and the Aspen line (`ln_p2`)
  became available. Its web launch still returned 409, so no workspace export
  could be obtained; existing incident evidence and retirement metadata remain.
- Preserved the shared team entrance `3943820d59d372193c165ae8f5483edb`, judge
  workspace `fa2771398cdfe33b8b7608d3f06798cc` and separate text-evaluation
  registration `425d2f6cf4c505c5cd0f1c56c7bc10ea`.

The owner can select v19 explicitly through
https://hirezero.app/account/?workspace=54b8a70c8d5ffc849de1932fa2c275d7.
The existing shared team entrance retains its original installation and
membership. Generic account entry still prefers an available shared entrance;
it is not evidence that all old workspaces were upgraded to v19.

Protected landing configuration backup and receipt:
`/root/hirezero-signup-v19-20260930T023555Z/`.
Local private evidence: `artifacts/plow-v19-routing-20260929/`.

This routing update made no model calls, created no installation and sent no
SMS or Discord message. It does not replace the owner's real v19 work/SMS
acceptance or qualify a first-time signup on another person's phone.
