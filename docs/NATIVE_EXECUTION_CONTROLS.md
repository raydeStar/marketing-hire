# Native execution-control check

The Windows QA baseline remains available at http://localhost:5179/. The adapter
change below is in source; the running package has not been replaced. This check
does not add steering to the ordinary composer or certify the six delivery gates.

## Current result

The pinned OpenClaw 2026.9.4 Gateway rejected steering because the adapter sent
the administrator-only system-provenance option suppressCommandInterpretation.
Steering now sends ordinary user guidance through public chat.send, retaining
the task session, queueMode=steer, no external delivery and the operation ID.
No additional Gateway privilege is requested.

The real Windows/QEMU fixture observed the exact guidance in the second native
model request. OpenClaw acknowledged a separate continuation run ID; the fixture
tracks that ID. Its agent.wait response was pending, even though the broker had
received the request. That report is retained as worker-reported state.

Active cancellation is **not verified**. A subsequent sessions.abort for the
acknowledged continuation returned INVALID_REQUEST / unauthorized. An attempted
full-session abort was also rejected; that production-code experiment was
reverted. The pinned authorization implementation requires a matching owner
device/connection or administrator scope. Separate CLI invocations therefore
need further caller-identity investigation before this control contract can pass.
Do not bypass that investigation by broadening privileges or treating a stopped
original turn as proof that its continuation was cancelled.

This is a direct Gateway adapter check. Product research cancellation also
revokes the grant, cancels host work and releases the worker; the new result does
not establish a failure of that distinct UI path. Physical VM shutdown cancelled
the outstanding synthetic host request. Gateway acknowledgement and provider
cancellation are separate observations; this check does not promise that a real
provider stops billing immediately.

## Evidence

- artifacts/native-execution-controls-20260914-f/verified.json records
  steeringVerified=true, gatewayAbortVerified=false and passed=false.
- Its two synthetic request files, observations, private RPC diagnostic, final
  ledger and owned-process receipts preserve the actual sequence.
- artifacts/native-control-progress-20260914-f/verification.json records the
  source hashes, executable hashes and 78 passing focused backend tests covering
  the adapter, durable execution controller and research coordinator.
- Earlier attempts A through E remain as failure evidence. They include the
  privileged-field rejection and corrections to the fixture's assumptions about
  continuation IDs, pending inspection and the VM relay cancellation boundary.
  They are not additional successful product tests.

Each attempt used the existing pinned base with one small disposable overlay.
Every owned VM exited and its overlay was removed. The final fixture also
observed host-request cancellation during VM shutdown. No live model, GPU
inference or GitHub Actions ran. Build intermediates are removed after the
focused checks; compact diagnostics, hashes and receipts remain.

## Opt-in reproduction

Run only for a concrete control-contract change. This is excluded from routine
core checks and intentionally exits unsuccessfully while cancellation is
unqualified. Use Windows with the existing checked QEMU installation; do not
build another guest image.

After a locked restore and build of tools/Thaddeus.NativeCheck into a disposable
artifact build directory, invoke:

~~~text
dotnet PATH_TO_BUILT_Thaddeus.NativeCheck.dll execution-control-check FRESH_ARTIFACT_DIRECTORY PINNED_INSTALLATION_JSON
~~~

The fixture admits one worst-case overlay plus 64 MiB while reserving 10 GiB.
It uses a private temporary broker with synthetic responses, revokes its grant
and removes its owned overlay after process exit, including on failure. Remove
the disposable build directory after the process exits and record its exact
resolved path; preserve the pinned installation, QA app and rollback.

The next change should establish a supported stable Gateway caller identity,
then prove active cancellation and queued-work handling with this same fixture.
Steering still needs durable product orchestration before it can become a
composer action.

Pinned source references:
[request authorization](https://github.com/openclaw/openclaw/blob/3a9d69db306cd7f081e06254cb89c4bcc14a7107/src/gateway/server-methods/chat-send-request.ts),
[cancellation ownership](https://github.com/openclaw/openclaw/blob/3a9d69db306cd7f081e06254cb89c4bcc14a7107/src/gateway/server-methods/chat-abort-authorization.ts),
[session cancellation](https://github.com/openclaw/openclaw/blob/3a9d69db306cd7f081e06254cb89c4bcc14a7107/src/gateway/server-methods/sessions-abort.ts).
