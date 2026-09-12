# Native OpenClaw integration evidence

This developer check runs the pinned OpenClaw Gateway and embedded agent loop,
the production Gateway adapter, the official MCP transport, and Thaddeus's real
model, scope, question, approval and readback mechanisms together. It is separate
from the isolation qualification gate and the independent performance Lab.

The subsequent [native VM fixture](NATIVE_VM.md) uses these same host mechanisms
with QEMU/WHPX, including full VM shutdown/restart and broker/network negatives.
The container fixture below remains the protocol control; neither fixture is a
production worker admission switch.

## Reproduce

Keep the product host and benchmark processes running. With Docker Engine healthy:

```powershell
dotnet restore --locked-mode
dotnet build tools/Thaddeus.NativeCheck --no-restore
docker build --pull=false -t thaddeus-openclaw:2026.9.4-context-dev workers/openclaw
node scripts/native-integration-check.mjs scripted
```

The scripted mode supplies fixed inference proposals through `IInferenceTransport`.
OpenClaw executes them through its actual loop and tool transports; the model
usage values in this mode are explicitly synthetic. No live model runs.

When Luna High testing is authorized and the existing development bridge is ready:

```powershell
node scripts/native-integration-check.mjs luna
```

This uses `gpt-5.6-luna` with high reasoning through the existing host-side bridge.
The task allows up to six model dispatches and declares a 96,000-token allowance.
The bridge has no certified input/output bound, so the allowance is not a hard
remote spending ceiling. Unknown usage retains its reservation; an overrun stops
release of the response and is recorded. No automatic retry of that dispatch or
silent provider substitution occurs in Thaddeus.

## Topology and evidence boundaries

The fixture host listens only on loopback port 5182, with a separate controller
credential that never enters the worker. It creates one fresh ledger and one
fictional source under ignored `artifacts/native-integration-*`. It cannot use
the product's `.data` directory. The executable and controller routes are not
included in the published product host.

The disposable worker uses a pinned local image ID, two CPUs, 4 GiB RAM,
`--network none`, dropped capabilities, no host mounts and no GPU. A test-only
HTTP relay over Docker stdin/stdout forwards only the current task's MCP and
model routes to the loopback fixture host. Provider credentials stay outside it.
This proves native protocol integration while Docker Sandboxes is unavailable;
it does not qualify a VM, container escape resistance, or a production network
path. There is no production container fallback or worker admission switch.

The fixture exercises this sequence:

1. Bootstrap and verify Gateway health with the pinned package.
2. Use shared `Runtime.StartExecution` and `OpenClawBackend.Start`, durably recording
   the command intent before dispatch and the correlated acknowledgement afterward.
3. Observe native MCP reading the selected source and storing one durable question.
4. Observe the broker refusing more inference while the question is pending.
5. Record native quiescence through shared execution control, then stop the Gateway
   process, prove that process stopped, start a different PID,
   verify health, and resume the same native session with a fictional answer.
6. Let native file tools write an artifact and MCP propose its exact import.
7. Read the artifact out of the worker, compare its bytes to the approval, apply
   the fixture's explicit approval, and verify the host file's exact readback.

Private receipts include Gateway results/logs, the process restart observations,
HTTP statuses, the final product ledger, question, approvals and model dispatches.
Task grants are revoked and the fixture host/container are stopped during cleanup.
The successful scripted run's captured post-restart model request also retains
the earlier native source-read and question tool results.

## Observed results, 2026-09-12

