# v20: hosted chat and website onboarding

**Current deployment:** v19 is the qualified signup and one-click image. The
failed v20 installation was retired at the owner's request. See
[the v19 routing receipt](PLOW_V19_ROUTING_20260929.md).

Built from main commit `1aba701` on September 29, 2026 (America/Denver).

Image:
`ghcr.io/raydestar/hirezero-marketing@sha256:480ddc50a25efd81af130530999c741034e235a1ba9d223f5babf27c6a04388e`

Tag: `v0.1.0-plow.20`.

## What failed and what changed

The cockpit supplied prompts through `/dev/stdin`. The current OpenClaw
`agent --message-file` command requires a regular file and rejected that input
in the hosted process, preventing chat and website onboarding.

Direct transport now writes a private UTF-8 temporary file (0600 on Unix) and
removes it after success, failure or cancellation. Local Docker transport is
unchanged. The v19 field test confirmed a real cockpit reply in 27 seconds.

The same field test exposed a second defect: a website brief finished in about
75 seconds, after the hosted web request had already returned a 504. The host
now acknowledges unfinished chat within 20 seconds. Onboarding follows that
request's saved result, including recovery after a proxy error, without sending
the prompt again or mistaking another conversation's reply for its brief.

Both images are small application overlays of v18. The Plow base, channel,
persona, text-mode hooks, model policy and metering code remain unchanged.
The owner confirmed a real SMS reply on v19 before its replacement by v20.
That is phone delivery evidence for v19, not a separate v20 SMS acceptance run.

## Checks

- 29 focused host transport, chat wait, Plow ingress, text workflow and companion
  ingress checks passed; the three chat wait checks passed again after the
  acknowledgement interval changed.
- 14 text-mode, persona and companion configuration checks passed.
- Ten network-disabled installed-runtime checks passed with synthetic provider
  replies. Coverage includes the actual chat CLI, a long Unicode prompt,
  worker admission, model/session/budget restrictions, usage and duplicate refusal.
- The web build passed.
- The final packaged browser/persistence check passed, including new onboarding
  cases for HTTP 202 and 504. Both reach brief review with one submission and
  reject an unrelated reply. Their screenshot layout checks passed.
- The final image manifest is readable without registry credentials.

Local evidence is retained under ignored `artifacts/plow-*promptfile*` and
`artifacts/plow-*onboarding-wait*` directories. Disposable package staging and
the labelled test containers, volumes and networks were removed by the runners.
One earlier failed transport-test directory in Windows Temp was retained because
automatic approval review blocked its cleanup.

## Deployment

**Live v20 acceptance failed; this image is published but NOT promoted.**
At the failed acceptance check, the landing signup and Plow one-click pins were
still on v18. Both now use v19; v20 remains unpromoted.

The owner opened v20 and started website onboarding with `https://hirezero.app`.
The page later showed 502 errors both for onboarding and ordinary state refresh.
At 2026-09-30 01:58 UTC, Plow still reported the installation as `running`, but
its official web-launch endpoint returned HTTP 409 with `NO_WEB_PAGE`.
The preserved v18 and shared-team installations still returned valid web launches.
This does not identify why v20's web service became unavailable; runtime logs,
process exits and memory events require platform inspection. Do not call this
a confirmed OOM, provider-model outage or successful final deployment.

Retired failed installation: `d99bae1c68fa9d54136cdbed33e4db1d`.
See [the platform handoff](PLOW_V20_HOSTED_INCIDENT_20260929.md).

The shared team entrance, judge's v13 installation and separate text-evaluation
agent are preserved. The owner's former v18 workspace was backed up and replaced
with the working v19 installation. The idle v5 slot was backed
up and reused for the temporary v19 test; v19 was also backed up before v20.
