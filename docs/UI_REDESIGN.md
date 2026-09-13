# Requested UI redesign

User request, 2026-09-13: after the functional work, do a UI pass because the
existing interface is not liked. This is outstanding work, not a completed
design review. A preference question has been sent; no visual direction has
been accepted yet.

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
   and narrow-screen direction for the user to judge. Conversation-first is a
   proposal; the pending user preference can change it.
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
