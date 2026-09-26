// Playbooks from the owner's research: the documents that repeatedly save elite marketing managers time.
export type WikiTemplate={title:string;kind:'fact'|'policy'|'hypothesis'|'question';summary:string;body:string};

export const wikiTemplates:WikiTemplate[]=[
  {title:'Voice: how we sound',kind:'policy',summary:'Paste posts you wrote; the employee writes like them',body:`## Posts that sound like us
Paste three to ten posts or emails you wrote and liked, one after another. The employee matches their rhythm and word choice; it never copies them.

-

## Words we use, and words we don't
-

## How long, how formal
-
`},
  {title:'Stories: true stories to tell',kind:'fact',summary:'Your founder story, a customer moment, a strong opinion',body:`The employee tells these as you told them, never embellished, when one fits the work.

## Why we started
-

## A customer moment
What happened, who it was (or a description), and what changed for them.
-

## Something we believe that most people in our market don't
-
`},
  {title:'Company ethos',kind:'policy',summary:'What we believe and how we show up',body:`## What we believe
-

## How we sound
-

## What we will never do
-

## Proof we can point to
-
`},
  {title:'One-page campaign brief',kind:'policy',summary:'Objective, insight, proposition, KPI, guardrails',body:`**Business objective:**

**Who it's for and their problem:**

**Key insight:**

**Proposition:**

**Desired behavior:** what should people *do*?

**Channels:**

**Primary KPI:**   **Guardrail metrics:**

**Budget:**   **Owner:**   **Launch date:**

**Explicitly not doing:**
`},
  {title:'Creative review rubric',kind:'policy',summary:'Eight questions before anything ships',body:`1. Is the strategy evident?
2. Is there a recognizable customer truth?
3. Is it distinctive, or could any brand have made it?
4. Is it native to the channel?
5. Is the brand identifiable?
6. Is the desired action clear?
7. Are factual or regulated claims defensible?
8. What would make someone voluntarily share it?
`},
  {title:'Weekly growth scorecard',kind:'fact',summary:'Outcomes separate from diagnostics',body:`| | This week | Last week | Note |
|---|---|---|---|
| **Primary KPI** | | | |
| Leading indicator 1 | | | |
| Leading indicator 2 | | | |
| Spend / efficiency | | | |

**Notable segment changes:**

**Experiments running:**

**Decisions needed:**
`},
  {title:'Experiment card',kind:'hypothesis',summary:'Decide the rule before you see the data',body:`**Hypothesis:** If we …, then … because …

**Intervention:**

**Audience:**

**Primary metric:**   **Guardrails:**

**Duration / sample:**

**Decision rule:** we ship if …, we stop if …
`},
  {title:'Launch checklist',kind:'policy',summary:'Nothing ships with a gap',body:`- [ ] Owners ready and named
- [ ] Creative approved
- [ ] Tracking and UTMs in place
- [ ] Landing page QA (links, mobile, speed)
- [ ] Audience configured
- [ ] CRM triggers tested
- [ ] Support / customer success briefed
- [ ] PR and social response plan
- [ ] Legal and claims checked
- [ ] Rollback criteria and escalation contacts
`},
  {title:'Decision log',kind:'fact',summary:'So nobody re-argues a settled question',body:`| Date | Decision | Decided by | Evidence | Assumptions | Revisit if |
|---|---|---|---|---|---|
| | | | | | |
`},
  {title:'Campaign postmortem',kind:'fact',summary:'What happened, why, and what changes',body:`## What happened

## Why it happened

## What's still uncertain

## What we can generalize

## What concrete process changes
`}
];
