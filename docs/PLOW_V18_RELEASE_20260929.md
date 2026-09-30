# v18: onboarding saves, first shift by text, a fairer fact check

Published September 29, 2026 from main commit `9e60320`. It was built with
`scripts/build-plow-package.mjs` and retagged with release labels only. The
filesystem layers are identical to the tested candidate, and its source hashes
match the commit.

```text
ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.18
ghcr.io/raydestar/hirezero-marketing@sha256:a2bc4aa54ecd23a0ec1208b9e49812834a7ce4d0b4536b205acdf522ffc297eb
```

It is promoted as the one-click pin. Roll back by promoting v17
(`sha256:c855c24f401c16414f8c69594a709e2d69eb59f7dc54e4f953b719de7a00d747`).

## Fixes since v17

- **Onboarding saves a long drafted brief.** The editor stopped typing at a
  field's limit, but not text that onboarding drafted from the owner's site or
  interview. "Where to listen and show up" was capped at 400 characters and
  failed with "invalid profile channels". The limits are now:
  - channels and voice: 1,000 characters;
  - audience and goals: 1,200 characters.

  A save that's still too long names the field, its length and the limit.
- **The first shift is offered by text.** After onboarding by text, Chip saves
  the brief and offers the cockpit's own first shift: the biggest fix, plus the
  usual first pieces for that kind of business. On the owner's yes, the same
  first-shift service as the cockpit's button runs, and the worker starts at
  once. On GLM 5.2, 2 of 2 onboarding conversations ended with the host-worded
  offer.
- **The fact check stops flagging the owner's own facts.** A live run marked a
  correct email "[unconfirmed]" because "Subject", "costs" and "comes" weren't
  the owner's words. Ordinary verbs and an email's scaffolding no longer count
  as facts; prices are still checked through their numbers.
- **The fact check's repair can only blank or cut.** It names replacements,
  each a bracketed blank or a cut, and code applies them, so it can't add a
  claim. The old full rewrite had run GLM past its output limit.

## Verification

- Host suite, gate, `hire` and packaging tests all pass. The fact check has
  tests built from a live run's own claim list.
- The packaged meter check passed on the released filesystem.

## Known limitation

GLM 5.2 on Plow reasons at length even when asked not to. Plow ignored three
ways of switching thinking off. Some worker turns spend the whole 4,096-token
output allowance on reasoning and come back empty, so their reviews are
skipped; drafts still arrive. Raising the cap changes the metered accounting,
so it is left for after the leaderboard snapshot.
