# Linked revision grant contract

This is the implementation contract for turning a saved owner revision request into one bounded new assignment. It does not authorize a run by itself. The local host keeps new model admission closed until a route can enforce the pilot's request and token ceilings before each underlying request.

## Authority and identity

- Only a signed-in owner may issue a grant. The host supplies the owner principal from its verified session; a client-provided `actor_owner` flag is never authority.
- A grant names one deferred `revision_requested` review, its exact source artifact ID and SHA-256 digest, the source project ID and version, an idempotency key, and the new assignment's explicit scope, maximum work, and expiry.
- The source review must still point to that artifact and have no released revision step. The source project must have no active or unknown execution. Any changed project version, artifact digest, review, or owner brief rejects the request and asks the owner to refresh.
- An identical idempotency key and payload returns the same grant. Reusing the key with different material rejects the request. A second grant for the same review is rejected even with a different key.

## Budget boundary

- Persist a separate pilot identity and a lineage edge from the new assignment to the source review and artifact. Count **all** lineage executions toward the pilot's six admitted turns, 20 underlying model requests, 250,000 aggregate input and output tokens, and 30-minute live window. A new project row does not reset those ceilings.
- The legacy pilot has `deadline_at=NULL`. Its two unused run slots and unused reservation balance are historical counters, not authorization for another run. A fresh owner grant records an explicit new expiry and budget; it must also be checked against the pilot-wide ceilings. Extending the pilot-wide 30-minute window requires an explicit new owner authorization, not an implicit database migration.
- Reserve before each underlying model request and refuse it before dispatch if either remaining ceiling is insufficient. Charge uncertain or missing usage conservatively and hold the execution for reconciliation. A host crash or timeout cannot replay the same model request automatically.
- An unmetered route may store the proposed grant as `held_for_metering`, but cannot create a claimable task or schedule a wake. Recheck the exact grant and available budget when a metered route is configured; never activate by simply toggling a boolean in an old row.

## Revision work and result

- The new assignment has one `revision_angles` deliverable with the saved owner instruction and source artifact as read-only context. It carries forward only the checked source records needed for that draft. No publication, outreach, account changes, or money movement is in scope.
- The generated artifact must cite allowlisted sources, materially differ from the source digest, and satisfy the revision criterion. Save the new artifact with its predecessor link; an owner may inspect both versions and the exact model usage receipts.
- Direct Chat and collaborator notes may add attributed context, but cannot release, change, or resume this grant. A shared host execution claim/version fence serializes Chat changes and the autonomous step.

## Minimum negative controls

1. Reject forged non-owner identity, stale source project version, changed digest, wrong review, and a review already linked to another grant.
2. Return the same grant on an identical retry; reject a changed payload under the same idempotency key.
3. Reject a grant with no explicit expiry or an expiry outside the owner-authorized live window. Reject reuse of the legacy null deadline.
4. Keep `held_for_metering` non-claimable and idle with zero model calls. Exercise a pre-request refusal at the 20-request and 250,000-token boundaries.
5. Crash after reservation and verify an unknown receipt stays held. Change the task or owner brief during execution and verify a stale result cannot overwrite it.
6. Inspect the resulting previous assignment and its predecessor record through owner-only, read-only archive endpoints.

Current code locations: `business/agent/hire/bin/runway.py` owns the ledger; `src/Thaddeus.Host/MarketingRunway.cs` owns host admission and identity; `web/src/components/MarketingRunwayPanel.tsx` owns the Work review surface. The existing deferred review path stores the exact instruction but has no grant release action. Do not display a saved review as a running revision.
