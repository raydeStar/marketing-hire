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
were visually reviewed before inclusion and approved by the owner. They were the
original [Agent Index listing](https://aiworthusing.com/agent-index/hirezero-marketing)
images, using URLs pinned to the release commit. Their approved bytes are unchanged;
the portrait set below replaces the gallery images.

### Portrait listing shots

On desktop, the Agent Index shows each listing image in a tall frame (about
189 × 358, `object-fit: cover`), which crops the 16:9 images above. These
1170 × 2214 alternatives match that frame. They were captured September 28 from the
current React UI at a 390 × 714 phone viewport at 3× scale, using the same
fictional fixture as the screenshots below, with the illustrative-workspace label on top.
The owner approved all three for publication on September 28, 2026. Their order,
dimensions and SHA-256 hashes are recorded in [the portrait manifest](listing-portrait-assets.json).
The live gallery uses **cockpit, then review**, pinned to commit `a6e548c`.
Adding a third image makes the Index split the gallery into two short rows,
cropping the portraits again. The plan image is published here as an additional
asset. The original video and other listing fields were left unchanged.
Below 821 px wide, the listing
switches to a 4:3 frame, so on narrow screens only the middle of a portrait image shows.

| Image | Shows |
|---|---|
| [listing-phone-cockpit.png](listing-phone-cockpit.png) | Chip's prepared recommendation in the phone cockpit |
| [listing-phone-review.png](listing-phone-review.png) | A LinkedIn draft with grade, claim, blocker and review controls |
| [listing-phone-plan.png](listing-phone-plan.png) | Campaign status, dates, goal progress and plan |

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
