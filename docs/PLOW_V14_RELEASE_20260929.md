# v14 published; existing installations preserved

Published September 29, 2026 from main runtime commit
`5dc2af70572bd2a921a0e895aa911dae4c7f2052`.

```text
ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.14
ghcr.io/raydestar/hirezero-marketing@sha256:b19fc00aafdafc0d2b715db446c4e63110ba38075bea49ad16bd54c7b7d600bf
```

This rebuild includes the current host, web application, worker commands,
prompt, skills, and metering implementation. The release labels identify the
HireZero source commit; its filesystem and runtime configuration exactly match
the tested candidate.

## Release scope

The owner paused the landing-page rollout and container cleanup when the judge
began reviewing the existing submission, then authorized publishing this new
image separately. No existing Plow installation was stopped, deleted, replaced,
or upgraded. In particular, v13 `fa2771398cdfe33b8b7608d3f06798cc` and the shared
team entrance `3943820d59d372193c165ae8f5483edb` remain unchanged.

The catalog image and landing signup pin were not promoted. v14 is available in
the public registry but has not been deployed on Plow or tested with live model
requests. Preserve the current review target until the owner asks to switch it.

The landing queue commit `f1df34c` passed 11 provisioning and 15 navigation
checks, but was not installed. Production's signup cap remains unset. The
queue's notification flow is manual; it does not send automatic email or SMS.

## Checks

- Locked .NET publication, TypeScript compilation, and Vite build passed.
- The packaged cockpit API/browser workflow and container restart check passed.
  Owner identity, business profile, tasks, campaigns, owner direction, and the
  encrypted credential vault survived the restart.
- All 10 packaged Plow meter checks passed with external networking disabled
  and synthetic replies. They cover admission, physical dispatch, usage
  accounting, replay, and refusal paths; they do not prove hosted billing.
- Anonymous registry requests verified the published tag's immutable digest,
  Linux amd64 manifest, image configuration, and source revision. A fresh full
  download of every image layer was not performed.
- Build scratch and disposable check containers, volumes, and networks were
  removed. The active local employee and all hosted installations were preserved.

Compact receipts are in ignored `artifacts/plow-package-refresh-20260929`,
`artifacts/plow-check-refresh-20260929`,
`artifacts/plow-meter-check-refresh-20260929`, and
`artifacts/plow-refresh-20260929`.

## Discord follow-up (after the current review)

> A newer HireZero image is now published. Please continue your current review;
> the installation you are using has not changed or been removed.
>
> Agent: HireZero · Marketing Lead
> Agent Index ID: hirezero-marketing
> Repository: https://github.com/raydeStar/marketing-hire
> New release commit: 5dc2af70572bd2a921a0e895aa911dae4c7f2052
> New image: ghcr.io/raydestar/hirezero-marketing@sha256:b19fc00aafdafc0d2b715db446c4e63110ba38075bea49ad16bd54c7b7d600bf
> Builder UID: 32fa0d4f-ca0e-4124-b6a1-f5136d7b3750
> Demo: https://youtu.be/D6nLXgSkcuQ
> Install: https://github.com/raydeStar/marketing-hire/blob/main/docs/INSTALL.md
> Website: https://hirezero.app/
>
> v14 contains the latest cockpit UX, first-run setup, invitation permissions,
> and shift/usage fixes. The packaged workflow, restart persistence, and offline
> metering checks passed. It is published separately and has not yet had a live
> Plow deployment test; it does not silently replace the image under review.
