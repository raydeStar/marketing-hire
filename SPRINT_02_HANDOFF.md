# Astra handoff: “My first employee” meeting to result

**Snapshot:** September 23, 2026, 23:55 UTC  
**Local repo:** `C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit`  
**Branch:** `business/marketing-hire`  
**Implementation revision:** `642c2ec0378711063d639508323eca83d45a83d6` (after `39b83d7` and `34b3a64`; baseline `597fefe`)  
**Status:** Local implementation and controlled checks pass. The real meeting-to-result acceptance run has **not run**.

## Read this first

The owner's product promise is **“my first employee.”** The first employee markets the owner's personal brand, which sells configurable marketing agents. The brand name is undecided. The working audience for this one pilot is independent technical founders selling B2B software; that hypothesis has not been accepted as permanent positioning. The owner's current marketing brief remains version 6.

The app runs locally at `http://localhost:5189/`. It has Chat and Work, a wide conversation area, a resizable and collapsible company panel, saved records, a Kanban board, approvals, and a CEO–Marketing meeting room. Extra departments are represented in the organization model, but they do not have functioning agent runtimes. Plow, public hosting, outreach, publication, purchases, and recurring autonomous work are deferred.

**The key distinction:** A CEO recommendation is recorded advice. It cannot grant execution. The owner must approve the exact plan revision, digest, and two source URLs before this restricted job can assign tasks. No owner grant or live meeting was created during Sprint 02.

## What changed in Sprint 02

1. **Authority and bounds.** Removed release after two minutes of owner silence. A grant records the owner actor and authority source, plan revision/digest, exact source URLs, allowed action types and capabilities, local artifact destination, model route, expiry, execution deadline, assignment and dispatch limits, and task/dispatch receipts. Stale or legacy plans fail closed. Veto revokes the grant, stops pending dispatch, and requests cancellation of active work while retaining honest unknown outcomes. Assigned task instructions and worker briefs now carry the same owner attribution; plan outcomes are bounded to fit the hire board's 1,000-character instruction field.
2. **Restricted work path.** CEO and planner have no tools. The new meeting worker has OpenClaw `tools.deny: ["*"]`. Host code can fetch only the two approved existing Hacker News item URLs, with HTTPS, redirect, public-IP, and response-size checks; validate source excerpts; save an evidence brief and local draft; and update only their linked hire tasks. One worker turn runs per task. Direct Marketing chat still uses its existing broader integration, so this is a restriction on the meeting path, not a global security claim.
3. **Result and return view.** The meeting recap is derived from stored plan, artifacts, task states, and errors without another model call. It separates CEO recommendation, owner approval, produced output, owner acceptance, active/blocked tasks, and the next decision. The owner can open linked detail and veto pending work. Work approvals shows recommended plans. “Start meeting” clearly creates a separate transcript and now discloses the model route and possible model use before the first CEO turn.
4. **Draft acceptance recovery.** Accepting a saved local draft records the owner decision and uses a stable receipt to mark its linked hire task `done`. If the board update is interrupted, the host reconciles the saved acceptance on its next work pass. Nothing is published.

The operational description is in `docs/COMPANY_WORKSPACE.md`. The older `ASTRA_PROGRESS_HANDOFF.md` describes the pre-sprint product baseline; this document supersedes its implementation status.

## Evidence at handoff

| Category | Result | Evidence and limit |
| --- | --- | --- |
| Backend controlled runtime | **PASS** | 18 focused `CompanyMeetingTests` and `MeetingExecutionTests` pass, including silence, prohibited actions, stale/replayed grants, deadline, veto, artifact absence, unknown recovery, accepted-draft board reconciliation, owner attribution, and hire board size bounds. Scripted runtimes, no model. |
| Frontend build | **PASS** | `npm run build` passes; current static bundle served by the app. |
| Browser fixture | **PASS** | `web/tests/company-meetings.spec.ts`: meeting entry, exact source grant UI, recap, owner draft acceptance and board state, reload, and phone width. Fixture IDs `meeting-1`, `grant-1`, `brief-1`, and `draft-1` are **fictional**. Screenshots: `artifacts/company-meeting-desktop-sprint02.png` and `artifacts/company-meeting-mobile-sprint02.png`; they show fixture data. |
| Local service | **PASS** | Host returned HTTP 200 after targeted restart; OpenClaw container `marketing-business-hire` was up and gateway health returned OK. No duplicate host was launched. |
| Restricted public reader | **PASS, narrow** | Host reader fetched the two selected URLs with HTTP 200 and readable text. This proves reachability at check time, not claim quality or model grounding in a real run. |
| Runtime tool denial | **PASS for inspected configuration** | Effective meeting-worker configuration has wildcard tool denial; local OpenClaw policy code shows deny wildcard takes precedence. No live worker tool-call attempt was made. |
| Real CEO/Marketing/worker model cycle | **NOT RUN** | Zero saved live meetings and zero Sprint 02 task/result IDs. Owner has not started or granted the exact live run. |
| Owner usability check | **NOT RUN** | No owner tryout without a walkthrough. Fixture navigation does not establish usability. |
| Provider usage or charges | **UNKNOWN** | No model call was initiated in Sprint 02 checks. No provider bill/usage record was queried; do not infer a dollar amount. |

