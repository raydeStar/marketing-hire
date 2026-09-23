# Configurable marketing agent: first local pilot

2026-09-23. This pilot uses the **provisional** audience hypothesis of
founder-led small teams with limited marketing capacity. The owner has not
confirmed that segment. The pilot job is one source-backed brief about public
marketing workflow problems. It runs locally through the existing OpenClaw OAuth
route and shared `hire` ledger. No Plow, post, DM, email or schedule was used.

## Acceptance threshold

The local workflow passes if the agent reads at least two original public pages,
attaches both to its task, writes a brief with a next experiment and coverage
limits, and leaves the task in a truthful final state. This is a workflow check,
not a claim of demand, sales, ROI or autonomous discovery.

## Observed sequence

1. The owner brief was set to version 3 with the provisional audience and goal.
   The pilot task is `1dd195adb6f1419aa5cbcf7b895b42d7` in Work.
2. A bounded local `pulse` scan for `small business marketing automation`
   reported partial coverage. Its 14-day digest had zero **dated stored**
   mentions. The first agent turn attached no sources, correctly reporting it
   could not inspect an original page. It marked the scan task done, which did
   not satisfy the pilot threshold.
3. The new `pulse items` view exposed five stored Stack Overflow candidates,
   dated 2011–2023. Their titles and snippets were unrelated or weakly related
   to this pilot. Fetched count was not useful evidence of relevant demand.
   Stack Overflow remains selectable but is no longer in the default marketing
   scan.
4. Two direct URLs were supplied to the agent. It read and attached the
   [solo-founder marketing discussion](https://news.ycombinator.com/item?id=47667504).
   Reddit returned a JavaScript challenge, so the agent left that
   [small-business discussion](https://www.reddit.com/r/smallbusiness/comments/1r9sfhg/small_business_owners_what_ai_tools_are_actually/)
   unattached and kept the task Ready with a blocker.
5. A second supplied Hacker News page about
   [AI marketing and spam concerns](https://news.ycombinator.com/item?id=49703771)
   was read and attached. The agent wrote a two-source brief and moved the task
   to Done, version 4, with no blocker. The two attached links and final reply
   were present in the live ledger and host state. The two fictional approval-test
   drafts remained the only drafts; the pilot created none.

The attached Hacker News links were **supplied leads**, not discovered by the
`pulse` scan. The first evidence record carries the original scan query as
metadata even though its URL was supplied; do not infer acquisition provenance
from that field. Both sources come from one community, and neither is a
representative sample. The Reddit page was checked by the Codex research pass,
but not by the local OpenClaw agent, so it does not count toward this agent
pilot's source threshold.

## Decision

**Push the local research workflow; confidence: medium.** The technical
threshold is crossed: the brief, task updates and two directly read source
records agree. **Audience and market threshold: not crossed; confidence: low.**
The sources support testing a reviewable workflow and avoiding automated spam;
they do not establish who would buy it or what they would pay.

The next proposed experiment is a small 14-day *local* observation of one
human-reviewed marketing workflow, logging time spent, accepted and rejected
drafts, and quality problems. It has not been scheduled or started. First the
owner should confirm or change the audience and pilot job in the brief; that
choice is still tracked as a Needs You task.
