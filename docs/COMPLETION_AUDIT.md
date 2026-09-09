# Remaining-work completion audit

Goal: complete the development backlog agreed in this task, preserving the original
private/local-first scope and all security and evidence boundaries. A green narrow
test does not close a broader requirement.

| Requirement | Completion evidence required | Current status |
|---|---|---|
| Live conversation, persisted context, cancellation, streaming, goal creation | Runtime/provider tests, Luna High conversation and browser flow | Runtime/provider tests and two-turn Luna High smoke passed; browser goal flow passed |
| Aggregate token admission, actual/unknown usage, capability diagnostics | Predispatch rejection and bounded transport tests; exact provider diagnostic | Predispatch/unknown-usage tests passed; uncertified Luna bounds are exposed and strict mode rejects |
| Interrupted-write reconciliation and transactional content recovery | Crash-injection and reconciliation tests plus browser receipts | Crash-injection and reconciliation tests passed; browser verified an actual injected-crash ledger and one revision |
| Phone TLS/pairing/install/revoke/reconnect | Automated HTTPS tests and actual phone evidence | Software ready: real TLS and proxy tests passed, Tailscale guide/launcher prepared; physical phone and account setup remain manual at user request |
| UI modules, direct-edit activity, approvals, source navigation, raven | Responsive browser evidence and state/motion tests | Conversation, budget, raven, receipt and reconciliation components extracted; six browser flows passed |
| Controlled live evaluation with repeats and disjoint cases | Frozen registration, Luna High paired receipts, honest verdict | Complete: twelve frozen runs, repeated development pairs and disjoint validation, retained rejection; INCONCLUSIVE |
| Delivery | Full checks, private remote exact revision, running development host | 65 backend checks, six browser checks, live host/provider/export diagnostics passed; pushed privately, with remote CI tracked by commit checks |

The original brief explicitly defers production scheduling, swarms, native apps,
service installation and broad plugin infrastructure. Backlog item 7 asks to
consider these after the current milestones, not to contradict those boundaries.
They remain architectural follow-ups; the numbered requirements above are not
reduced to fit the current implementation.
