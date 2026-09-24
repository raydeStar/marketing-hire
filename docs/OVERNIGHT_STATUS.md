# Overnight first-customer journey — working checkpoint

Started from clean `business/marketing-hire` at `699d888` after reading `NEXT_SPRINT_HANDOFF.md` and the overnight assignment. This is a live implementation checkpoint, not an acceptance report.

| Area | Current evidence | Status at start |
| --- | --- | --- |
| Local runtime | `marketing-business-hire` running image `sha256:6c4a3462a6f87f6f7dfaca3921aaba9629cb8c9ae47f3bdf8c07844fdfdb4e18`, OpenClaw `2026.9.4 (3a9d69d)`; host listening on 5189; `openai/gpt-5.6-luna` via usable Codex OAuth runtime; fallback list empty | Working |
| Durable work | Existing `hire.sqlite` runway has three saved artifacts and is quietly `needs_review`, with no active execution or reservation. Three-step ledger, host pump, versioned claims, and Work panel exist. | Working for one fixed pilot; broader journey unverified |
| Frontend journey | Chat/Work and editable marketing profile exist. Brief is tucked under Manage; the assignment form has a fixed pilot goal; completed project has no linked revision/approval action. | Missing connected brief → proposal → review/revision flow |
| Multiplayer | Native OpenClaw multi-user capability is installed. Branded Chat uses a generic Gateway CLI principal. No two distinct humans in one native shared conversation. | Partial, unverified acceptance |
| Plow | Local Docker runtime only. No authenticated hosted Gateway contract or hosted persistence/lifecycle receipt. | Integration dependency remains |
| Overnight live allowance | New overnight pilot has not been started. Previous sprint's 3 model runs and 8,618 reported tokens are historical evidence, not a new overnight validation. | Not run |

## September 24 checkpoint

- The business brief is visible and editable in Work. Its saved offer, audience hypothesis, and immediate goal seed the next proposed assignment; the owner sees the exact grant, deliverables, allowed work, and current admission limits.
- Saved artifacts now render source links and exact digests. Owner review is idempotent and tied to one artifact version. A synthetic owner revision creates a linked fourth step only when remaining grant capacity and deadline allow it; approval records an internal decision without publishing.
- The existing `needs_review` project was not replayed. A read-only authenticated host check returned project `91c4b1df1e6942e2a986936127b37742`, 4 claims, 3 artifacts, 0 reviews, 0 reserved tokens, and `runwayLiveEnabled=false`. The real Work page rendered this state and the pause notice.
- Native OpenClaw human participation remains **NOT RUN**. Installed 2026.9.4 documents identify token-only connections as one shared owner; verified profiles require identity-bearing ingress. The current Gateway has a shared token, no named roles, and a full-tool `main` agent. Exposing it to a collaborator would violate the requested authority boundary. No native shared input path was enabled.
- Further live inference is **BLOCKED**. The current host meters top-level turns and reported aggregate usage after completion, while installed OpenClaw documentation says the Codex runtime owns native stream/network retries. An independently enforceable 20-underlying-request/250,000-total-token ceiling was not found. The host now rejects new live assignments and revisions and its pump does not claim work unless `THADDEUS_RUNWAY_LIVE_VALIDATION=1` is deliberately set after that gap is resolved.
- Focused verification: 12 Python runway tests, 2 C# runway tests, production frontend build, and 1 fixture browser journey passed. The fixture is not native multiplayer. `artifacts/overnight-live-readonly.png` is a read-only screenshot from the actual saved project.

The remaining verdicts and exact manual steps are in [OVERNIGHT_HANDOFF.md](OVERNIGHT_HANDOFF.md). The worker is quiet; no Plow, publishing, outreach, reporting, or spend occurred. The previously blocked disposable-directory deletion was left alone.
