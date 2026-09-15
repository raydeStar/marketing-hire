# Native execution controls

The pinned OpenClaw worker now passes start, steering, inspection, cancellation
of active and queued guidance, and refusal to recreate a lost caller. The
ordinary research browser workflow also passes through restart, correction and
exact approved import. The running Windows QA package remains unchanged; this
source checkpoint is for the next candidate build.

## Gateway caller and authority

Thaddeus uses the documented Gateway WebSocket protocol, version 4, against
OpenClaw 2026.9.4. A small first-party Node transport inside the existing worker
keeps one authenticated connection for the task. It requests only operator.read
and operator.write. Start, steering, inspection and stop therefore share the
same Gateway caller identity. OpenClaw still owns the model/tool loop.

The previous CLI transport created a new connection per request and deliberately
omitted device identity for local shared-secret authentication. The Gateway
rejected active cancellation from that different owner. The fix does not request
administrator authority or alter OpenClaw's ownership checks.

Steering sends ordinary user guidance through chat.send with queueMode=steer,
no external delivery and a durable operation ID supplied by the caller. It does
not use administrator-only system-provenance fields. Cancellation addresses the
task's unique session and clears queued work as well as the active turn. A
runId-limited abort would not provide that full-session queue behavior.

## Lifetime and containment

The transport is embedded in the host assembly and sent through the existing
sandbox command channel. It needs no new package dependency, guest image, host
mount or provider credential. Its private Unix socket, code hash and lease bind
subsequent calls to the original controller. Gateway version, negotiated scopes,
request/result limits and correlation are checked.

A lost connection, missing acknowledgement or dead controller is not reconnected
or replayed automatically. The host retains the existing unknown-outcome
handling. A reconciled restart may replace the caller lease during grant refresh,
only while the Gateway-stopped port guard is held and the frozen worker/context
binding matches. A live Gateway refuses that refresh. A new guest boot is also
distinguished from the old lease. This supports the existing worker lifecycle
without assuming access to the user's host filesystem.

Gateway cancellation and host/provider cancellation remain separate observations.
The VM relay's pending synthetic HTTP request was cancelled during physical worker
shutdown. This is not a promise that a remote provider stops billing immediately.
User-facing research cancellation also revokes its grant, interrupts host work
and releases the worker.

## Evidence and limits

- artifacts/native-execution-controls-20260914-i/verified.json passes the
  complete direct-adapter fixture with two synthetic requests. It records one
  Gateway connection and exactly read/write scopes across all native operations.
  Active and queued turns end with stopReason=rpc.
- The same fixture refuses grant/caller reset beside a live Gateway. It then
  terminates its exact controller through a Linux process handle, refuses a
  subsequent resume, captures that refusal and observes no additional inference.
- artifacts/native-control-research-20260914-a contains a passing real-browser
  product workflow: selected context, question, worker restart, continuation,
  retained failed draft, bounded correction, exact approval/import, export and
  reviewed workspace removal. Seven synthetic requests; the actual production
  API, coordinator, brokers and worker perform the workflow.
- artifacts/native-control-progress-20260914-j records 830 passing backend tests,
  29 passing protocol checks, cleanup and delivery verification, alongside
  source fingerprints and tested assembly hashes. The research project does not
  compile NativeCheck's control-fixture entrypoint; the verification records its
  later test-only change separately from the unchanged research runtime inputs.
- Earlier failed cases A through F remain as historical evidence. G and H are
  successful intermediate control checks; I is the final control fixture.

Each VM check reuses the same pinned base with a disposable overlay. Owned
processes exited, test workspaces/overlays were removed, and separate build
intermediates were cleaned after verification. Compact logs, hashes, screenshots
and receipts remain. No live model, GPU inference or GitHub Actions ran.

These checks qualify the bounded control protocol on the Windows/QEMU development
path. They do not qualify Docker Sandboxes, a native Mac, complete isolation,
model efficacy or the earlier frozen Lab campaigns against the new transport.
Durable product orchestration is still needed before steering is a composer
action. The six broader delivery gates and manual UI acceptance remain separate.

## Opt-in reproduction

Use this fixture only for a concrete control-contract change. After a locked
restore and build of tools/Thaddeus.NativeCheck into a disposable artifact build
directory, invoke:

~~~text
dotnet PATH_TO_BUILT_Thaddeus.NativeCheck.dll execution-control-check FRESH_ARTIFACT_DIRECTORY PINNED_INSTALLATION_JSON
~~~

The fixture reserves 10 GiB plus one worst-case overlay and 64 MiB before starting.
It uses a temporary private broker, revokes its grant, and removes its owned
overlay after process exit, including on failure. Remove its separate build
directory after the process exits and record the resolved cleanup path. Preserve
the pinned worker inputs, QA app, rollback package and user data.

The small transport checks run with:

~~~text
node --test scripts/openclaw-gateway-control.test.mjs
~~~

They are included in ordinary core checks; the real VM fixture is opt-in.

Sources: [public Gateway protocol](https://docs.openclaw.ai/gateway/protocol),
[pinned CLI identity policy](https://github.com/openclaw/openclaw/blob/3a9d69db306cd7f081e06254cb89c4bcc14a7107/src/gateway/call.ts),
[pinned cancellation ownership](https://github.com/openclaw/openclaw/blob/3a9d69db306cd7f081e06254cb89c4bcc14a7107/src/gateway/server-methods/chat-abort-authorization.ts),
[pinned session cancellation](https://github.com/openclaw/openclaw/blob/3a9d69db306cd7f081e06254cb89c4bcc14a7107/src/gateway/server-methods/sessions-abort.ts).
