# Worker transport boundary checks

The Windows QEMU adapter uses shared host components for newline-framed JSON,
broker admission and HTTP forwarding. `VmJsonFrames`, `VmBrokerQuota` and
`VmBrokerProxy` are called by the actual `QemuWorkerSession`; the tests do not
substitute a separate parser or proxy. Models and agent code do not run inside
these host components.

## Enforced bounds

| Boundary | Host enforcement |
|---|---|
| Destination | Only this task's exact MCP and model paths at its configured loopback broker port |
| HTTP methods | GET, POST and DELETE; GET must have no body |
| Credentials | Only the explicit task authorization and small protocol header allowlist; no guest cookies, host override or browser CSRF header |
| HTTP client | No ambient proxy, cookie store or automatic redirects |
| Request | At most 150,000 decoded body bytes; at most 200,000 base64 characters |
| Headers | At most 16,000 combined name/raw-value characters; each forwarded value at most 8,192 characters without CR/LF |
| Response | At most 1,500,000 body bytes; response cookies and redirect destinations are not forwarded |
| Admission | Eight active forwarded requests, 200 unique request IDs per boot; completed IDs cannot be reused |
| Framing | 2,200,000 bytes per guest frame, 300,000 per QMP frame, JSON depth 32 and 5,000 frames per channel |
| Lifetime | Existing command, connection and owned-worker deadlines remain in effect |

Request leases release once, after response delivery or cancellation. An invalid
frame, repeated/invalid ID, or exhausted admission limit fails the transport and
stops its owned VM. A route/body/header rejection returns a bounded error without
forwarding the disallowed request. The adapter refuses further commands as soon
as the session is stopping or its channel is cancelled; it need not wait for the
asynchronous owned-process completion receipt. Existing execution uncertainty
and explicit stopped-disk recovery rules remain authoritative.

## September 13 evidence

`artifacts/transport-qualification-20260913/native-c/verified.json` records one
real pinned QEMU/WHPX worker and its actual supervisor/serial/TLS/broker path.
No OpenClaw agent or model was started by this hostile-traffic fixture.

Twelve guest cases verified a successful exchange without cookies/CSRF headers;
denial of product, other-task, traversal and query routes; rejected method and
GET body; a missing fixture grant; oversized authorization/request/response;
and a redirect that did not reach its destination. The test broker is local and
fictional; this validates transport, not an external provider's authorization.

The subsequent bounded request schedule exhausted the 200-ID allowance. Of those
admissions, 193 reached the test broker; the denied paths were never dispatched.
The host observed the original VM process exit through its retained handle,
answered a separate health request, and refused another guest command. Explicit
reconciliation verified a stopped, unchanged overlay without booting or replaying
anything. The private worker disk and all observations remain retained.

Host private-memory snapshots were 29,028,352 bytes before the fixture and
69,357,568 after; peak working set was 141,418,496 bytes. These are observations
from one bounded run, not a hard cap on the whole application or a throughput
benchmark. The VM's existing OS-enforced resource caps remain separate evidence.

Thirty-seven focused tests exercise the production framing/proxy/quota components,
including fragmented UTF-8, malformed/deep/truncated/oversized frames, the frame
count limit, response-size boundaries, cancellation, exact request identity,
concurrency, and a real loopback HTTP redirect/cookie control. The final backend
suite passed 472 tests. The normal research browser regression also passed with
the real VM, explicit checkpoint restoration, seven synthetic replies, one source
correction, exact approved import and reviewed workspace removal. Its receipts
are under `artifacts/transport-qualification-20260913/research-a`; it tests the
actual product coordinator and grants alongside the stricter transport.

Attempt A refused startup because `dotnet run` selected a project working
directory outside the allowed artifact root. Attempt B passed the twelve guest
cases and stopped the flood, then exposed the post-stop admission race. Both
attempts remain recorded. Attempt C passed after the explicit session guard.

Run from the repository root after building `tools/Thaddeus.NativeCheck`:

```powershell
dotnet tools/Thaddeus.NativeCheck/bin/Debug/net10.0/Thaddeus.NativeCheck.dll transport-check FRESH_PRIVATE_ARTIFACT_DIRECTORY PINNED_INSTALLATION_JSON
```

This closes the declared framing, routing and request-exhaustion checks for the
Windows development path. It is not an exhaustive VM escape assessment, guest
filesystem-integrity proof, signed distribution, macOS/Linux worker validation,
or qualification of a default production installation. There were no paid model
calls, GPU inference, benchmark changes, or changes to the main study's data.
