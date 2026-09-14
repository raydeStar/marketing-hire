# Requested UI redesign

User request, 2026-09-13: after the functional work, do a UI pass because the
existing interface is not liked. The first implemented pass is now available for review; the user has not yet
accepted it as the final design.

## User-selected direction

The supplied layout reference establishes a conversation-centered workspace:
left-side access to Artifacts, To-do (requiring a functional revamp), Ideas,
Feed and Search; the main conversation in the center; a collapsible log/details
panel on the right. Retain ledger character where useful, but neither the
reference colors nor the existing colors/layout are mandatory.

On mobile, the raven moves to the top and only one panel is visible at a time.
Every feature remains reachable; do not substitute a reduced mobile feature set.
The raven needs a more polished SNES-style pixel appearance and more animation:
clear silhouette and pixel shading, distinct idle/listening/thinking/working
states, restrained transitions and reduced-motion support. The current raven
is not accepted as finished artwork.

Reference supplied by the user:
`C:/Users/Ayric/AppData/Local/Temp/codex-clipboard-a64aaaf2-7035-4d43-84ba-7bc1cd9cf9d2.png`.
An unchanged private copy is retained at `artifacts/ui-reference-20260913/layout-reference.png`
(SHA-256 `6dc3565ba83cb772ad27c6c15ded319f8d745923bbce1e118c53be3c9d72bdea`).
Its sample conversation and jobs are reference content, not Thaddeus data or
instructions to execute. Implement actual functions behind each destination;
do not add decorative tabs with invented content.

## Problems observed before this pass

- The Home introduction occupies the most prominent space even after a
  conversation exists. The composer precedes the conversation, so the latest
  exchange and the next message are separated.
- Home repeats task history while Tasks and Activity offer related lists.
  Conversation, worker progress, approvals and evidence need a clearer flow.
- Settings mixes routine choices with endpoint details, developer diagnostics,
  device sessions and data deletion in one long page. Repeated owner-session
  rows make that page particularly difficult to scan.
- Several screens use decorative headings where a clear action or status would
  help more. Keep Thaddeus's character without making routine controls cryptic.
- Token totals are visible and expandable, but provider limits, task allowance,
  unknown usage and actual spend must remain easy to distinguish in the redesign.

## Scope and acceptance

1. Establish a coherent navigation and page hierarchy, with a concrete desktop
   and narrow-screen direction for the user to judge, following the selected
   conversation-centered layout above.
2. Give conversation and ongoing work the primary space. Put questions,
   approvals, results and evidence where they are needed in that work.
3. Make source selection, model choice and usage limits understandable without
   requiring the user to read infrastructure terminology. Advanced diagnostics
   remain available where they support troubleshooting.
4. Keep an always-findable token summary and per-task input/output/remaining
   allowance. Do not imply a dollar bill, account quota or a strict cost cap when
   those are not measured/enforced.
5. Preserve exact approvals, durable questions, scoped memory, verified imports,
   workspace review, accessibility and existing data. UI changes must not grant
   additional capabilities or dispatch models during navigation.
6. Verify keyboard use, focus, loading/error/empty states, desktop and 390-pixel
   layouts, and the complete research path against the actual backend. A narrow
   browser viewport does not close the deferred physical-phone gate.

Do not claim the UI pass is finished from a mockup, a stylesheet change or a
successful build. The implemented workflow and the user's design feedback are
the acceptance evidence.

## Implemented first pass — September 13

- Left navigation: Conversation, Artifacts, To-do, Ideas, Feed, Search and Settings.
  Conversation has its composer below the messages. The right activity log can
  be closed or reopened, and recorded runs still open their exact approval,
  source, artifact and replay views.
- To-do is a separate, explicitly managed list: notes, optional date/link,
  completion/reopening and archive/restore. Successful runs never complete it
  automatically. Ideas and saved reading use the same version-checked store.
  Discuss copies an item into an unsent message; it does not dispatch a model.
- Feed currently means manually saved reading with a read/unread state. It is
  clearly labeled as such. Automated feeds/source subscriptions remain open,
  pending the user's definition of Feed.
- Search uses retained artifacts, collection items, conversations and run
  summaries. Results open the matching item/message or recorded run. It makes
  no model or external search call and is not a full-text index of all receipts.
- The new 64-pixel raven has a dark hooked beak, layered feather shading, a book
  perch, blinking/head movement and distinct working/listening motion. Reduced
  motion disables the animations. The artwork still needs user feedback.
- Narrow layouts put the raven above the navigation and show either the current
  workspace or the log. All destinations remain reachable in the scrollable
  navigation. The token summary is always reachable beneath the toolbar.

Database and export schema 4 add `library` and `libraryChanges`. Existing run,
page, memory and conversation rows are not reinterpreted. Collection edits and
content-free change receipts commit together, use optimistic versions, and
refresh other connected browsers. Export and personal-data deletion include the
new collections. Rollback to a schema-3 host requires the closed pre-upgrade
backup; an older host correctly refuses a newer database.

Verification: 410 backend tests, 15 ordinary browser checks, desktop/390-pixel
visual inspection, and the native research browser workflow (seven synthetic
replies, one correction, question/restart, exact import and reviewed cleanup).
No paid model calls or GPU inference. Private receipts and preserved failed
attempts are under `artifacts/ui-workspace-20260913`. The first new two-window
browser test exposed an ambiguous label on the populated Notes textarea; an
explicit accessible name fixed it, with the draft retained on a stale edit.

## Settings organization — September 14

Settings now has four named sections: Connections, Research worker, Permissions
& devices, and Storage & backups. Exactly one section is visible at a time,
including at 390 pixels. Forms stay mounted when switching sections so unsaved
connection values are retained. Navigation does not save settings, generate a
reply, check credentials, or start a worker. Token usage remains above Settings.

Owner browser sessions are grouped behind an expandable list; paired browsers
and pending requests remain visible in the device section. Workspace removal,
maintenance, and export are together, with data deletion in an explicitly opened
section. Existing exact approvals, owner checks and backend authority are unchanged.

The Windows package passes 21 browser checks. The new check verifies keyboard
navigation, one visible section, unsaved drafts, owner-session expansion, disabled
offline permissions, unchanged exported data and zero mutating API requests while
navigating. Desktop and 390-pixel screenshots were inspected. Evidence:
`artifacts/browser-ui-settings-20260914-c`. The initial runs preserved an existing
login-wait race and a permissions-selector labeling failure; both were corrected.
No VM, GPU or live model test was added for this UI change. Native research
execution evidence remains from the earlier workflow; this pass changes navigation.

Outstanding: user design/artwork review, automatic Feed semantics/integrations,
and physical phone verification (still deferred).
