# Progress handoff for Astra: “My first employee”

> **Update, September 23, 2026:** This is the pre-Sprint 02 product baseline. For current implementation status, verification, limits, and next actions, read `SPRINT_02_HANDOFF.md` first. Its live acceptance remains NOT RUN.

**Prepared:** September 23, 2026  
**Product checkpoint:** `597fefedbfe01bd06ed4925ee2ff9cd9be09022f`  
**Purpose:** Review the current product, identify the highest-value next steps, and return a focused implementation brief for Codex. This document is context for review; it does not authorize deployment, purchases, outreach, or further autonomous runs.

## 1. Executive summary

We have a running local business workspace built from Thaddeus 2.0 and connected to a real OpenClaw marketing employee. It has a spacious Chat view, a resizable company sidebar, and a Work view containing a Kanban board, saved records, activity logs, approvals, and task details.

The basic live employee integration has been exercised: chat, agent-created tasks, conversational priority changes, persistent records, refresh/restart recovery, and a bounded research pilot. A newer CEO–Marketing meeting workflow is implemented and covered by backend tests and browser fixtures, but **a complete live meeting → ratified plan → assigned work cycle has not yet been demonstrated**. There are currently zero saved live meetings.

The business direction is the owner's **personal brand selling configurable agents that do marketing**. The first agent is the owner's own marketing employee. The intended product promise is **“my first employee.”** A final brand name and first buyer segment have not been chosen. Additional departments are a future expansion, not functioning teams today.

The latest pass focused on usability and the owner's Muse/ChatGPT-inspired visual direction. We should now evaluate the actual first-employee experience and its execution controls before adding more departments or hosting.

## 2. What the owner has asked for

### Product and positioning

- Stick with **“my first employee”** as the product's central promise.
- The internal employee markets the owner's personal brand and the agents that brand sells.
- Do not confuse the employee's identity, the software product, and the thing being marketed.
- Make the employee configurable; grow into other departments later.
- Keep work local for now. Plow is explicitly deferred.

### Everyday interface

- Only two main modes: **Chat** and **Work**. Remove Study from the business experience.
- Chat should look and behave like a real conversation, with substantial usable space.
- Borrow Muse's broad chat canvas and useful right panel, retaining the recognizable Thaddeus identity.
- Use neutral grays/charcoal, readable sans-serif text, restrained status colors, and a professional business tone.
- Make it easy to find agent records, switch agents/departments, and see the organization as a whole.
- The panel needs **drag resizing** and an obvious **collapse/reopen control in the top menu**.
- Work should contain necessary operational detail: Kanban, logs, approvals, and deeper task/record inspection.
- Adding someone to a conversation and setting up a meeting should be intuitive.

### Meetings and authority

- Start a meeting and set its agenda.
- CEO asks pointed questions and clarifies what is needed.
- Marketing proposes a plan with concrete actions.
- CEO challenges the plan, compares it with company ethos, and accepts it or requests changes.
- Close the meeting and let Marketing perform the agreed work.
- The owner does **not** want to approve every routine internal step.
- Spending and substantial resource use should be gated. The owner retains veto power.
- If the owner is away, preserve notes and provide a path to the board and controls afterward.
- Proactive engagement is part of the intended experience. It is not yet a general autonomous operating loop.

The owner tentatively accepted the initial meeting policy. Its thresholds should be treated as a first implementation to review, not settled business policy.

## 3. Built today

| Area | Implemented behavior | Important limit |
| --- | --- | --- |
| Chat | Real Marketing conversation, saved history, persistent unsent drafts, task-specific discussions | No claim of voice or arbitrary agent group chat |
| Company panel | At a glance, Members, department conversations, meeting agenda, plan, CEO review, resource controls, veto | Additional directory members need runtime connections |
| Panel controls | Drag left divider; saved width; keyboard sizing; top-right collapse/reopen; mobile overlay | Reopen means restoring the panel within the app, not a separate browser window |
| Work board | Needs decision, In progress, Assigned; separate expandable Paused and Completed sections; task search | No drag-and-drop card movement; use task details to change status/priority |
| Records | Search/filter saved replies, sources, drafts, decisions, and completed work; provenance, preview, Markdown download, related task | Bounded snapshot plus paged reply history; not a universal document repository |
| Activity | Latest 100 persisted hire events, newest first, searchable, expandable receipt data, task links | Recorded action does not necessarily mean successful outcome; no general system-log console |
| Approvals | Exact draft review; resource-gated meeting plans link to their full review controls | Approval records a decision; it does not publish a draft or make a purchase |
| Organization | Persistent department/agent directory and scope selector | One executing Marketing employee; extra departments are not provisioned runtimes |
| Brief | Owner-editable, versioned product, audience, goals, voice, channels, guardrails | New turns receive updated context; old conversations are not rewritten |
| Meeting setup | Add to chat / Start meeting opens participants, title, agenda, and company ethos | Currently CEO + Marketing + owner; no arbitrary participant selection |
| Meeting lifecycle | Discuss → Propose → Review → Assign; saved transcript, revisions, review, veto, assignment associations | Tested with controlled runtimes/fixtures; live end-to-end cycle still unverified |

