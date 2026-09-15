# Windows manual QA checkpoint

The development package is `portable-theme-toggle-20260915-a`, available at
http://localhost:5179/ while the host is running. It includes the conversation-centered UI, revised raven,
collections, Feed subscriptions, visible token accounting and the existing
research, approval and backup workflows. Use this package as the identified QA
baseline; additional development should use disposable candidate studies so
reports remain tied to one running build.

The [September 15 UX pass](UX_PASS_20260915.md) is complete and is the stopping
point for this cycle. It records the real Luna creation/edit checks, fixes,
token usage, cleanup and current bridge endpoint (5182). The follow-up theme
toggle move places it above Settings in the sidebar; source and live asset
verification are in `artifacts/theme-toggle-20260915`.

## Model-designed apps

A slow reply should release the composer after eight seconds, with a spinner
and active-task count in the header. Ask a separate ordinary question while an
app is building; it should be able to finish independently. Inspect the task
counter for completion, failure and Cancel. Open a completed app there or from
its chat receipt. Up to two replies can run in the background. See
[background behavior and limits](BACKGROUND_CHAT.md).

1. Choose **Artifacts → Build an app**, then send "Build me a mood app."
   Luna should clarify material missing details before creating it. Answer with
   your preferences (mood choices, optional notes, desired style).
2. Confirm its custom page opens automatically with its own layout and working
   controls. Add an entry in the page, then open **Show chat** and describe another
   entry. Both should appear in the same data.
3. Ask Luna to change its appearance or add a compatible interaction. Confirm
   records survive. Try **Data & history → History → Undo**, then reload.
4. Try a checklist with checkboxes, and a different small app of your own. The
   model chooses and writes each page; there are no built-in starter definitions.
5. Open an older app and ask "Redesign this as its own app page; keep my data."
   Answer any clarification. The answer should produce a saved redesign,
   including when the runtime first needs to look up the app. Existing apps
   keep their old view until explicitly redesigned.
6. In **Artifacts**, use the card's **Edit** to rename the app and change its
   description. Its design and records should survive. Use **Delete**, then
   **Trash → Restore** and confirm it returns with its records.
7. Open it, show Chat and request deletion. Its page should close and the
   receipt should point to Trash. Restore it there again. To remove only a
   record, say so explicitly or use its manual Remove control.

The app keeps its own URL and slim title/close header. Hide/show chat and check
that a draft survives. On mobile one pane is shown at a time. The selected-app
chip identifies what Chat can update; its X clears that context. Opening another
app through chat selects it for the next message when the request is only to view
it. A pending edit/deletion can use one additional model call to finish within
the same total-token allowance. Explicit smaller call limits remain enforced.

Synthetic backend/browser checks cover creation, clarification, saving, chat
updates, generated layouts, undo, containment and error recovery. This manual
pass checks actual Luna High instruction quality and design quality. No live
model call was used for automated verification. Every conversational reply is
counted; page controls do not call a model. Inspect the model name → Info.

Scope and limits are in [artifact apps](ARTIFACT_APPS.md). Generated code is for
self-contained browser apps, with bounded persistent records and no external
libraries or API integrations. A faulty page has **Data & history** outside it;
use that to recover, and ask Chat to repair the page.

This update keeps schema 7 and preserves existing study rows. Activation and
cleanup evidence for the latest UX pass is in `artifacts/ux-pass-20260915`.
Background workflow evidence remains in `artifacts/background-chat-20260915`.
The text-fix-A package
and a verified schema-7 backup are retained for rollback. No owner app was
redesigned or deleted by the automated checks.

## Existing UI and workflows

The left sidebar defaults to completely hidden, with no reserved gutter or
hidden navigation in the keyboard order. The panel button at the top left
shows or hides a narrow icon rail: Chat, Search, Feed, Ideas, To-do and Artifacts,
centered vertically, with Settings at the bottom. Labels appear on hover or
keyboard focus. At desktop width, selecting a destination keeps the rail open; the toggle or
Escape closes it. At phone widths, choosing a destination closes the rail.
Floated artifacts are deferred.

The raven is centered in the top bar when the right log closes, and stays there
at narrow widths. Click him to open Activity; Enter and Space work too. Closing
the log returns keyboard focus to him. The separate mobile masthead is removed.

Hover or focus the model name to see token usage; click it to open Log → Info
with the usage dropdown expanded. The green dot beside the model means the host
is connected (gray when disconnected); it does not certify model availability.
The separate Log button and token bar are removed. This works
at desktop and mobile widths and keeps reported, reserved and unreported usage
distinct. The log's Activity view retains the existing run history.

The message footer is cleared: no permanent mode selector, allowance line,
resource dropdown or demo shortcuts. Use + inside the composer for research or
active-task guidance. Reply limits are in Log → Info; research retains its
explicit source selection and allowance controls.

The composer is a 54–56 px single-line pill that grows with typing or pasted
text, up to eight lines (less on short screens), then scrolls internally. Enter
sends; Shift+Enter adds a line break. Empty, disabled, repeated and IME-confirmation
Enter events do not dispatch. Failed sends and newer unsent drafts are preserved.
The chat scrollbar is a slim thumb shown on hover/focus, without arrow buttons.

