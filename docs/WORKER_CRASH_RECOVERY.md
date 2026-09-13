# Explicit worker recovery

The Windows QEMU development backend now reconciles abandoned worker ownership.
It does not automatically resume a task, repair an image, replay a command, or
enable product admission. The owner can now inspect and restore a confirmed saved
stopping point through the task view. All six delivery gates remain open.

## Ownership and disk inspection

`QemuSandboxBackend` holds an exclusive `qemu-owner.lock` in its Store for the
worker's lifetime, including stopped periods. Removal or backend disposal releases
it; Windows releases it on host death. This supplements the Store's single-host
lock and rejects another adapter in the same host. Registration must match the
active worker, pinned image and exact task broker route.

`ReconcileStopped` requires that lease and a read handle which denies any writer
to the overlay. It records recovery intent, retires only the named credentials in
bounded `boot-<id>/control` and `console` directories, and checks the overlay using
the pinned `qemu-img check`. The command has no repair or force-share option. Its
explicit read-only qcow2/file nodes and null backing prevent an image header from
selecting another backing file or protocol. Before/after hashes check that the
inspection did not change the overlay.

Only a successful check with an unchanged image permits physical status `stopped`.
A missing or locked image is retained as unresolved. A corrupt image stays
`recovery-required`, with diagnostics and no boot. This check concerns qcow2
structure; it is not a guest filesystem repair or proof that every application
file survived a crash. QEMU diagnostics are now retained in bounded private logs.

Credential retirement checks entry names, sizes and links before deleting known
files. A recorded Windows CNG key must have the generated ownership-name format;
deletion is checked against the current user's key store. No global key-store
sweep, PID-based process killing or recursive directory removal occurs.

## Task continuation and the storage bug this found

The first real crash attempts exposed two separate problems. The stopped-VM
fixture endpoint returned an empty body instead of JSON null; that was a fixture
response bug. The next attempt reached grant refresh and failed because all four
recently written bootstrap files had returned as zero-length files. Stopping the
agent had not flushed the guest filesystem. The failed receipts/disks remain at
`artifacts/qemu-managed-scripted-crash-1789259864482` and
`artifacts/qemu-managed-scripted-crash-1789259962053`.

The shared OpenClaw stop adapter now invokes Linux `syncfs` after a successful
`sessions.abort` response. It must acknowledge that checkpoint before the host
records quiescence. An error or missing checkpoint leaves the operation uncertain;
it does not cause another stop or task dispatch automatically. This establishes a
storage barrier for writes already submitted at that checkpoint. A worker's
checkpoint response remains worker-reported evidence, not a trusted task outcome.

The recovery fixture reopens only its marked private Store with the same worker,
installation, task and frozen context. It revokes the old task grant, invokes the
shared Runtime recovery classifier and reconciles the physical worker. Unknown
task effects remain unknown. A pending question with acknowledged execution
commands remains pending. Recovery itself leaves the VM stopped.

After explicit boot, `OpenClawBackend.RefreshGrant` checks the original binding,
context/configuration hashes and exact environment shape. It requires the Gateway
port to be available, then atomically replaces only the short-lived task grant,
preserving the Gateway key, context and transcript. The replacement file and
directory are fsynced. The host verifies its returned digest. This does not
bootstrap a new session or put a provider's master credential inside the worker.

## Evidence and limits

The scripted native crash workflow passed at
`artifacts/qemu-managed-scripted-crash-1789260238398`. The pinned OpenClaw engine
read the fictional source and asked a durable question. After quiescence, a
separate fixture command appended/fsynced a unique private marker and remained
in flight. The controller killed only its own host process. A witness holding
the original QEMU process handle observed that VM exit, avoiding recycled-PID
ambiguity.

Recovery rejected both old credentials, retired two abandoned CNG channel keys,
preserved the question and model-call count, and did not boot a VM or replay the
unfinished command. Explicit continuation on the same overlay found the marker
exactly once and completed the exact approved import. All model responses in this
case were scripted; no inference or quality improvement is claimed.