**Navigation:** Team and Brief & ethos are under **Work → Manage**. Meeting notes are reachable from Work and the panel's conversations view. Add to chat currently creates a separate shared meeting; it does not copy the direct conversation into that meeting.

## 4. Architecture and persistence

### Ownership and runtime

- Independent local product checkout derived from Thaddeus 2.0; original source checkouts were preserved.
- Stack: ASP.NET Core/.NET 10, React/TypeScript, SQLite, OpenClaw in Docker.
- Product checkout: `C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit`.
- Branch: `business/marketing-hire`. This product checkout has **no Git remote** and has not been pushed or deployed.
- Original supplied repository was `https://github.com/raydeStar/marketing-hire`; the runnable derived product is the separate local checkout above.
- Local application: `http://localhost:5189/`. This address works on the owner's machine; Astra in ChatGPT cannot inspect it through that URL.
- Container: `marketing-business-hire`; persistent Docker volume: `dev_state`; gateway bound to loopback port `18795`.
- Existing subscription/OAuth route: `openai/gpt-5.6-luna`, configured with no fallback models. Earlier live checks verified that route. No credentials belong in the handoff.

### One task authority

The agent and app use the same `hire` CLI and persisted task ledger. The browser calls authenticated host APIs; it does not write SQLite or talk to the gateway directly. Task mutations use request IDs for replay safety and versions for stale-write rejection.

Task state includes status, priority, next action, action state, blocker, conversation association, version, and update time. Current statuses are `ready`, `working`, `needs_you`, `paused`, and `done`. Board and sidebar derive their information from these records.

The host persists chat requests/replies and owner decision receipts under the private `.data` directory. Organization and meetings are persisted in the host store. Agent tasks, sources, and model sessions remain on the existing Docker state volume. The sidebar width and unsent UI drafts are local browser preferences, not business-record authorities.

Main chat and each task have separate durable session associations. Switching views, reading records, resizing the panel, and changing task fields do not invoke a model. Starting a meeting, sending a message, asking for a plan/review, and dispatching agreed work do.

### Roles

1. **Marketing employee:** executes bounded tasks through the existing agent tools and shared ledger.
2. **Marketing planner:** proposes actions during meetings in a separate durable session.
3. **CEO:** chairs and reviews in a separate durable session.

The planner and CEO are configured with all tools denied. They produce plans and assessments; the host interprets the structured results and controls assignment release. They are not a demonstrated multi-department executive organization.

## 5. Meeting policy: what is enforced and what needs scrutiny

### Current flow

1. Save title, agenda, participant list, and a snapshot of company ethos.
2. Ask the CEO for questions. Owner messages are saved and receive a CEO response.
3. Explicit **Develop plan & review** asks Marketing for a structured proposal, then the CEO for a review of that revision.
4. Open CEO questions force revision rather than acceptance. Discussion changes clear previous approval.
5. If both planner and CEO classify an accepted plan as routine, it enters a **two-minute veto window**. Otherwise it waits for owner approval.
6. Closing creates shared hire tasks with stable request IDs and dispatches one bounded turn per task, sequentially.
7. Pending dispatch checks that the task remains Ready with an agent-ready action. Veto stops pending work; the board can also pause pending assignments.

### First-pass routine-work rule

The prompts define routine as at most three small internal research/drafting actions using the existing subscription and about ten minutes of estimated total work. Spending, new services, substantial compute, bulk work, external actions, or uncertainty require owner review.

**Important implementation distinction:** the host enforces structure, revision binding, the action-count threshold, conservative missing approval flags, and both roles' approval classifications. Resource/cost assessment and the ten-minute estimate largely depend on model judgments and text. This is **not** a spend meter, hard compute budget, or independent policy engine. Execution turns have a 600-second deadline; that is not the same as enforcing ten minutes for the whole plan.

### Execution limits

- Only internal research/drafting is in scope; no posting, messaging, purchasing, or recurring schedule was enabled by this work.
- An already-active turn can finish after a veto. Veto is not instantaneous cancellation or rollback.
- Interrupted/unconfirmed work is marked unknown and remaining work pauses; it is not blindly retried.
- A successful model turn is not proof the task's intended outcome was achieved.
- The host must stay running for the queue to advance.
- Marking a task Ready is not a general scheduler; the implemented automatic dispatch path belongs to ratified meeting assignments.
- Inspect approval-actor wording before relying on autonomous meetings: the dispatch prompt currently says the owner ratified the plan even when routine approval can come from the CEO. Attribution should remain accurate.
- The executing Marketing agent has broader tool access than the planning roles. No production security audit or proof of complete outbound/spending isolation is claimed.

