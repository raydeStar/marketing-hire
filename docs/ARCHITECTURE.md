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

SQLite commits run state and its next event together; conversation admission and
completion include the corresponding message in that same transaction. Context
is frozen at admission (last twenty messages), and only one conversation reply is
admitted at a time. Conversation has no knowledge scope or tool authority.

Content, revision and write-operation identity commit together before Markdown
projection. A crash leaves a durable committed operation. Restart marks running
work needs-attention and charges any unknown outstanding token reservation; it
never silently projects or retries. The reconciliation view binds its choice to
the inspected file hash: verify matching content, explicitly complete committed
content, or close without more writes. Changed sources, conflicting file content
and agent permission Off prevent stale execution. Reconciliation preserves one
revision and checks the exact hash. Export includes pending committed writes.

A file lease rejects a second host; per-run semaphores reject duplicate execution
and decisions, and version checks reject lost updates. Files and SQLite still
cannot share one atomic transaction; the committed-content protocol exposes and
reconciles that boundary. Malicious same-user filesystem races and exactly-once
external side effects remain outside scope.

Token admission precedes dispatch. Certified providers reserve their declared
input/output upper bound; uncertified providers reserve the full remaining
allowance. Reported usage settles the charge; missing usage retains the reservation.
Strict mode refuses uncertified bounds, including the Luna CLI bridge. A detected
provider overrun stops further action but cannot retroactively prevent remote use.
Cost remains unknown without a price source. SSE parsing bounds both line size and
total transport characters and rejects truncated responses.

Activity rows are deterministic projections of run state, not model-generated
history. Advanced disclosure retains canonical args, results, evidence, approval,
validation, retry, timing and usage records. SSE sends global cursors; reconnects
catch up from Last-Event-ID. Replay endpoints only read events. No hidden model
chain-of-thought is persisted. Provider failures do not log response bodies or keys.

Default network boundary: exact loopback origin, HttpOnly SameSite=Strict session,
per-session CSRF, Origin/Host/Sec-Fetch checks, no CORS, and no forwarded-header trust by default.
Pairing needs a one-time 40-bit code, a separate 192-bit claim cookie, a host owner
confirmation from loopback, then a single-use exchange. Requests are rate limited.
Device sessions are hashed at rest, expire after seven days, and are revalidated
for requests and live SSE. The host bootstrap key is plaintext under the OS account;
sessions/configuration are not advertised as encrypted.

Optional direct Kestrel HTTPS has an explicit allowed origin and trusted certificate.
Opt-in Tailscale Serve mode trusts one symmetric forwarded hop from exact IPv4/IPv6
loopback proxies and only the configured phone hostname. Owner bootstrap and host
confirmation also require the original local origin; proxy requests cannot acquire
owner authority. Funnel-marked requests are rejected. Real local TLS and proxy
boundaries are tested; live Tailscale deployment and physical mobile install remain
manual verification. No internet exposure is automated. Data exports include private notes
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
