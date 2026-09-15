# Windows manual QA checkpoint

The running development package is `portable-notices-20260914-b`, available at
http://localhost:5179/. It includes the conversation-centered UI, revised raven,
collections, Feed subscriptions, visible token accounting and the existing
research, approval and backup workflows. Use this package as the identified QA
baseline; additional development should use disposable candidate studies so
reports remain tied to one running build.

The newer persistent OpenClaw control transport has passed separate backend and
native workflow checks in source; it is not yet installed in this QA baseline.
Sending mid-task guidance from the chat composer remains unfinished. UI feedback
on this baseline can proceed while that candidate is prepared.

The previous handoff-C package is retained as one rollback. Activation verified
a fresh backup, preserved all study-table fingerprints and the existing owner
session, and left the Luna bridge running. Physical-phone setup remains deferred.

## Suggested first pass

1. **Conversation and usage.** Send a short message. Check the response, history
   after reload, visible token totals and task allowance. Usage that is estimated
   or unknown must remain labeled that way. See [token accounting](TOKEN_USAGE.md)
   for the CLI provider's hard-limit limitations.
2. **Workspace layout.** Open Artifacts, To-do, Ideas, Feed and Search. Collapse
   the activity log, narrow the browser window, and check that every destination
   remains reachable with one main panel visible. Note anything cumbersome or
   missing, including the raven's appearance and motion.
3. **Organizing work.** Add and edit a fictional to-do and idea, save a link,
   subscribe to a chosen public feed, then search for the saved content. Reload
   and confirm it persists. Remove the fictional entries when finished.
4. **Research and approval.** With a checked/enabled worker and explicit task
   allowance, use a small fictional note and public source. Answer its question,
   reload during the paused workflow, review the artifact and source evidence,
   and approve only the intended note. Check the imported content and activity.
   A refusal or failed run should retain an understandable explanation and usage.
5. **Settings and maintenance.** Check model/worker status, permissions, and
   Storage & backups. Make a backup through the normal screen and reopen the
   study. A restore should create a separate study and leave newer original edits
   intact. Keep the verified backup if trying the application-switching controls.

Start with items 1–3 for usability feedback; research makes model requests under
the configured provider. The preparation checks used no live inference or GPU.
Do not use real confidential documents or consequential external actions as
the first test data for this development preview.

For a bug report, include the action, expected result, actual result, task ID
when present, and whether reloading changes it. A screenshot of an awkward
layout is useful. Do not include host keys, provider credentials or private logs.

## Remaining broader delivery work

- Resolve and qualify the Docker Sandboxes backend; the explicit QEMU preview
  remains the tested execution path on this Windows host.
- Native Mac verification and broader Linux desktop/version-transition coverage.
- Consumer installation/signing/trust, plus complete worker redistribution
  notices and applicable source provisions.
- Remaining platform/security and broader Lab quality/resource evidence.
- User acceptance of the interface and fixes found during this manual QA pass.

The Windows baseline has 825 passing backend tests and 17 extracted-package
checks. Its notice bundle covers 14 NuGet/runtime dependencies and 89 npm entries
through 41 preserved text files. Existing browser and native research evidence
remain applicable to unchanged product/UI code; this packaging checkpoint did
not repeat paid model, GPU or worker-VM tests. These are scoped development
receipts, not a claim of completed cross-platform release qualification.