## 6. Current business/work snapshot

Fresh read at handoff:

| Item | Current state |
| --- | --- |
| Services | Local host listening on 5189; Marketing container running |
| Tasks | 6 total: 1 Needs decision, 3 Paused, 2 Done |
| Attached source records | 9 |
| Recent hire events | 55 currently available in the latest-100 view |
| Saved live meetings | 0 |
| Drafts | 2 approved **fictional acceptance-test drafts**, not publishable marketing copy |
| Active profile | Version 6; personal brand selling configurable marketing agents |

The current decision task is **“Position the personal brand and marketing-agent offer.”** It asks the owner to choose a buyer segment and approve a bounded local test.

Candidate segments in that task are hypotheses:

- Founder-led small businesses needing consistent marketing content.
- Small agencies needing repeatable client marketing operations.
- Solo marketers needing help with repeatable work.

The suggested experiment is a 14-day local trial of one human-reviewed workflow, logging time, outputs, edits, accept/reject decisions, quality problems, and owner value. **It has not been scheduled or started.** “My first employee” is a product promise, not evidence that any one segment will buy it.

The three older Framewright tasks are preserved as Paused and should only resume if the owner asks to market Framewright. Two prior research tasks are marked Done. That records the work completed; it does not establish market demand or sales readiness.

## 7. Evidence: separate the live checks from simulations

| Claim | Evidence and confidence boundary |
| --- | --- |
| Real employee chat and task writes | Earlier live app turns produced replies, created tasks, and changed priorities in the shared ledger. Historical integration evidence; not rerun for the UI redesign. |
| Persistence/reconnection | Earlier browser refresh, disconnection, and container restart checks preserved records. Later restart preserved six tasks and nine source records. |
| Bounded research workflow | A real agent read two supplied public pages, attached source records, produced a brief, and updated its task. The successful URLs were supplied leads, not demonstrated autonomous discovery. |
| Research limitations | Pulse scans had partial source coverage and weak or unusable candidates. One Reddit page challenged the agent. Source records and fetched counts are not demand validation. |
| Task/receipt behavior | Latest isolated Python CLI suite: **5 passed**. Includes versioning/replay, profile/evidence behavior, draft digest binding, and persisted pause plus audit receipt. |
| Meeting state machine | Prior targeted .NET result: **9 passed** for directory/meeting persistence and control behavior, using controlled runtimes. Preserved test receipt; not a live model evaluation. |
| Latest browser behavior | **2 Playwright scenarios passed**, using route fixtures. Cover records, task controls, approvals, meeting setup/review/veto, archive navigation, pointer/keyboard resize, reload persistence, unsent drafts, and phone layout. |
| Build | Latest frontend production build and host Release build passed; host build reported zero warnings/errors. |
| Current live UI | Visually checked against the running app: real board, one current decision, three paused tasks, and real activity history. |
| Complete autonomous meeting | **Not yet proven live.** No live meeting exists in the current store. |
| Market, ROI, distribution, production readiness | **Not established.** No outreach, launch, cloud deployment, or multi-user hosted acceptance has occurred. |

No model calls were made for the latest UI regression checks. Earlier live integration/pilot work used the configured subscription route; total account usage and charges were not established by these receipts.

## 8. What Astra should review next

Please return a prioritized recommendation, rather than assuming the next step is another UI redesign or more departments.

### A. First-employee experience

- Does the current Chat / Work split support an owner giving direction, returning later, and understanding what happened?
- Is Add to chat creating a fresh CEO–Marketing meeting the right mental model, or should there be a clearer distinction between inviting participants and starting a decision meeting?
- Which missing interaction would materially improve this first version: meeting recap, explicit unresolved-question queue, plan editing, an agenda inbox, or a clearer assigned-work handoff?
- What should proactive engagement mean in a bounded first release? No recurring meeting scheduler or general proactive trigger system exists yet.

### B. Authority and recovery

- Which resource constraints must be represented as structured, host-enforced fields before permitting unattended execution?
- Is a two-minute veto window useful when the owner is away? Distinguish advance consent, later veto, cancellation of active work, and rollback.
- How should CEO approval versus owner approval be represented consistently in transcripts, receipts, dispatch prompts, and the board?
- Define an honest failure/recovery policy and outcome acceptance criteria; avoid using “turn completed” as “employee achieved the result.”

