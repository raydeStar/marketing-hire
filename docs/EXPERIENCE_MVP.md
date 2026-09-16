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

## Comparison audit — September 15, 2026

The authenticated Muse reference was inspected through its Chat/activity,
Library, Ideas, Goals and Feed screens. Only interaction patterns were carried
over; its private messages, personal recommendations and artwork were not added
to Thaddeus. The owner's requested departures take precedence over the reference.

| Requested behavior | Current implementation and evidence |
| --- | --- |
| Thaddeus branding, expressive SNES-style raven | The pixel SVG and whole-pixel blink, head, wing and tail animations remain. The live desktop view keeps the ledger typography and gold accents. |
| Chat, Search, Feed, Ideas, To-do, Artifacts; hidden by default | `StudyNavigation` keeps that exact order. The live reload shows an Expand sidebar button and no icon rail until opened. Icons are centered; theme and Settings are at the bottom. |
| Centered raven opens the log; model chip shows usage | The header and model tooltip retain those controls. Token details live under Log → Info. The removed standalone Log button and old composer guidance remain absent. |
| Compact growing composer; Enter sends, Shift+Enter adds a line | `MessageComposer` retains its resize observer, bounded height and composition-aware keyboard handler. These inputs were unchanged in this pass. |
| Apps designed through chat, with independent pages and close behavior | Existing generated-page and artifact UX fixtures cover clarification, custom HTML/CSS/interactions, record changes, edit/delete/restore, and returning to the previous view with the draft intact. These app handlers were unchanged. |
| Artifact filters and basic uploads | All, Apps, Notes & memory, Documents, Images and Trash are present. The packaged MVP browser workflow covers upload, preview, filtering, attachment, deletion and restore. |
| Tracked, daily, weekly and overall goals; no create-a-goal section | `TodoBoard` has the four sections, editable next steps and check-in dates. The new navigation fixture also checks finish/reopen, archive/restore and stale-edit recovery. Reopening preserves the last check-in instead of recording another one. |
| Interest-based idea groups that lead into chat/app building | Saved suggestions come from the configured model and recent chat; broad starting categories appear initially. The MVP fixture verifies model-saved categories and clicking an idea to build a custom app. Generation is explicit and budgeted. |
| Feed subscriptions and saved links, with free-plan search | Existing RSS/Atom subscriptions remain, plus temporary Brave discovery. The live settings retain 999 requests/month. Viewing the pages does not spend search quota. |
| Readable activity rows and a detail modal | The live log groups rows by date. The MVP fixture checks step selection, readable request details, desktop/mobile modal layout, Escape, and draft preservation. |
| Convenient navigation while preserving unfinished work | Discussing a Feed item, task or saved idea now offers Add to draft or Keep current draft when text/files are already present. The new fixture verifies both choices, attachments, narrow-screen controls, Escape, and zero model/search dispatch. |
| Cleanup, bounded checks and live availability | Publication removed its staging dependencies. Disposable studies and the superseded rollback package were removed after process checks. The new host preserved every study-table fingerprint, key and owner session; the bridge was not restarted. |

The final navigation package is
`artifacts/portable-muse-navigation-20260915-a/thaddeus-win-x64`.
`artifacts/muse-navigation-20260915/` holds activation, backup, cleanup and launch
receipts. Its browser proof is
`artifacts/muse-navigation-check-20260915-b/verified.json`; the earlier failed
attempt was a test locator mismatch corrected to use the textbox's accessible
role, with no application-source change between those two checks.

The broader MVP proof remains
`artifacts/muse-experience-final-b/verified.json`, and app editing/navigation proof
is `artifacts/muse-experience-ux-final/verified.json`. The final comparison changed
only `TodoBoard.tsx`, `main.tsx` and `experience.css` in runtime sources. The 53
backend and nine bridge/protocol checks from the preceding pass were reused for
unchanged inputs. The focused navigation check sent no model or search request.

For manual QA, begin at the running study, create a tracked item, check it in,
finish and reopen it, then leave a draft in Chat and choose Discuss on a saved
Feed link. Artifacts, Ideas and the activity log are ready for the same hands-on
pass. Native phone/Mac qualification and the explicitly deferred media features
remain outside this comparison pass.
