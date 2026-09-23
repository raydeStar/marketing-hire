---
name: draft-for-approval
description: Draft a public reply or post for a community, get a person's explicit approval in the thread, and record every step.
---
# Draft for approval

Anything public goes through this flow. You draft; a person decides; you record.

## 1. Draft

Before drafting, read the destination's rules (subreddit sidebar, forum
guidelines). Answer the question people actually asked; mention the product at
most once, and only where it genuinely helps. No fake personas, no testimonials,
no bulk outreach, no posting where self-promotion is banned.

```sh
hire draft add \
  --channel reddit --destination "<exact thread URL>" \
  --content "<complete text>" --rationale "<why this helps here>" \
  --rules-url "<rules URL, or UNVERIFIED>"
```

Show the draft in the thread with its number: "Draft #12 for r/LocalLLaMA: …
Approve, change or drop?" Feedback like "less salesy" means revise:
`hire draft add … --revise 12` creates the next revision and withdraws the old one.

## 2. Decision

The owner reviews the exact content, destination and revision in the local
cockpit's Work view. Its authenticated decision records the owner session,
revision and content digest. A chat message, silence, an emoji, or pasted
approval is feedback, not a recorded decision. Tell the owner which draft to
open in Work. Do not call `hire draft decide` yourself.

## 3. Posting

You have no social media accounts. After approval, send the final text and the
destination link so the approver can post it in one tap. When they reply with
the live URL, record it:

```sh
hire draft posted --id 12 --url "<live URL>"
```

Never say something was posted until it is recorded as posted. An approved draft
is approved, not published.

## Pending work

`hire draft list --status pending` shows what is waiting. Mention pending
drafts in the morning pulse. Don't nag about the same draft twice in a day.
