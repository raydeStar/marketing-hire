# HireZero screenshots and listing assets

## Listing assets

| Image | Intended use |
|---|---|
| [marketing-cover.png](marketing-cover.png) | 1920 × 1080 listing cover |
| [marketing-review.png](marketing-review.png) | 1920 × 1080 campaign review image |
| [hirezero-mark.png](hirezero-mark.png) | 512 × 512 listing logo |

These assets accompany the [marketing preview and narration kit](../../demo/README.md).
The cover and campaign image retain the visible **Product preview · illustrative
workspace** label. They show fictional work, not a completed live campaign.
The [listing asset manifest](listing-assets.json) records hashes, dimensions and
capture provenance. The demo was captured at `a2447ee` with concurrent local UI
edits; these images do not certify the final release commit. All three images
were visually reviewed before inclusion. Publication is still pending.

## Application screenshots

Captured September 27, 2026 from the current React application in a disposable
local host at port 5183. All business content, conversation, drafts, grades and
campaign responses are **fictional examples** derived from
[`magical-cockpit.spec.ts`](../../../web/tests/magical-cockpit.spec.ts), adapted
for a readable launch walkthrough. No live model or owner workspace was used.

| Image | Shows |
|---|---|
| [cockpit-desktop.png](cockpit-desktop.png) | Conversation and the prepared opportunity |
| [campaign-desktop.png](campaign-desktop.png) | Campaign plan and central angle beside chat |
| [campaign-pieces-desktop.png](campaign-pieces-desktop.png) | Campaign pieces grouped by week and channel |
| [cockpit-phone.png](cockpit-phone.png) | The prepared opportunity in the phone drawer |
| [campaign-phone.png](campaign-phone.png) | Campaign plan on a phone-sized viewport |
| [campaign-pieces-phone.png](campaign-pieces-phone.png) | A draft's grade, claim and review controls on a phone |

Desktop: 1440 × 960. Phone: 390 × 844. The browser rendered the actual application;
these are not image-generated mockups. The phone images are viewport captures,
not evidence of a physical-device, SMS or hosted deployment check.

Each capture passed the layout checker used by
[`ux-tour.mjs`](../../../web/tools/ux-tour.mjs), with no page JavaScript errors.
The screenshots were visually reviewed. The disposable host exited and its
workspace and web build were removed; compact local evidence is retained in
`artifacts/readme-refresh-20260927/` (ignored by Git).

The [capture manifest](capture.json) records source revision, image hashes,
dimensions and check results without workstation paths or private data.
Refresh these images after material UI changes; never relabel fictional output
as customer results or live-run proof.
