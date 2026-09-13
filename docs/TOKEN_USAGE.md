# Visible token accounting

The shared page layout shows token usage on every screen. Expand it for recent
task receipts and input/output counts, conservative allowance charges, current
reservations and remaining allowances. Counts derive from durable run records;
refreshing the browser does not reset them. Totals cover retained host history,
exclude scripted-demo providers and identify tasks with unreported usage. They
are not an account-wide total and exclude separate CLI/benchmark activity.

Before sending a chat or research message, the composer shows the token allowance,
output limit and model-call limit. Budget controls submit that exact snapshot to
the host. Default chat remains one model call, 4,096 output tokens and a 64,000
total allowance; research defaults to six calls, 4,096 output tokens per call and
96,000 total. Previous conversation context sent again counts as input usage.

Reported usage, conservative charges and reservations are different quantities.
Missing usage never becomes measured zero. For uncertified providers the broker
reserves the remaining allowance and retains it if usage cannot be established.
Further dispatch is refused when the allowance is exhausted. Such providers can
still exceed a requested limit on an in-flight call; this is not a hard remote
spend cap. Strict token admission refuses providers without certified bounds,
including the current Luna CLI bridge. There is no silent model fallback.

No dollar estimate, provider billing total or Codex account quota is inferred.
Deletion changes the retained-history total; exports preserve the recorded
accounting. Daily/monthly account-wide monetary ceilings remain future work.
