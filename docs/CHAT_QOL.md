# Chat and workspace recovery

September 16, 2026: the local Windows MVP now offers Retry on failed, cancelled
and interrupted chat replies; Try again on plain completed replies; Copy; and
Edit and resend. Editing restores a copy in the composer, including available
original attachments and selected app. Replacing an unfinished draft requires a
choice in the interface. Sending the edit keeps the earlier conversation.

A retry is an explicit new attempt with its own usage and log. It preserves the
original message, conversation context, date and limits, while using the current
model connection and fresh data for the selected app. The original attempt and
its charges are retained. An idempotency key prevents an interrupted acceptance
request from dispatching the same retry twice. There is no automatic provider
retry, budget increase, or hidden inference. Retrying is unavailable if any
attempt already completed an app action; ask for the desired change instead.

Chat shows one user message with selectable reply attempts. Later prompts use
the most recent successful reply from that family. Earlier replies remain in
the log and are reachable through local search. Later messages are not rerun.
Scrolling up exposes a Latest messages button; streaming does not pull a reader
away from older messages. User line breaks are preserved. Source disclosures
stay within the scrollable transcript instead of expanding the whole workspace.

Search now includes app titles/descriptions and uploaded-file names alongside
notes, conversations, collections and runs. Results open the app or file preview;
deleted files lead to their Restore control. File-content indexing is not claimed.
These searches run locally with no model or Brave requests.

## Verification and current package

Active package: `artifacts/portable-task-recovery-20260916-b/thaddeus-win-x64`.
Rollback: `artifacts/portable-draft-feed-qol-20260916-b/thaddeus-win-x64`.

- 49 focused backend tests passed: conversation history, retry admission,
  authentication/CSRF, duplicate dispatch, token accounting, current app versions,
  deleted attachments, cancellation and artifact action guards.
- Packaged `chat-qol.spec.ts` verifies failure/retry, copy, edit with attachments,
  draft replacement, keyboard handling, explicit regeneration, old reply search,
  reload, scrolling, cancellation, app-action guards, mobile and offline controls.
- Packaged `study-search.spec.ts` verifies app/file lookup, preview, Trash restore
  and preserving a chat draft without any inference.
- Packaged `ux-artifacts.spec.ts` verifies native app forms, navigation,
  shelf editing and reversible deletion; `experience-navigation.spec.ts` verifies
  recurring tasks, stale-edit handling and preserving Feed/Ideas drafts.
- The earlier candidate passed both `muse-experience.spec.ts` cases and the
  source-disclosure browser check. Their unchanged behavior reuses those receipts;
  the final candidate also exercises the touched navigation and artifact paths.

Evidence is under `artifacts/chat-qol-20260916` and the named browser evidence
directories. Routine checks used local synthetic providers; no Luna, GPU, worker
VM, Brave quota or GitHub Actions were used. Test studies and superseded package
binaries are disposable; keep their compact receipts, screenshots and source.
The current package's runtime sources match the checkout. Its manifest identifies
the prior commit plus the captured source hashes because it was built before
the task-recovery commit. Documentation updates change no shipped code.

Activation made a verified backup, preserved every existing study table, the
owner key and owner session, and kept the existing Luna bridge. The PC stays on.

## Draft recovery and duplicate Feed stories

Unsent messages now recover after reload in the same browser tab and sign-in.
The draft includes attachment references, selected app, research sources and
limits. It uses browser session storage, expires after seven days, and is not
sent automatically. It is not a cross-device backup or a guarantee after closing
the browser. Acknowledged sends clear the sent text and attachment selection;
unavailable files/apps produce a review notice. Recovery failures remain visible.
Signing in again starts a separate draft namespace. No credentials or file
contents are added to the draft cache. Guidance drafts reopen as ordinary chat.

A publisher changing an RSS item ID no longer creates another visible story
when the exact article URL and subscription match. Existing duplicates share
read state, feedback and saved-link associations. Normal feed retention can
still age out old entries; refreshed stories retain those associations when an
old alias ages out. Saved notes are preserved. Different publishers and entries
without URLs remain separate.

Evidence: `artifacts/draft-feed-qol-20260916` contains 45 initial focused checks
and 40 final Feed checks (46 distinct backend checks with reused authentication
evidence). Packaged `draft-recovery.spec.ts` passed reload, attachments, app
context, research limits, no automatic send, missing context, invalid storage,
separate sign-in and mobile checks. The existing chat QoL test passed again.
Both Feed ranking/reader tests passed against the final package. The first
combined browser run found an ambiguous test selector for Today in the Feed
and activity log; scoping it to the Feed heading fixed the test. All inference
was synthetic; live model calls, GPU use and Brave requests remained zero.

The core local workflows are ready for continued manual QA. Further work should
address observed usability failures, not extend the feature list. Do not reopen
benchmarks, voice, additional sandbox backends, Mac or phone setup in this pass.
Wider distribution requirements remain in [the MVP checklist](MVP_CHECKLIST.md).

## To-do and Ideas recovery

To-do offers Undo after a check-in, completion/reopening, archive or restore.
It restores the exact prior record, including its prior next check-in, without
touching newer changes. Both the UI and the existing server version check refuse
a stale Undo. This is an immediate Undo while the current To-do page remains
open; it is not a permanent revision browser. Editor saves clear that Undo.
Tracking dates remain manual planning aids, visible in Upcoming, with no promised
automatic reminders or notifications.

Ideas shows the latest request's progress, success, cancellation or failure on
the Ideas page, including after reload. View details opens its readable log;
active requests can be cancelled there or directly in Ideas. Fresh ideas starts
an explicit new attempt and its own accounted usage. Existing suggestions remain
unchanged after a failed request. Opening the Ideas page never calls a model.

The final packaged `task-recovery.spec.ts` passes exact schedule restoration,
protection against newer edits, archive/completion Undo, visible failures,
log navigation, reload, cancellation, an explicit successful retry and mobile
layout. The previous candidate also passed `experience-navigation.spec.ts` and
both `muse-experience.spec.ts` cases. The final change only compacts the mobile
Undo notice; unchanged navigation, settings and app behavior reuse that evidence.
Evidence is under `artifacts/task-recovery-20260916` and the named browser result
folders. No backend contract changed, so prior version/CSRF checks are reused.
All model responses were synthetic; no GPU, live provider or Brave calls.
