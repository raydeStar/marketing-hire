# Native source quotation checks and bounded repair

This describes the preserved version-3 profile and import contract 1. New research
tasks use [version 4 and captured-file import](ARTIFACT_IMPORT.md), with the same
quotation validator and correction limits but host-captured content and native
continuation for feedback.

Contract-1 research tasks freeze the registered `thaddeus-evidence` version-3 profile.
The existing `thaddeus_propose_import` MCP tool adds a versioned `citations`
contract. Each item names a captured source, its exact recorded version and a
nonempty quotation. Source identities are note paths, `memory:<id>` for explicitly
selected memories, or the final URL from a successful public retrieval receipt.
The worker must include the quotation and source path/URL in its proposed artifact.

The deterministic host validator compares quotations with the captured text and
versions. Memory citations can use only the selected source quotation; a remembered
statement is not independent evidence. Public citations require broker-observed
receipts, matching captured text hashes and the task's exact granted hostnames.
Truncated public text remains identified. No validator request reaches a model or
fetches additional sources. Duplicate, missing, invented or mismatched citations
cannot receive a passing assessment. One valid quotation does not establish that
all claims were cited, are true, or follow from that quotation.

Each reviewed attempt retains its exact content, citations, content/proposal hashes,
assessment, time and outcome in the run. The assessment and capability response
commit together with the run/event transaction. An invalid proposal can return
`repair-requested` once, limited by both the frozen profile and task repair cap.
OpenClaw consumes that tool feedback in its existing loop. The host does not run a
second drafting or judge loop. Retrying the same operation ID returns its recorded
result without spending again; changing arguments under that ID is refused.
An unchanged failing proposal under a fresh ID stops. A second different failure
also stops after the repair allowance is used.

Repair never increases model calls, tools, tokens or active time. A request is
offered only while another model call and token allowance remain, no model
reservation is unresolved, and at least two tool calls remain for another proposal
and its approved import. This admits a possible correction; it does not guarantee
that the model can finish inside the remaining allowance. Normal broker limits
continue to apply to every subsequent dispatch. Product research defaults remain
six model calls and 96,000 tokens. Mandatory path/scope/size/write-policy violations
remain rejected tool calls, never quality-check passes.

Changed or unavailable frozen sources stop the proposal without repair. Validator
exceptions or inconsistent verdicts produce `validation-unavailable`, no passing
claim and no approval. This intentionally rejects v1's fail-open validator catch.
A passing assessment is bound to the approval ID, destination, content and proposal
hash. Original files still require the existing independent artifact readback,
stopped worker, exact user approval, source/destination version checks, durable
write intent and post-write verification. The review cannot authorize a write.
Explicit completion of an interrupted import also requires its matching review;
read-only verification of an already recorded effect remains possible.

`thaddeus-evidence-unchecked` version 3 is a separately registered experiment
control with the same citation schema and context. It records `not-evaluated`,
never a pass, and cannot bypass scope, source versions or import approval. The
product API does not accept a profile selector. The independent native Lab still
needs frozen controls and graders; introducing this control does not close that gate.

Version-1/2 profiles retain their earlier digest shapes and proposal contract.
Old JSON rows are not rewritten and default to an empty `nativeProposals` list.
No new database migration is needed. Task details disclose both failed and passing
checks separately from exact file verification and the remaining factual review.

## Verification boundary

The backend tests exercise exact note/public/memory provenance, malformed inputs,
idempotency, repetition, exhausted allowances, source changes, validator faults,
approval binding, explicit unchecked controls and legacy profile/JSON compatibility.
The normal browser checks continue to cover token usage and task allowances.
`ResearchCheck` additionally injects one incorrect quotation into its synthetic
native transport, then supplies a corrected artifact. Its browser explicitly
selects an eight-call fixture allowance; this does not change product defaults.
The fixture must observe seven requests, one correction, two retained assessments,
one exact approved import, grant revocation and verified workspace removal.

`artifacts/research-browser-evidence-20260912-a` passed that real OpenClaw/QEMU
workflow through the product browser. The sixth native request contains the
broker's exact failed-proposal hash and quotation feedback. The corrected second
proposal alone received approval; the imported hash matches independent worker
readback. Both 1440- and 390-pixel screenshots disclose the two attempts and show
the token meter without horizontal overflow. Seven scripted replies report 910
synthetic tokens; they are not measured model consumption. The completed worker
was removed through Settings and its grant was revoked. The 286-test backend
suite and 11 ordinary browser checks also pass.

These are protocol, activation and negative-control checks. They establish neither
model improvement nor factual research quality, production VM confinement or a
completed architecture gate. Live model experiments remain separately declared.
