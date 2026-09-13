# Thaddeus 2.0 implementation contract

Accepted direction, 2026-09-12. This supersedes the original weekly-plan milestone
as the development objective. The old completion audit describes that milestone,
not completion of this architecture.

## Product

A local-first personal assistant with one browser/PWA client for Windows, macOS,
Linux and phones. A supported computer hosts the service; a phone connects to
that host. Windows is the first host validation platform. Nontechnical setup,
portable host packages and a documented support matrix are part of delivery.

Agent execution never assumes access to the host. OpenClaw owns the execution
loop inside an isolated environment. Thaddeus owns product identity, project
context, source-linked memory, experiments, durable goals and verified outcomes.
Mandatory permissions, credential custody and model admission are outside the
agent environment and cannot be disabled by experimental policy.

## Ownership and interfaces

- React/PWA talks only to the authenticated Thaddeus application API.
- IExecutionBackend starts, steers, cancels, inspects and resumes OpenClaw work.
- ISandboxBackend provisions, inspects, starts, stops and transfers artifacts;
  its report describes observed capabilities rather than a trusted brand name.
- ICapabilityBroker authorizes typed, scoped operations and records durable IDs,
  exact approvals and verified effects. Worker reports are not trusted receipts.
- Versioned PolicyProfile controls context, retrieval, tool exposure and bounded
  repair through supported OpenClaw integration points. One engine owns the loop.
- Models run outside the worker. Local tests may use the already-loaded Glimmer
  instance only after coordinating the shared benchmark lane. Luna High remains
  available for separately declared development tests; no silent model swaps.
- SQLite/Markdown remain the authoritative product store. OpenClaw owns its
  execution transcript. Correlated projections never become a second owner.

## Delivery gates

1. Qualify pinned Docker Sandboxes as the first real ISandboxBackend. Use private
   workspace copies, deny network except granted broker endpoints, no host home,
   Docker socket, browser profile, shared credentials, or GPU. No automatic
   container/host fallback. Record actual lifecycle and confinement evidence.
2. Connect a pinned OpenClaw package using documented public contracts. Generalize
   typed proposals, add real MCP transport and authenticated broker/model access.
   Preserve old history/exports with a versioned migration. Reconcile uncertain
   writes and never replay an external effect blindly.
3. Adapt/retest v1 personality profiles, source-linked correctable memory,
   deterministic tools, evidence checks and bounded repair. Do not import v1's
   entire routing history or transfer historical performance claims.
4. Deliver the ordinary-chat research workflow: selected documents plus public
   research, isolated files/shell work, a durable question, restart/continuation,
   review of artifacts, exact approved import and independent verification.
5. Integrate the independent Lab with the same production mechanisms. Preserve
   separate model-capacity, task-capability and product-quality scorecards.
   Freeze model/runtime/tools/state/budgets; record activation and all model use;
   retain unknown usage, identical-arm controls and false-success negatives.
6. Package a self-contained host and prebuilt PWA, with guided backend/model/owner
   setup, upgrade/rollback and device pairing. Verify Windows first, then macOS
   and Linux; actual physical-phone setup remains last and user-operated.
   The user also requests a UI redesign after functional work; the current
   interface is not accepted as the final design. See [UI_REDESIGN.md](UI_REDESIGN.md).

Every gate requires current evidence. A simulated worker, interface alone,
successful compile or model-generated completion claim does not close a gate.
The original v1 checkout, unrelated OpenClaw work, active benchmarks and user
data must remain intact.

## Initial boundaries

One active worker with explicit resources and per-task budgets. Drafts inside
its private workspace may proceed under the task grant. Original host files and
external mutations cross an exact, version-checked approval boundary. Begin with
text/Markdown and public web research. Authenticated browsing waits for separate
browser/session custody outside the agent shell's reach. No automatic personal
profiling, arbitrary plugin installation, background monitoring or swarms.

## Future hosting seam

Keep one modular product with infrastructure adapters. A future hosted control
plane supplies accounts/provisioning/billing while isolated user instances own
data, credentials and workers. Reuse the client and execution contracts; do not
build a separate SaaS fork, shared database tenancy or billing in this cycle.

Sources: [OpenClaw hooks](https://docs.openclaw.ai/plugins/hooks),
[context engine](https://docs.openclaw.ai/concepts/context-engine),
[Docker installation](https://docs.docker.com/ai/sandboxes/install/),
[Docker isolation](https://docs.docker.com/ai/sandboxes/security/isolation/),
[ports and adapters](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures),
[hosted control planes](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/considerations/control-planes).
