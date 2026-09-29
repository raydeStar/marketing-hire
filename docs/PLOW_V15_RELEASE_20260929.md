# v15: rebuilt on the merged Plow base

Published September 29, 2026 from main commit
`0fc28cdc3f8f5fc94d3fd6b00bd2244d3be9c24a` using
`scripts/build-plow-package.mjs`.

```text
ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.15
ghcr.io/raydestar/hirezero-marketing@sha256:3423298684078910fbc84fdb4943df9fd2478608ae5d4ec4505fc5057b64de73
```

The actual filesystem layers inherit Plow base
`771198a9609dcef54d44843e7da5329c17fa51b4`, pinned at
`sha256:f1e7c421b97a80f1bd17015f96daceb965f350a241f7edc7e4d856a0e3a6f8f5`.
This replaces v14's older base. A clean Git archive fixed the build inputs while
other work continued in the shared checkout.

The new official Index client handles compressed transcripts itself. The
packaging adaptation now preserves that implementation and its child-process
credential filtering, retains HireZero's worker-usage receipts and offline
agentsview collection, and refuses missing transcript payloads. See
[Index compatibility](PLOW_INDEX_COMPATIBILITY.md) for the exact boundaries.

## Verification

- Locked .NET publish, TypeScript compilation and Vite build passed.
- All 14 reporting/regression checks passed against the packaged client and
  retained historical helper; no Index credentials or network were used.
- The real packaged cockpit/browser workflow passed. Owner identity, business
  profile, tasks, campaigns, direction and the encrypted vault survived restart.
- All 10 installed gateway/meter checks passed with synthetic replies and no
  external network. They cover admission, per-install proxy routing, one-send
  accounting, replay and refusal paths.
- Release labels were added without changing the tested filesystem or runtime
  configuration. The new base's complete layer prefix matches the release.
- Anonymous registry access verified the published index, Linux amd64 manifest,
  config hashes, runtime configuration and source/base labels. It did not
  redownload every image layer.

Build staging, frozen source copies, archives and the disposable test containers,
volume and network were removed. Compact evidence remains under ignored
`artifacts/plow-base-refresh-20260929/`.

No hosted installation was stopped, removed or upgraded. The judge's workspace,
shared team entrance, catalog image and landing-site signup pin were unchanged.
v15 is publicly available for Plow's retest; this publication is not a live
hosted-model acceptance result.

## Discord follow-up

> Rebuilt and republished HireZero with our packaging script after the base
> merge. v0.1.0-plow.15 contains Plow base
> 771198a9609dcef54d44843e7da5329c17fa51b4, verified against its actual layers.
>
> Agent Index ID: hirezero-marketing
> Source commit: 0fc28cdc3f8f5fc94d3fd6b00bd2244d3be9c24a
> New image: ghcr.io/raydestar/hirezero-marketing@sha256:3423298684078910fbc84fdb4943df9fd2478608ae5d4ec4505fc5057b64de73
>
> The packaged cockpit, reporting and offline gateway/meter checks pass, and
> anonymous registry access is verified. Existing review installations are
> preserved. Please retest this new digest. Thank you!
