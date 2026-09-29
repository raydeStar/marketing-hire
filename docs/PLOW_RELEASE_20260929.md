# v13 candidate: September 28 release work / September 29 UTC

The public image contains the runtime and web application from main at
`e49529fbef95fe24d8840f437d35f5d7fee2016f`, including the bounded chat wait that
returns a pending response before the hosted entrance's 110-second cutoff.
This is a small application overlay on the existing v12 image, preserving the
Plow provider, GLM 5.2 selection, and reporting implementation.

```
ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.13
ghcr.io/raydestar/hirezero-marketing@sha256:58c237890ab30195a11db079f366dc73419afb3ea97b3d5d58880ce559a73a2d
```

The registry index and its Linux amd64 manifest are anonymously readable. This
publication is **not a completed hosted deployment**. No existing installation
or judge workspace was modified, no line was retired, and the landing service's
new-installation pin remains v12 pending cloud acceptance.

## Verification

- Locked .NET publication and the TypeScript/Vite web build passed. The build
  recorded source hashes and removed its staging/build directories.
- All three `ChatWaitTests` passed. These establish pending-before-timeout and
  continued completion in a deterministic test, not a real 110-second cloud run.
- The actual packaged Linux host passed `magical-host.spec.ts` with fictional
  work and a scripted worker, plus all four desktop/phone cases in
  `marketing-access-owner.spec.ts`. The optional private HTTPS pairing case was
  skipped because no matching trusted test route was configured.
- The Team spec now respects the actual host's pairing/email capabilities and
  verifies that a configured companion invitation takes precedence. Two cases
  use fictional invitation availability; they do not prove a real teammate join.
- Restart retained the owner account, brief, tasks, campaigns, owner direction,
  and encrypted vault. Vault directory/key permissions stayed 700/600.
- Desktop and phone Team screenshots were reviewed; the real campaign workflow
  also passed the layout checker. Disposable container, volume, and network were
  removed. Test compilation output was removed after process exit.
- No GitHub Actions, live model requests, SMS, or live campaign shifts were used.

Receipts are retained under `artifacts/plow-package-release-20260929`,
`artifacts/plow-check-release-20260929`, and
`artifacts/plow-release-tests-20260929`. The compact adapted packaged-check runner
is `artifacts/check-plow-release-20260929.mjs`; it adds the Team spec to the existing
packaged persistence check without changing application code.

## Reconciliation with the landing-site handoff

The separate landing repository already deployed automatic phone sign-in and
workspace creation in commit `f962ebd`. The public `/account/?intent=start` page
leads with **Get started** and keeps **I already have a workspace** secondary.
Restoring **Request a workspace** would restore the manual onboarding flow the
owner rejected, so no button reversal was made. Its provisioning limits remain
documented in that repository's `cms/SELF_SERVE_SIGNUP.md`.

## Cloud deployment blocker

The current official Plow API exposes zero available phone lines to the builder
account; its five lines are occupied by the owner's existing installations.
The agent PATCH schema accepts only a name. No retained-volume image replacement
is exposed by the inspected API. [Official promotion guidance](https://github.com/plow-pbc/plow-agents#register-admit-then-promote-an-agent-image)
also states that promotion affects new installations while running agents keep
their images.

Cloud acceptance therefore needs either Plow to update an existing installation
while retaining its volume and identity, additional deployment capacity, or a
separately approved retirement/replacement of an identified older test workspace
after verified backups. The v12 workspace is preserved for review as requested.
Application exports alone do not prove complete VM/vault recovery.

Do not describe the hosted chat fix as live until a hosted installation runs
the new digest and its entrance has passed the pending-response/reconnect check.
