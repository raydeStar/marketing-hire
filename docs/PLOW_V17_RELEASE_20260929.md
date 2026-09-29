# v17: hosted installs text their owner

Published September 29, 2026 from main commit `0aa845c`. It was built with
`scripts/build-plow-package.mjs` and retagged with release labels only. The
filesystem layers are identical to the tested candidate, and all 430 source
hashes match the commit.

```text
ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.17
ghcr.io/raydestar/hirezero-marketing@sha256:c855c24f401c16414f8c69594a709e2d69eb59f7dc54e4f953b719de7a00d747
```

It is promoted as the one-click pin, and the listing stays verified. Roll back
by promoting v16
(`sha256:b3022cc114ece48e1fc40aaeb0d62d30438796b31ec3a8df05a848b61be5834f`).

## Fixes since v16

- **Hosted installs reach Plow through the platform's per-install proxy.** It
  can be plain HTTP on a private address. Owner texts had required HTTPS or
  loopback, so on a hosted install they turned off, and the cockpit by text
  with them. They now accept the same addresses as the Plow channel.
- **A post the owner makes themselves** (scheduled by text, with no account
  connected) is texted to them with its words when it's due, once. The
  reminder used to show only in the cockpit.

## Verification

- Host suite: 1,508 passed, 1 skipped. This includes tests for:
  - texting through a hosted-style proxy (plain HTTP, a private IP and a path
    prefix);
  - every change by text: approve, reject, post, schedule and its reminder,
    starting and stopping a shift, working hours, the weekly plan and brief
    edits.
- The packaged meter check passed on the released filesystem.
- Behavior on GLM 5.2 is unchanged from v16; see
  [Working by text](TEXT_WORKFLOW_20260929.md).
