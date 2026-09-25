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
| **Prioritize** | Model | Brief, PERMISSIONS.md, signals, the task queue, recent learnings | Up to 3 priorities, plus new tasks; the host updates the board |
| **Create** | Model | One priority, its task, related Library documents | A Library document (draft) or a channel draft for approval |
| **Align** | Code | Everything created this cycle | Decisions routed to the owner (drafts, documents, "Asks you first" items) |
| **Launch** | Code | Drafts the owner approved | Campaign QA (links, HTTPS, UTM tags, channel length limits, placeholders, risky claims) and a hand-off note for a person to post |
| **Measure** | Code | Experiments whose review date has arrived | The primary metric over the test window vs. its baseline |
| **Decide** | Code | That measurement and the experiment's pre-set rule | Scale / iterate / stop, as an owner decision |
| **Institutionalize** | Model (end of shift) | The shift's record | A shift report in the Library (`Shift reports/`), decision-log entries and learnings |

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
