# Working by text, and no invented product facts

September 29, 2026. Plow's reviewers use HireZero by text, not the cockpit. Their
v15 test found the employee inventing product facts: for "our new walnut desk"
it wrote a 47"–120" lift range and "a limited first run of 50 desks". It also
never started the campaign worker for a text-only owner. This change fixes both
and makes most of the cockpit usable by text, while leaving the web cockpit as
it was.

## What changed

**The texting agent takes the brief and queues the work; it writes no copy.** A
plugin hook (`business/agent/meter/plow/text-mode.mjs`) runs on every text that
arrives over Plow. Cockpit chat is left alone. For each text it:

- Adds a tagged block, built by code from the ledger, next to the owner's
  message. The block holds:
  - the saved facts, which are the only product facts the agent may state;
  - the open tasks, with their ids and any questions they're waiting on;
  - this turn's rule: ask for missing facts (at most four numbered
    questions), save business details to the brief, or queue the work;
  - the exact `hire` commands, with the brief's current version.
- Refuses file writes and sub-agent sessions on text turns. GLM 5.2 had used
  both to do the drafting somewhere the owner couldn't see it.

This follows what worked best in the first Sir Thaddeus project for small
models: state injected next to the message, and tools removed rather than
forbidden in prose.

**The worker's drafts are checked against the facts it was given**
(`EmployeeShifts.FactAudit.cs`).

1. One listing turn returns every claim in the draft that a customer could
   check, each with a quote of its support.
2. Code decides which claims stand. A claim stands only when:
   - the quote really appears in the brief, the owner's words or a source the
     worker read;
   - the quote carries most of the claim's key words;
   - every number in the claim appears in those facts.
3. When something is unsupported, one repair turn turns it into a blank, such
   as `[material]`.
4. Anything the repair leaves in is marked `[unconfirmed]`.

Blanks can't be published: launch QA now refuses any named blank such as
`[price]`, while citations like `[1]` and link text are still allowed.

**The persona and skills** now open with "Never invent product facts": ask
first, or use named blanks. The example uses a product the tests never use.

**Text-only owners get the worker.**

- The cockpit starts at boot instead of waiting for the first web visit.
- Queued work is picked up within a minute.
- When a run or shift ends, the host texts the owner once through Plow's API.
  The text contains the work itself, the blanks to fill in, the worker's
  questions and how to reply. It pauses while the owner has the cockpit on
  screen (`OwnerTexts.cs`, `EmployeeShifts.Texts.cs`).

**The cockpit by text** (`TextCommands.cs`, `hire cockpit`).

- **Reading:** status, the queue, drafts, scheduled posts, working hours and
  connected accounts.
- **Changing:** approve, reject, post, schedule, start or stop a shift, working
  hours, the weekly plan and brief edits.

Every change goes through the same steps:

1. The agent proposes it.
2. The host words the question and gives it a code.
3. The change runs only when Plow's history for the owner's own chat shows that
   exact question, followed by the owner's own plain yes.

The agent can neither write nor answer that question itself. The same services
as the cockpit's buttons carry the change out. Connecting an account still
needs its sign-in in the cockpit.

## Measurements (GLM 5.2 through Plow, local agent on a free line)

The eval sends texts to the real Plow main agent, in private sessions that are
never delivered to a phone. Every run starts from an empty ledger and an empty
workspace. The scorer is a heuristic: it counts
numbers and spec, date or scarcity words the owner never said. Every flagged
reply was also read by hand.

| Owner texts | v15 prompt | Prompt and skills only | With the text gate |
|---|---|---|---|
| Judge's request, then "Just draft it now." (3 runs, 6 replies) | 6/6 replies invent specs | 6/6 | 0/6: 4 numbered questions, then queued |
| Facts given up front (3 runs) | 3/3 invent (solid, electric, free shipping) | 3/3 | 0/3: brief saved, work queued |

With the gate, the flagged words appear only inside the questions ("materials,
weight capacity?"), not as statements.

The worker's own drafts for the judge's request, before the fact check, used
blanks but still invented "solid walnut, not veneer", "grain chosen by hand"
and "you asked us, more than once". The self-review flagged the lines, but its
rewrite scored lower and was dropped. That is why the check is decided by code.

The second build added the onboarding fix, exact command templates and
refusal of sub-agent handoffs. It was measured on a fresh install; each run
starts from an empty ledger and workspace:

| Owner texts (3 runs each) | Result |
|---|---|
| Judge's request, then "Just draft it now." | 3/3: four numbered questions, then queued, with no copy in the text |
| Facts given up front | 3/3: saved to the brief and queued |
| "hi", then a description of the business | 3/3: a one-line intro and four questions, then the brief saved with a one-line recap and a suggested first piece; no task |
| Held out: a coffee roaster, then "Go ahead and draft it." | 3/3: questions, then queued |
| Held out: a wedding photographer, then "just write it" | 3/3: questions, then queued |

No invented fact appeared as a statement in any of the 27 replies. The words
the scorer flagged were all examples inside questions ("250g bag?",
"washed or natural?", "limited dates?"), plus the owner's own $1,290.

The command templates also cut the cost of the queueing turn from 86k–208k
tokens (GLM retried `hire profile update` 7–12 times) to about 29k. Asking
turns cost about 14k.

**A second pass on guided conversations.** The judge will use a made-up
company, so this pass used six businesses no instruction mentions: a dog
walker, invoicing software, a bakery, a plumber, a skincare brand and an indie
game studio. Each had a scripted owner who answers partly and then says go.
The pass also covered answering the questions, answering some of them,
onboarding followed by a request, a vague ask, and a worker question answered
by text. It ran on GLM 5.2, one or two runs each, 16 conversations:

- The questions were about the business and the piece: puppy-walk area and
  booking, the loaf's price and whether it's walk-in, the game's name and hook.
- After a partial answer, Chip asked once more for only what was still missing,
  offering go, in the dog-walker, bakery, game and one partial run. The
  invoicing, plumber and other partial runs queued the work instead. Answers
  always reached the task word for word.
- With the brief saved, a request queued at once, without asking again.
- A vague "help me get more customers" got onboarding questions, not advice.
- A worker question answered by text went back to the right task.

Fixed after this pass:

- One run saved an assumption as a fact. Only the owner's words are saved now,
  and the fact check skips any saved line that calls itself an assumption.
- One run rewrote a task's instructions around the answer and lost the
  original ask. `hire task answer` now appends the owner's words and requeues
  the task in code.
- Text onboarding now asks what the owner wants from marketing and saves it as
  goals, so the cockpit doesn't run its own onboarding again.

**Texts and the worker take turns.** During a worker turn the meter pins
model requests to that turn, so a text turn used to be refused. A refused turn
gets no reply, and its message is dropped from the transcript. Now a text
waits up to 13 seconds for the worker's turn to settle. While the text is
being answered, a marker holds the worker's next turn for up to 90 seconds; a
marker older than 3 minutes is ignored.

## Tests

- Host: `TextWorkflowTests` (texts, confirm-before-change, and container-only
  access), `FactAuditTests`, and `RequestsRunTests` (a finished run texts the
  owner). The full host suite passed: 1,502 passed, 1 skipped.
- Plugin: `text-mode.test.mjs`.
- `hire`: 76 tests.
- The packaged meter check passed on the first build.
