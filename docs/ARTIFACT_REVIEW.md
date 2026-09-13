# Artifact readback and failed-task retirement

The host compares the worker file with the exact proposed import after native
quiescence and before exposing an approval decision. An `ArtifactCheck` is now
stored on the run and in a `research.artifact.checked` event. It identifies the
approval, filename, expected content hash, independently computed observed hash,
time and one of four statuses: `matched`, `content-mismatch`, `identity-mismatch`
or `unavailable`. Unavailable reads retain a bounded exception type, not raw
worker exception text or an invented content hash.

A failed comparison still refuses import, revokes the grant and stops worker
ownership. Its message now explains whether the bytes differed, the identity/hash
was inconsistent, or readback could not be verified. Neither a successful
quotation check nor a pending approval substitutes for matching artifact bytes.
The task detail view preserves these receipts after cancellation and retirement.
Even a matched receipt is not evidence of an approved or completed host write.

This is an additive run/event field; old history remains readable without
rewriting its JSON or changing the SQLite schema version. The original proposal,
its approval digest, source checks and resource limits remain unchanged. Automatic
correction of a mismatched artifact is not implemented by this checkpoint.

## Native Lab cleanup

After a failed case, the runner cancels it, then gives the normal product
controller up to 30 seconds to finish retirement before saving the failed
capture. This cleanup interval is separate from the work deadline and permits
no new model work. Failure to confirm retirement remains explicit; workspace
files are retained for inspection and require the separate reviewed removal flow.

An already-cancelled registered Lab task can also be retired explicitly:

```powershell
dotnet run --project tools/Thaddeus.NativeLab -- retire artifacts/native-luna-pilot-20260913-a live-unchanged-0
```

The command checks its saved capture/registration identity and rejects active,
approved, imported or already-finished tasks. Its product-host composition disables
model transports, worker preparation, waking and execution. Only existing owned
QEMU reconciliation and retirement are available. It verifies unchanged model
usage, goal and execution-command history, preserves the original capture hash,
and records source/assembly hashes in a separate maintenance intent. It does not
delete the workspace or reinterpret the original campaign verdict.

## September 13 proof

- Five coordinator cases cover mismatched bytes, path, hash, malformed response
  and unreadable file. All refuse approval, retain the comparison through a
  restart, retire after cancellation and allow a later task without inference.
- The full backend suite has 318 passing tests. A browser fixture verifies the
  historical mismatch receipt and absence of an import action after cancellation.
- Explicit maintenance of the real failed Luna workspace completed with zero
  new inference attempts, unchanged four calls / 73,160 reported tokens, unchanged
  original capture and an inspectable retained workspace. The repeated command
  was refused before another maintenance intent. No QEMU process remained.
- Private receipts are in
  `artifacts/native-luna-pilot-20260913-a/live-unchanged-0/retirement-check.json`,
  `artifacts/artifact-recovery-20260913` and
  `artifacts/ui-artifact-receipts-20260913/browser-results.json`.

The earlier live pilot remains failed. Its old capture still describes cleanup
as pending at capture time; the separate maintenance receipt establishes the
later retirement. The model's conflicting artifact/proposal has not been repaired
or silently replaced. A revised import workflow needs its own registered test.
