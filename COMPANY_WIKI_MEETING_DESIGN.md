# Company wiki and decision meetings — design contract

**Status:** The single-employee MVP runs Marketing through direct Chat and Work. New CEO meetings and their worker are paused; old pilot meetings remain audit records. A versioned business-wiki editor and role-scoped meeting snapshot code exist as future scaffolding, but **direct Marketing chat does not read the wiki** and no new meeting can run. Per-member personalities and context citations are not implemented. The older generic Thaddeus Knowledge pages remain separate and are not silently migrated.

## MVP decision

The owner is the CEO for now. Marketing should ask the owner targeted questions directly, use the approved Marketing brief and its saved work, propose actions on the Work board, and wait for owner approval when a resource or external action requires it. A simulated CEO would add model turns and another approval layer without adding real organizational perspective. The pilot's meeting record is retained for audit, not as an active workflow.

Before direct Marketing chat consumes wiki pages, it needs the same explicit context boundary described below: an owner-approved source set, bounded prompt packet, page revision references saved with each turn, and a way to say what is missing. Until that path is built and tested, publishing a wiki page records future context only; it does not change what Marketing knows in direct chat.

## When meetings may return

Restore meetings only when several connected roles have distinct knowledge or authority and can genuinely test a consequential plan: cross-department priorities, resource use, company ethos, or a change in direction. Routine execution belongs on the board. A meeting must improve the decision by making assumptions, evidence, disagreement, and authority visible. An agreeable transcript by itself is not success.

The live learning pilot illustrates both sides. CEO review forced Marketing to separate observations, hypotheses, and product claims. It also asked for task IDs before the grant that creates them. The next meeting flow must provide the roles with the actual company context and a clear process model, then preserve what each role saw for later audit.

## Wiki layers and access

| Layer | Reads | Writes and publication |
| --- | --- | --- |
| Company | Every connected member and the owner | Owner publishes; agents may propose changes for owner review |
| Department | Members assigned to that department and the owner | Owner publishes; department agents may propose changes |
| Member | That member and the owner | Owner publishes; that member may propose changes |

The CEO, as a company-level member with no department, reads company pages and its own member pages. Cross-department material reaches the CEO only when the owner shares it into the company layer or grants it explicitly for one meeting. Moving an agent to another department changes future access but does not rewrite old meeting records. The owner can view and audit every layer. A disconnected directory seat has no runtime wiki access.

## Page record

Each page needs a stable ID, layer and scope ID, title, Markdown body, author/provenance, status (`draft`, `active`, `archived`), version, content digest, and timestamps. Every edit creates an immutable revision. Only an **active, owner-published** revision can enter an agent's context. Drafts and archived pages remain owner-visible for audit, but cannot silently influence agents. Agent suggestions are proposals, not page edits.

Pages should make their epistemic status visible: verified company fact, current policy, working hypothesis, or open question. A source citation is separate from the page's author. Contradictory active pages must be flagged in context rather than resolved by guessing. Product capabilities and customer outcomes should not be promoted from drafts or meeting speculation into company facts without owner review.

Suggested initial pages are company mission and ethos, product truth and claim limits, audience hypotheses, a Marketing playbook, and a CEO decision rubric. Their contents should be written or ratified by the owner; the system should not fabricate them from chat history.

## Member identity and permissions

Each member has a stable ID, name, department, responsibility, runtime connection, and optional owner-selected personality. A personality changes voice and manner only. It cannot alter wiki access, tools, spend limits, or approval authority. The meeting captures the member's role and personality **version** as they existed when the meeting began. Existing meetings did not capture this snapshot; the audit UI labels that gap rather than inventing it.

## Meeting context packet

Before a member speaks, the host resolves active pages under that member's access rules. It records the exact page IDs, versions, and digests in a meeting context snapshot, plus the role/personality version. The agent receives only its own authorized packet, with page text marked as context data, not commands. The CEO must not receive Marketing's private wiki by virtue of sharing a meeting. A page changed during the meeting stays pinned for that meeting unless the owner explicitly refreshes context and the transcript records the refresh.

Context selection should search only authorized pages, prioritize agenda-relevant pages, and have a bounded size. The response should cite page titles and versions for material claims, identify missing context, and ask pointed questions when a fact is absent. Model replies should distinguish wiki facts, public source observations, hypotheses, and recommendations. The meeting audit must show which context snapshot informed each turn, without exposing a private page to a participant who could not read it.

## Meeting lifecycle and audit

1. **Open:** Owner sets agenda, desired decision, attendees, and resource scope. The system freezes each attendee's identity and wiki context packet.
2. **Discuss:** CEO frames the decision and asks pointed questions. Marketing and any other connected attendee respond from their assigned responsibilities and permitted context. Bounded turns allow genuine challenge and response before a plan is drafted; no model turn is authority to act.
3. **Propose and review:** Marketing proposes concrete actions, evidence needs, and resource estimate. CEO compares the proposal to company ethos and facts, records objections, and recommends accept or revise. Unresolved questions stay visible.
4. **Decide:** The owner can grant an exact plan and source scope, request changes, or veto. Spending and larger resource use require their own gates. Silence does not approve.
5. **Execute and return:** Scoped tasks run separately. The meeting shows task IDs, status, artifacts, failures, and unknown outcomes. The final recap distinguishes CEO recommendation, owner decision, work performed, results, and next decision. A finished meeting is still searchable and exportable.

The audit timeline should be append-only for speaker turns, context refreshes, plan revisions, reviews, grants, vetoes, task dispatches, and artifacts. Corrections should add new entries rather than replacing the original. Existing meeting records have timestamped messages and artifacts but do not yet record wiki context or a per-turn model/session receipt.

## Delivery sequence and acceptance

1. Keep the single-employee path simple: direct Marketing chat, its board, saved records, and owner approvals. Test that no new meeting or CEO turn can start and that old pilot records remain auditable.
2. Verify the versioned company wiki ledger and owner editor in **Work → Wiki**. Negative access cases, revision history, publish/archive behavior, and no implicit migration from generic Knowledge are required. Publishing currently stores a revision for future use only.
3. Build and audit a direct-Marketing context packet from owner-approved brief and wiki pages. Save exact source revisions with each turn and require pointed missing-context questions. Do not claim this integration before the model prompt and saved receipts prove it.
4. When several departments are connected, add role and personality profiles, bounded attendee discussion, per-role wiki snapshots, and cross-role challenge. Verify privacy, citations, decisions, grants, results, and restart-safe audit. Reenable meeting endpoints and worker only after those gates pass.

The archived pilot transcript is a **planning and decision record**. It did not have business-wiki context. The future snapshot code must not be used as evidence that the earlier CEO or Marketing consulted a wiki.
