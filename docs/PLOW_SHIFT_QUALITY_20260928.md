# Shift quality correction — September 28

Latest published candidate: `ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.11`

Immutable image:
`ghcr.io/raydestar/hirezero-marketing@sha256:4eae02aeb9651a3acb23f9590dded0d34caa58b44b46b6f14fe557e32e2209e4`

Application source: `1b3009c`. The image is a small overlay on v10. The supplied
Plow proxy, GLM 5.2 route, session identity, output cap and usage reporter remain
unchanged. Plow shift prompts now allow 64,000 escaped bytes, and its request
transport allows 80,000 bytes. Larger requests require larger durable token
reservations within the owner's existing grant; actual provider usage settles
the hold. Other model routes retain their previous context limit. Anonymous
manifest access was verified. No verification or deployability flag changed.

## Latest live acceptance result

The v10 cloud test completed planning but refused both create packets before
inference because their required context exceeded its 16,000-byte allowance.
It used one model turn and 3,345 reported tokens, with no saved deliverables.
The owner then explicitly authorized a larger context allowance.

The published v11 image completed real, metered Plow work: a sourced campaign
plan, one LinkedIn draft and its image, and a separate revision responding to
owner feedback. The original draft remained unchanged and was marked rejected;
the revised draft stayed pending approval in the same campaign. The revision
met the requested 80–100 word range and retained the offer, approval condition
and call to action. No generated piece was approved or posted.

Two v11 phases used 17 model turns and 101,060 reported tokens. The first stopped
at its 12-turn limit and wrote a report. The revision phase used five turns and
was paused after the requested work. Including v10, this acceptance exercise
used 104,405 reported tokens. Final application exports and hashes were retained
privately before disposable-workspace cleanup. Both v10 and v11 test installations
were retired after export hash verification; four existing owner installations
remain. No owner or judge installation was changed by these tests.

This is a **mixed quality result**, not a claim of unattended readiness:

- The campaign plan had 758 words before its review and source appendix against
  the requested 350–500. The host's range matcher accepts "words" but misses the
  common "350-500 word campaign plan" wording. Its overall A label coexisted with
  explicit unmet-assignment blockers after repeated review/revision turns.
- The plan was filed as a campaign piece, but `planWikiId` stayed null. Automatic
  attachment's title matcher does not accept the colon in "campaign: seven-day
  activation plan". The package consequently said no plan was attached.
- The source-use ask was flagged unmet even though both requested sources were
  read and cited in the saved document. Review currently tests URLs against the
  body before the host appends its source list; using a source and printing its
  URL inline need different checks.
- The live feed repeated entries while requests overlapped. Also, the shift
  dialog still describes a fixed 25,000-token hold; that copy needs to reflect
  the larger context-dependent reservation. These UI follow-ups are unmodified.
- A revision added a local brand-origin claim inferred from the company brief.
  It still needs human review; a rubric grade does not establish factual truth.

Focused verification for the context change: 38 backend cases, nine Python
shift-ledger cases, and 28 Plow fetch-guard cases passed. The real installed SDK
and Gateway also passed with a large synthetic packet and no network, then the
packaged host passed its persistence checks. The web build, secret scan and
whitespace checks passed. Build scratch and local disposable containers,
networks and volumes were removed. No hosted Actions were used.

Receipts: `artifacts/plow-live-acceptance-20260928/`,
`artifacts/plow-package-context-budget-20260928/`,
`artifacts/plow-check-context-budget-20260928/`, and
`artifacts/plow-meter-check-context-budget-packaged-20260928/`.

The earlier findings below describe prior images, not the v11 result.

## What changed

- Assessment and revision use separate bounded JSON responses. A malformed
  review gets one explicit compact retry; all eight rubric scores are required.
  Long work is revised with short exact replacements. Edits need another review
  before inheriting a grade; the last assessed version survives a failed review
  or exhausted budget. Failed JSON and oversized output remain charged to the
  shift and stage. Structural parse diagnostics contain no reply contents.
- Full-length owner feedback fits the real task-store limit, including a
  1,000-character note with a long document title. The full note is retained in
  both the task and redraft record. Change-direction notes also keep their full
  text instead of losing their ending to added boilerplate.
- Explicit assignment/redraft URLs are considered before model-selected pages,
  with the same HTTPS, host allowlist and public-address restrictions. Bounded
  owner-source excerpts survive optional-context trimming. Required context that
  cannot fit is refused before inference, rather than silently shortened.
- Directory excerpts retain named outbound links as evidence without following
  them. The prompt distinguishes current citation numbers from an older draft's
  numbers, internal campaign dates from external deadlines, and owner goals from
  official judging criteria or observed results. These prompts are safeguards,
  not a guarantee that generated claims are verified.
- A campaign plan made for an assigned campaign attaches to that campaign. It
  does not create a duplicate or replace a different existing plan. A failed
  self-review appears as a campaign-piece blocker with no grade.
- A four-hour shift can be started directly from the cockpit.

## Evidence

- `npm run build` passed.
- 64 focused backend tests passed, including the new `ShiftRecoveryTests` and
  existing shift, redraft, review, campaign, series, polish, video, experience,
  page-proposal, customer-note and experiment callers.
- The actual packaged Linux host passed `magical-host.spec.ts` and the focused
  four-hour case in `shifts.spec.ts`, using a scripted runtime and real APIs.
  The former covers Today, campaign packages, first-shift behavior and continuity;
  the latter starts a real four-hour fixture shift through the phone dialog.
