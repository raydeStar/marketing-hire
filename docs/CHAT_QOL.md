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

Active package: `artifacts/portable-model-setup-20260916-a/thaddeus-win-x64`.
Rollback: `artifacts/portable-note-maintenance-20260916-a/thaddeus-win-x64`.
The focused model-setup follow-up below reuses the earlier chat/backend evidence;
the package names in those earlier verification paragraphs are historical.

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
The current package's runtime sources match the checkout and all 426 packaged
files match their manifest. Its manifest identifies the prior commit plus 323
captured source files because the package was built before the Settings test
observer was corrected. That test-only difference changes no shipped code.

Activation made a verified backup, preserved every existing study table, the
owner key and owner session, and kept the existing Luna bridge. The PC stays on.

## Model setup from Chat and research

A new study begins with Scripted Demo so it can open without credentials. Chat
now identifies those replies as demonstrations and gives the owner a direct
**Connect a model** action. It opens Settings at the model connection heading
without discarding an unfinished message. Research setup uses the same direct
action instead of referring vaguely to settings elsewhere on the page.

The packaged `model-setup.spec.ts` verifies that path at desktop and phone width,
preserves the chat and research form drafts, saves a compatible endpoint without
calling it, and performs exactly one explicit `/v1/models` request when the user
chooses **Check saved connection**. After that check, Chat shows the configured
model and removes the demo notice. The existing Settings flow also passes after
updating its stale collapsed-sidebar observer. Evidence is in
`artifacts/model-setup-ui-20260916-a`,
`artifacts/model-setup-settings-regression-20260916-e` and
`artifacts/model-setup-20260916`. No inference, GPU, Brave request or worker boot
was used.

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

## File upload recovery

Chat and the Artifacts file shelf show the current filename and progress while
uploading. A rejected file no longer stops later files in the same selection.
Successful uploads remain attached or visible in the shelf, and a dismissible
result identifies each failure. Unconfirmed uploads are never automatically
retried; check the shelf before selecting those files again.

While chat uploads are pending, the composer remains editable but Send, another
upload, editing an earlier message and switching to research are unavailable.
The four-attachment check happens before dispatching a batch. Offline uploads
are disabled. Uploading still supports only the existing base formats and sizes.

The previous package fails the new progress assertion. The current package
passes `uploads-qol.spec.ts` with delayed upload, a rejected file between two
valid files, partial success in Chat and Artifacts, attachment limits, offline
controls, preserved drafts and a reviewed mobile screenshot. The existing
`chat-qol.spec.ts` also passes with seven synthetic replies. The first baseline
attempt had a missing test-helper import; an initial mobile screenshot kept the
navigation overlay open, and a subsequent selector targeted the obscured header
button. The final check uses the visible Close sidebar control. These fixture
corrections did not alter runtime code or require another package.

Evidence: `artifacts/uploads-qol-20260916`, `uploads-qol-ui-20260916-final-b`
and `uploads-qol-chat-regression-20260916`. All 178 captured runtime source files
match the checkout. Activation preserved every existing study table, schema 8,
owner key/session and the existing Luna bridge. The local tab was refreshed.
The installer was subsequently refreshed to match this package, then the note
update below. Worker distribution stays open.

## Unsaved note protection

Selecting another note or New note previously replaced unfinished Markdown
without warning. The editor now offers Keep editing or Discard changes first.
The old draft remains until the requested note and its revision history have
loaded successfully. Switching between workspace sections keeps the draft in
memory, and the browser warns before reloading or leaving with unsaved changes.
These are editing protections, not automatic saving or recovery after a crash.
Maintenance now refuses to close this browser's study while a note is unsaved.
It names the note and offers Return to note, preserving the draft and focusing
the editor. Save or explicitly discard it before starting maintenance. This
local-browser guard cannot preserve a draft when another device closes the
study or the PC loses power; notes still require explicit saving.

The save control shows Unsaved changes, Saving and Saved states. Loading/saving
temporarily locks the note inputs; a failed save retains the draft and the
existing version check prevents overwriting a newer edit. Revision-history
refresh failure is identified separately from a successful save. Clearing or
partially typing a new note's path no longer crashes its heading.

`note-editing.spec.ts` first reproduced the missing warning against the upload
build. The final package passes navigation/new-note protection, cancelled reload,
save/reload, failed loading, concurrent-save refusal and mobile layout checks.
`study-search.spec.ts` passes separately against that same package. Manual note
saves create audit entries but dispatch no model; both checks used zero model
and search requests. Evidence is `artifacts/note-editing-ui-20260916-final`,
`artifacts/note-editing-search-20260916` and `artifacts/note-recovery-20260916`.

An early candidate had non-UTF-8 ellipsis bytes introduced by a local edit; those
were corrected before activation. An observer initially waited for a reload it
had intentionally cancelled; another assertion incorrectly treated manual-save
audit entries as model requests. The final observer checks the real cancelled
navigation and zero model dispatches. These corrections did not suppress product
errors. Rejected candidates and fictional studies were cleaned after exit.

Activation preserved all existing study tables, schema 8, owner key/session and
the Luna bridge, with a verified backup. All 179 selected runtime source files
match the captured package manifest. Its refreshed host-only installer passes
eight native cases; the previous upload build remains the rollback package.


The subsequent maintenance guard is verified in
`artifacts/note-maintenance-ui-20260916-a`: both backup and no-backup modes wait
for saved/discarded notes, Return to note restores focus, and a saved note survives
a verified fixture backup and reopen alongside the chat draft. The preceding
package reproduced the unguarded close. The first observer incorrectly scoped
Settings to the primary navigation; after correcting that locator, the actual
baseline failed and the updated package passed. `note-editing.spec.ts` also
passes independently against this package. No live model or search calls ran.

Activation and cleanup evidence is in `artifacts/note-maintenance-20260916`.
The 179 selected runtime sources and 426 package files match their recorded
hashes. Activation preserved all study tables, schema, owner session/key and
bridge. The prior note-recovery package and installer are retained for rollback;
the superseded upload build, fictional studies and fixture backup were removed.
