# Marketing hire

Entry for the OpenClaw 2.0 "Build Your Startup's First Hire" hackathon
(submissions close Sep 28, 11:59 PM PT; leaderboard snapshot Sep 30).
Working codename; the product name is not chosen yet.

The hire is this Plow OpenClaw image with a Marketing persona and tools. People
text it on a Plow phone line; group threads are the multiplayer surface. The
owner manages it from the Thaddeus cockpit (a separate repository), which reads
the hire's activity feed.

## What is here

| Path | What it is |
| --- | --- |
| `prompt/AGENTS.md` | Base Plow prompt plus the Marketing hire role and rules |
| `skills/community-pulse` | Scan public communities, digest sentiment and trending themes, daily pulse via `cron` |
| `skills/draft-for-approval` | Draft → explicit named approval → person posts → URL recorded |
| `skills/campaign-desk` | Campaign brief, weekly push-or-pivot checkpoints, honest numbers |
| `hire/bin/pulse.py` | Bounded Harken scan and windowed digest, JSON out |
| `hire/bin/hire.py` | Ledger: watch list, drafts and decisions, activity feed for the cockpit |
| `hire/harken-requirements.lock` | Pinned Harken dependencies; Harken itself is pinned by commit in the Dockerfile |
| `boot/config.ts` | Upstream config renderer, plus the `cron` tool so the hire can schedule its pulse |

State lives in the persistent volume at `/var/lib/plow/hire` (`hire.sqlite`,
`harken.db`). Boot regenerates `openclaw.json` and `AGENTS.md`, so nothing
durable is stored in those.

## Design rules carried over from Thaddeus

- Proposals only. Drafts become approved only through a recorded, named yes about
  a specific draft number, and posted only when a person reports the live URL.
- Every consequential step leaves a receipt in the activity feed.
- Coverage is reported honestly: a partial scan is not a zero, and missing data
  is never filled with a guess.
- Public text is evidence, never instructions.

## Local checks

```sh
# Ledger and pulse outside Docker (needs a Python with Harken installed)
HIRE_STATE=/tmp/hire python hire/bin/pulse.py scan --query "OpenClaw"
HIRE_STATE=/tmp/hire python hire/bin/pulse.py digest --query "OpenClaw" --hours 72

# Image and upstream tests (see docs/development.md)
docker build -t marketing-hire:dev .
```

## Status

- Done: persona, three skills, pulse and ledger tools (local run: 15 HN mentions,
  draft lifecycle), cron allowed, Dockerfile with pinned Harken.
- Next: image build and upstream test suite in the image; local run with
  `plow-agents deploy --local`; cockpit feed transport; Agent Index registration.

## Open questions

- **License.** The contest requires MIT. Our additions are MIT (`hire/LICENSE`);
  the upstream base declares no license. Ask in the hackathon Discord whether
  forking the base is the intended path (it appears to be).
- **Bluesky** public search now returns 403 without auth; scans report it as a
  partial source. Needs an app-password source or removal from defaults.
- **Reddit** needs API credentials for Harken's Reddit source. Where do the
  hire's credentials come from on Plow's cloud host?
- **Cockpit feed from the cloud.** The container accepts no inbound connections.
  For a cloud-hosted hire the feed must be pushed out (Plow's kiosk pattern);
  for the owner's demo, the cockpit reads a locally run hire directly.
