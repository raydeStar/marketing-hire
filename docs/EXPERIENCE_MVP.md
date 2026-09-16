# Muse-inspired MVP experience

This pass keeps Thaddeus's branding and borrows the useful navigation pattern:
focused pages, an optional activity rail, and readable activity details that open
without replacing the current page or chat draft.

- **To-do:** Tracked, Daily to-do, Weekly to-do and Overall goals. Items have
  editable notes, next steps, dates and optional numeric progress. A recurring
  check-in records the local date and advances the next daily/weekly check-in;
  it stays active until finished. Upcoming lists these dates. These are visible
  plans, not automatic notifications or scheduled agent jobs.
- **Ideas:** broad starting categories, then explicitly requested suggestions
  from the selected model using recent conversation. The model saves categories
  and actionable prompts. Choosing an idea starts chat, which can clarify and
  build an app. Merely viewing Ideas makes no model request. Existing drafts
  and attachments must be handled before starting a different idea.
- **Artifacts:** All artifacts, Apps, Notes & memory, Documents, Images and
  recoverable Trash; title search, previews, downloads and attachment actions.
  Existing custom app pages, data editing, version history and restore remain.
- **Uploads:** UTF-8 TXT, MD, CSV and JSON, plus PNG, JPEG and WebP. Each file is
  at most 2 MiB; text is at most 60,000 characters. A message admits at most four
  files and 4 MiB combined. The study holds at most 100 files / 64 MiB including
  Trash. Contents are immutable and explicitly admitted to a chat turn, with
  authenticated downloads and versioned soft deletion. SQLite backups include
  files; JSON exports include base64 contents. Images use the compatible
  provider's image input, and the Luna bridge supplies CLI image attachments.
- **Search and Feed:** local study search remains available. Search > Web and
  Feed > Find sources offer explicit temporary Brave search. Feed subscriptions
  and saved links remain. Opening a page sends no Brave request. Standard-plan
  results do not enter history, export, or a result cache. Only the monthly
  reservation count persists; failed attempts count and do not retry. Retained
  agent research still requires separate storage rights.
- **Settings:** Save search settings saves both allowance and connection edits.
  Enter in the allowance field also saves. Checks, reloads and failed writes
  preserve unfinished allowance edits. Existing keys can change the result
  retention setting without re-entry. The local request allowance does not
  configure Brave account billing.

Podcasts, audio, speech recognition, speech synthesis, video editing and PDF
extraction remain deferred. Automatic notifications and periodic personalized
idea generation are not claimed by this pass. Native Mac and physical-phone
qualification remain separate from narrow-screen browser verification.

## Verification

Focused backend checks cover upload bounds/authentication, immutable admission,
soft deletion, tracked metadata, idea save receipts, temporary search retention
and shared allowance admission. Protocol tests substitute the CLI and verify
image arguments and scratch cleanup. No real model, Brave or GPU request is
required by these tests.

The `muse-experience.spec.ts` browser workflow uses a fictional compatible
provider and disposable study. It exercises search saves and stale edits,
tracked follow-ups, uploads and Trash, idea generation, custom app creation,
native app record changes, modal closure, draft preservation and narrow layouts.
`ux-artifacts.spec.ts` separately verifies edit/delete/restore, both themes,
navigation and app form isolation. Reuse the unchanged backend and bridge proof
when a subsequent package changes only activity presentation.

Study schema 8 adds the upload table and protects new metadata from older
editors. An older schema-7 package cannot reopen the upgraded study; use the
verified pre-upgrade backup with the retained rollback package when rolling back.
Activation and cleanup receipts are retained in
`artifacts/muse-experience-20260915/`.
