# Development status — 2026-09-13

All six [delivery gates](IMPLEMENTATION_PLAN.md) remain open. This checkpoint
implements and tests foundations; it does not claim a complete OpenClaw product.

The [model connection form](MODEL_CONNECTIONS.md) adds explicit native or
session-only credential storage, endpoint binding, removal and setup checks that
do not generate text. Keys stay outside application data and the worker. Local
validation passed 515 backend tests and sixteen ordinary browser checks;
native package receipts separately establish each operating system's support.
Signing and consumer credential prompts across upgrades remain open.

Credential/restart validation also exposed unsafe store disposal during an
admitted write. A deterministic regression fails before the fix; disposal now
uses the same operation lock, and the single owned SQLite connection does not
leave a pooled handle behind after its lease ends. Earlier failed test receipts
remain under `artifacts/model-onboarding-20260913`.

The [portable package workflow](PORTABLE_PACKAGES.md) now builds native Windows
x64, Linux x64 and both Mac architectures from captured sources, with the runtime
and PWA included. An extracted Windows package passed local startup, owner-ticket,
conflict and data-preserving restart checks. Native CI executes each target;
the retained run receipts determine which archives are verified. This adds a
foreground Unix launcher, not a signed installer or a macOS/Linux VM worker.
Main-study data and its running Windows instance are not changed by packaging.

The current worker qualification work exercises the actual [transport boundaries](TRANSPORT_BOUNDARIES.md):
37 focused checks plus a real VM routing/size-limit/request-exhaustion workflow.
It exposed and fixed post-stop command admission before the asynchronous process
receipt finished. This is bounded Windows development evidence, not a claim of
universal confinement or a qualified default installation.

