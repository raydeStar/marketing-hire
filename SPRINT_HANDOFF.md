# Marketing Hire cockpit: local sprint handoff

**Outcome: live local integration working.** Started 2026-09-23 18:07 UTC with a 21:07 UTC stop bound; required local checks passed before the bound. Chat and Work use the real OpenClaw `main` agent and one durable `hire` task ledger. The three Framewright tasks and conversations below are live local records, not fixtures. No research result, outbound post, hosted deployment, or two-person session was claimed.

## Checkout and launch

- Product: `C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit`, branch `business/marketing-hire`; the local checkpoint revision is this branch's HEAD (`git rev-parse HEAD`). It derives from Thaddeus 2.0 `7b3dda5d12a8abc842c3920a8e5038d7365a9768`. Copied marketing source is from `raydeStar/marketing-hire` `76afaafd0a36657f605b42ab8a854e52446e941e`, with its `hire/LICENSE` retained. Product Git has no remote. Both original checkouts were clean after integration.
- Changed areas: `business/agent/` contains the copied CLI, prompts, skills, local Compose profile, and focused task tests; `src/Thaddeus.Host/Marketing*` owns the authenticated API and chat mirror; `web/src/components/MarketingWorkspace.tsx` adds Chat and Work inside the Thaddeus shell; `docs/MARKETING_CONTRACT.md` records the transport and state contract. The existing Thaddeus Study side remains available.
- Required: Docker Desktop, existing local `marketing-hire:dev` image and `dev_state` volume, the sibling `marketing-hire/dev/.env` OAuth setup, .NET SDK 10, Node/npm. The start script builds locally, stops only the old `dev-hire-1` container, then starts `marketing-business-hire` on the same existing volume. It does not delete state.
- Launch in a PowerShell terminal from the product checkout:

  ```powershell
  Set-Location 'C:\Users\Ayric\Documents\ChatGPT\marketing-hire-cockpit'
  .\scripts\start-marketing.ps1
  ```

  Open **http://localhost:5189**, use the private host key in `.data/host-key.txt` to log in, then select **Marketing**. Leave the script terminal running for the ASP.NET host. The product container listens on loopback `127.0.0.1:18795`. At handoff the product container and host are running. `docker restart marketing-business-hire` restarts the agent safely without deleting `dev_state`.

## Acceptance evidence

| Check | Result | Observed evidence |
| --- | --- | --- |
| Separate Thaddeus base | **PASS** | Independent normal clone, branch `business/marketing-hire`, no Git remote; original `marketing-hire` and `Thaddeus 2.0` source trees remained clean. |
| Visual continuity | **PASS** | [Baseline](artifacts/marketing-baseline.png), [live Chat](artifacts/marketing-chat-final.png), and [live Work](artifacts/marketing-work-final.png) screenshots retain the dark/gold/serif Thaddeus shell and raven, with a small Marketing selector. Screenshots are local, ignored proof files. |
| Local startup | **PASS** | `scripts/start-marketing.ps1` completed the web and host builds, Compose validation and product container startup; `http://localhost:5189` served the browser. |
| Live chat and runtime | **PASS** | The exact requested Framewright prompt returned a confirmed OpenClaw reply in main Chat. A second request in the task-specific session returned its next action; both appeared in the browser after refresh. |
| Agent task operation | **PASS** | That OpenClaw turn called `hire task create` and committed task `fb6394021a814657b56140c7ede105de` as high priority, ready, version 1. The task appeared in the CLI, API, board and feed. |
| Shared board and next steps | **PASS** | Three live tasks appeared in Work; the queue showed their stored next actions. A manual status edit moved a task to Working and back to Ready in an open page without reload. |
| Chat/Work continuity and task discussion | **PASS** | Switching views retained the same three records. The selected Framewright task used `agent:main:marketing-task-fb6394021a814657b56140c7ede105de`; its chat was visible in task detail after refresh. |
| Conversational priority | **PASS** | Main Chat request placed the launch plan ahead of competitor research. Real `hire task update` calls left launch high/version 2 and competitor low/version 2; the same values appeared in both views. |
| Browser refresh | **PASS** | Reload restored the three tasks and six stored messages from three successful chat requests. |
| Agent-container restart | **PASS** | Safe `docker restart` retained all task IDs/priorities/versions and the configured `openai/gpt-5.6-luna` route. The browser reopened with three tasks/six messages; an already-open Work page refreshed to connected after another restart without page reload. |
| Honest failure | **PASS** | With the product container briefly stopped, authenticated `/api/marketing/state` returned `disconnected` and `taskStoreAvailable=false`, and the live Marketing page showed **Disconnected**. Restart restored connected state and the same three tasks/six messages. Unconfirmed chat outcomes are stored as `unknown`, not successful replies. |
| Approval execution | **NOT IMPLEMENTED** | The existing `hire draft decide` command was retained, but no Marketing approval button or action execution path was exposed or tested. `needs_you`/`user_waiting` are visible, manual states. The queue does not schedule work. |
| No-inference UI operations | **PASS** | Session count, token counters and latest session timestamp were unchanged before/after browser reads and manual task edits. Focused browser test also observed no `/api/marketing/chat` POST from navigation, task selection or manual edits. |