### C. Positioning and pilot

- Preserve “my first employee” while identifying one narrow first buyer and one job worth delegating.
- Recommend the smallest experiment that tests owner value without public outreach or speculative infrastructure.
- Separate technical reliability, output usefulness, time saved after corrections, and willingness to pay. We have not proven all four.

### Suggested order for consideration, not an approved new work plan

1. Review approval attribution and resource-gate limitations; identify any must-fix controls.
2. Design one explicitly authorized, bounded live CEO–Marketing meeting acceptance run.
3. Verify the full cycle: agenda → clarification → revised/accepted plan → required owner gate or routine release → real task/evidence → saved recap → board control.
4. Choose the initial buyer/workflow hypothesis and define the local pilot's success/failure criteria.
5. Expand proactive behavior or departments only after that loop is useful and reliable.

### Requested return format

1. **Assessment:** what is solid, what is misleading or missing, and confidence based on this evidence.
2. **Top 3 next steps:** ordered by dependency, with concrete outcomes and acceptance checks.
3. **Owner decisions:** only the choices that implementation cannot responsibly infer.
4. **Codex implementation brief:** specific scope, affected areas, required validation, and explicit exclusions.

Please label assumptions. ChatGPT cannot directly inspect this local app or private checkout from the paths in this document. Ask for specific screenshots/files if necessary rather than assuming access or inventing observed behavior.

## 9. Constraints for the next implementation pass

- Keep the current product checkout as the implementation home; preserve original Thaddeus and marketing-hire projects.
- Preserve `.data`, `dev_state`, OAuth configuration, task history, and existing source records.
- Do not deploy to Plow, create hosting, push publicly, post, DM, email, purchase, or enable a paid fallback based on this handoff.
- Prefer the existing runtime and task authority. Avoid introducing a second authoritative work list or an unnecessary orchestration framework.
- Board reads, navigation, and manual record edits must remain free of model calls.
- Do not call a model merely to test cosmetic changes. A real meeting test needs a bounded purpose and authorization.
- Use narrow tests and preserve active services. Do not stop Docker/WSL or prune volumes as incidental cleanup.
- Existing disposable test files were retained after automatic approval review rejected a cleanup command. The test host was stopped; the rejection was documented and not bypassed.

## 10. Technical pointers for Codex after review

These paths are relative to the product checkout. They are navigation aids for the implementation agent, not dependencies required to understand this document.

| File | Responsibility |
| --- | --- |
| `web/src/components/BusinessWorkspace.tsx` | Chat/Work shell, meetings, sidebar, navigation |
| `web/src/components/PanelResize.tsx` | Pointer/keyboard panel sizing and saved-width helpers |
| `web/src/components/CompanyWorkspace.tsx` | Work scope, records, approvals, task detail, team/brief management |
| `web/src/components/WorkBoard.tsx` | Kanban and paused/completed sections |
| `web/src/components/WorkActivity.tsx` | Saved hire activity view |
| `web/src/components/MarketingPanels.tsx` | Chat, brief, draft/evidence UI and shared types |
| `web/src/business-theme.css` | Current business appearance and responsive layout |
| `src/Thaddeus.Host/CompanyMeetings.cs` | Meeting state, review/approval policy, release, dispatch, recovery |
| `src/Thaddeus.Host/MarketingBackend.cs` | Runtime bridge, shared snapshots, chat and owner receipts |
| `src/Thaddeus.Host/MarketingEndpoints.cs` | Marketing and meeting routes |
| `business/agent/hire/bin/hire.py` | Durable tasks, profile, evidence, draft operations and feed |
| `business/agent/boot-dev.mjs` | Lasting model/role/runtime setup applied at boot |
| `docs/COMPANY_WORKSPACE.md` | Current workflow and verification notes |

Evidence files retained locally include `artifacts/browser-results.json`, `artifacts/company-checks/company-workspace.trx`, `artifacts/company-checks/cleanup.txt`, and UI screenshots. Good optional screenshot attachments are `artifacts/business-chat-desktop.png`, `artifacts/business-work-board.png`, and `artifacts/company-meeting-desktop.png`. **Those named screenshots use fictional test data.**

Normal local launch, only when a host is not already running:

```powershell
Set-Location 'C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit'
.\scripts\start-marketing.ps1
```

The script builds and starts the local services using existing private configuration. It is not a deployment command. Do not include the host access key or OAuth material in a ChatGPT review attachment.

**Document precedence:** This is the current consolidated progress handoff. `SPRINT_HANDOFF.md`, `LOCAL_CONTINUATION_HANDOFF.md`, and inherited Thaddeus README sections describe earlier checkpoints. Their old Study navigation, counts, styling, or “not implemented” statements should not override the current state above.
