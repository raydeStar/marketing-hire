# Architecture and threat boundaries

One ASP.NET host serves the compiled React app and authenticated APIs. Core owns
typed goals, outcomes, tool/model/policy contracts. Infrastructure implements
SQLite, Markdown, the runtime and model transports. Host composes them and owns
HTTP/device authorization. Lab depends on Infrastructure and invokes the same
runtime. Providers receive observations and return proposals; they cannot write
knowledge. The browser cannot execute tools or authorize itself.

The small tool surface is `knowledge.read` and `knowledge.write`, with typed JSON
requests/results compatible with a future MCP adapter. Only writes to `plans/`
are agent actions; direct user edits support `notes/` and `plans/`. No shell,
network-mutation, external plugin or background-observation tool exists.

Runtime reads explicitly scoped notes, reserves a model call, gets a typed proposal,
validates its structure, optionally repairs once, and persists an approval. Approval
is bound to run, ID, canonical typed action, `plans/` scope, target hash and expiry.
All source hashes are checked again. Denial terminates the run. Successful approval
records intent, writes, reads back the hash, and records the outcome. Factual
accuracy and conflict resolution stay human/unverified criteria.

SQLite commits run state and its next versioned event together. A file lease rejects
a second host; per-run semaphores reject duplicate execution/decisions, and version
checks reject lost updates. Markdown replacement and SQLite cannot form one atomic
transaction: a crash between them can leave a page without a complete receipt.
Restart marks running actions needs-attention and never blindly retries them.
Queued work becomes safely resumable paused work. Awaiting-approval state survives.
Reconciliation currently means inspect/export and start a new explicitly approved
run; no automated reconciliation UI is claimed. A malicious same-user process racing
the filesystem is outside scope, as is exactly-once external side-effect delivery.

Activity rows are deterministic projections of run state, not model-generated
history. Advanced disclosure retains canonical args, results, evidence, approval,
validation, retry, timing and usage records. SSE sends global cursors; reconnects
catch up from Last-Event-ID. Replay endpoints only read events. No hidden model
chain-of-thought is persisted. Provider failures do not log response bodies or keys.

Default network boundary: exact loopback origin, HttpOnly SameSite=Strict session,
per-session CSRF, Origin/Host/Sec-Fetch checks, no CORS, no trusted forwarded headers.
Pairing needs a one-time 40-bit code, a separate 192-bit claim cookie, a host owner
confirmation from loopback, then a single-use exchange. Requests are rate limited.
Device sessions are hashed at rest, expire after seven days, and are revalidated
for requests and live SSE. The host bootstrap key is plaintext under the OS account;
sessions/configuration are not advertised as encrypted.

Optional direct Kestrel HTTPS has an explicit allowed origin and trusted certificate.
No internet exposure is automated. TLS deployment, mobile install, certificate
rotation and a physical phone are unverified. Data exports include private notes
and receipts; user-initiated downloads must be handled as private files.

Raw Markdown HTML is not executed; unsafe URL protocols are removed by the renderer.
Path names use a strict allowlist and reject Windows ADS/traversal and reparse links.
The static shell alone is cached; API responses are no-store. Offline mutations
are disabled and never queued. Local administrators, filesystem races by hostile
same-user processes, hardware loss, backups, and secure erasure are outside scope.

The Luna bridge is an optional development process, not part of the deployed host.
It uses data-only structured CLI output, reports actual usage, checks for unexpected
tool execution, and never falls back to another model. Its final-output SSE framing
and uncertified CLI token ceiling are distinct from the compatible adapter's native
stream parser. This is not a hardened sandbox or a production authentication scheme.
