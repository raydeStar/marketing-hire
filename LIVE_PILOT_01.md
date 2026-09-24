# Live pilot 01: learning-only marketing brief and draft

**Outcome:** The owner authorized and the local host completed two restricted internal tasks. The brief and draft are saved in the meeting ledger for review. No draft was accepted or published, and no follow-on pilot was scheduled.

## Owner terms and scope

- Working promise: “my first employee” for a personal brand selling configurable marketing agents. The buyer segment of independent technical B2B software founders remains a hypothesis.
- This run is for learning. The owner set no continuation threshold and no cap on their review time.
- The grant covered exactly [HN source 1](https://news.ycombinator.com/item?id=47667504) and [HN source 2](https://news.ycombinator.com/item?id=49703771), a source-backed evidence brief, then one local draft. It allowed no outreach, publication, purchases, account changes, provisioning, or recurring work.
- No new funding was allocated. The existing Luna OAuth subscription was used; its provider charges and quota use were not independently measured. The 10-minute host execution deadline and two dispatch limit were safety bounds, not a review-time or billing cap.

## Live receipts

| Record | Saved result |
| --- | --- |
| Meeting | `11c84cc16ebb4761933194b181c4261d`, version 19, `closed`, no error |
| Plan | Revision 2, digest `cab2bfb52589e02c143069e8289e97e896d5e99e9d42886aa3b188fb78a24fae`; CEO `accept` was advisory |
| Owner grant | `ec10b342289e497eb33672733d741dc5`; two exact URLs, two assigned tasks, two worker dispatches |
| Evidence brief | Task `c153f341d20340fda5f84ac8572e3a8c` is `done`; artifact `c04c43ade321482e8d46d6274b2d2f90`, digest `7d466e792113f5dd17d694bc432796ebb910a72bc1c9d00d726928ebf02aedda` |
| Local draft | Task `7ccc01595c814cc0a34361020087a259` is `needs_you`; artifact `ee19e58993714d7687819d8c9816c4ac`, digest `ba952875f8862923c45c1872ecd1036934939a667fda573fc294b61c332c1298`; `ownerAccepted=false` |
| Model | Live CEO, Marketing, and worker sessions each report `gpt-5.6-luna` and `thinkingLevel: high`; no fallback is configured |

The two artifacts and complete transcript are in the app's **Chat → Department conversations → First employee · learning-only content pilot**. The task board is under **Work**. A private JSON export is in `artifacts/live-proposal-meeting-11c84cc1.json` (SHA-256 `31021C1FA460C203937F34D9E56DEF33582FDE3C40C329E140CD06A196FA3A57`); the live ledger is authoritative. The earlier proposal in `LIVE_PROPOSAL_01.md` was vetoed as superseded by the owner's updated terms.

## What Marketing produced

1. **Shipping versus follow-through.** Source 1 describes a pattern of building, posting, getting brief traction, then stopping. Marketing recommends discussing the week after launch. It assumes this pattern resonates with the provisional buyer.
2. **Customer-feedback loop.** Source 1 advises contacting customers to learn why they installed a product and what they want. Marketing proposes content about customer conversations, while noting that access to customers is an assumption.
3. **Judgment in AI marketing.** Source 2 argues that AI helps a competent marketer but can yield unwanted low-quality output. Marketing proposes a founder-directed angle, while noting that buyer concern about AI output is an assumption.

The draft selects angle 1. It opens: “Technical founders often do not have a launch problem. They have a follow-through problem.” It quotes source 1, labels the B2B-founder connection a provisional hypothesis, and says the “my first employee” product promise still needs validation. It ends by asking founders what their plan is for the week after a launch post. The full text is in the saved meeting artifact.

## Editorial read

The source excerpts passed the host's exact-text checks, and both artifacts were saved and linked to their tasks. The sources are anecdotes and opinions; they do not establish demand, product capability, customer results, or ROI. The draft's opening generalizes beyond the sources and should be softened or labeled as a hypothesis before any external use. The product claim is explicitly marked for owner validation. This is a useful learning draft, not approved marketing copy.

## Verification and limits

The host was rebuilt in Release with zero warnings and errors after adding `--thinking high` to meeting role calls. Live OpenClaw session status confirmed `thinkingLevel: high` for CEO, Marketing, and worker. The meeting closed with no error; both actions are `delivered`; the hire board shows the brief `done` and draft `needs_you`; two new source evidence records exist. No send or publish action was requested. A pre-run SQLite backup and integrity receipt remain at `.data-backups/live-proposal-before-20260924T000601Z/receipt.json`.

The next decision belongs to the owner: review the saved draft, ask for a revision if desired, or accept it for further use in the app. Acceptance alone does not publish it. There is no automatic continuation.
