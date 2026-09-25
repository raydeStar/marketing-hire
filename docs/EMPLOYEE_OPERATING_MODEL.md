# The marketing employee: operating model and agent tooling

This turns the owner's research on how elite marketing managers work
([research/ELITE_MARKETING_MANAGER_WORKFLOW.md](research/ELITE_MARKETING_MANAGER_WORKFLOW.md))
into the product. The cockpit (this repo) owns the owner-facing surfaces and host records.
The agent runtime and its tools will be wired in **Plow**; this document is the contract
for that work.

## The idea in one paragraph

The best marketing managers are not content machines. They run a loop, **sense → prioritize →
create → align → launch → measure → decide → learn**, and they protect their judgment by
automating the coordination around it. Our employee should do the same: work proactively
from a daily and weekly rhythm, prepare decisions instead of asking open questions, produce
derivatives and research, and bring the owner only what needs the owner. Positioning, final
creative judgment, spending and anything published stay with the human.

## What already exists in the cockpit

| Research element | Cockpit surface | Host record |
|---|---|---|
| Morning signal scan and triage | **Today → Morning meeting**: sends the heartbeat prompt; shows today's brief | Marketing chat (`/api/marketing/chat`) |
| Weekly rhythm (Mon review … Fri retro) | **Today → This week's rhythm**: one prompt per day, today highlighted | Chat |
| Decisions that need the owner | **Inbox**: brief gaps, draft angles to review, drafts to approve, blocked tasks | Marketing ledger |
| Creative review against a rubric | **Campaigns** review desk + wiki **Creative review rubric** template | Runway reviews |
| One-page campaign brief, experiment card, launch checklist, postmortem, scorecard, decision log | **Wiki → New page** playbook templates | Company wiki (`/api/company-wiki`) |
| Decision log, experiment tracker, content calendar as working tools | **Assets → New page → Working tools** (record-keeping pages) | Artifact apps (`/api/artifacts/{id}`) |
| Campaign pages (landing, announcement, link in bio) | **Assets → New page → Campaign pages**, code editor, history, trash | Artifact apps |
| Media for campaigns | **Assets → Upload media**: images, GIF, MP4, WebM (24 MiB each) | Uploads (`/api/uploads`) |
| Operating instructions per employee | **Team → member → Files**: AGENTS.md, HEARTBEAT.md, SOUL.md, IDENTITY.md, USER.md, TOOLS.md | Employee files (below) |
| Ethos and brand | **Onboarding** (links / interview / form) → business brief + "Company ethos" wiki page | `/api/marketing/profile`, wiki |
| Institutional memory | **History** (records + activity log), wiki revisions | Ledger, wiki |

## Contract for the Plow agent work

### 1. Employee files → the agent workspace

- `GET /api/organization/agents/{agentId}/files`: latest non-deleted revision of each file
  `{agentId, name, version, content, digest, author, deleted, createdAt, updatedAt}`.
- `GET /api/organization/agents/{agentId}/files/{name}/history`
- `PUT /api/organization/agents/{agentId}/files` `{requestId, name, version, content, deleted?}`
  (owner only; `version` must match; request IDs replay).
- Names: `^[A-Za-z0-9][A-Za-z0-9_.-]{0,59}\.md$`, at most 16 files per member, 32,000 characters each.

**Plow should:** on agent start and whenever a file's digest changes, write the files into the
OpenClaw workspace (today `boot-dev.mjs` deletes SOUL/IDENTITY/USER and writes a fixed AGENTS.md;
replace that with these files). Record the digests delivered so the UI can later show
"in sync / pending". `HEARTBEAT.md` is the proactive checklist OpenClaw's heartbeat reads.

### 2. Proactive rhythm (the "employee" part)

- A scheduled **morning run** per workspace: execute HEARTBEAT.md, post the brief as an
  assistant message in the main session whose preceding user turn starts with
  `Morning meeting` (Today renders the latest such reply as "Today's brief"). A dedicated
  `briefs` record would be cleaner; until then, keep that shape.
