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

The registry index and its Linux amd64 manifest are anonymously readable.
**Follow-up: v13 is now running on Plow**, with the acceptance limits below.
The landing service's new-installation pin remains v12; the catalog was not
promoted. The published image's runtime source remains `e49529f`.

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

## Hosted rollout and remaining acceptance

The initial official Plow API inventory exposed zero available phone lines to
the builder account; five lines held the owner's existing installations.
The agent PATCH schema accepts only a name. No retained-volume image replacement
is exposed by the inspected API. [Official promotion guidance](https://github.com/plow-pbc/plow-agents#register-admit-then-promote-an-agent-image)
also states that promotion affects new installations while running agents keep
their images.

The owner subsequently authorized retiring old deployments in creation order.
Only the oldest, v4 `457cc10dc49d45831bd860a887a5c891` ("HireZero - previous
workspace"), was retired. Fresh workspace/work JSON exports were downloaded,
parsed and hash-checked first. It had no completed work or shifts, and its
settings reported zero stored vault keys. These are application exports, not a
full VM/vault recovery image. Private exports remain under ignored
`artifacts/plow-rolloff-v13-20260929/retired-457cc10dc49d45831bd860a887a5c891/`.

Plow confirmed retirement and release of `ln_p3`. The new deployment is:

- Installation: `fa2771398cdfe33b8b7608d3f06798cc`, named **HireZero v13**.
- URL: https://fa2771398cdfe33b8b7608d3f06798cc.plow.run/
- Image: the exact v13 digest above; API status **running**.
- The other four installations, including v12 retained for review, are unchanged.
- This is a fresh workspace, not a migration of the main owner's business data.

Real phone-code sign-in through HireZero succeeded. Selecting v13 reached its
authenticated cockpit and fresh onboarding screen. The deployed Team page
correctly explains that this installation has no connected shared invitation
service. No inference, shift, invitation, or publication was started.

A second ticket handoff reproduced `ERR_INVALID_RESPONSE` in the Codex in-app
browser. Direct authenticated entry in a fresh tab then succeeded. The failing
layer is not localized; repeated sign-in is not fully accepted. Shared-team
provisioning and an actual teammate join also remain unaccepted on v13.

At the recorded catalog check, Index `blessed_at` and `deployable_at` were empty,
and Plow's public agent-image endpoint returned 404 for `hirezero-marketing`.
Initial catalog admission and verification therefore still need the organizers.
The owner's earlier capacity-request draft was superseded and never sent.

Deployment, retirement, inventory, acceptance receipts and live screenshots are
under `artifacts/plow-rolloff-v13-20260929`. The [Discord review message](PLOW_REVIEW_REQUEST_20260929.md)
identifies the exact image and does not claim unresolved acceptance has passed.

The bounded chat-wait fix is deployed. Its deterministic checks passed, but a
real hosted long-running chat/reconnect test has not run on this new instance.
