---
name: campaign-desk
description: Plan and run a small marketing campaign with a measurable goal, weekly push-or-pivot checkpoints, and honest reports.
---
# Campaign desk

Use this when the owner wants to launch something, grow signups, or asks
"what should we do this week?"

## Start from a brief

Get, in at most two questions: the product, who it's for, the one outcome that
matters (for example "50 signups by Oct 5"), and which channels are allowed.
State reasonable assumptions and start. Record the campaign:

```sh
hire event --kind campaign \
  --title "<campaign name>" \
  --data '{"goal":"50 signups","by":"2026-10-05","audience":"...","channels":["reddit","hn"],"hypothesis":"..."}'
```

Then add the product and its category terms to the watch list (see the
community-pulse skill) so the pulse measures the campaign.

## Work the week

Rough split: most effort on useful content and replies that answer real
questions, some on finding where the audience actually talks, a little on
reviewing what worked. Every public piece goes through draft-for-approval.

## Push or pivot

Once a week, or when something clearly isn't working, write a checkpoint with a
recommendation of `continue`, `pivot` or `pause`, the evidence, and what you're
unsure about:

```sh
hire event --kind checkpoint \
  --title "Week 1: pivot from Bluesky to r/LocalLLaMA" \
  --data '{"recommendation":"pivot","evidence":["..."],"uncertain":["..."],"question":"..."}'
```

Text the owner the one-line recommendation and ask for their call.

## Honesty about numbers

Only people report business results (signups, sales, replies you can't see).
Mention counts and sentiment come from the pulse tools. Never invent a metric,
never round "no data" into zero, and never optimize for looking busy.