- Desktop and phone screenshots were visually reviewed. The package workflow's
  layout checker passed. Workspace identity, profile, tasks, campaigns, owner
  direction and the credential vault survived a fixture restart.
- Secret scan and diff whitespace checks passed. No hosted Actions or live
  inference were used for these checks.

Local receipts (ignored, not part of the public image):

- `artifacts/shift-recovery-check-20260928/all-callers-final.trx`
- `artifacts/plow-package-quality-20260928/{receipt,published}.json`
- `artifacts/plow-check-quality-verified-20260928/receipt.json`
- `artifacts/plow-check-quality-verified-20260928/screenshots/`

The initial browser attempts found test-selector/viewport-transition issues;
the final phone test opens the dialog at phone width and clicks its visible
choice label. Their failed receipts remain. All of these disposable containers,
volumes and networks were removed after exit; the package builder removed its
own publish/build scratch. The published image and v8 rollback remain.

## Cloud retest and follow-up

The owner approved retiring the stopped local Willow registration after a
read-only volume backup. Its original local volume and private archive remain.
v9 then booted on the freed line and opened through HireZero phone sign-in.
A separate HireZero brief, campaign and one sourced plan assignment were saved.
The existing four-hour Alder shift and its data were not moved or stopped.

The bounded v9 test used two model turns and 4,565 reported tokens, including
its closing report. It did **not** finish the plan: the Chamber directory returned
403 from the cloud reader, and the complete create packet exceeded its input
allowance. Required evidence was not silently shortened; that create call was
refused before inference. The shift was stopped, both private application exports
were saved and hashed, and the disposable v9 registration was retired as part of
the owner's requested workspace cleanup. No generated work was approved or sent.

v10 fixes the reproduced context problem. A create request now includes only the
requested deliverable's additional format instructions, instead of every video,
page, experiment and draft format. Common factual, approval and budget rules stay
in force. Failed page reads are supplied as protected source gaps, so the writer
can identify missing evidence without pretending to have read it.

- 61 focused backend tests passed, including a 1,000-character assignment, two
  protected 1,400-character source excerpts, and a failed-source notice. Restoring
  the unrelated format instructions reproduces the refused create request.
- The v10 web build and packaged Linux workflow passed. The package also switched
  v10 to v9 and back on one disposable volume: owner identity, profile, tasks,
  campaigns, owner direction and vault key survived.
- Public access to the v10 registry manifest and both child manifests passed.
  Build scratch and the test container, volume and network were removed.
- Receipts: `artifacts/shift-recovery-check-20260928/context-fit-callers.trx`,
  `artifacts/plow-package-context-20260928/{receipt,published}.json`, and
  `artifacts/plow-check-context-20260928/receipt.json`.

**At v10 publication, its cloud quality retest was still pending.** That evidence
proves local packaging and retained-volume updates, not a completed hosted update
or model quality. The judge's installation is to remain on its existing build;
no other account's installation was identified or modified.

## Hosted upgrade boundary

[Plow's documented promotion](https://github.com/plow-pbc/plow-agents#register-admit-then-promote-an-agent-image)
changes new installations; running agents retain their images. Its documented
CLI has no image replacement that retains an existing agent and volume. A new
cloud deployment is a new workspace, not an upgrade. Application JSON exports
are not a complete VM/vault snapshot or an automatic restore.

Keep the active workspace intact until Plow provides a retained-volume update,
or an explicitly tested migration carries its full state forward. The local
Compose upgrade path already retains storage and requires no repeat onboarding.
The catalog still has no admitted `hirezero-marketing` row; verification and
deployability flags remain off pending a successful updated-image cloud retest
and organizer action. No prospects were contacted by this release work.

## Workspace consolidation check

The owner requested that the unidentified judge's installation stay on its
existing build. Only the owner's authenticated agent list was inspected; no
judge workspace was identified or changed.

The stopped local Willow registration and the disposable v9 quality-test
registration have been retired. Willow's local volume and verified archive
remain; both v9 application exports were saved and hashed before retirement.
Four owner cloud installations remained at the 17:04 UTC inventory:

- Alder/v8: the current HireZero business workspace. Both application exports
  were saved and hashed. At 10:58 MDT its four-hour shift paused itself on an
  unsettled model turn while reviewing X draft 4. The host recorded 26 turns
  and 170,776 tokens for that shift; this excludes any as-yet-unsettled usage.
  The meter's last provider receipt was still `unknown` at 17:06 UTC, so the
  pause is not merely a stale UI indication.
  No resume, replay or new inference was requested during the cleanup audit.
- Elm/v7: the retained shared browser entrance. Fresh exports were saved and
  hashed; the Team view showed only the owner and the vault showed zero keys.
  Keep it until shared entry has been verified on the main workspace.
- The v4 and v5 setup installations: Plow still lists them as running, but fresh
  authenticated entrance attempts timed out. The earlier v4 exports remain;
  fresh exports could not be obtained. They were retained pending confirmation
  that no new owner work was added after the recorded setup tests.

Private exports and compact receipts are under ignored
`artifacts/plow-cleanup-20260928/`. These application exports do not include
Plow chat history or a complete runtime-volume backup. No landing-service
configuration, teammate access or competition admission flag was changed by
this audit.
