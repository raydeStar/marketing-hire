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
Those registrations remain unchanged. A separate single-task artifact pilot uses
the current version-4 profile and captured-file import contract 2.

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

## Explicit Luna High pilot

### Captured-file workflow and visible usage

`register-artifact-live` / `run-artifact-live` freeze and run one fictional
workshop task using the existing contract-2 product workflow. The exact model is
Luna High; allowances are six brokered model requests, 16 broker tools, one repair,
300 active seconds, 4,096 requested output tokens per call and 96,000 total tokens.
The wall deadline is ten minutes. There is no automatic repeat or paired-improvement
claim. Existing six-case and two-arm registrations retain their original plans.
Each run mode requires its matching command before any run intent or inference.

```powershell
dotnet run --project tools/Thaddeus.NativeLab -- register-artifact-live artifacts/native-artifact-new artifacts/pinned-installation.json
dotnet run --no-build --project tools/Thaddeus.NativeLab -- run-artifact-live artifacts/native-artifact-new
dotnet run --no-build --project tools/Thaddeus.NativeLab -- usage artifacts/native-artifact-new
```

The runner refreshes `usage.md` and `usage.json` in the campaign directory before
dispatch and while waiting for product checkpoints. They show known reported
input/output subtotals, calls with pending or missing usage, charges, reservations
and remaining per-task allowances. The read-only `usage` command opens existing
SQLite ledgers without constructing the product Store, migrating data, recovering
tasks or dispatching work. Unavailable accounting is explicitly unknown. These
figures cover this campaign, separately from the main app and other experiments.
The current bridge does not provide a certified remote token ceiling, dollar bill,
controlled sampling/weights or an independently measured CLI-internal call count.

Contract-2 scoring requires the captured file/request/source-review bindings,
recorded native question and any artifact correction, capture-before-review-before-
approval ordering, exact saved bytes and normal cleanup receipts. Document facts
are still judged independently of the production source-check flag. A protocol
pass with incorrect document content is a failed smoke check and a retained false
success, not a product improvement. No holdout or model-capacity result is claimed.

Generic runner exceptions are recorded as unclassified execution/capture failures;
they do not establish an infrastructure diagnosis. The legacy capture JSON field
`infrastructureFailure` remains readable for historical compatibility.

### Preserved paired contract-1 pilot

`register-live` and `run-live` opt into real inference through the existing fixed
Luna High development bridge on loopback port 5181. Ordinary `register`/`run`
commands remain scripted and reject a live manifest. Read-only `grade` supports
both modes. The live transport records each request before forwarding it once
through the same product inference adapter, then records the response and actual
provider-reported usage. Missing responses keep the broker's conservative charge.

The predeclared pilot runs one matched fictional workshop task in each existing
policy arm, with fresh worker state and no injected defect. Each task permits six
brokered requests, 16 broker tools, 300 active seconds, one repair, 4,096 requested
output tokens per call and a 96,000-token allowance. The combined allowance is
192,000 tokens and the campaign deadline is ten minutes. These allowances are
not a certified remote ceiling or dollar cap. No repeat starts automatically.
Only the declared fixture question, exact import and owned workspace removal are
answered by the runner; the model and tool loop remain inside native OpenClaw.

The accepted report is a Workshop brief heading followed by one typed JSON object,
plain or within one `json` Markdown fence, using LF or CRLF newlines. Required
fields, source facts, exact user answer and independent import checks remain
strict. This format is declared before inference. The original scripted scorer
keeps its plain-JSON format and repeated false-success controls.

```powershell
dotnet run --project tools/Thaddeus.NativeLab -- register-live artifacts/native-luna-new artifacts/pinned-installation.json
dotnet run --no-build --project tools/Thaddeus.NativeLab -- run-live artifacts/native-luna-new
dotnet run --no-build --project tools/Thaddeus.NativeLab -- grade artifacts/native-luna-new
```

Registration and startup record the bridge's advertised model without inference.
Wrapper sources, loaded product/evaluator assemblies, prepared source context,
tool contracts and VM inputs are frozen. Managed model weights, sampling,
runtime-generated prompt metadata, loaded CLI identity and internal CLI model-call
count are not independently certified. The report identifies these limits and
separates real provider counts from synthetic fixture counts. The Lab's private
task stores are separate from the ordinary app's retained-history token total.

A protocol pass requires both captures and complete usage/transport evidence.
Content correctness and false successes remain separately reported; a protocol
pass is not a quality pass. Two tasks cannot establish improvement or repeatability.
Model capacity remains `NOT_EVALUATED`, efficacy remains `INCONCLUSIVE`, and the
pilot must be reviewed before a repeat or disjoint validation consumes more tokens.

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

## September 13 live pilot

The registered `artifacts/native-luna-pilot-20260913-a` used source revision
`467a85df5de2373566469d8114e13f96f8095c44`, Luna High and the pinned OpenClaw/QEMU
worker. The ordinary `run` command was first verified to refuse its live manifest
before creating a run intent. Only the explicit `run-live` command dispatched.

The unchanged arm read the note, asked and persisted the audience question, stopped
the VM, then resumed through a new native continuation. It made four brokered model
calls: **70,866 input + 2,294 output = 73,160 reported tokens**, all settled with
zero remaining reservations. These are separate from the main app's token total.

The model's write request contained the required 177-byte typed JSON report, and
the native tool reported writing those bytes. Its following import proposal
contained a different prose report. The product stopped at artifact readback with
an `IOException`; no approval was offered by the runner and no host file was
imported. The specific exception text is not retained by the current coordinator,
so the content mismatch is established from the captured requests rather than a
detailed readback-error receipt. The second arm was not started. There was no repeat.

The failure handler revoked the task grant and cancelled the task. No QEMU process
remained, but the saved workspace and cleanup-pending state were retained for
inspection; completed workspace retirement/removal is not claimed. The original
capture, model requests/responses, failure and report remain unchanged. Campaign
verdict: `INCOMPLETE_OR_FAILED`; efficacy: `INCONCLUSIVE`.

The first report also flagged a response-hash discrepancy. A focused regression
reproduced JSON reserialization changing Unicode escaping in a parsed capture.
The evaluator now verifies the separately retained original JSON bytes against
the broker digest and checks that their parsed value matches the capture. Altered
bytes or altered parsed content still fail. A read-only regrade removes that
instrumentation error without importing anything, replaying inference, changing
the model's report or changing the failed campaign verdict. The regrade records
its distinct evaluator hash. The backend suite now has 313 passing tests.

Next development work: make artifact-review failures actionable, keep a proposed
import bound to the actual artifact throughout the workflow, and fully close the
failed-task cleanup path. Any changed workflow or new paid comparison needs a new
registration; this failed pilot must remain visible in subsequent evaluation.

A subsequent [artifact-review and retirement checkpoint](ARTIFACT_REVIEW.md)
adds durable readback classifications and completes failed-case retirement before
capture. An explicit maintenance run retired the saved pilot workspace without
model calls, worker execution or deletion. The original capture and failed verdict
remain unchanged; its later ownership state is recorded in `retirement-check.json`.
Automatic correction of the conflicting model proposal remains open.
