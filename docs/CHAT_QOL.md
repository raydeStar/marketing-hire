# Chat recovery and saved-work search

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

Active package: `artifacts/portable-chat-qol-20260916-d/thaddeus-win-x64`.
Rollback: `artifacts/portable-chat-web-20260915-a/thaddeus-win-x64`.

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
The packaged source contains one extra trailing blank line in Conversation.tsx
which was removed before commit; no executable code differs on that account.

Activation made a verified backup, preserved every existing study table, the
owner key and owner session, and kept the existing Luna bridge. The PC stays on.

## Next bounded review

The current core workflows are available for manual QA. Continue with two
remaining usability observations: unfinished composer drafts do not survive a
page reload, and a live Feed showed a repeated story with the same source URL.
Check those specific cases before expanding features. Do not reopen benchmarks,
voice, additional sandbox backends, Mac or phone setup in this pass. Wider
distribution requirements remain in [the MVP checklist](MVP_CHECKLIST.md).
