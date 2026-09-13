# Explicit, correctable remembered context

Knowledge now includes **Remembered context**. A user records a short statement,
chooses a saved note and quotes its exact supporting text. The host checks the
note's current SHA-256 version and quotation before saving. A valid quotation
establishes provenance, not that the user's statement is true or follows from it.
No model extracts personal facts, consolidates memories or profiles the user.

Entries can be corrected, reviewed after a source edit, or forgotten. A correction
uses optimistic version checks. Forgetting removes the entry's statement and
source quotation from the active library and leaves a content-free tombstone,
so a delayed save cannot resurrect the same identity. Minimal change receipts
record identity, operation, version and time; they do not duplicate the text.
Other tabs refresh through the authenticated event stream.

Original notes, prior task snapshots, provider-side copies, exports and backups
are separate records. Forgetting does not erase them or guarantee disk erasure.
The separate “Delete my data” flow also clears memory records and change receipts
after its existing worker-storage checks. Its device/provider settings remain.

## Scope and the single execution loop

The research composer accepts up to eight explicitly selected memory identities
and versions. None are selected automatically. A memory grant supplies its statement
and quoted source only; it does not grant the worker the source note's other text.
Full-note access still requires the separate selected-note capability. The host
refuses a changed/forgotten entry or changed source rather than silently fetching
a newer version on the user's behalf.

The context builder charges each memory read against the task's tool allowance,
retains the exact entry in `Run.MemoryEvidence`, and places it in the frozen context
with provenance and an explicit untrusted-data instruction. The existing 90 KB
context bound also applies. `thaddeus-evidence` version 2 enables selected memory;
version 1 and the baseline retain their original digests and behavior. Personality
and mandatory permissions are shared. The old conversation/plan loop refuses
memory selections instead of silently ignoring or implicitly recalling them.

The existing native context plugin delivers this prepared text through supported
OpenClaw hooks. OpenClaw remains the sole worker model/tool loop and its separate
memory plugin remains disabled. Thaddeus records prepared-context presence in
each actual model request. Presence proves delivery, not useful model reasoning.

Changing a selected memory or its source blocks a new worker start, continuation,
model admission and approval/completion of an old import. A provider request
already admitted cannot be recalled; its usage is still settled, then its reply
is refused if the selected memory changed in flight. A completed host write can
still be verified during reconciliation. Historical task snapshots stay intact;
a new task is needed to use the corrected context. Existing worker-private copies
are not retroactively wiped by a memory edit.

## Storage and compatibility

Database schema 3 adds `memories` and `memory_changes` transactionally. It leaves
existing run/event bytes and old policy/context hashes unchanged. A record and
its change receipt commit together. Export schema 3 gains additive `memories`
and `memoryChanges` collections; it reports the new database schema separately.
Older hosts refuse a newer database, so rollback requires a compatible backup.
The running user data has not been upgraded by these disposable checks.

Memory mutations use the same authenticated browser, origin, JSON and CSRF
boundary as manual note edits. Worker bearer credentials cannot call those app
routes. The library is limited to 256 active entries, 1,000 characters per statement
and 2,000 per quotation. Changes cannot grant tools, host files, network or secrets.

## V1 adaptation and evidence

The inspected v1 checkout was clean at
`d0cac1e1d67e5ffa94543747825992f0a0cc8a95`. Relevant references were
`packages/personality-engine/SirThaddeus.PersonalityEngine/Profiles/PersonalityProfile.cs`,
`packages/memory/SirThaddeus.Memory/IMemoryStore.cs` and
`src/Thaddeus.Runtime/Api/MemoryAuditApi.cs`. Their useful mechanisms are declarative
personality, explicit memory browsing/correction/deletion, provenance and exclusion
of forgotten records from recall. V1's automatic extraction, ranking, consolidation
and historical routing/performance claims were not transferred. No v1 files changed.

The current backend suite has 255 passing tests. Memory cases cover source/quote
validation, stale versions, transactional interruption, forgotten-record replay,
schema-2 preservation, baseline nonactivation, scoped quotations, explicit recall
charging, refusal before worker creation/resume/inference/import, and in-flight
revocation with preserved token accounting. Browser checks cover creating and
correcting an entry, source-change warnings, forgetting across tabs, reload,
export and layouts at 1440 and 390 pixels.

`artifacts/research-browser-memory-20260912-b` passed native OpenClaw/QEMU research,
question, restart/continuation, exact approved import and workspace removal. The
browser created two remembered entries, selected one, and withheld the source note's
full-read grant. All five actual native model request payloads contained the exact
selected context and omitted the other entry and unselected source text. The
independent host receipt records these checks. Model replies and their 650 reported
tokens were synthetic protocol fixtures; no paid inference or GPU was used.
The preceding `-a` fixture stopped at a source-selector labeling error before
worker admission. Its artifacts are retained.

This closes an implementation gap in selected-memory handling. It does not close
the full v1-reuse gate, prove a model-quality gain, qualify a production worker,
or complete native evidence repair and independent Lab evaluation.
