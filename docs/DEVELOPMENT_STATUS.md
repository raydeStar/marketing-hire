# Development status — 2026-09-12

All six [delivery gates](IMPLEMENTATION_PLAN.md) remain open. This checkpoint
implements and tests foundations; it does not claim a complete OpenClaw product.

| Area | Implemented | Still required |
|---|---|---|
| Product | Existing conversation, scoped plans, approval, reconciliation, history, export; setup and durable-question UI | Ordinary-chat research admission and native continuation |
| Isolation | `ISandboxBackend`, pinned Docker CLI adapter; explicit Windows QEMU backend with owned processes, mutual TLS, private overlays, bounded transfer and explicit crash reconciliation | Resolve Docker image-service failure; complete production confinement/resource and broader crash qualification |
| OpenClaw | Pinned image, public Gateway adapter, bootstrap and context hooks; native MCP/model/question/restart/import integration passed in a dedicated fixture with Luna High; shared durable start, stop acknowledgement, continuation and read-only inspection | Qualified worker network path, production admission/orchestration and outcome reconciliation |
| Brokers | Official MCP SDK, short-lived task grants, scoped tools, exact import proposals, model destination/budget enforcement; bounded public-page retrieval with task host grants and source receipts | Public search and research admission, qualified network path, native effect reconciliation |
| V1 reuse | Existing receipt/approval/repair mechanisms; declarative personality, frozen source context and native hooks; full context observed in each model dispatch of the passing native case | Native evidence repair, correctable memory, broader independent activation evidence |
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
An upstream refresh on September 12 confirmed current reports of the same failure
on 0.42.1 and the tested nightly; resets and reboots had not fixed those reports.
Docker's maintainers are investigating. No verified workaround was available in
the issue thread, so those disruptive steps were not repeated on this host.

A separate [QEMU feasibility probe](QEMU_FEASIBILITY.md) then booted Alpine Linux
through the existing Windows hypervisor with one CPU, 512 MiB RAM, no guest NIC
and no host filesystem shares. A dedicated virtual serial channel returned
65,536 random bytes exactly, and QMP observed pause, resume and clean guest
shutdown. This establishes a candidate second VM backend without a Docker account
in its execution path. It is not registered in the product; OpenClaw, authorized
broker transport, persistence, recovery and production confinement remain open.

The later [native VM integration](NATIVE_VM.md) passed selected-note and public
retrieval workflows with the pinned OpenClaw engine inside QEMU/WHPX. It used the
shared host brokers and execution control, shut the entire VM down at a durable
question, booted a distinct process on the same private overlay, and continued to
an exact approved import. Direct public/host-app access and unauthorized broker
routes were denied in the observed negative cases. These scripted integration
results strengthen the native boundary evidence; a production backend, admission,
hostile-input/resource bounds and crash recovery remain required.

The next [Windows ownership primitive](WORKER_PROCESS_OWNERSHIP.md) assigns a trusted
worker to a kill-on-close job atomically at process creation. Real helper-process
checks and a pinned QEMU/WHPX check passed abrupt owner death, descendant cleanup,
cancellation, lifetime/output limits and unrelated-process preservation. Cancelled
job termination can return OS exit code zero; the typed completion record retains
the stop reason and refuses to classify it as success.

The later [managed QEMU backend](QEMU_MANAGED_BACKEND.md) connects that owner and
fresh mutually authenticated TLS channels to the real `ISandboxBackend`. The
NativeCheck host now owns the VM directly. Its public-source/question/whole-VM
restart/import case passed with TLS 1.3 on both channels, separate per-boot
certificates and independent shutdown receipts. Input files remain pinned and
read-locked; private keys were cleaned up and the base disk was unchanged. This
explicit development path remains outside product admission. The subsequent
[crash recovery checkpoint](WORKER_CRASH_RECOVERY.md) adds a backend ownership
lease, read-only overlay checking, abandoned TLS-key retirement and task-grant
rotation. A real host kill exposed unflushed bootstrap files; native quiescence
now also requires a guest filesystem checkpoint. The full public-source workflow
passed after this fix, preserving its question and interrupted-write marker.
Corrupt, missing and locked images were refused without automatic repair or boot.
Broader crash-point and adversarial qualification, production orchestration and
release packaging remain open.

## Evidence

