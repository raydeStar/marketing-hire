# Employee shifts

The marketing employee works **shifts** of 8, 16 or 24 hours. During a shift it runs the operating
loop from the owner's research, *sense → prioritize → create → align → launch → measure → decide →
institutionalize learning*, as a repeating cycle, usually hourly. It stops when the window ends,
the budget runs out, the owner pauses it, or nothing useful is left to do. It never pads the time
with filler.

## Design rules

- **The host runs the shift.** Every cycle, stage, model turn and effect is recorded on the host.
  A browser tab being open is not the scheduler.
- **The model gets no tools.** Each model stage is one bounded turn. The host sends a packet (brief,
  permissions, signals, queue) and requires a strict JSON answer. The host then validates it and
  applies the effects itself, the same pattern the campaign runner already uses. Permissions are
  enforced in code, not in the prompt.
- **Deterministic first.** Sensing, QA, measuring and rule-based decisions are ordinary code. The
  model is used only where judgment is needed (prioritize, create, write up learning), and only when
  there is something to act on.
- **Nothing leaves the building.** No posting, sending, spending or account changes. Public-facing
  output becomes a draft for owner approval. Launch means a QA'd hand-off for a person to post.
- **The runtime can be swapped.** The model sits behind `IShiftRuntime`:
  - `scripted`: a deterministic stand-in for tests, so a full shift costs nothing;
  - `openclaw`: the local OpenClaw gateway, metered, with no tools;
  - later, a Plow runtime behind the same interface.

## Stages

| Stage | Who | Reads | Produces |
|---|---|---|---|
| **Sense** | Code | Scorecard anomalies, tasks (blocked, stale, needs a decision), drafts awaiting approval, approved drafts not yet checked, experiments due, teammate input | Signals, each with a severity |
| **Prioritize** | Model | Brief, objectives, PERMISSIONS.md, signals, the task queue, recent learnings, the owner's feedback and the Marketing notebook, the research allowlist | Up to 3 priorities (each may ask for research and up to two allowlisted pages), plus new tasks; the host updates the board |
| **Create** | Model, then a review turn | One priority, its task, sources, related Library documents, the owner's feedback and the notebook | A Library document (draft) or a channel draft for approval, critiqued against the creative-review rubric and revised if it scores 3 or lower anywhere |
| **Align** | Code | Everything created this cycle | Decisions routed to the owner (drafts, documents, "Asks you first" items) |
| **Launch** | Code | Drafts the owner approved | Campaign QA (links, HTTPS, UTM tags, channel length limits, placeholders, risky claims) and a hand-off note for a person to post |
| **Measure** | Code | Experiments whose review date has arrived | The primary metric over the test window vs. its baseline |
| **Decide** | Code | That measurement and the experiment's pre-set rule | Scale / iterate / stop, as an owner decision |
| **Institutionalize** | Model (end of shift) | The shift's record, the owner's feedback, the notebook | A shift report in the Library (`Shift reports/`), learnings, and additions to the Marketing notebook |

## Research during a shift

Prioritize may ask for research on a priority by naming one short topic. The host, not the model, does the research:
- **Discussions:** it searches recent Hacker News threads (the last 90 days) and reads the two most-discussed pages in full.
- **Headlines:** it runs the employee's own `pulse` tool inside its container (Hacker News, Reddit and Google News mentions) and keeps up to six sources in total. These are headlines and snippets, not read pages, and the model is told to cite them only for what they say. In practice Google News supplies most of them; Reddit often returns nothing.
- **Allowlisted sites:** a priority may also name up to two pages to read, but only on the owner's **research sites** (Objectives → Research sites: the owner's own site and competitors', at most ten, subdomains included). The reader is HTTPS-only on port 443, connects to public IPv4 addresses only, follows at most three redirects and only within the allowlist, reads 512 KB at most, and keeps about 3,000 characters of text. Anything else the model names is skipped and noted in the log.

Only the host contacts these services, over public addresses, with size limits. Without the container (the scripted fixture) the headlines are skipped.

Create receives all of these as numbered sources and may cite only them. The host then:
- appends a source list, with where each one came from and its date, so every citation can be checked;
- records each source as evidence on the task, which puts it in Library → Research → Sources.

The document labels them as signals, not proof of demand.

