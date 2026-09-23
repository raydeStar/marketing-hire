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
| `hire/bin/pulse.py` (`pulse`) | Bounded Harken scan and windowed digest, JSON out. Keyless defaults: HN, Reddit and Google News search feeds, Stack Overflow |
| `hire/bin/hire.py` (`hire`) | Ledger: watch list, drafts and decisions, versioned activity feed ([FEED.md](FEED.md)) |
| `dev/` | Local dev mode without Plow, and the [acceptance scenarios](../dev/SCENARIOS.md) |
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

## Local dev mode

Runs the same image without Plow: no identity lookup, phone line or usage
reporting. The OpenClaw Control UI is on at http://127.0.0.1:18795 and the
model is whatever account you connect there (for example Codex), so testing
spends no Plow tokens. Skills, prompt and tools are mounted from the checkout.

```sh
cd dev
cp .env.example .env   # then set DEV_GATEWAY_TOKEN to a random 64-hex string
docker compose up --build -d
MSYS_NO_PATHCONV=1 docker compose exec -T hire node /app/openclaw.mjs dashboard --no-open
```

Open the printed link once to pair the browser. Start a new chat (`/new`) after
editing a skill or the prompt. `docker compose down` stops it; `-v` also wipes
its state. Upstream checks: see docs/development.md.

## Plan

1. **Local:** pass every scenario in `dev/SCENARIOS.md`. (current)
2. **Plow:** deploy on a line, verify texting, group threads and usage reporting.
3. **UI:** a companion web app that reads the feed and sends decisions back.

## Open questions

- **License.** The contest requires MIT. Our additions are MIT (`hire/LICENSE`);
  the upstream base declares no license. Ask in the hackathon Discord whether
  forking the base is the intended path (it appears to be).
- **Bluesky** public search returns 403 without auth, so it is opt-in only.
- **Reddit** uses its public search feed by default and its OAuth API when
  `HARKEN_REDDIT_CLIENT_ID`/`SECRET` are set. Can a cloud install get per-user
  secrets at all?
- **Cockpit feed from the cloud.** The container accepts no inbound connections.
  For a cloud-hosted hire the feed must be pushed out (Plow's kiosk pattern);
  for the owner's demo, the cockpit reads a locally run hire directly.