- Backend suite: 194 passing tests, including the official MCP client over the
  test HTTP transport, scoped authorization, exact import/replay, model admission,
  unknown usage, migration preservation, interrupted-dispatch recovery, uncertain
  native command recovery, duplicate continuation refusal and cumulative active time;
  six TLS cases cover authenticated transport and bounded credential cleanup;
  stop-adapter cases refuse an absent filesystem checkpoint without a retry.
- Luna bridge protocol: four passing CPU-only contract tests.
- Worker configuration: three additional CPU-only tests cover task-bound broker
  routes, refusal of direct host/model endpoints, altered context, and session
  mismatch. A disposable, network-disabled container ran the real OpenClaw schema
  validator without warnings and loaded both typed context hooks with the required
  permissions. Exclusive bootstrap and private file-mode checks passed. No model
  call occurred. See [worker package](../workers/openclaw/README.md) for the exact
  test command and its evidence limits.
- Real Luna High general-function probe: one `thaddeus_ask_user` proposal,
  12,747 reported input tokens and 141 output tokens; zero tool executions.
  Private receipt: `artifacts/model-probes/luna-tool-proposal-1789245180957.json`.
  This verifies inference transport, not native OpenClaw behavior or efficacy.
- The later [native integration](NATIVE_INTEGRATION.md) passed with Luna High:
  scoped MCP read, one durable question, verified Gateway process restart,
  native continuation, worker artifact, exact fixture approval and readback.
  The passing case used three calls and 55,929 reported tokens. An earlier failed
  case exposed an unclear artifact filename contract and retained all 111,144
  reported tokens. These are integration observations, not a benchmark gain.
  The container had no network or host mounts; a test-only stdio relay reached
  the real host brokers. The production VM/network boundary remains unqualified.
- The native fixture now uses shared durable execution control. Its later scripted
  run passed start/quiesce/resume/quiesce, a real Gateway process restart and exact
  import: `artifacts/native-integration-scripted-1789249593075`. Command intents
  precede RPC, lost acknowledgements cannot be replayed, and broker time allowance
  carries across answers. Native tool/process budgets remain a separate open gate.
- A later [public-research check](PUBLIC_RESEARCH.md) passed native selected-note
  reading, real brokered HTTPS retrieval, durable question, Gateway restart,
  continuation and approved import with five scripted model responses. The complete
  source text and URL were observed in native model input. The worker had no
  network; public HTTP ran outside it. No live model or benchmark work was used.
- Browser suite: eight tests passed against a disposable host, including the new
  setup screen at 1440 and 390 pixels and export schema 3. These are browser
  viewport tests, not physical-phone evidence.
- Worker image built from official pinned base manifests. Python 3.14.4,
  Node 24.19.0 and OpenClaw 2026.9.4 were observed. A disposable Docker command
  with network disabled, two CPUs, 4 GiB RAM, capabilities dropped and
  no-new-privileges successfully ran `openclaw --version`.
- Initial image digest before adding the native bootstrap/context package:
  `thaddeus-openclaw@sha256:061f69f26d8abff7d615f2224a6f8ca7f95c4af0958130f75ab88b969d3dee89`.
  Exported as `artifacts/thaddeus-openclaw-2026.9.4-dev.tar`; import failed at
  Docker's local image service. It has not executed inside Docker Sandboxes.
- A self-contained Windows publish passed the fictional plan/approval/import
  smoke from its own output directory, including bundled fixtures. The updated
  development host runs from `artifacts/dev-host-20260912-control`; authenticated
  checks retained all 18 existing runs and the Luna High provider after migration
  to database schema 2. The prior data snapshot is private under
  `artifacts/data-backup-before-schema2-20260912`.
  The control checkpoint preserved all 18 run IDs/states, five pages and the exact
  provider profile. Its published binary path and source provenance were checked
  against the listening process, with a stopped-host snapshot under
  `artifacts/data-backup-before-control-20260912`. A separate package smoke verified
  the served client asset, bundled fixtures, approval/import and export schema.
- Locked restore passes; npm audit reported zero vulnerabilities. The development
  publisher restores RID-specific packages inside a separate source staging tree,
  preserving the source checkout's normal lockfiles and the running host's files.

No model-quality improvement, secure VM boundary, production readiness,
cross-platform host qualification or physical-phone success is asserted.

Protocol references: [MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk),
[Chat Completions](https://developers.openai.com/api/reference/resources/chat),
[OpenClaw hooks](https://docs.openclaw.ai/plugins/hooks),
[Docker isolation](https://docs.docker.com/ai/sandboxes/security/isolation/).