| Case | Result | Evidence |
|---|---|---|
| Scripted native loop with confirmed Gateway process restart | Passed; four synthetic inference responses | `artifacts/native-integration-scripted-1789248288853` |
| Initial Luna High run | Failed at the task budget after repeated invalid artifact names; all usage retained | `artifacts/native-integration-luna-1789248320981` |
| Luna High after clarifying the artifact contract | Passed; three model calls, scoped read, durable question, Gateway restart, artifact and exact approved import | `artifacts/native-integration-luna-1789248479843` |
| Shared execution control after replacing fixture-only lifecycle code | Passed; start/quiesce/resume/quiesce all durably acknowledged, four synthetic inference responses, native process restart and exact import | `artifacts/native-integration-scripted-1789249593075` |
| Public research through the external broker | Passed; real HTTPS retrieval, full source text received by native OpenClaw, five synthetic responses, question/restart/import | `artifacts/native-integration-scripted-web-1789250839574` |

The failed Luna case reported 109,085 input and 2,059 output tokens (111,144 total).
The successful case reported 55,127 input and 802 output tokens (55,929 total).
Across both native live cases: nine model calls and 167,073 reported tokens.
Every successful-case model dispatch contained the full prepared context.
The imported note correctly retained the fictional 45-minute duration, selected
Developers audience, and `notes/source.md` attribution. This is one inspected
integration case, not general factual validation or an optimization result.

The native checks uncovered and fixed two product contracts:

- Public Gateway callers cannot supply privileged provider/model overrides.
  The adapter now uses the bootstrapped agent route; the external model broker
  independently enforces the product task's frozen model.
- The import tool's `artifact` field lacked its actual filename constraints.
  Its schema and failure message now describe the required extension and naming
  rules. Operation ID guidance distinguishes exact retries from corrected calls.

The pinned runtime also rejected the test's `host.docker.internal` URL when it
resolved to loopback. A literal worker-local relay address works. Production
broker endpoint discovery and egress still require qualification on each backend;
neither URL is accepted as proof merely because it appears in configuration.

## Durable execution control checkpoint

`Runtime.StartExecution`, `ResumeExecution`, `QuiesceExecution` and
`InspectExecution` control an already provisioned worker. The native fixture now
uses the first three directly. They do not provision a worker, qualify isolation,
expose a production admission switch, or run a second agent loop.

Command identities bind the session, worker, runtime version, model, limits,
scope, objective and frozen context. Intents commit before RPC. A repeated
acknowledged start or identical answer returns its saved acknowledgement without
dispatching again. A lost acknowledgement remains uncertain after restart;
neither answering the question nor stopping the worker permits a blind replay.
Inspection cannot substitute the previous native run for an uncertain continuation,
and a worker's successful report never creates verified product completion.

Broker locks are released during RPC so a source read or durable question can
arrive before the Gateway acknowledgement. Recording that acknowledgement reloads
the latest task rather than overwriting a concurrent question, proposal or failure.
Active time accumulates across continuations and pauses when the broker records a
question or proposal. Model and capability admission subtract the accumulated
time. This does not yet prove total native shell/tool time or process termination.

The backend suite now has 134 passing tests, including uncertain-command recovery,
concurrent duplicate start, exact continuation identity, invalid stop replies,
old-run inspection refusal and cumulative budget exhaustion. The revised control
path was checked with scripted native inference; the earlier Luna High receipts
precede this refactor. No additional live inference was necessary for this check.

## Still open

Production admission and orchestration, worker lifecycle, native outcome
reconciliation, total native-tool accounting, public search and research admission,
ordinary-chat admission, independent Lab integration and VM boundary remain open.
The fixture currently observes question/approval stops through broker admission
refusals and native abort RPCs; a finished product must present these as normal
pauses rather than exposing internal provider errors. Actual host-process recovery
and physical-device setup remain distinct from this Gateway restart check.

The [public-page broker](PUBLIC_RESEARCH.md) now has separate native evidence with
real HTTP retrieval and scripted inference. It does not provide a search engine
or automatically grant network access to existing conversations.

References: [OpenClaw Gateway](https://docs.openclaw.ai/cli/gateway),
[MCP configuration](https://docs.openclaw.ai/tools/mcp),
[model protocol](https://developers.openai.com/api/reference/resources/chat).
