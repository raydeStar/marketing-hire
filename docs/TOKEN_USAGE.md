# Visible token accounting

Hover or keyboard-focus the model name in the header for today's reported token
count, reservations and any incomplete-usage label. The daily count rolls over
at local midnight without deleting its durable history. Click it to open the log's
Info view with Token usage expanded. The shortcut remains visible at mobile
widths; the former full-width token bar has been removed. Activity and Info are
separate log views. The centered raven opens Activity; the model name opens Info.
Closing the log returns focus to whichever shortcut opened it.
The standalone Log button is removed. The dot beside the model is green when
the host connection is open and gray when disconnected; the tooltip names that
state explicitly. This indicates host connectivity, not provider readiness.

The same dropdown provides a Day / Week / Month graph, the retained-history
total, recent task receipts and input/output counts,
conservative allowance charges, current reservations and remaining allowances.
The model-name tooltip describes retained history across all models, rather than
attributing every token to the currently selected model. Counts derive from durable run records
and are grouped by the task's request date in local time. Refreshing
the browser does not reset them; the header's Today count rolls over at local
midnight. Retained totals cover host history,
exclude scripted-demo providers and identify tasks with unreported usage. They
are not an account-wide total and exclude separate CLI/benchmark activity.

Reply allowance, output limit and model-call controls are in Log → Info → Reply
limits. Research limits remain inside the source-selection panel, opened from
the composer's + menu. The permanent mode/allowance footer and demo shortcuts
have been removed. Budget controls submit that exact snapshot to
the host. Default chat allows up to two model calls, 4,096 output tokens per call and a 64,000
total allowance; research defaults to six calls, 4,096 output tokens per call and
96,000 total. Previous conversation context sent again counts as input usage.

[Artifact apps](ARTIFACT_APPS.md) allow two app actions: a single app lookup can
continue into an edit or deletion in the same reply. Most replies still use
one model call. The second call shares the original total allowance and must
pass token admission again; explicit lower call limits are never increased.
Active app metadata and the selected app's code and bounded recent records count as input
when sent. Clarification, creation and subsequent edits each use a normal model call;
manual forms, generated app controls, checkboxes and UI navigation use none.
App generation retains the existing reply allowance; it never silently raises it. App changes
are refused after a reported provider overrun, with usage retained in the ledger.

Reported usage, conservative charges and reservations are different quantities.
Missing usage never becomes measured zero. For uncertified providers the broker
reserves the remaining allowance and retains it if usage cannot be established.
Further dispatch is refused when the allowance is exhausted. Such providers can
still exceed a requested limit on an in-flight call; this is not a hard remote
spend cap. Strict token admission refuses providers without certified bounds,
including the current Luna CLI bridge. There is no silent model fallback.

[Background chat](BACKGROUND_CHAT.md) releases the composer after a slow reply
has waited eight seconds. It permits two background replies and one foreground
reply, each with its own allowance. All three can reserve 192,000 tokens under
the default limits; the totals above include every active run. Handoff itself
adds no inference. Default wall time is now ten minutes; explicit smaller limits
are preserved, and cancellation retains unreported reservations.

No dollar estimate, provider billing total or Codex account quota is inferred.
Deletion changes the retained-history total; exports preserve the recorded
accounting. Daily/monthly account-wide monetary ceilings remain future work.
