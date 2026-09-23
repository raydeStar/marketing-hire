# Configurable marketing agent: local continuation

2026-09-23, after the initial [sprint handoff](SPRINT_HANDOFF.md). This document
records the later local work and supersedes its statements that approval and
research evidence were pending. The earlier handoff remains a historical
checkpoint. The product has no final name. The owner's clarified target is a
configurable marketing agent, with other departments to follow later.

## Current local state

- Product checkout: `C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit`,
  branch `business/marketing-hire`, no Git remote. Original Thaddeus and
  marketing-hire source checkouts were not edited for this continuation.
- Run `./scripts/start-marketing.ps1` from this checkout. Open
  `http://localhost:5189`, unlock with this checkout's private
  `.data/host-key.txt`, select **Marketing**, then **Work**. Keep the foreground
  host terminal running. The local `marketing-business-hire` container uses the
  existing `dev_state` volume. Neither app nor container is a Plow deployment.
- The marketing brief is owner editable and versioned. Its current working name
  is **Marketing agent**; audience and goals remain for the owner to choose.
  The profile is added to new agent turns, not retroactively to saved chats.
- Work shows durable tasks, next steps, source evidence, and exact pending draft
  text and destination. Approve/reject requires the owner session and matching
  revision and content digest. The host stores a separate owner receipt; a raw
  container ledger status without that receipt is shown as unverified. Approval
  does not send or publish anything.
- Old Framewright tasks remain in the ledger as earlier integration records.
  Current product research task `3fadf2f98bd04263915046f975f5564c` is done
  and holds three verified public links. Next task
  `8d142b8aaa874ae8b86f349ffdeffc8d` waits for an owner audience and pilot
  job decision. See [research and limits](docs/LOCAL_MARKETING_RESEARCH.md).
- Two approved records in the live ledger are conspicuously fictional local
  acceptance drafts and must never be published. Draft #1 predates host receipt
  tracking and is shown as unverified; draft #2 has a matching confirmed owner
  receipt. Neither is marketing copy for the product.

## Verification

The focused ledger suite passed 4 tests: task versioning/blocker behavior,
profile/evidence replay, and exact revision/digest draft decisions. The web
production build passed, the .NET solution build passed with zero warnings and
errors, and `marketing-workspace.spec.ts` passed (1 browser test) after the
receipt UI change. A live authenticated state read after host restart returned
`connected`, profile version 2, 5 tasks, 3 evidence links, 2 test drafts and 1
host owner receipt. A direct receipt comparison confirmed draft #2 and rejected
verification of the older draft #1. Live profile editing and draft decision
replay were exercised earlier in this continuation; the same request returned
the decision and a different duplicate request conflicted. No model was called
for routine verification or the read-only research pass.

`pulse` ran two bounded scans, but the installed RSS adapter can silently omit
individual feeds. The wrapper now labels those sources unverified and overall
coverage partial. The [research note](docs/LOCAL_MARKETING_RESEARCH.md) gives
the observed counts, checked public pages and narrow inferences. There is no
validated demand, ROI or autonomous distribution claim.

## Next decision and boundary

The owner should select one audience and one pilot job, then enter audience and
goals in the Work brief. A reasonable candidate to test is founder-led or small
marketing teams wanting cited public research and human-reviewed drafts; this is
a hypothesis, not a market finding. The next local run should stay bounded and
read-only until that choice is made. Plow endpoint, hosting, credits, and
outbound channels were not needed or touched. No push, social post, DM, email,
or external account action occurred.