The complete public-source variant and the corrupted/missing/locked-image
controls passed at `artifacts/qemu-managed-scripted-web-crash-1789260550088`.
The killed fixture host was PID 8096; QEMU processes 38668 and 46664 used distinct
TLS 1.3 channel credentials. Five scripted responses produced four capability
receipts. An independent host check matched the imported Markdown exactly,
confirmed the marker occurred once, found no retained client keys, checked both
abandoned CNG keys were absent, and rehashed the unchanged base disk. The three
negative image cases retained their evidence and refused further execution.
The existing container control also passed with the updated shared stop adapter
at `artifacts/native-integration-scripted-1789260670913`. The backend suite has
194 passing cases; seven Node protocol/configuration cases passed as well.

Reproduce the full public-source variant and additional corrupted/missing/locked
image controls with:

```powershell
dotnet build --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
node scripts/qemu-managed-integration-check.mjs artifacts/qemu-inputs-script-check artifacts/qemu-worker-20260912-native scripted-web artifacts/qemu-runtime-FRESH/runtime-reference.json --host-crash
if ($LASTEXITCODE -ne 0) { throw 'Crash recovery check failed' }
```

This is abrupt application-host termination with Windows still running. It is
not a physical power-loss test, exhaustive crash-point coverage, an escape
assessment, an external-effect reconciliation proof, or macOS/Linux host evidence.
Crashes before a storage checkpoint can still require manual inspection of
incomplete worker files. Production qualification and cross-platform packaging
remain separate work.

New invocations also require a [prepared runtime package reference](QEMU_RUNTIME_PACKAGE.md).
The earlier crash receipt keeps its original installation and evidence scope.

## Restoring a saved stopping point in the product

A host can stop after the native `syncfs` acknowledgement and artifact capture,
but before the controller records that the question or review is ready. The run
then correctly requires attention. Its owner can select **Inspect saved worker**
in task details. The host classifies durable receipts, revokes the old grant and,
only for an eligible checkpoint, uses the existing physical reconciliation path.
Inspection does not boot the VM or send a model request. It can retire abandoned
channel credentials and writes its own host receipt; it is not a wholly read-only
operation.

An eligible review is bound to the exact task version and digest for ten minutes.
**Restore saved checkpoint** restores an unanswered question, a pending captured
artifact review, or a paused saved answer/correction. Answering, continuing, and
approving an import remain separate actions under their existing checks. Unknown
command/model outcomes, outstanding reservations, a missing storage checkpoint,
changed frozen context, expired approvals, and missing or mismatched captured
bytes cannot be cleared this way. An already approved import uses the separate
write-reconciliation controls. Cancellation or another receipt invalidates a
review in flight. Paired browsers cannot invoke the owner-only recovery endpoints.

The recovery field is additive to stored research JSON; older runs deserialize
with no recovery review. Database/export schema remains 4. The event ledger
records inspection and restoration without changing historical command receipts,
the saved question, prepared context, or model usage.

Current evidence is under `artifacts/checkpoint-recovery-20260913`: 435 backend
checks, 15 ordinary browser checks, and the real QEMU/OpenClaw browser workflow.
The latter injects an interruption **after an actual stopped-VM checkpoint**,
before the ready-state receipt; it is not an abrupt process-kill or power-loss
test. The browser reloads, inspects the unchanged overlay, restores the unanswered
question with the same three synthetic model calls and 390 reported fixture
tokens, then explicitly answers. The complete workflow ends with seven synthetic
replies, one source correction, exact approved import and verified workspace
removal. Inspection and restoration add zero model calls, zero boots and zero
replayed commands. The desktop and 390-pixel recovery views were visually checked.
No paid inference, GPU inference, quality gain, or broader platform qualification
is claimed.

Reproduce on the explicitly enrolled Windows development installation, using a
fresh private fixture directory and the pinned installation JSON:

```powershell
dotnet run --project tools/Thaddeus.ResearchCheck -- FRESH_PRIVATE_ARTIFACT_DIRECTORY PINNED_INSTALLATION_JSON checkpoint-recovery
```

In a second shell, point Playwright at that fixture's origin and directory, set
`THADDEUS_NATIVE_RESEARCH=1` and `THADDEUS_CHECKPOINT_RECOVERY=1`, then run
`web/tests/research.spec.ts`. Never point this fixture at the user's active data.

Sources: [QEMU image checks](https://www.qemu.org/docs/master/tools/qemu-img.html),
[QEMU 11.1 image-opening source](https://github.com/qemu/qemu/blob/v11.1.0/qemu-img.c),
[Linux filesystem checkpoint semantics](https://man7.org/linux/man-pages/man2/sync.2.html).
