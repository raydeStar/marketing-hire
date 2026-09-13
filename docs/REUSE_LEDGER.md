# V1 reuse ledger

Read-only reference: `raydeStar/sir-thaddeus` at
`d0cac1e1d67e5ffa94543747825992f0a0cc8a95` (clean checkout at inspection).
No v1 files, user data, model weights, secrets, benchmark answers, or assets were
copied. These are mechanism adaptations in new code, not a language migration.
The reference checkout remained outside this repository and was not modified.

The audit read AGENTS, README, ASSISTANT_PIPELINE, EXPERIMENTATION, TESTING,
ARCHITECTURE_PUBLIC, RESEARCH_METHOD, research/README, CURRENT_EVIDENCE,
CALIBRATED_IMPROVEMENT_PLAN, and the implementations/tests below. Research reports
have their own older evidence cutoffs; their reported gains were not rerun here.

All paths below are relative to the pinned v1 source.

| Mechanism | Source / supporting evidence | Limit / decision | V2 verification |
|---|---|---|---|
| Exact tool evidence | `src/Thaddeus.Runtime/Chat/HarnessToolEvidenceStore.cs`; `tests/runtime/HarnessToolEvidenceStoreTests.cs` capture/order/isolation | V1 store is harness-only, in-memory. **Adapt** as durable typed run evidence, never import its lifetime assumption. | RuntimeTests restart/replay, ordered unique events, exact content |
| Permission vocabulary | `src/Thaddeus.Runtime/Tools/ToolPermissionGate.cs`; `tests/runtime/RuntimePermissionGateAdapterTests.cs` | V1 supports broader session/Always grants. **Adapt** Off/Ask only for writes; bind one action plus source/target versions, expiry and run. | Tamper, denial, policy-off, stale source/target, eight concurrent approvals |
| Markdown with revision storage | `packages/wiki/SirThaddeus.Wiki/Storage/LocalWikiStore.cs`; `tests/SirThaddeus.Tests/Agent/SandboxedFileSystemTests.cs` and file-policy tests identify boundary coverage | Large store couples registry/roots/search. **Adapt** a small store; do not claim these tests prove every symlink boundary. New filesystem/DB commit gap explicitly retained as a limitation. | Revision conflict, traversal/ADS names, linked directory, unknown write outcome |
| Bounded repair | `packages/agent/SirThaddeus.Agent/Validation/CompletionValidator.cs`; `tests/SirThaddeus.Tests/Agent/Validation/RepairLoopTests.cs` | **Adapt/retest** bounded deterministic draft checks. No always-on critic. | Repair count/call reservation; repeat-proposal stop; ablation negative |
| Validator exception treated as pass | Same CompletionValidator catch block | **Reject**. A judge/transport failure cannot establish verification. | Provider failure never succeeds; factual criteria remain unverified |
| Verified terminal file effects | `docs/research/CURRENT_EVIDENCE.md` reports PR #329 matched experiments; `docs/ASSISTANT_PIPELINE.md` describes receipt projection | Reported v1 evidence, not fresh v2 proof. **Adapt** exact read-back/hash receipt; no transfer claim. | EndToEnd exact content; not a factual-accuracy score |
| Model boundary | `docs/ASSISTANT_PIPELINE.md` native tool-call authority and capability qualification; actual runtime separates model and permission stages | **Adapt** native typed function call, reject malformed args; **defer** compatibility grammars/certificates. | Fragmented SSE/usage, malformed args, unsafe endpoints; Luna smoke |
| Scientific method | `docs/RESEARCH_METHOD.md`, `docs/research/README.md`, `docs/CALIBRATED_IMPROVEMENT_PLAN.md` | **Reuse principles**: fixed controls, negatives, unknown usage, no model-swap uplift claim. **Defer** full registered campaigns and sealed suites. | 16-case scripted Lab, same runtime/security, preserved false-success control |
| Broad retrieval, self-consistency, swarm/critic defaults | AGENTS and current research summaries warn about negative/inconclusive evidence | **Defer/reject as defaults**. Not required for this bounded product slice. | No hidden auxiliary model calls |
| V1 bearer-query/UI token transport | `docs/ARCHITECTURE_PUBLIC.md` describes query fallback | **Reject for v2**. Same-origin HttpOnly sessions and CSRF; no bearer query strings. | Browser unauthenticated/origin/CSRF checks, session revocation |

Provenance permalink prefix:
https://github.com/raydeStar/sir-thaddeus/tree/d0cac1e1d67e5ffa94543747825992f0a0cc8a95/

## OpenClaw transition additions, 2026-09-12

Read `packages/personality-engine/SirThaddeus.PersonalityEngine/Profiles/PersonalityProfile.cs`
and `packages/memory/SirThaddeus.Memory/Models.cs` read-only. Adapted the declarative
persona, directness, uncertainty and never-override-permissions mechanism into
Core `PersonalityProfile`; no v1 code or personalized profile data was copied.
`ExecutionContextBuilder` freezes explicitly selected source paths and hashes,
counts reads, preserves corrected notes as new versions, and keeps baseline
personality constant. Context tests cover excluded sources, source changes,
profile drift and failure receipts. This is scoped document context, not a
finished persistent-memory system. Native OpenClaw delivery remains unverified.

Subsequent checkpoints replace that initial evidence limit: [source-linked memory](SOURCE_LINKED_MEMORY.md)
has explicit correction/forgetting and native scoped delivery evidence. Version-3
research adds [native quotation checks and bounded repair](NATIVE_EVIDENCE_REPAIR.md).
It adapts the deterministic feedback/retry mechanism, retains failed drafts, and
stops on validator exceptions. It does not copy v1's always-on judge or fail-open
exception handling. Protocol fixtures are not transferred model-quality claims.