The two existing genuine source records available for a future bounded grant are:

- `https://news.ycombinator.com/item?id=47667504` — source record `c0bc6dcb818d40f4bcc3a93cbbd95a8e`
- `https://news.ycombinator.com/item?id=49703771` — source record `b4784de66a3b4ab394e1be03900a4d30`

Do not treat either as support for a marketing assertion until its retrieved content and context are reviewed. Two old fictional acceptance-test drafts also remain in the hire history and should be excluded from business evidence.

## Authority and execution limits

**Enforced in the meeting path:** two sequential types (`evidence_brief`, `local_draft`); two exact approved public source URLs; local meeting artifact destination; at most two task assignments and two worker dispatch attempts; one plan revision; at most eight top-level model turns across meeting and worker; a 15-minute grant expiry and 10-minute execution deadline; one 120-second worker-turn timeout; no fallback route; stable assignment/status receipts; grant revalidation before assignment and dispatch; no automatic retry of an uncertain worker outcome. New work is blocked after veto or invalid grant. Interrupted work is marked unknown and remaining work pauses.

**Not a hard cost or remote-stop guarantee:** One top-level turn can involve more than one provider request. Cancellation requests and host deadlines cannot prove that a provider stopped billing or that an already completed external effect was rolled back. The pilot has no measured token/dollar total. The no-tools OpenClaw config and restricted host wrapper are inspected and controlled-test evidence, not a live adversarial capability test.

**Actual attribution:** Existing historical data is preserved. There is no Sprint 02 live approver, grant, assignment, artifact, active job, or unknown job. At the last read, the local meeting ledger had 0 meetings; `marketing-chat.sqlite` had 7 saved requests, all `succeeded`. The hire history contained 6 tasks (2 done, 1 needs you, 3 paused) and 9 evidence records. No task was rewritten by these checks.

## Preservation and review

Before deploy, a consistent host backup was saved at `.data-backups/sprint02-before-20260923T2332Z/`; its `backup.json` SHA-256 is `7138ad2729754c599068ba8775be8220ca3c17cdbca817b17df4a9f0ea3be5ff`. A separate SQLite online hire backup is `.data-backups/hire-sprint02-20260923T2332Z.sqlite`, SHA-256 `5d330a6369343cf806873666805ba317a747f85a53928df81c470d38f3533809`. These are private and gitignored; do not share the host key or OAuth material. The `dev_state` Docker volume and original repos were preserved. No remote was added, no push was made, and no cleanup of persistent data occurred.

To review locally, open `http://localhost:5189/` through the existing owner launch/login flow. The host currently runs `src/Thaddeus.Host/bin/Release/net10.0/Thaddeus.Host.dll` with `Thaddeus__LocalOrigin=http://localhost:5189` and `Thaddeus__Data=<repo>\.data`; OpenClaw runs in `marketing-business-hire`. Do not paste `host-key.txt` into a chat. If the local host is no longer running, build `src/Thaddeus.Host/Thaddeus.Host.csproj` in Release and start that DLL with those two environment values. Review the source and controlled checks first; opening or reading the app does not invoke a model, while pressing **Start meeting** does.

## Next three actions, in dependency order

1. **Astra review:** inspect whether the restricted meeting path and recap credibly support one useful first-employee job. Return concrete defects or a narrowly scoped implementation brief. Keep fixture proof separate from live proof.
2. **Owner-granted live acceptance:** once the owner chooses to start a meeting, use the displayed audience assumption and the two source records to seek three grounded content angles and one local draft. The owner reviews the exact plan/sources and grants it in the UI. Capture real meeting, grant, task, source, artifact, and receipt IDs plus observed usage. Do not run this from the handoff alone.
3. **Owner usefulness test:** have the owner find the decision, evidence, draft, next action, and veto state without a walkthrough. Record wrong turns and whether the draft is worth keeping; fix only the concrete blockers before any three-session pilot or broader autonomy.

The result is a reviewable local implementation, **not** a demonstrated dependable live employee or production-ready service. The owner will bring Astra's next-step feedback back to Codex.