The prior [checkpoint recovery](WORKER_CRASH_RECOVERY.md#restoring-a-saved-stopping-point-in-the-product)
update is active in the main study: exact owner inspection and restoration,
435 backend checks, fifteen browser checks and the real VM workflow with synthetic
replies. Its closed backup and byte-level preservation receipts are under
`artifacts/checkpoint-recovery-20260913`. Historical builds below retain their own
evidence cutoffs; use the latest private `checkpoint.json` for current process,
package and CI identities.

The first [workspace redesign](UI_REDESIGN.md) is implemented: conversation in
front, separate saved collections, a collapsible activity log and an animated
pixel raven. Collection schema 4 preserves existing history; To-do completion
is explicit. Local validation passed 410 backend tests, fifteen ordinary
browser checks and the full native research path with synthetic replies.
Feed is saved reading for now; subscriptions and final user design acceptance
remain open. Upgrade and current-instance receipts for this pass are retained
under `artifacts/ui-workspace-20260913`; the instance history below describes the
previous builds, not a claim that the entire architecture is finished.

The [guided Windows preview setup](HOST_SETUP_PREVIEW.md) now uses the actual
product worker factory and an owner-enabled, installation-bound configuration.
Its complete native browser workflow passed with separate guest/host relay
ports, seven synthetic calls, one bounded repair and reviewed workspace removal.
All 391 backend tests and twelve ordinary browser checks passed locally. This
is an explicit development option; default and production qualification remain
separate, open requirements.

Published Windows development packages also include a [portable launcher](WINDOWS_LAUNCHER.md):
it keeps data outside the package, reuses an exactly recorded running host,
refuses conflicts and opens the browser with a one-minute, single-use owner
handoff. Signing, downloaded-package trust and consumer installation remain
unqualified. The main study uses this launcher with its existing data directory;
the default launch profile reuses the current owned host.

| Area | Implemented | Still required |
|---|---|---|
| Product | Conversation, scoped plans, approvals/history/export; research composer, durable coordinator, native continuation and reviewed import; visible token usage, per-message limits and reviewed workspace removal | Qualified default worker admission and broader interruption recovery |
| Isolation | `ISandboxBackend`, pinned Docker CLI adapter; explicit Windows QEMU backend with owned processes, queried host resource caps, mutual TLS, private overlays, bounded transfer and explicit crash reconciliation | Resolve Docker image-service failure; complete production confinement/resource and broader crash qualification |
| OpenClaw | Pinned image, public Gateway adapter, bootstrap/context hooks; native integration with Luna High; product-host research orchestration verified through a real VM with scripted replies | Qualified worker network path and production admission; broader outcome reconciliation |
| Brokers | Official MCP SDK, short-lived task grants, scoped tools, exact import proposals, model destination/budget enforcement; bounded public-page retrieval with task host grants and source receipts | Public search and research admission, qualified network path, native effect reconciliation |
| V1 reuse | Declarative personality, frozen source context and native hooks; explicit source-linked memory and scoped delivery; native quotation checks and bounded repair with retained attempts | Broader evidence validation and independent model/product-quality evaluation |
| Lab | Independent native runner uses the product API/coordinator; frozen repeated controls, actual repair delivery, separate scorecards and false-success negatives verified with real OpenClaw/QEMU and scripted replies; one live Luna captured-file task passed with complete usage | Live native comparisons, controlled model context/sampling, held-out quality evaluation and worker/provider resource measurements |
| Distribution | Native portable package pipeline, bundled PWA/runtime, Windows launcher and foreground Unix launch path; extracted archive checks | Signed supported releases, consumer lifecycle, macOS/Linux worker qualification and wider OS coverage; physical phone last |

## Development instance history

The host uses `http://localhost:5179` and the existing private `.data` directory.
The Luna bridge uses loopback port 5181, model `gpt-5.6-luna`, high reasoning.
After the user's shutdown, both ports were confirmed stopped. A complete closed
data-directory backup was copied and hash-verified before starting the tested
`ba60e17` package. The main browser now shows the token bar and remains unlocked.
Its 19 tasks, five pages, ten chat entries and 103,309 reported live tokens were
observed after that initial startup. Recovery receipts:
`artifacts/recovery-20260913-0554`.

The subsequent guided-setup update runs the self-contained `607d9b9` package,
whose exact revision passed CI run `34763653178`. A fresh closed-data backup and
row/file comparison preserved all 20 tasks, 232 events, five pages, 18 revisions
and 12 chat entries present at this update. The existing owner browser remained
unlocked and visibly reported 116,213 retained live tokens. This total excludes
separate Lab/CLI use and is not a bill or account quota.

The operator-configured Windows preview is now checked and explicitly enabled;
its host worker listener is loopback port 5183. The model remains Luna High via
5181. No model call or VM boot was used to enable this main instance. Package
and state receipts are under `artifacts/host-setup-20260913`; the active web asset
is `index-TTT9Ciq3.js`. The first launch used the repository as content root and
returned 404 for the web shell; it was corrected to the package working
directory. Both launch records and the prior immutable package are retained.
Default installation and production admission still await qualification.
Glimmer remains loaded elsewhere for shared benchmarks; no local inference,
reload, eviction or benchmark mutation was performed for this checkpoint.
The worker model gate refuses direct local GPU requests until a resource lease
adapter is implemented. The caller's existing benchmark queue must be respected.

The user has requested a UI redesign after the functional work. See
[UI_REDESIGN.md](UI_REDESIGN.md) for the observed problems and acceptance scope;
the current interface is not an accepted final design.

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

- [Full QEMU runtime package](QEMU_RUNTIME_PACKAGE.md): new launches require a
  pinned manifest of the complete vendor tree, including libraries and firmware.
  A fresh portable extraction produced 3,389 verified files; changed, missing,
  unexpected, linked and case-colliding inputs are refused. The real native
  question/shutdown/restart/import workflow passed with that bundle at `3ad2211`;
  post-run hashes and exact saved bytes matched. Receipts:
  `artifacts/qemu-package-integrity-20260913` and
  `artifacts/qemu-managed-scripted-web-1789308260562`. All 379 backend tests pass.
  Five synthetic responses, zero live inference/GPU. Publisher signing, updates,
  guided distribution and broader worker qualification remain open.
- [Windows VM host resource limits](QEMU_HOST_RESOURCES.md): committed-memory, CPU
  and host-process limits are applied and queried before QEMU starts. Six added
  real Windows resource controls passed; the complete ownership fixture has 16
  checks and the backend suite has 359 passing tests. A new native public-source/
  question/whole-VM restart/exact-import workflow passed at source `af15ab3`, with
  limits unchanged across both boots and shutdowns. Five synthetic responses,
  zero live inference/GPU; pinned inputs unchanged and no VM remaining. Receipts:
  `artifacts/qemu-host-resource-20260913` and
  `artifacts/qemu-managed-scripted-web-1789307315188`. Broader qualification remains open.
- [Live captured-file pilot](NATIVE_LAB.md#september-13-captured-file-pilot): one
  registered Luna High task passed the native question/shutdown/resume/capture/
  exact-import workflow and independent document checks, without repair. Four
  calls reported 70,874 input + 1,174 output = 72,048 tokens, with no unknown usage
  or remaining reservations. The campaign's `usage.md` makes its accounting
  directly visible. All 227 source/assembly/VM pins were unchanged; the grant was
  revoked and the owned worker removed. Receipt:
  `artifacts/native-luna-artifact-20260913-a`, source `91b1a09`.
  Protocol `PASSED`, efficacy `INCONCLUSIVE`; no GPU, repeat or release claim.
  The backend suite has 359 passing tests. Default worker admission remains open.
- [Captured-file import](ARTIFACT_IMPORT.md): new managed tasks use contract 2;
  approval is built from the paused worker's file instead of a second model-authored
  content copy. Source failures retain the file and can request one native correction
  inside the original budgets. The real OpenClaw/QEMU product/browser case passed
  with seven synthetic replies / 910 test tokens, exact import and reviewed removal;
  all VM pins were unchanged and no VM process remained. Full backend suite: 345;
  ordinary browser suite: 12. Receipt: `artifacts/research-artifact-reference-20260913-a`.
  The separate live check above passed; default worker qualification remains open. Source changes
  are verified separately from the main app's running `ba60e17` package.
- [Artifact review and retirement](ARTIFACT_REVIEW.md): failed readback now retains
  expected/observed hashes and an actionable classification, visible after cancellation.
  The original failed Luna workspace was retired without new inference or deletion;
  four calls / 73,160 tokens and its original capture remain unchanged. The full
  backend suite has 318 passing tests, plus a focused browser check. These changes
  are in source; the running main app remains the tested `ba60e17` package.
- [Live native Lab pilot](NATIVE_LAB.md#september-13-live-pilot): Luna High completed
  source read, durable question and native continuation, then produced a proposal
  differing from the written artifact. Import was refused and the campaign stopped
  before its second arm. Four calls consumed 73,160 reported tokens; no GPU or
  automatic repeat. The failed capture and workspace remain available. A separate
  Unicode-serialization fix corrected a Lab response-hash false alarm using saved
  evidence only. Verdict remains `INCOMPLETE_OR_FAILED` / `INCONCLUSIVE`; 313 tests pass.
- [Independent native Lab](NATIVE_LAB.md): six frozen native cases completed,
  reversed-order repeats agreed, and both valid-quotation/wrong-conclusion negatives
  were independently caught. All six exact imports were verified, with four false
  successes retained in content scoring. Twenty-eight scripted replies; no live
  inference or GPU. Private campaign: `artifacts/native-lab-protocol-20260912-a`.
  Protocol verdict `PASSED`, efficacy `INCONCLUSIVE`; 307 backend tests pass,
  including deterministic reproduction of a CI-exposed cancellation-ordering race
  and preservation of approved imports awaiting reconciliation.
- [Native evidence repair](NATIVE_EVIDENCE_REPAIR.md): versioned quotation contracts,
  captured source checks, one correction within original limits, fail-closed
  validation and exact approval/review binding. The backend suite has 286 passing
  tests and the ordinary browser suite has 11. A real OpenClaw/QEMU browser case
  delivered failed-check feedback, retained the failed draft, corrected it, verified
  exact approved import and removed its workspace: `artifacts/research-browser-evidence-20260912-a`.
  Seven synthetic requests, no live model/GPU; no research-quality improvement claimed.
- [Source-linked memory](SOURCE_LINKED_MEMORY.md): explicit notebook entries,
  source-version checks, correction/forgetting and scoped native context. The
  255-test backend suite verifies stale-context refusal and retained usage for
  in-flight revocation. Eleven browser cases pass across the ordinary checks;
  a separate native VM case observed the selected entry and excluded unselected
  text in all five model requests. Receipt: `artifacts/research-browser-memory-20260912-b`.
  These are synthetic transport/activation checks, not model-quality results.
- [Workspace maintenance](WORKSPACE_MAINTENANCE.md): owner-reviewed removal is
  available independently of worker startup. A fresh real OpenClaw/QEMU browser
  case verified removal after exact import, preserved the note and receipts,
  revoked the grant and confirmed workspace absence. Receipt:
  `artifacts/research-browser-removal-20260912-b`. The current backend suite has
  237 passing tests; ten ordinary browser tests and one explicit native VM browser
  test pass. These maintenance checks used no live inference or GPU.
- [Research workflow](RESEARCH_WORKFLOW.md): the real product host and browser
  completed native note/public-page research, saved question, reload, continuation,
  artifact readback, exact approval/import, export and retired workspace. Five
  synthetic responses; no inference. Receipt: `artifacts/research-browser-20260912-d`.
- This checkpoint adds coordinator/API coverage for admission, stopped-worker
  review, grant rotation, interruption/cancellation, incomplete cleanup, committed
  answer/decision recovery and caller-supplied chat budgets. The backend suite is
  214 tests; the default browser suite is ten tests, plus one explicit native VM
  browser test. [Token accounting](TOKEN_USAGE.md) is visible on every screen.

- Previous backend suite: 194 passing tests, including the official MCP client over the
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
