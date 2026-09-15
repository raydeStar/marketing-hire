# Guiding active research

While a research task is working, use **Guide this research** in its task view,
or choose **Message mode → Guide active research** in Conversation. Send an
adjustment to the audience, emphasis or desired result. The message belongs to
that task; it does not create another task or grant new sources, tools or tokens.
Answer saved questions and review proposed artifacts through their existing
controls. Guidance is unavailable at those stopping points.

The card shows the task's allowance and saved guidance. **Received by the worker**
means OpenClaw acknowledged the input, not that the resulting work is correct.
An unconfirmed delivery remains visible and requires inspection. Reconnecting
or submitting the same operation ID does not send it twice. The original task's
cancel action also stops queued guidance.

## Durable control and accounting

`POST /api/runs/{id}/guidance` requires the existing authenticated session, matching
origin and CSRF token. An authenticated paired device has the same task controls;
physical-phone qualification is still deferred. Each request has a client-created
32-hex operation ID and at most 4,000 characters. The host records the exact text,
request hash and command intent atomically with its conversation entry before
calling OpenClaw. Existing exports retain both the input and the control receipt.
The optional command message field is backward compatible with older JSON rows.

Start, continuation and guidance use the same public `chat.send` user-turn path
on the pinned persistent Gateway connection. Only explicit start/continuation can
establish its private controller lease. Guidance uses `queueMode: steer`, keeps
the configured tool allowlist and does not request administrative provenance.
Its input ticket is recorded separately from the current execution ID: delivery
can finish while the underlying task is still running. Inspection keeps that
distinction; cancellation covers the entire task session.
Permission, credential and model admission remain outside the worker.

Inference keeps a durable token reservation while releasing the task mutation
lock during provider I/O. This lets guidance and cancellation proceed while a
model is working, without admitting a second simultaneous inference. Settlement
reloads the current task so it preserves concurrent commands, questions and user
cancellation. Guidance never resets the active-time clock, model-call counter or
token allowance. A stopped task's outstanding reservation is also charged on
restart, without reopening or replaying the task.

## Development evidence

`artifacts/guidance-20260914-d` records 849 passing backend tests. These include
guidance during a held inference, exact duplicate/restart behavior, changed
operation refusal, unknown delivery, cancellation races, token settlement,
exhausted allowances and owner/paired-session authentication. The unchanged web
sources built successfully in `artifacts/guidance-20260914-a`.

`artifacts/guidance-research-20260914-c` passes the real Windows/QEMU browser
workflow. A synthetic model request is held while the browser sends guidance.
The control is acknowledged before that request is released; request 2 contains
the exact guidance. Desktop and narrow-screen screenshots, reload, duplicate
submission, frozen context/allowance, question/continuation, bounded correction,
exact approved import and reviewed workspace removal are checked. Seven
synthetic calls are recorded; no live model or GPU is used.

The earlier A attempt passed its browser workflow but failed the stricter
next-request check: input first appeared in request 4, after restart. It remains
recorded as a failed overall check. Aligning the initial task and guidance on
the same public user-turn path fixed the observed delay; B first proved request-2
delivery. The subsequent direct control fixture also exposed the separate input
ticket/execution IDs. `artifacts/native-guidance-controls-20260914-b` passes real
active/queued cancellation, live-reset refusal and lost-caller refusal with that
distinction preserved. The failed first control fixture is retained as evidence.
The final C browser run preserves both IDs and repeats the complete workflow.
All owned workspaces and overlays were removed, and shared build intermediates
were cleaned after matching the backend/native/browser infrastructure assemblies.
`artifacts/guidance-20260914-d/final-verification.json` records those hashes and
the resolved cleanup path. No new base
image was built or copied, and hosted Actions were not used.

This qualifies the exercised Windows/QEMU control workflow. It is not evidence
of model quality, all-runtime steering behavior or physical-phone operation.

References: [OpenClaw steering boundaries](https://docs.openclaw.ai/concepts/queue-steering)
and the [pinned input-authority checks](https://github.com/openclaw/openclaw/blob/3a9d69db306cd7f081e06254cb89c4bcc14a7107/src/auto-reply/reply/reply-run-registry.message-injection.ts).