- **Weekly rhythm** runs are optional: the same prompts as Today's rhythm buttons
  (`web/src/app/TodayView.tsx`).
- Every proactive run must stay inside the owner's grants: research and drafts only; no
  posting, sending, spending or account changes without an Inbox decision.

### 3. Tools the agent needs (from the research's automation priorities)

| Priority | Tool | Output lands in |
|---|---|---|
| P1 | **Signal scan / anomaly report**: pull channel metrics, explain deltas, alert only on material change | Morning brief; Inbox item when a decision is needed |
| P1 | **Decision log writer**: append `{date, decision, decided_by, evidence, revisit}` | Decision log working tool (artifact entries via the page bridge schema) |
| P1 | **Campaign QA / launch checklist**: UTMs, links, specs, claims, missing approvals | Wiki checklist copy per campaign; Inbox if blocked |
| P1 | **Content derivatives**: variants, resizes, channel adaptations, translations | Drafts for approval (`hire draft`) and Assets pages |
| P2 | **Research synthesis**: community/competitor scans with citations | Evidence records (`hire evidence`), wiki "hypothesis" pages |
| P2 | **Experiment analysis**: compute results against the pre-set decision rule | Experiment tracker entries; decision log |
| P2 | **Page builder**: create/update campaign pages | `PUT /api/artifacts/{id}` with `version:"absent"` to create; page runs sandboxed (no network, `data:` images) |
| P3 | Media/budget optimization | Recommendations only, as Inbox decisions |

Artifacts: a page is an artifact app with `definition.page {html, css, javaScript}` (≤ 40,000
characters together) and at least one field. Records pages use `thaddeus.onChange(state)` and
`thaddeus.save({upserts, deleteIds})`; see `web/src/app/pageTemplates.ts`.

### 4. Wiki as agent context

Published (`status: "active"`) pages are already pinned into meeting prompts by revision
(`CompanyWiki.Capture/Render`). Direct marketing chat does **not** read them yet. Plow should
inject the member's readable published pages (company, department, member-private) into the
employee's context with the same revision pinning.

### 5. Onboarding import

Onboarding sends one chat turn asking the employee to read the owner's links and reply with a
JSON object (`display_name, product_summary, audience, goals, voice, claims, examples, channels,
guardrails, ethos`) inside a ```json block (`web/src/app/Onboarding.tsx`). The agent needs a
**web fetch** tool for this; without one it should say so rather than invent details.

## Many users (competition traffic)

The current host is **single-tenant**: one owner key, one SQLite workspace, one agent container.
Collaborators (paired browsers or customer sign-in) only reach campaigns the owner shares.
For many people each getting *their own* employee, Plow must provide **one isolated workspace
per tenant**: its own host data directory, owner identity, agent container/session, model
budget and rate limits. Nothing in the UI assumes a particular owner; it reads identity from the
session.

Already done for load:
- `/api/marketing/state` coalesces concurrent readers, reuses results for 2 s, caches the gateway
  health check for 15 s, and invalidates on every marketing write (`MarketingBackend.State`).
- Clients poll only while the tab is visible; the shell fetches one shared snapshot for all views and
  re-reads the team directory once a minute. A refresh after a write always waits for a post-write read.
- The strict sign-in budget (12 per minute per address) applies only to secret-guessing endpoints
  (host key login, launch issuing, pairing). Identity-provider sign-in and one-time launch claims use
  the ordinary 600-per-minute budget, so people behind one venue address can all sign in; clients retry
  a claim that gets 503.
- Measured on the fixture host: 20 concurrent state reads complete in ~280 ms, the cost of one read.

Still needed for a public event:
- Tenant isolation and sign-up in Plow (customer sign-in exists: `docs/CUSTOMER_IDENTITY_PLAN.md`).
- Per-tenant token budgets and a global request cap on model turns.
- Replace polling with the event stream for marketing state if tenants share a host process.
- A demo/fixture tenant so judges can explore without spending model budget
  (`scripts/start-campaign-fixture.ps1` is the local equivalent).
