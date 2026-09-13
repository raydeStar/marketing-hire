# Requested UI redesign

User request, 2026-09-13: after the functional work, do a UI pass because the
existing interface is not liked. This is outstanding work, not a completed
design review.

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

## Problems observed in the running product

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
