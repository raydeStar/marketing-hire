# Product Hunt submission draft

This is the handoff draft for the GPT-6 Astra Challenge intended launch on
September 18, 2026 (not scheduled or submitted). It follows the [official Product Hunt launch
guide](https://producthunt.s.gy/forum-astra-launch-guide) and the [official
challenge page](https://www.producthunt.com/contests/gpt-6-astra-challenge),
checked September 16, 2026. Nothing in this document has been published.

Product Hunt's [current posting
guide](https://help.producthunt.com/en/articles/479557-how-to-post-a-product)
recommends a square `240×240` thumbnail and `1270×760` gallery images; at least
two gallery images are needed for the gallery to appear. Video is optional and
accepts a full YouTube URL.

## Listing copy

**Name:** Thaddeus

**Tagline (48/60 characters):** Conversation that becomes reviewed, durable work

**Description (234/260 characters):**

> Thaddeus is a local-first Windows agent that turns conversation into reviewed
> work: persistent apps, source-linked To-dos, research, reminders, and bounded
> MCP actions. Credentials stay on the host; consequential calls require review.

**Suggested topics (choose up to three in the form):** AI Agents, Productivity,
Privacy. Confirm the exact topic labels in Product Hunt before submission.

**Suggested shoutouts:** OpenAI, ChatGPT, Codex, and GPT-6 Astra. Astra and
Codex helped architect, implement, and test this release candidate. Thaddeus can
use a user-selected model at runtime, so the listing must not imply that Astra is
its only runtime provider.

## Maker comment draft

I built Thaddeus because useful agents need enough authority to finish real
work, but unrestricted access to a person's computer, credentials, and accounts
is the wrong bargain.

Thaddeus is a local-first Windows agent with its own bounded workspace. You can
ask it to make and maintain small apps, turn source material into editable
To-dos, research the web, schedule reminders, or prepare actions through MCP.
Credentials stay with the host. Consequential actions show their exact scope and
arguments for review before dispatch, and their receipts remain visible
afterward.

GPT-6 Astra and Codex helped turn that architecture into a working vertical
slice and helped pressure-test the UX and acceptance contract. The current
preview preserves conversation, artifacts, jobs, approvals, and logs across
restarts while keeping external delegation bounded and auditable.

This is an unsigned, host-only Windows preview. Bring your own compatible
model endpoint; isolated research-worker setup is separate. Google needs app
registration, service access and consent; other MCP connections need compatible
tools. Owner-authorized live Gmail and Calendar reads passed; delayed-send,
recurring-workflow, inbox-watch, notification, and fresh-user acceptance remain
open. Mac and phone
installation paths are later work. I would rather show those limits plainly than dress a prototype in a
borrowed wizard's robe.

## Gallery plan

1. **Conversation and Upcoming work** — show a natural-language request beside
   the durable job, schedule, timezone, and state.
2. **Exact review before action** — show recipient, subject, body, time, tool,
   and connection on the approval card.
3. **A model-designed artifact app** — show the app as its own page and the chat
   context that can update it.
4. **Source-linked To-dos** — show uploaded or public reading becoming editable
   items with source evidence.
5. **Readable receipt and log** — show the high-level result, timeline, safe
   arguments, token/search use, and technical detail disclosure.

Use the existing Thaddeus palette and raven branding. Capture at a readable
desktop size; include one mobile-responsive shot only if it remains legible.

A five-image source set is retained in
`artifacts/submission-gallery-20260916-a`. The reviewed Product Hunt exports are
in `artifacts/submission-assets-20260916-a/gallery`: all five are exactly
`1270×760`, preserve the product screenshots, and use plain-language captions.
The first two frames explicitly say `SYNTHETIC DEMO · NO EXTERNAL ACTION`; the
remaining frames say `FICTIONAL WORKSPACE`. The asset manifest records every
dimension and hash and confirms that no owner data is present.

The packaged raven component has also been rendered directly into an exact
`240×240` thumbnail at
`artifacts/submission-assets-20260916-a/thaddeus-thumbnail-240.png`. Its
`manifest.json` records dimensions, hash, provenance, zero owner data, and zero
generated-model calls.

## 60-90 second demo route

1. Open Chat and ask: "Remind me tomorrow at 9 AM to send the final launch
   screenshots."
2. Review the interpreted time and create the reminder. Open **Upcoming** to
   show that it is durable and controllable.
3. Open a model-designed artifact app, change one value through Chat, and show
   the retained app data.
4. Ask Thaddeus to turn a short source into two To-dos, then open the To-do page
   and show the source links.
5. Open an MCP action review. Show the exact arguments and deny it; no external
   effect is needed for this visual safety demonstration.
6. Open the log and show the readable timeline and receipt details.

The complete operator route is in
[`MVP_DELEGATION_MANUAL_QA.md`](MVP_DELEGATION_MANUAL_QA.md).

## Submission checklist

- [ ] Schedule the launch for September 18, 2026; the official guide says a
  scheduled launch publishes at 12:01 AM Pacific.
- [x] Name and tagline drafted; tagline is within the 60-character limit.
- [x] Plain-language description drafted; description is within the
  260-character limit.
- [ ] Confirm up to three exact Product Hunt topic labels.
- [x] Honest maker comment drafted with the product edge and Astra challenge
  context.
- [x] Square `240×240` raven thumbnail rendered from the packaged product.
- [x] Five gallery images visually reviewed and exported at the recommended
  `1270×760` size with explicit fictional/synthetic labels.
- [ ] Record and caption the short demo video.
- [ ] Provide a public landing or download URL.
- [x] Complete one owner-authorized live connector pass and retain its receipt.
- [ ] Complete current-package notification click after its helper exits. Prior
  owner-observed display is retained; warm activation does not prove cold activation.
- [ ] Run the portable preview once from a fresh Windows user profile.
- [ ] Submit from the owner's Product Hunt account.
- [ ] Be present for questions and feedback on launch day.
- [ ] Do not ask for or incentivize upvotes.

## Current candidate and unpublished assets

Archive `artifacts/portable-local-gmail-empty-query-package-r1/thaddeus-win-x64.zip` (102,389,554 bytes), source `0d0e463acac3b8621267eb8cbb1ee02ff790c00a`.
SHA256 `912b7ad53147c74a49cf7df08a6438031c1a477af7d716d854c51d091bf9b81f`. Package evidence:
`artifacts/local-check-gmail-empty-query-package-r1/verified.json`.
This is READY FOR OWNER ACCEPTANCE, not an accepted or published release.

The exact candidate is available to authorized repository users as the private
[`v0.1.0-preview` prerelease](https://github.com/raydeStar/sir-thaddeus-2/releases/tag/v0.1.0-preview).
That private download does not satisfy the public landing/download checklist.

The earlier local page/checksum payload at
`artifacts/publication-final-acceptance-20260917` belongs to the preceding
candidate and must be refreshed only after current owner acceptance. The gallery
above retains its fictional September 16 provenance; it is not a current-candidate recapture.
Proposed download links are not live-verified. See `PUBLICATION_HANDOFF.md`.

September 17 official recheck: the [posting guide](https://help.producthunt.com/en/articles/479557-how-to-post-a-product)
still specifies a personal account, 260-character description, 240x240 thumbnail,
1270x760 gallery with at least two images, and optional full YouTube video URL.
The [challenge page](https://www.producthunt.com/contests/gpt-6-astra-challenge)
displays September 18, 2026; its fetched countdown showed zero and does not verify
a cutoff. The earlier shortened launch-guide URL could not be reopened by the
web tool. Confirm exact scheduling and eligibility in the owner's submission form
after separate publication approval. No draft in this repository is proof of a
scheduled launch or submission.