## Review before the owner sees it

Every deliverable gets a second model turn, a critique, when the budget allows. It scores the work 1–5 on the creative-review rubric: strategy, customer truth, distinctiveness, channel fit, brand, a clear action, defensible claims, and shareability. It lists up to four issues. If anything scores 3 or lower, it returns a revision with the same facts and citations and no new claims.

The host keeps the original when the revision is missing, too short, or cites a source number that doesn't exist. It then records the result where the owner will see it: at the foot of a document, or in a draft's "Why this draft", for example *Self-review 3.5/5, revised: Generic: name the segment.* That makes each deliverable cost two turns, so a shift with a small budget produces fewer, better pieces.

## Feedback and the Marketing notebook

The employee learns from the owner in two ways:
- **Verdicts.** Every document the employee wrote has a **Useful / Not useful** row with an optional reason. Every draft decision has an optional **Your reason** field. Verdicts are stored per item (the latest wins; 200 are kept) at `GET/POST /api/feedback`. Owners, managers and contributors can give them.
- **The notebook.** At the end of each shift the employee adds what it established to the **Marketing notebook** (Library → Company): what we know, what was decided, open questions, what worked, and what didn't. Each list is capped at 15, and answered questions are removed. The owner can edit the page freely. The next update starts from the owner's version of each list.

The latest twelve verdicts, with their reasons, and the notebook go into every prioritize, create and end-of-shift turn. When the owner rejects a draft, the reason is written onto the task that goes back to the queue ("The owner rejected draft #4 because: …").

Text meant for somewhere the host can't post to, such as a submission, a bio or an email body, is saved as a draft document in Library → Campaigns → Drafts for review, instead of being rejected.

## The scorecard: the employee's data

Successful marketing leaders run from **one scorecard**, not raw platform dashboards: a primary KPI,
leading indicators, spend and efficiency, funnel movement and current experiments. The employee does
the same. The scorecard is fed by:

- **CSV import.** Every platform exports CSV: Analytics, ad platforms, Shopify, Stripe, CRMs. Both
  *wide* (`date, sessions, signups, …`) and *long* (`date, metric, value`) layouts are accepted.
- **A published Google Sheet** (`File → Share → Publish to web → CSV`). The host fetches only
  `docs.google.com`.

Anomaly detection is plain statistics: the latest value against the trailing 14-day mean. A change is
flagged as material when it is at least 2.5 standard deviations or 25 %, and only metrics with 7 or
more points qualify. Future connectors (Analytics, Search Console, ads) write into the same scorecard
from Plow.

## Budget and control

Only the owner starts a shift, choosing:
- its length (8, 16 or 24 hours);
- how often it cycles (default 60 minutes);
- a model-turn budget.

The host also counts reported tokens. Pause, resume and stop take effect before the next stage.
Chat, the campaign runner and shifts share one execution gate, so they never run model turns at
the same time.

## Objectives: what the work is for

**Library → Company → Objectives & positioning** is a versioned record. It holds:
- the **north star**: a metric, a target, a date and why it matters, optionally tied to a scorecard metric so its progress is tracked;
- this quarter's **objectives**, each with key results;
- **positioning**: who it's for, the problem, what they use instead, why us, and **proof points**;
- **competitors**;
- the **current focus** and **non-goals**.

Every prioritize, create and end-of-shift turn receives it, together with the brief (claims and examples included) and the whole scorecard. Prioritize ranks work by contribution to the north star and respects the non-goals. Create uses only the proof points given.

The cockpit shows the north star's progress. Onboarding (links or interview) drafts all of this for review. Managers and the owner can edit it; teammates can read it.

## Running it for real

```bash
powershell -File scripts/start-marketing.ps1 -LiveShifts
```

That starts your real workspace with live shifts. Each shift turn is metered in the employee's receipt ledger, and a shift is capped by the turns you give it. For a disposable test against the real employee, use `scripts/start-campaign-fixture.ps1 -LiveShiftContainer marketing-business-hire`: work records stay in a throwaway ledger, and only model turns reach the employee.

The first live run spent 5,232 tokens over 6 turns, roughly 900–1,900 tokens per turn. An 8-hour shift checking in hourly with a 16-turn budget is therefore on the order of 20–30k tokens.
