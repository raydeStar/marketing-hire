# v16: working by text, and no invented product facts

Published September 29, 2026 from main commit
`b7c5258c1d474a485a5631dd7272c6c5514f7699`. It was built with
`scripts/build-plow-package.mjs`, then retagged with release labels only. The
filesystem layers are identical to the tested candidate, and all 430 source
hashes in the build receipt match the commit.

```text
ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.16
ghcr.io/raydestar/hirezero-marketing@sha256:b3022cc114ece48e1fc40aaeb0d62d30438796b31ec3a8df05a848b61be5834f
```

The Plow base is unchanged from v15:
`771198a9609dcef54d44843e7da5329c17fa51b4`. Anonymous registry access returns
the index with its linux/amd64 manifest. The one-click pin (`plow-agents image
promote hirezero-marketing`) now points at this digest. It changes new
installs only; running installs are not upgraded. To roll back, promote the v15
digest
`sha256:3423298684078910fbc84fdb4943df9fd2478608ae5d4ec4505fc5057b64de73`.

What changed and how it was measured: [Working by text](TEXT_WORKFLOW_20260929.md).

## Verification

- Host suite: 1,506 passed, 1 skipped. Plugin gate tests: 4. `hire` tests: 77.
  Packaging tests: 13.
- The packaged meter check passed on the released filesystem. It covers
  gateway admission, one-send accounting and replay through the real gateway,
  with synthetic replies and no network.
- Browser specs for publishing, assisted posting, shifts, chat actions,
  answers, the cockpit, layout, settings and the first-run shell: 25 passed.
  One onboarding spec flaked on a lost host connection and passed on its
  re-run.
- GLM 5.2 through Plow, on a local agent on a free Plow line, in private eval
  sessions that were never delivered to a phone:
  - v15 invented product facts in 9 of 9 replies;
  - this build invented them in none of 27 replies, including two held-out
    businesses;
  - a second pass with six made-up businesses and the answer, follow-up,
    onboarding and worker-question flows behaved as intended.
- The worker's drafts for the reviewer's exact request no longer carry the
  invented specs ("solid walnut, not veneer", "same warranty"). Missing facts
  are named blanks.

## Not yet shown

- A hosted install of this image completing a worker run and texting the owner.
  The local agent ran the same image on a real Plow line.
- The reviewer's own retest.
