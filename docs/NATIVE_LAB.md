# Independent native Lab

The native Lab executes the ordinary product research workflow. Its test-only
Windows runner composes the pinned QEMU factory and an explicit synthetic model
transport into the real product host. It authenticates a fresh owner session and
uses `/api/chat`, question, approval, export and workspace-maintenance routes.
The product's coordinator, native OpenClaw engine, MCP tools, source checks, model
admission, exact import approval and independent file verification are unchanged.
The default production worker remains unavailable pending qualification.

The host can receive a registered research policy through trusted dependency
injection. There is no request-body or settings selector that disables evidence
checks. The Lab compares the existing version-3 evidence profile with the explicit
unchecked version-3 control. Both use identical source context, tool contracts,
provider and budgets; the evidence check and its bounded repair are the difference.

## Registration and execution

`tools/Thaddeus.NativeLab` provides `register`, `run` and read-only `grade` modes.
Registration records source paths and byte hashes, source revision, loaded product
and Lab assembly hashes, host runtime/platform, pinned VM inputs, provider, budgets,
fixtures, schedule, policy digests and prepared context/tool hashes. It creates
preflight stores without starting a worker or model. Running refuses changed source
or binaries, an existing run intent, or any previously created planned workspace.
There is no resume or selective replay command. Raw requests/responses, exports,
run events and captures are retained under the campaign directory.

The six-run protocol uses two reversed-order repetitions of the unchanged and
candidate arms, plus a separate valid-quotation/wrong-conclusion negative in both
arms. Each task allows eight model calls, 16 broker tools, one repair, 4,096 output
tokens per call, 96,000 total tokens and 180 active seconds. The campaign has a
ten-minute wall deadline. These are scripted counts; no inference endpoint or GPU
is contacted. Host CPU and shared-process peak memory are recorded, explicitly
excluding worker/provider peaks and GPU measurements.

The synthetic responder follows the actual `repair-requested` tool feedback, without
reading the selected policy arm. It first writes an injected defect. A correction
only follows delivered feedback. The valid-quotation negative intentionally keeps
an incorrect duration even though its quotation passes the production validator.
Successful fixtures remove only their own worker storage through the reviewed
product API; imported notes, exports and receipts remain. Infrastructure failures
stop the campaign and retain their failure record and partial evidence.

## Independent scoring

The evaluator lives in `evals/Thaddeus.Lab`, outside the model-visible workspace.
It reads captures after execution, verifies request/response hashes against model
dispatch receipts, context delivery, observed tool-catalog consistency, unchanged
allowances, recorded answers, exact approval and artifact hashes, source identity,
events, worker removal and grant revocation. It checks a small typed report against
the fictional source facts and recorded user answer without calling the production
validator or trusting its pass flag. A correct quotation alone cannot satisfy that
content check. Run `succeeded` with incorrect content is reported as false success.

The report keeps three distinct scorecards:

- Model capacity: `NOT_EVALUATED`; scripted replies cannot measure a model.
- Task capability: observed native transport, questions, repair and exact imports.
- Product quality: the declared fictional content predicates only, with false
  successes retained. General research quality remains unmeasured.

Unknown or unresolved usage retains nullable input/output counts, charged tokens
and reservations. It never becomes measured zero. Repeated controls must agree,
the entire schedule must be captured exactly once, and both false-success negatives
must be caught for a protocol pass. The efficacy decision remains `INCONCLUSIVE`
with no performance promotion, sealed holdout claim or architecture-gate closure.

Example development commands, from the repository root:

```powershell
dotnet run --project tools/Thaddeus.NativeLab -- register artifacts/native-lab-new artifacts/pinned-installation.json
dotnet run --no-build --project tools/Thaddeus.NativeLab -- run artifacts/native-lab-new
dotnet run --no-build --project tools/Thaddeus.NativeLab -- grade artifacts/native-lab-new
```

The installation file must be an independently checked development QEMU configuration,
not an arbitrary downloaded executable bundle. Run only against fresh fictional data
under `artifacts`; this tool is not part of the shipped product or a phone workflow.

## September 12 evidence

`artifacts/native-lab-protocol-20260912-a` completed all six planned cases through
real OpenClaw/QEMU workers. Registration froze 206 source files, five loaded
assemblies and the five pinned VM inputs. The original report and subsequent
read-only regrade both report `PASSED` for the protocol and `INCONCLUSIVE` for
efficacy. The regrade additionally compares repeated artifact hashes and records
the evaluator assembly hash; it does not repeat a worker or model request.

| Case | Arm | Repetitions | Calls per run | Exact imports | Content check | Repair feedback |
|---|---|---:|---:|---|---|---|
| Injected quotation/duration defect | Unchecked | 2 | 4 | Verified | Failed | None |
| Injected quotation/duration defect | Evidence | 2 | 6 | Verified | Passed | Observed |
| Valid quotation, wrong duration | Unchecked | 1 | 4 | Verified | Failed | None |
| Valid quotation, wrong duration | Evidence | 1 | 4 | Verified | Failed | None |

All four repeated outputs have matching hashes within their respective arms.
The independent grader identifies four false successes, including both deliberately
wrong conclusions with valid quotations. The run recorded 28 synthetic requests
and 3,640 synthetic tokens. All six grants were revoked, worker registrations
marked purged, and private workspace directories absent. No QEMU process remained
after completion. Source notes, imported artifacts, raw requests/responses and
before/after exports remain in the private campaign data.

The final backend suite has 307 passing tests, including independent false-success
scoring, missing/tampered request and response evidence, changed model/budgets,
incomplete event streams, unknown usage and invalid infrastructure captures.
The run's source manifest remains the authority for the executable checkpoint;
later documentation and evaluator refinements are not retroactively presented as
the originally executed binary. Production runtime, model fixture and registered
inputs were unchanged by the read-only regrade.

Linux CI then exposed a cancellation-ordering race in the coordinator. A follow-up
fix lets explicit cancellation close unapproved research even when provisioning
records attention first. An approved import with an uncertain outcome must still
be reconciled or abandoned; cancellation cannot hide it. Both failure cases were
reproduced with the real coordinator/store before the fix and pass afterward.
This later cancellation change is separate from the recorded six-run campaign.

Still open: live native model comparisons, controlled sampling/context windows and
runtime-generated prompt metadata, disjoint model holdouts, broader research-quality
grading and worker/provider resource measurements. This protocol alone does not
qualify production confinement or complete the Lab delivery gate.
