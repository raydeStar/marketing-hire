# Shift quality correction — September 28

Published candidate: `ghcr.io/raydestar/hirezero-marketing:v0.1.0-plow.9`

Immutable image:
`ghcr.io/raydestar/hirezero-marketing@sha256:59a3462991c973f648c85b96691a665f6db65ae183d1b4acc7a90d1ca5a54ddb`

Application source: `30170f24802fe8b123211fb39df83b0bebcd02e0` (including
`fef9c92`). The image is a small host/web overlay on the already qualified v8
image. It inherits the unchanged Plow proxy, GLM 5.2 policy, session checks,
token reservations and usage reporter. Anonymous access to its manifests was
verified after publication. No verification or deployability flag was changed.

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

## Deployment boundary

The updated image has been **published, not installed into the active cloud
workspace**. All five available phone lines were occupied at the deployment
check. [Plow's documented promotion](https://github.com/plow-pbc/plow-agents#register-admit-then-promote-an-agent-image)
changes new installations; running agents retain their images. The catalog
still has no admitted `hirezero-marketing` row, and both Agent Index flags remain
off pending a successful updated-image cloud retest and organizer action.

The active four-hour shift and its work have been preserved. A stopped local
installation's volume was backed up read-only as an option for freeing its line;
its original volume and registration are unchanged. Retiring that registration
also retires its Plow chat history, so it requires the owner's explicit choice.
The alternative is a Plow-supported in-place cloud upgrade.

After installation, retest one bounded create/review/revision cycle through the
supplied `PLOW_API_BASE`, inspect actual usage receipts and source fidelity, then
request organizer verification. Offline tests do not establish live model
quality or a completed deployment. No drafts were approved or published, and no
prospects were contacted by this release work.
