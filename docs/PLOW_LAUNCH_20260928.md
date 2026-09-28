# Plow launch receipt — September 28, 2026

The source, approved media and Agent Index listing are public. The running
owner installation has the compatibility fixes and reporting enabled. **Outside
users are not yet cleared for one-click installation:** the image is public,
hosted sign-in/onboarding works, and initial Plow admission is still outstanding.

## Published

- MIT source: [raydeStar/marketing-hire](https://github.com/raydeStar/marketing-hire).
- Listing: [HireZero · Marketing Lead](https://aiworthusing.com/agent-index/hirezero-marketing).
  Registered with the approved identity, repository, install guide, video and
  both images. The builder profile now displays HireZero.
- Selected 68-second promo: [Meet Chip](https://youtu.be/D6nLXgSkcuQ).
  YouTube confirmed publication and reported no copyright or community-guideline
  issues in its upload checks. The description identifies sample content and
  motion recreations; this video is marketing material, not live-work proof.
- Approved images use URLs pinned to `8ce2139`. Their hashes and the MP4 hash are
  unchanged; the earlier name remains in the baked media by the owner's choice.
- The official Index client accepted the first usage report: **77,164 tokens**.
  After the revision and reporter fix, the listing shows **113K**, rounded from
  the **112,550** tokens in actual receipts across two days. This is one owner's work,
  not outside-user traction. The inherited five-minute reporter is enabled and
  uses the persistent install identity across container restarts.

## Exact package

Release source: `c0bcc31f8713728905526a2a776439bb4c42f3b0`.

```text
ghcr.io/raydestar/hirezero-marketing@sha256:c9d3e27cf06d81e0738d7ad4619c78fbc391f8a2bdb0f10b2ab61400e02c2cc3
```

The tag `v0.1.0-plow.4` has been pushed. The GHCR package is public, and an
anonymous pull of this digest passed with an empty Docker login configuration.
Initial one-click deployment still requires Plow admission.

The release wrapper preserves the checked host, web and worker application
layers. Small overlays fix reporting and hosted entry, add OCI labels and set
`AGENT_ID=hirezero-marketing`. The owner installation retains its account,
brief, conversation, campaign, drafts and credential vault. Private volume
backups remain outside Git; a hosted deployment starts with a separate workspace.
The existing local owner installation remains on `v0.1.0-plow.2`, with reporting
working; the hosted-only fixes are in `v0.1.0-plow.4`. Its saved campaign history
has not been moved or duplicated into the new hosted workspace.

## Hosted acceptance

Plow reports the corrected image running. The account's phone-code sign-in
issued an official one-minute web launch ticket. That ticket opened the
authenticated `plow.run` cockpit, where Chip reports **Online**. Manual onboarding
saved HireZero's brief, guiding notes, site and call to action. After a full reload,
the employee's Business brief view showed the saved content and online status.
An anonymous request to the public cockpit origin returned 401.
No shift, inference, publishing connection or external message was started by
this hosted check. Voice samples were skipped rather than invented.

The raw `exe.xyz` VM URL is not the owner's web entrance. CLI activation credentials
cannot mint web launch tickets: this endpoint needs a separate account login.
Use Plow's authenticated web launch flow; never make the private VM public or
weaken cookie/CSRF checks to work around it. The hosted workspace is separate
from the retained local owner installation; this is not a data migration or a
complete adversarial tenant-isolation test.

## Real work and the approval boundary

The approved bounded acceptance assignment produced a LinkedIn draft. The
owner's correction was entered through the real cockpit's **Send back for a
redraft** control. The employee produced draft #2, linked to the original and
filed with the same campaign, with evidence and a saved B-to-A rubric review.
The revised post is 92 words and ends with the requested question. It remains
pending review. No publication occurred and no shift remains running.

The first cycle and closing reflection used 21,049 tokens. The revision cycle
and reflection used 35,386 more, all backed by provider receipts. The bounded
acceptance used twelve worker turns in total. A separate retry refused before
dispatch when it detected the old persisted output limit and used no tokens.

Known review defects remain visible: the rubric incorrectly reports the
92-word revision as missing the 80–120-word requirement. An additional queued
website-copy task produced a document whose review response still exhausted
the 4,096-token output cap. Do not present grades as independent verification
or these defects as resolved. No extra model runs were started to hide them.

Text and the cockpit share the employee and work ledger. Text is for discussion
and feedback; the cockpit records decisions against the exact content and
revision. A casual chat response does not approve a draft. Plain approval and
explicit scheduling/publishing remain different actions. The local cockpit is
loopback-only; a phone connection alone does not give a new user browser access.

## Checks and fixes

- Plow campaign workers now use the supported `off` reasoning setting.
- The worker response cap is 4,096 tokens; the meter and installed transport
  agree. The existing owner configuration was migrated to the same value.
- Usage collection includes compressed transcripts and digest-bound receipts
  from actual worker calls, deduplicated against chat response IDs. Unknown,
  reserved and synthetic usage is excluded; inconsistent receipts fail closed.
- Package builds passed, including the web build. Focused installed Gateway/SDK
  checks and 14 offline Index checks passed without model calls.
- The recurring collector initially left an agentsview daemon holding its
  database. The repaired reporter syncs without a daemon and reads the synced
  database offline; a failed sync stops that pass. Subsequent scheduled usage
  reached the public listing, preserving the installation identity.
- Real hosted sign-in reached the cockpit, but the first brief save exposed a
  private-Host/public-Origin mismatch. The corrected adapter derives this
  agent's exact `plow.run` origin from its authenticated private VM Host. The
  actual packaged host reproduced 403 before the fix and persisted a 200 save
  afterward; invalid CSRF, foreign origins and missing identity still returned 403.
- Hosted cockpit CLI checks also lacked Plow's `proxied` credential placeholder.
  The wrapper now mirrors the base's default in the parent process while keeping
  real local credentials. Read-only model-route inspection reproduced failure
  without it and success with it. No model request was sent by this check.
- Packaged browser/API, restart, upgrade, rollback and vault checks passed.
  These fixture checks remain distinct from the real work described above.
- The release history scan covered 683 commits and 4,882 blobs; the image scan
  covered 3,940 application files. No scanned credential patterns were found.
  These are bounded checks, not a guarantee that all possible sensitive data is
  absent. Owner state and credentials were excluded from the image.
- No GitHub Actions were dispatched. Workflows accept manual dispatch only;
  the disabled workflow state was restored before pushing. Disposable check resources were removed;
  compact receipts and private owner backups are retained under ignored artifacts.
- Cleanup retired the two superseded hosted checks and six obsolete local image
  records. The onboarded hosted deployment, local owner installation, current
  public image, local rollback, pinned base and all owner backups remain. Shared
  Docker layers were not counted as reclaimed disk space. The temporary account
  login credential used for the hosted check was removed.

## Remaining launch work

1. Confirm a real phone reply and useful work on the hosted deployment. Connected
   status and a saved brief do not prove a completed hosted model turn.
2. Confirm native multiplayer with distinct
   authenticated people. Cockpit review roles alone are not multiplayer proof.
3. Send the prepared [organizer handoff](HACKATHON_SUBMISSION.md#organizer-handoff--prepare-then-send)
   for initial image admission and competition verification. Neither is confirmed.
4. Have a separate person install, onboard their business and complete useful work.

Compact local evidence is in `artifacts/plow-launch-20260928/`, including
`public-image-4.json`, `anonymous-pull-4.log`, `hosted-origin-final.log`,
`history-scan.json`, `image-audit-2.json`, the publication
screenshot, Index receipt and real-work receipts. Files marked private and owner
backups are not submission material.
