# Chat while a project runs

An ordinary reply that is still waiting after eight seconds moves into the
background when a slot is available. Its message says work has begun and the
composer becomes available again. The original inference continues; the handoff
does not dispatch another model call or restart the task.

Up to two replies can work in the background, leaving one foreground slot for
chat. If those slots are full, the next reply stays in the foreground until it
finishes or is cancelled. This is bounded concurrency, not an unbounded queue.
An external provider can still impose its own concurrency or rate limit.

The header's spinner and number show active tasks. Click it to inspect work,
cancel a specific task, open a completed app or view its receipts. It is also
available on an app's full-page header and at phone widths. Recent background
results remain there after refresh. Completed replies appear in Chat; a
background app does not force navigation away from an ongoing conversation.
Failures stay attached to the request and appear in the task panel. Offline
status is the last observed status, not proof of continued execution.

Every task retains its own context, selected app version, deadline, cancellation
and token ledger. Concurrent edits to the same app still use version checks;
a stale change is rejected rather than overwriting the newer one. A new message
is a separate request, not additional instructions for a running app task.

Default chat now has a ten-minute wall-time ceiling, two model calls, two app
actions, 4,096 output tokens per call and 64,000 total tokens. Explicit smaller
limits remain enforced. Parallel tasks have separate allowances: two background
tasks and a foreground reply can reserve up to 192,000 tokens with these defaults.
The existing token panel includes all of them. Unknown usage is conservatively
charged; it is not a measured bill. No automatic retry or model fallback is added.

Work continues while the host runs, including when the browser closes. It does
not survive a computer shutdown as active work: interrupted runs become Needs
attention on restart, with uncertain usage retained and no automatic replay.

## Luna development bridge

The fixed Luna High bridge admits three concurrent CLI processes, uses isolated
small temporary directories, and cancels only the process for the closed request.
Its ceiling is ten minutes; the host's earlier deadline still cancels the request.
`/v1/models` exposes current occupied slots without running inference.

Each dispatched request writes a diagnostic receipt in `artifacts/luna`,
including failures: elapsed time, stage, exit status, deadline/client cancellation,
output sizes, observed tool events and available usage. Failure receipts omit raw
prompts, generated data and stderr. A provider deadline returns 504, capacity
returns 429 and an unusable provider result returns 502. The UI distinguishes
these from authentication failure. The CLI output target is included in its
prompt; this remains an uncertified transport, not a hard remote spending cap.

The owner's September 15 failure lasted about 80 seconds and returned a provider
error before the 180-second host ceiling. The old bridge kept no failure details,
so a timeout cannot be established retrospectively. The failed request is not
automatically retried by this update.

## Focused verification

`BackgroundConversationTests` covers concurrency, independent completion,
cancellation, deadlines, restart and error classification alongside the existing
conversation/app tests. `node --test scripts/luna-bridge-server.test.mjs
scripts/luna-protocol.test.mjs` uses fictional child processes, no Codex inference.
The packaged `background-chat.spec.ts` holds one synthetic app response while
chat finishes, cancels another task and checks completion, reload and mobile UI.
The existing `artifact-crud.spec.ts` verifies fast app opening is preserved.
Clean those fictional studies and build intermediates after owned processes exit.
