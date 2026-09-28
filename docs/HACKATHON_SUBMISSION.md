# HireZero: submission kit

Prepared for **AI Worth Using × OpenClaw 2.0: Build Your Startup's First Hire**.
The source, selected video and Agent Index listing are public. Verification and
one-click admission are still pending. See the [launch receipt](PLOW_LAUNCH_20260928.md)
for what has actually shipped and the remaining hosted-install checks.

## Approved listing identity

| Field | Value |
|---|---|
| Agent ID / slug | `hirezero-marketing` |
| Name | HireZero · Marketing Lead |
| Blurb | The marketing hire that wrote its own listing. Evidence-backed campaigns, shipped only with your approval. |
| Website | https://hirezero.app |
| Repository | https://github.com/raydeStar/marketing-hire |
| Installation instructions | https://github.com/raydeStar/marketing-hire/blob/main/docs/INSTALL.md |
| Listing | https://aiworthusing.com/agent-index/hirezero-marketing |
| Listing cover | https://raw.githubusercontent.com/raydeStar/marketing-hire/8ce213927d26cba3ab1786c470ebc59be7e0c15d/docs/media/hirezero/marketing-cover.png |
| Second listing image | https://raw.githubusercontent.com/raydeStar/marketing-hire/8ce213927d26cba3ab1786c470ebc59be7e0c15d/docs/media/hirezero/marketing-review.png |
| Listing logo | `docs/media/hirezero/hirezero-mark.png` |
| Selected video | [HireZero — Meet Chip, your AI marketing lead](https://youtu.be/D6nLXgSkcuQ), published September 27; YouTube ID `D6nLXgSkcuQ` |
| Container image | `ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.4`; public, exact digest anonymously pulled |

The [README](../README.md), [install guide](INSTALL.md) and
[screenshots](media/hirezero/README.md) are public. Listing image URLs are pinned
to the reviewed release commit; the approved image and video bytes are unchanged.

The owner selected [Claude's final promo](demo/promo/README.md) in place of the
earlier silent preview. Narration is optional and is no longer a launch task.
The chosen MP4 is 68 seconds, 1920 × 1080, H.264 with stereo AAC music. Its SHA-256
is `fce44e328a7d01314041509350a1b359d3caedeaaf033fff525968954fe52362`.
The complete file decoded successfully in the September 27 review, and its
representative scene frames were inspected. It contains sample workspace content
and motion recreations, labelled on screen; it does not establish a successful
real campaign or native multiplayer run. Preserve that distinction in the upload
description and verification material. The owner approved the cover, campaign
image and logo for publication on September 27. Keep their approved bytes intact.

## Description

HireZero gives a founder a marketing employee and a cockpit for working with it.
Chip learns the business from an owner-reviewed brief, works on assigned campaigns,
and brings back a recommendation with the work already prepared.

The owner can inspect the campaign plan, review pieces by week and channel,
follow claims to their evidence, compare revisions and send work back with a
specific note. Publishing actions show whether they schedule a post, save a
draft or open an external composer. The owner chooses the action.

The Plow package keeps the OpenClaw employee beside the existing HireZero web app,
host and work ledger. Its first business is HireZero itself. Only describe live
results, phone continuity or multiplayer as demonstrated after the corresponding
acceptance run is complete.

## Optional real-work verification walkthrough: 75–90 seconds

If organizers need a recorded walkthrough in addition to the selected promo,
use the accepted release and real saved work. The README screenshots use
fictional data and cannot serve as evidence of real employee output.

| Time | Show | Explain |
|---|---|---|
| 0:00–0:10 | Business brief and assignment | Who the business serves and the concrete job given to Chip |
| 0:10–0:25 | Shift progress and a saved deliverable | What the employee actually prepared; label any time cut |
| 0:25–0:40 | Prepared opportunity → campaign package | Why this work leads, the plan, pieces, evidence and blockers |
| 0:40–0:55 | One revision and its comparison | The owner's feedback and the resulting change |
| 0:55–1:10 | A second person interacting | The verified native multiplayer workflow and permission boundary |
| 1:10–1:25 | Review action, usage and install instructions | What the selected action does and how somebody else can start |

Do not claim a post was published if it was only approved or saved as a draft.
Show the actual action's label. Do not imply the employee has no tools in all
contexts: the bounded campaign worker and the main OpenClaw conversation have
different permissions.

## Release and submission checklist

- [ ] Complete the [release acceptance sequence](PLOW_RELEASE_CHECKLIST.md),
  including a real campaign shift, phone continuity and native multiplayer.
- [x] Freeze the release commit and image; review source/history and the image
  for credentials and private workspace data.
- [x] Use Plow's documented variant-image distribution route; preserve applicable notices.
- [x] Obtain the owner's approval of the selected video, images and public launch.
- [x] Publish the checked MIT repository.
- [x] Make the uploaded container publicly pullable and verify anonymous access.
- [x] Document the public Docker route; keep hosted acceptance limits explicit.
- [x] Upload the approved 68-second promo with the approved cover and install URL.
- [x] Register the approved identity with the official Index client; enable
  five-minute reports for the employee's actual usage and verify receipt.
- [ ] Request initial image admission and competition verification from Plow.
- [ ] Have a separate person install, onboard and complete real useful work.
- [ ] Confirm event registration, entrant eligibility and October 6 attendance
  or the alternative recorded segment with the organizers.

**Submission deadline:** September 28, 2026, 11:59 PM Pacific.
**Leaderboard snapshot:** September 30, 2026, 11:59 PM Pacific.
The [official rules](https://luma.com/zhkhsnpa) require OpenClaw 2.0 multiplayer,
real startup work, public MIT-licensed code, official usage reporting and a
60-second-or-longer demo. Do not generate artificial usage.

## Organizer handoff — prepare, then send

Initial one-click admission needs the builder's account UID, slug and the full
public image reference printed by the push. Ask in the
[Plow Discord](https://aiworthusing.com/discord). Verification needs the public
repository, exact commit and Agent Index ID in the
[verification thread](https://discord.com/channels/1519035948191449268/1549100840583700481).
Never include the account token or `plow-credentials`.

```text
Agent: HireZero · Marketing Lead
Agent Index ID: hirezero-marketing
Repository: https://github.com/raydeStar/marketing-hire
Release commit: c0bcc31f8713728905526a2a776439bb4c42f3b0
Image: ghcr.io/raydestar/hirezero-marketing@sha256:c9d3e27cf06d81e0738d7ad4619c78fbc391f8a2bdb0f10b2ab61400e02c2cc3
Builder UID: 32fa0d4f-ca0e-4124-b6a1-f5136d7b3750
Demo: https://youtu.be/D6nLXgSkcuQ
Install: https://github.com/raydeStar/marketing-hire/blob/main/docs/INSTALL.md
Acceptance: public image pull, hosted sign-in/onboarding, and local real campaign/revision passed.
Still pending: real hosted reply/work and native multiplayer acceptance.

Please verify the entry and enable its initial one-click deployment.
```

The image is anonymously pullable. Hosted account sign-in, connected status,
onboarding and saved-brief persistence passed. Hosted model work, phone continuity
and native multiplayer are not yet confirmed; disclose that status when requesting
verification. An Index listing alone does not prove
verification or one-click availability; admin turnaround is external.

## Source and package evidence

The September 28 scan at `8ce2139` checked all reachable history: 683 commits and
4,882 blobs (130,093,188 bytes). It found no matches for the scanned credential
patterns. This is a bounded automated check, not proof that every secret or
personal detail is absent. The added application image files were also scanned,
and no owner database, credential or private workspace was included. The public
wrapper preserves the checked application layers and adds small reporting and
hosted-entry fixes. It includes release labels and enables reporting under the
approved Agent Index identity. The launch receipt identifies the tested image.
A successful local screenshot build does not qualify a public container or
live multiplayer.

Official references: [publishing](https://aiworthusing.com/agent-index/publish),
[Index client](https://github.com/plow-pbc/agent-index-client),
[Plow CLI](https://github.com/plow-pbc/plow-agents).
