# Import the captured file

New managed research tasks use import contract 2 in the registered
`thaddeus-evidence` version 4 profile. OpenClaw writes its private file, then
requests import with `path`, `artifact` and `citations`. A second `content`
argument is rejected. This removes the duplicate model-authored copy that
diverged from the file in the first native Luna pilot.

The request is a durable intention, not an approval or a host write. Its
operation ID, destination version and citations are recorded together with the
broker result. Repeating the same operation returns that original result;
changing its arguments requires a new operation ID.

The product controller then:

1. Pauses OpenClaw and requires its native stop/checkpoint acknowledgement.
2. Reads the named file through the sandbox adapter. It independently checks
   the returned filename, content hash and nonempty UTF-8 text limit of 100 KB.
3. Checks the captured bytes against the existing source quotation validator
   and rechecks destination/write authority. It retains rejected bytes in the
   source-review record, along with the capture hash and failed assessment.
4. Builds an exact, expiring, destination-version-bound approval from those
   captured bytes only after source checks pass. The worker must also stop
   before the user can authorize import.
5. Applies the existing approved-write journal and independent readback checks.
   A later change to the private file cannot replace the captured content.

For a source-check failure, the existing profile/task repair limit and remaining
model, tool, token and active-time budgets decide whether one correction can be
requested. The controller saves and stops the VM, rotates its grant on reopening,
and delivers the recorded feedback through OpenClaw's native continuation API.
It does not generate a model reply itself. The correction has its own durable
`artifact-repair` command identity; a lost acknowledgement is not replayed.
Repeated failed content, exhausted budgets and unavailable validation stop the
task without approval. An unreadable or inconsistent file also stops for
inspection, without manufacturing content or automatically restarting work.

After a host restart, a saved correction awaiting dispatch requires explicit
resume. A restart during worker control requires inspection. Cancellation during
capture cannot create an approval. Paused time is excluded from the cumulative
active execution allowance, including when a saved task waits overnight.

The task view lists captured files and their correction/approval status. A file
capture is not an import receipt. Source quotations still do not establish truth,
entailment, complete claim coverage or overall research quality.

## Compatibility and evidence

Existing profiles and tasks retain their original digests and contract 1. Old
approvals, the failed Luna pilot, and frozen Lab campaigns are unchanged. The
new run collection is additive; old JSON defaults to an empty list. The separate
Lab's already-registered controls remain version 3 and keep their original
model/tool contracts. A future live comparison of contract 2 needs a new frozen
registration, not reinterpretation of the earlier failed pilot.

The implementation intent is retained at
`artifacts/artifact-reference-contract-20260913/intent.json`. Twenty-five focused
backend cases cover captured bytes, invalid references/readbacks, changed
authority, cancellation, bounded correction, repeated failures and restart
semantics. The complete backend suite has 343 passing tests; all 12 ordinary
browser cases pass.

The real OpenClaw/QEMU product/browser workflow passed at source `76043d9` in
`artifacts/research-artifact-reference-20260913-a`. It used seven synthetic replies
(700 input + 210 output test tokens), one recorded native artifact correction,
exact user approval/import, export and reviewed workspace removal. The first
captured file retained its failed assessment and never received an approval.
The second capture hash matched both the approval and saved host note. The native
continuation's actual model input contained the saved correction feedback.
Selected memory stayed present; unselected source text stayed absent. Both
desktop and narrow browser layouts passed. No VM process remained, and all five
pinned VM input hashes were unchanged afterward.

This used no paid inference or GPU. It verifies native integration with synthetic
replies, not live model reliability, confinement qualification or model efficacy.
The earlier failed Luna result is unchanged. A live contract-2 pilot remains
necessary, with its own registration and accounting.

The running personal development host is a separate previously verified package.
Default production worker admission remains disabled pending backend qualification.