Focused verification: two isolated Python CLI tests passed (idempotent create/update, stale version, blocker cleanup); `dotnet build` completed with zero warnings/errors; web build passed; `web/tests/marketing-workspace.spec.ts` passed (1 test). The browser checks above used the live API and container, not fixture data. No unrelated full suite was run.

## Model, auth and cost boundary

The local container runs OpenClaw `2026.9.4 (3a9d69d)`. The preexisting OpenAI OAuth profile was usable without user interaction. The initially selected `openai/gpt-6-luna` failed a live turn at its prepared subscription route, so the durable boot template and host adapter were pinned to `openai/gpt-5.6-luna`. A fresh turn after reboot returned `READY`; metadata reported OAuth profile, `agentHarnessId=codex`, and `fallbackUsed=false`. No paid API fallback or API key was configured in the product. The two successful auth probes reported 9,185 input/5 output and 9,549 input/5 output tokens respectively. Total sprint token usage and account charges were **not measurable** from the available records; no separately metered API call was observed. The three successful app chat requests are recorded in `.data/marketing-chat.sqlite`; model sessions and tasks remain in the existing `dev_state` volume. Credentials and that private data are excluded from Git.

## Plow boundary and next actions

The [bounded research note](docs/PLOW_LOCAL_RESEARCH.md) records primary source URLs and confidence. The [Plow template](https://github.com/plow-pbc/plow-openclaw-agent) documents `/var/lib/plow` state and regenerated startup files; the [CLI guide](https://github.com/plow-pbc/plow-agents) does not establish a custom authenticated cockpit ingress or an account-specific hosting price. OpenClaw [external app support](https://docs.openclaw.ai/gateway/external-apps) does not prove Plow exposes that port. Plow ingress, TLS/auth routing, volume guarantees, credits and inference cost remain unknown. A separate hosted profile is an untested design, not a deployed feature.

1. Wire and verify a revision-bound local draft approval in Marketing Work before enabling any outbound action. Keep the visible waiting state manual until that gate is proven.
2. Use the existing `pulse` tool for one read-only Framewright source lookup, attach evidence to the created task, and keep outreach in dry-run mode.
3. If hosting is desired, resolve Plow's custom HTTPS/WebSocket ingress, persistent-volume and account cost/credit contract; then run a two-person authenticated smoke with task creation, refresh and container restart.

**User action:** none for the local cockpit. Before hosted work, obtain or provide Plow's endpoint/routing and cost/credit terms; the current brief did not include an endpoint, and this sprint did not guess one.