The served client hashes match the identified package. The model shortcut and
Info view show 131,991 reported tokens. The existing owner session was preserved. No live model
or search request was made by these checks. Evidence:
`artifacts/artifact-apps-20260915/live-verification.json` and
`artifacts/artifact-apps-browser-20260915-d/verified.json`.

This baseline includes the persistent OpenClaw control transport and
[durable composer guidance](RESEARCH_GUIDANCE.md). Additional instructions can
reach active research without starting another task or resetting its allowance.
Refresh an already-open browser tab to load the current client.

This update adds the study-wide monthly search counter and hard stop in Settings
and research setup, defaulting to 100 requests. Zero pauses new searches. Saving
the limit or checking a saved key makes no search request. Supplied-source
research works with search off; model usage remains separate.

This baseline also supports reopening the same native desktop entry without a
second host. It preserves the running study and uses a fresh one-use login link.
The separate updated Windows installer is still an unsigned, host-only preview.

Windows desktop startup refusals now stay visible in a dismissible dialog. An
occupied port explains how to close a study through maintenance before switching
versions. Unattended `--no-browser` launches retain console errors and exit code 1.
The current installer includes this change; it remains unsigned and host-only.

The previous hidden-rail package and schema-5 backup are retained for rollback.
Activation preserved all existing data-table fingerprints and the owner session,
and left the Luna bridge running. Physical-phone setup remains deferred.

## Suggested first pass

1. **Conversation and usage.** Type a short message, use Shift+Enter for a new
   line and Enter to send. Check growth and shrinking, the response, history
   after reload, model-name tooltip, Log → Info totals and task allowance. Usage that is estimated
   or unknown must remain labeled that way. See [token accounting](TOKEN_USAGE.md)
   for the CLI provider's hard-limit limitations.
2. **Workspace layout.** Open Chat, Search, Feed, Ideas, To-do and Artifacts; check
   Settings at the bottom of the rail and labels with keyboard focus. Collapse
   the activity log, narrow the browser window, and check that every destination
   remains reachable with one main panel visible. Note anything cumbersome or
   missing, including the raven's appearance and motion.
3. **Organizing work.** Add and edit a fictional to-do and idea, save a link,
   subscribe to a chosen public feed, then search for the saved content. Reload
   and confirm it persists. Remove the fictional entries when finished.
4. **Research and approval.** With a checked/enabled worker and explicit task
   allowance, use a small fictional note and public source. While it is working,
   select **+ → Guide active research** in Conversation (or open the task detail),
   send an additional instruction and check its receipt and retained history
   after reload. Confirm the same task and allowance remain visible. Receipt
   means the worker received the instruction; review whether it followed it.
   Guidance is separate from answering questions or approving writes.
   Answer its question,
   reload during the paused workflow, review the artifact and source evidence,
   and approve only the intended note. Check the imported content and activity.
   A refusal or failed run should retain an understandable explanation and usage.
5. **Settings and maintenance.** Check model/worker status, permissions, and
   Storage & backups. In Connect public search, save a small monthly limit and
   check it survives reload; use zero to pause search. No API key is needed to
   try the allowance controls. Make a backup through the normal screen and reopen the
   study. A restore should create a separate study and leave newer original edits
   intact. Keep the verified backup if trying the application-switching controls.

Start with items 1–3 for usability feedback; research makes model requests under
the configured provider. The preparation checks used no live inference or GPU.
Do not use real confidential documents or consequential external actions as
the first test data for this development preview.

For a bug report, include the action, expected result, actual result, task ID
when present, and whether reloading changes it. A screenshot of an awkward
layout is useful. Do not include host keys, provider credentials or private logs.

## MVP scope and later work

The owner has deferred broad benchmarks and additional sandbox backend work,
shelved Mac implementation and taken over the old installer-fixture cleanup.
Use [the MVP checklist](MVP_CHECKLIST.md) as the finite finish line. The existing
QEMU boundary remains; Docker Sandboxes is no longer a prerequisite for MVP QA.

- Native Mac verification and broader Linux desktop/version-transition coverage.
- Consumer installation/signing/trust, plus complete worker redistribution
  notices and applicable source provisions.
- Necessary setup/data-safety acceptance for the platforms being shipped.
- User acceptance of the interface and fixes found during this manual QA pass.

The Windows baseline has 849 passing backend tests and 17 extracted-package
checks. Its notice bundle covers 14 NuGet/runtime dependencies and 89 npm entries
through 41 preserved text files. New native/browser checks verified guidance in
the next synthetic model request, desktop/mobile layouts, question/restart,
approved import, cancellation and workspace cleanup. No live model, GPU or
hosted Actions ran. Activation and cleanup receipts are in
`artifacts/guidance-delivery-20260914-a`; disposable test builds, overlays and
the superseded rollback were removed. These are scoped development receipts,
not a claim of completed cross-platform release qualification.
