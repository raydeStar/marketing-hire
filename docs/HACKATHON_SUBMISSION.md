# HireZero: submission kit

Prepared for **AI Worth Using × OpenClaw 2.0: Build Your Startup's First Hire**.
This is review copy, not a submission receipt. Public release, listing registration,
verification and one-click admission are separate actions.

## Approved listing identity

| Field | Value |
|---|---|
| Agent ID / slug | `hirezero-marketing` |
| Name | HireZero · Marketing Lead |
| Blurb | The marketing hire that wrote its own listing. Evidence-backed campaigns, shipped only with your approval. |
| Website | https://hirezero.app |
| Repository, once public | https://github.com/raydeStar/marketing-hire |
| Installation instructions, once public | https://github.com/raydeStar/marketing-hire/blob/main/docs/INSTALL.md |
| Listing cover, once public | https://raw.githubusercontent.com/raydeStar/marketing-hire/main/docs/media/hirezero/marketing-cover.png |
| Second listing image, once public | https://raw.githubusercontent.com/raydeStar/marketing-hire/main/docs/media/hirezero/marketing-review.png |
| Listing logo | `docs/media/hirezero/hirezero-mark.png` |
| Selected video | [Claude's 68-second promo with music](media/hirezero/hirezero-promo.mp4), accepted by the owner; YouTube upload pending. The Index client takes a YouTube video ID |
| Public container image | Pending reviewed build and public registry push; record the immutable digest |

The local [README](../README.md), [install guide](INSTALL.md) and
[screenshots](media/hirezero/README.md) are ready to review. GitHub/raw links above
will not work for the public while the repository remains private. Replace the
mutable screenshot URL with a release-commit URL when submitting.

The owner selected [Claude's final promo](demo/promo/README.md) in place of the
earlier silent preview. Narration is optional and is no longer a launch task.
The chosen MP4 is 68 seconds, 1920 × 1080, H.264 with stereo AAC music. Its SHA-256
is `fce44e328a7d01314041509350a1b359d3caedeaaf033fff525968954fe52362`.
The complete file decoded successfully in the September 27 review, and its
representative scene frames were inspected. It contains sample workspace content
and motion recreations, labelled on screen; it does not establish a successful
real campaign or native multiplayer run. Preserve that distinction in the upload
description and verification material. The cover, campaign image and logo above
are proposed public assets awaiting the owner's review.

## Description

HireZero gives a founder a marketing employee and a cockpit for working with it.
Claw learns the business from an owner-reviewed brief, works on assigned campaigns,
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
| 0:00–0:10 | Business brief and assignment | Who the business serves and the concrete job given to Claw |
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
- [ ] Freeze the release commit and image; review source/history and the image
  for credentials and private workspace data.
- [ ] Resolve inherited Plow redistribution terms; preserve applicable notices.
- [ ] Obtain the owner's approval of the concrete public package, then publish
  the repository and publicly pullable image.
- [ ] Replace the install guide's preview status with the tested public route.
- [ ] Upload the approved 68-second promo; include an image and install URL.
- [ ] Register the approved identity with the official Index client; enable
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
Release commit: [fill with the reviewed public commit]
Public image: [fill with repository@sha256:digest]
Builder UID: [fill from plow-agents profile --show]
Demo: [fill with the uploaded video link]
Install: https://github.com/raydeStar/marketing-hire/blob/main/docs/INSTALL.md

Please verify the entry and enable its initial one-click deployment.
```

Check each link anonymously after publication. An Index listing alone does not
prove verification or one-click availability; admin turnaround is external.

## Evidence to refresh before publication

The September 27 scan at `fc36444` checked all reachable history: 669 commits and
4,809 blobs (107,257,046 bytes). It found no matches for the scanned credential
patterns. This is a bounded automated check, not proof that every secret or
personal detail is absent. Recheck the actual release revision and review
personal author addresses and handoff documents before changing visibility.
A successful local screenshot build does not qualify a public container or
live multiplayer.

Official references: [publishing](https://aiworthusing.com/agent-index/publish),
[Index client](https://github.com/plow-pbc/agent-index-client),
[Plow CLI](https://github.com/plow-pbc/plow-agents).
