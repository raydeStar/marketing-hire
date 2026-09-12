# Development status — 2026-09-12

All six [delivery gates](IMPLEMENTATION_PLAN.md) remain open. This checkpoint
implements and tests foundations; it does not claim a complete OpenClaw product.

| Area | Implemented | Still required |
|---|---|---|
| Product | Existing conversation, scoped plans, approval, reconciliation, history, export; setup and durable-question UI | Ordinary-chat research admission and native continuation |
| Isolation | `ISandboxBackend`, pinned Docker CLI adapter, owned-worker registry, one-worker limit, bounded text transfer, real setup inspection and separate qualification CLI | Resolve Windows image-service failure, VM creation, observed mounts/network/resources, broker-only egress, lifecycle proof |
| OpenClaw | Pinned image, Gateway RPC adapter with correlated run/session IDs and unknown-outcome handling | Actual Gateway/MCP/model integration inside qualified worker; coordinator |
| Brokers | Official MCP SDK, short-lived task grants, scoped tools, exact import proposals, model destination/budget enforcement | General public-research capability, qualified network path, native effect reconciliation |
| V1 reuse | Existing receipt/approval/repair mechanisms; declarative personality and frozen, scoped source context; context presence recorded at model dispatch | Profile hooks, correctable memory, activation evidence through native OpenClaw |
| Lab | Earlier scaffold Lab remains runnable; worker model receipts distinguish actual, unknown and reserved usage | Independent Lab integration, frozen native OpenClaw controls, mechanism activation and task outcomes |
| Distribution | Self-contained Windows development publish and responsive PWA | Supported release packages, nontechnical lifecycle, actual macOS/Linux validation; physical phone last |

## Current development instance

The host uses `http://localhost:5179` and the existing private `.data` directory.
The Luna bridge uses loopback port 5181, model `gpt-5.6-luna`, high reasoning.
Glimmer remains loaded elsewhere for shared benchmarks; no local inference,
reload, eviction or benchmark mutation was performed for this checkpoint.
The worker model gate refuses direct local GPU requests until a resource lease
adapter is implemented. The caller's existing benchmark queue must be respected.

Docker Sandboxes 0.42.1 is installed per user and now authenticated. After the
user signed into Docker, the supported credential helper supplied the existing
Docker Hub login to `sbx login --password-stdin`, without printing its value.
The free account is sufficient. The new Sandboxes policy was initialized to
`deny-all`; no existing sandbox policy or Docker Desktop setting was replaced.

Sandboxes inventory and all 12 generic diagnostics pass, including Windows
hypervisor support. However, template listing, local import and registry pull
fail because its image backend cannot connect to its own Windows Unix socket.
The observed symptom matches [Docker issue #157](https://github.com/docker/sbx-releases/issues/157),
reported against an older version; this run reproduces the symptom on 0.42.1.
No VM was created. Empty inventories reconciled both failed creation IDs, with
receipts under `artifacts/worker-qualification-20260912`. No reboot, UAC feature
change, Docker Desktop restart or automatic weaker backend fallback occurred.
The app's newer inspection distinguishes this image-service failure from sign-in.

## Evidence

- Backend suite: 121 passing tests, including the official MCP client over the
  test HTTP transport, scoped authorization, exact import/replay, model admission,
  unknown usage, migration preservation and interrupted-dispatch recovery.
- Luna bridge protocol: four passing CPU-only contract tests.
- Real Luna High general-function probe: one `thaddeus_ask_user` proposal,
  12,747 reported input tokens and 141 output tokens; zero tool executions.
  Private receipt: `artifacts/model-probes/luna-tool-proposal-1789245180957.json`.
  This verifies inference transport, not native OpenClaw behavior or efficacy.
- Browser suite: eight tests passed against a disposable host, including the new
  setup screen at 1440 and 390 pixels and export schema 3. These are browser
  viewport tests, not physical-phone evidence.
- Worker image built from official pinned base manifests. Python 3.14.4,
  Node 24.19.0 and OpenClaw 2026.9.4 were observed. A disposable Docker command
  with network disabled, two CPUs, 4 GiB RAM, capabilities dropped and
  no-new-privileges successfully ran `openclaw --version`.
- Image digest at this checkpoint:
  `thaddeus-openclaw@sha256:061f69f26d8abff7d615f2224a6f8ca7f95c4af0958130f75ab88b969d3dee89`.
  Exported as `artifacts/thaddeus-openclaw-2026.9.4-dev.tar`; import failed at
  Docker's local image service. It has not executed inside Docker Sandboxes.
- A self-contained Windows publish passed the fictional plan/approval/import
  smoke from its own output directory, including bundled fixtures. The updated
  development host runs from `artifacts/dev-host-20260912-brokers`; authenticated
  checks retained all 18 existing runs and the Luna High provider after migration
  to database schema 2. The prior data snapshot is private under
  `artifacts/data-backup-before-schema2-20260912`.
- Locked restore passes; npm audit reported zero vulnerabilities. The development
  publisher restores RID-specific packages inside a separate source staging tree,
  preserving the source checkout's normal lockfiles and the running host's files.

No model-quality improvement, secure VM boundary, production readiness,
cross-platform host qualification or physical-phone success is asserted.

Protocol references: [MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk),
[Chat Completions](https://developers.openai.com/api/reference/resources/chat),
[OpenClaw hooks](https://docs.openclaw.ai/plugins/hooks),
[Docker isolation](https://docs.docker.com/ai/sandboxes/security/isolation/).
