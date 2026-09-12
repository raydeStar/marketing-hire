# OpenClaw worker package

This is the pinned Linux execution package used behind `ISandboxBackend`, on any
supported host platform. It is not a second product server or a host execution
fallback. Worker admission remains disabled until the backend is qualified.

`bootstrap.mjs` accepts a bounded JSON document on stdin containing a task ID,
short-lived task grant, dedicated broker origin, exact model/reasoning selection,
and the host's prepared context snapshot. It verifies the OpenClaw version and
context hash, creates private files exclusively, and runs native configuration
validation. An uncertain or repeated bootstrap fails without overwriting files;
the future coordinator must inspect and reconcile it. It does not start a model.

The worker knows a revocable task capability. Provider credentials and the host
access key stay outside it. Both model HTTP and MCP URLs point to that task's
broker routes. The Gateway binds to worker loopback, using a separate random
worker-local token. Its embedded OpenClaw runtime is selected explicitly, with
no model fallbacks, scheduled heartbeats, catalog refresh, automatic updates,
browser, or imported skills. The 32,768-token context setting is this package's
declared operating envelope, not a measurement of the provider's full capacity.

OpenClaw's `sandbox.mode: off` and `exec.host: gateway` refer to the Linux worker,
which already sits inside the qualified outer boundary. They must never be used
to run the package on the actual user host. Configuration and plugin hooks are
defense in depth; broker authorization and the backend boundary remain mandatory.

The bundled `thaddeus-context` plugin uses public typed hooks to prepend the
already-prepared snapshot only to its bound agent/session and block a mismatched
session before the model runs. It performs no new retrieval and owns no agent
loop. Host model receipts observe actual context presence separately. Hook
registration alone does not establish model consumption, factual correctness,
bounded repair, or protection against an agent modifying its own worker.

## CPU-only compatibility check

After checking Docker Engine health, build the package and test the installed
OpenClaw schema and plugin loader without host mounts, external networking,
provider credentials, or GPU access:

```powershell
docker build --pull=false -t thaddeus-openclaw:2026.9.4-context-dev workers/openclaw
node scripts/worker-config-check.mjs
```

The check uses an immutable local image ID, fictional input, and a disposable
container with two CPUs and 4 GiB RAM. It requires a warning-free native schema,
the two expected registered hooks, and conversation/prompt permissions. It also
checks private file modes and rejects a repeated bootstrap. Raw receipts remain
under ignored `artifacts/worker-config`. This is package compatibility evidence;
it does not qualify Docker Sandboxes or prove the native model/MCP execution path.

The ordinary CPU contract tests run in the main CI. Native package checks are
explicit because they require the worker image and a healthy Docker Engine.

Remaining integration: qualified broker-only egress, coordinator/bootstrap
admission, Gateway lifecycle and transcript reconciliation, native MCP/model
execution, bounded evidence repair, and independent Lab activation checks.

References: [runtime policy](https://docs.openclaw.ai/gateway/config-agents/runtime-and-cli-backends),
[typed hooks](https://docs.openclaw.ai/plugins/hooks),
[MCP servers](https://docs.openclaw.ai/tools/mcp).
