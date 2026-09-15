# Windows manual QA checkpoint

The development package is `portable-hidden-rail-20260915-a`, available at
http://localhost:5179/ while the host is running. It includes the conversation-centered UI, revised raven,
collections, Feed subscriptions, visible token accounting and the existing
research, approval and backup workflows. Use this package as the identified QA
baseline; additional development should use disposable candidate studies so
reports remain tied to one running build.

The left sidebar defaults to completely hidden, with no reserved gutter or
hidden navigation in the keyboard order. The panel button at the top left
shows or hides a narrow icon rail: Chat, Search, Feed, Ideas, To-do and Artifacts,
centered vertically, with Settings at the bottom. Labels appear on hover or
keyboard focus. Selecting a destination keeps the rail open; the toggle or
Escape closes it. This behavior is the same at desktop and narrow widths.
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
`artifacts/hidden-rail-20260915-a/live-verification.json` and
`artifacts/hidden-rail-20260915-a/visual-verification.json`.

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

The previous centered-raven-A package is retained for rollback. Activation verified
a fresh backup, preserved all study-table fingerprints and the existing owner
session, and left the Luna bridge running. Physical-phone setup remains deferred.

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
