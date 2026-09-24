# Linked revision grant contract

This is the implementation contract for turning a saved owner revision request into one bounded new assignment. The **held grant and release transitions** are implemented in the local ledger and owner-only host API, but both owner-facing APIs return 409 while metering is unavailable so an expiring grant cannot be consumed prematurely. A held record does not authorize a run by itself. The local host keeps new model admission closed until a route can enforce the pilot's request and token ceilings before each underlying request.

## Authority and identity

- Only a signed-in owner may issue a grant. The host supplies the owner principal from its verified session; a client-provided `actor_owner` flag is never authority.
- Every revision review saves a deferred instruction in the ledger; no direct review call can create a runnable revision step. A separate owner grant and release are required.
- A grant names one deferred `revision_requested` review, its exact source artifact ID and SHA-256 digest, the source project ID and version, an idempotency key, and the new assignment's explicit scope, maximum work, and expiry.
- The source review must still point to that artifact and have no released revision step. The source project must have no active or unknown execution. Any changed project version, artifact digest, review, or owner brief rejects the request and asks the owner to refresh.
- An identical idempotency key and payload returns the same grant. Reusing the key with different material rejects the request. A second grant for the same review is rejected even with a different key.

## Budget boundary

- Persist a separate pilot identity and a lineage edge from the new assignment to the source review and artifact. Count **all executions under one pilot root** toward its six admitted turns, 20 underlying model requests, 250,000 aggregate input and output tokens, and 30-minute live window. A new project row under that root does not reset those ceilings.
- The historical three successful turns have aggregate usage but no underlying request receipts. Do not infer that each used only one provider request. A release under that pilot root requires authoritative historical request accounting. If it cannot be recovered, the owner must explicitly authorize a **new pilot root and budget window** that links back to the saved artifact for context and audit; this is new authorization, not unused capacity in the old root.
- The legacy pilot has `deadline_at=NULL`. Its two unused run slots and unused reservation balance are historical counters, not authorization for another run. A fresh owner grant records an explicit new expiry and budget; it must also be checked against the pilot-wide ceilings. Extending the pilot-wide 30-minute window requires an explicit new owner authorization, not an implicit database migration.
- Reserve before each underlying model request and refuse it before dispatch if either remaining ceiling is insufficient. Charge uncertain or missing usage conservatively and hold the execution for reconciliation. A host crash or timeout cannot replay the same model request automatically.
- The ledger can store the proposed grant as `held_for_metering`, but does not create a claimable task or schedule a wake. The host declines new grants until a metered route is available. The stored deadline is at most 30 minutes from the explicit owner action. If it expires before release, it cannot be released; a separate audited renewal path would be needed. Recheck the exact grant and available budget when a metered route is configured; never activate by simply toggling a boolean in an old row.

## Revision work and result

- The new assignment has one `revision_angles` deliverable with the saved owner instruction and source artifact as read-only context. It carries forward only the checked source records needed for that draft. No publication, outreach, account changes, or money movement is in scope.
- The generated artifact must cite allowlisted sources, materially differ from the source digest, and satisfy the revision criterion. Save the new artifact with its predecessor link; an owner may inspect both versions and the exact model usage receipts.
- A fresh pilot grant receives its own pilot root, request count, token allowance, and expiry. Its predecessor links are persisted on the new project record; the old artifact and review do not change. A same-pilot grant rechecks every recorded execution and rejects historical turns without request-level receipts or an unresolved request.
- Direct Chat and collaborator notes may add attributed context, but cannot release, change, or resume this grant. A SQLite claim in the hire ledger serializes direct Chat and the autonomous step across host processes. The existing task and project version checks fence an autonomous result before it is saved.

### Shared execution claim

The host saves a pending Chat request, then takes a durable `chat-claim` before dispatching OpenClaw. The ledger admits it only when no runway execution or unresolved Chat claim exists. The worker's `claim` checks the same table under an immediate SQLite transaction. Request ID, actor, session, and content digest make retries idempotent and reject changed content. A confirmed reply is saved to the host Chat database before `chat-finish` releases the claim. On restart, pending claims become `unknown`; only a matching, already saved successful reply can be reconciled automatically. An unknown model outcome stays held and requires investigation. The ledger does not count direct Chat toward the autonomous pilot budget; Chat remains a separately initiated, unmetered path.

This protocol is staged in the newer host binary and only participates when the autonomous inference gate is enabled. That gate remains closed until the underlying model-request and token limits are enforceable. The older running host does not contain this protocol.

## Minimum negative controls

1. Reject forged non-owner identity, stale source project version, changed digest, wrong review, and a review already linked to another grant.
2. Return the same grant on an identical retry; reject a changed payload under the same idempotency key.
3. Reject a grant with no explicit expiry or an expiry outside the owner-authorized live window. Reject reuse of the legacy null deadline.
4. Keep `held_for_metering` non-claimable and idle with zero model calls. Exercise a pre-request refusal at the 20-request and 250,000-token boundaries.
5. Crash after reservation and verify an unknown receipt stays held. Change the task or owner brief during execution and verify a stale result cannot overwrite it.
6. Inspect the resulting previous assignment and its predecessor record through owner-only, read-only archive endpoints.

Current code locations: `business/agent/hire/bin/runway.py` owns the ledger, `prepare-revision-grant`, and `release-revision-grant`; `src/Thaddeus.Host/MarketingRunway.cs` owns host admission and identity; `web/src/components/MarketingRunwayPanel.tsx` renders held grant receipts. The deferred review path stores the exact instruction. The staged release makes one linked assignment in deterministic tests, with a fresh root available for the unmetered historical pilot. The real OpenClaw transport still does not call the request meter, and the older running host has not loaded these routes. Neither a grant nor a revision assignment was created in the real pilot. Do not display a saved review as a running revision.
