# HireZero: hackathon submission draft

Entry for **AI Worth Using × OpenClaw 2.0: Build Your Startup's First Hire**
([rules](https://luma.com/zhkhsnpa), read September 25, 2026). Nothing here has been submitted.

- **Submission deadline:** Monday, September 28, 11:59 PM PT, on the AI Worth Using Agent Index.
- **Leaderboard snapshot:** Wednesday, September 30, 11:59 PM PT. Ranked by installs and token usage reported by the
  AI Worth Using client.

The Agent Index "publish an agent" form wasn't read for this draft, so its exact fields and limits are unconfirmed.
The copy below covers the usual fields; trim it to the form. Text in **[brackets]** is filled in after the first live
shift on hirezero.app. Until then, don't claim it.

This replaces the Thaddeus Product Hunt draft ([PRODUCT_HUNT_SUBMISSION_DRAFT.md](PRODUCT_HUNT_SUBMISSION_DRAFT.md)),
which describes an earlier product.

## Eligibility: what the rules require, and where we stand

| Rule | Status | To do |
|---|---|---|
| Built on OpenClaw 2.0 **in multiplayer mode**; others interface with it | **Partial.** The employee runs on OpenClaw 2026.9.4, and teammates join the workspace with roles. OpenClaw's native shared session with two real people is not verified ([MULTIPLAYER_AUDIT.md](MULTIPLAYER_AUDIT.md)). | Run the audit's short acceptance script with a second person, keep the receipt |
| A real startup job, done by the agent, not mocked | **Partial.** It has done real research and drafts for HireZero on test copies of the workspace. | The first live shift on the real workspace, publishing to hirezero.app |
| Publicly available, MIT licensed | **Ready to decide.** `raydeStar/marketing-hire` is MIT but **private**. The history scan below found no credentials | Owner: remove the one personal address if wanted, then make it public |
| Submitted to the Agent Index, **usage reported with the AI Worth Using client** | **Not done.** Local meter receipts are not official reporting. | Add the client to the employee's container; submit the listing |
| Demo video of 60 seconds or longer | **Not done.** A captions-only cut exists from a test run. | Record the script below on the real flow |
| Entrant 18+, can be in SF on October 6 or record a segment | Owner's call | Confirm at entry |

## Making the code public: history scan (September 26, 2026)

A read-only scan covered all 502 commits and every added line:
- **No credentials found:** no OpenAI, Anthropic, GitHub, AWS or Google keys, no OAuth secrets, private keys, JWTs, bearer tokens or HireZero agent keys.
- **No sensitive files, ever:** no `.env` files, databases, key files or host keys have been committed, including ones later deleted. The repo's own `scripts/scan-secrets.mjs` passes.
- **Secret-looking strings are fake test values:** 5 distinct ones, in test files.

What going public reveals, for the owner to decide:
- **Your personal Gmail** is the author address on your commits (inherent to git history). It also appears once in `docs/MVP_DELEGATION_ACCEPTANCE.md` (line 124), which could be edited out.
- **The Windows username** (`C:\Users\Ayric`) appears in 9 handoff and audit documents.
- **Upstream contributors' addresses** come with the forked OpenClaw base, and open-source authors' addresses are in bundled license texts. Both are normal for a public repository.

The work email (`goengineer.com`) appears nowhere. Visibility stays private until the owner changes it.

## Listing copy

**Name:** HireZero

**Tagline (36 characters):** A marketing employee that asks first

Alternatives: "Hire a marketing employee. Keep the final say." (46) · "Your first hire: marketing, with approval built in" (50)

**Short description (215 characters):**

> HireZero is an OpenClaw marketing employee that works shifts. It researches your market, drafts posts, blog articles
> and page copy, and brings you decisions. Nothing is published, sent or spent until you approve it.

**Category / role:** Marketing (a startup's first marketing hire)

**Links:**
- Site: https://hirezero.app
- Code: https://github.com/raydeStar/marketing-hire (MIT; **public before submission**)
- Demo video: **[YouTube link]**

## Description

Most early startups need marketing done every week, and nobody has the time. HireZero is that hire: an OpenClaw 2.0
agent with a marketing role that works **shifts**. In each shift it:
- reads what changed: mentions of the product and category, competitors' pricing pages, and how the last posts did;
- ranks the work against the owner's goals and assigned tasks;
- drafts posts, blog articles, landing-page copy and research, with sources;
- reviews its own work before anyone sees it.

Then it brings the owner decisions, not a stream of questions.

The owner keeps the final say, and the host enforces it, not the prompt:
- The model holds no tools.
- Every draft waits for an explicit, reasoned approval.
- Approving never publishes. The owner publishes or schedules each post to Bluesky, Mastodon, LinkedIn, X, WordPress
  or their own site.
- Every model turn is metered, averaging about 2,800 tokens a turn in live runs.
- Every action leaves a receipt.

The owner's reasons become the lessons in a marketing notebook it keeps.

**Its first job is marketing HireZero itself:**
- It sized the market from public small-business statistics (BLS and Census), with sources.
- It wrote a sourced positioning comparison against Jasper and Lindy.
- It drafts the blog and home page for hirezero.app, which land in the site's CMS as drafts for review.
- **[So far: N shifts, N posts published on hirezero.app/blog, home-page copy approved on DATE.]**

## Multiplayer: who works with it

**[Confirm after the native multiplayer check. Until then, describe only the workspace roles.]**

A HireZero workspace is shared. The owner invites teammates as viewers, reviewers, contributors or managers, and the
host checks each role on every request. Teammates assign it tasks, comment on drafts and review campaigns, but only the
owner approves, spends or grants access. **[Everyone talks to the same employee in one OpenClaw shared session, and it
knows who asked what.]** Customers meet its work on the owner's own site and channels.

## How to run it

**[Match the Agent Index install path once it's chosen.]** Today it runs locally:
1. Clone the repository.
2. Run `powershell -File scripts/start-marketing.ps1 -LiveShifts`. It builds the cockpit and starts the OpenClaw
   employee container.
3. Sign in with the host key.
4. Work through **Settings → Go-live checklist**: goals, your site, competitors, working hours.
5. Assign a task and start a one-hour shift.

For a free trial run with a scripted stand-in model, use `scripts/start-campaign-fixture.ps1`.

## Demo video (about 90 seconds; at least 60 is required)

Record on the real workspace after the first live shift, with hirezero.app open in a second tab. Narrate in your own
voice (the cockpit's script-and-record narration) or use captions.

| Time | On screen | Say |
|---|---|---|
| 0:00–0:08 | hirezero.app home page | "Every startup needs marketing every week. HireZero is our first hire: a marketing employee on OpenClaw. Its first job is marketing HireZero." |
| 0:08–0:20 | Cockpit: goals, then *Start shift* | "I gave it our goals, our site and our competitors. It works in shifts: it reads what changed, picks what matters and does the work." |
| 0:20–0:35 | Shift stage strip, then the research document with sources | "Here it sized our market from public small-business data and compared us to Jasper and Lindy. Every claim has a source." |
| 0:35–0:50 | *Needs your decision*: a blog draft with its self-review; approve it with a reason | "Nothing goes out without me. It reviewed its own draft first. I approve it and say why. The reason becomes its lesson." |
| 0:50–1:05 | hirezero.app admin, the draft in Review; publish; the live blog post | "Approval doesn't publish. The post lands on our site as a draft, and I publish it myself." |
| 1:05–1:20 | Team tab (roles); a teammate's comment or task **[the shared session, once verified]** | "It's multiplayer. My teammates give it work and review drafts; only I approve and spend." |
| 1:20–1:30 | The shift report and token meter | "Every turn is metered and logged. That shift cost [N] tokens. HireZero: a marketing employee that asks first." |

## Before submitting

- [ ] Native multiplayer acceptance run with a second person, receipt kept
- [ ] First live shift on the real workspace; the bracketed results filled in
- [ ] Code repository public, after a history scan for keys and personal data
- [ ] AI Worth Using client reporting usage from the employee
- [ ] Demo video recorded (60 seconds or more) and uploaded
- [ ] Agent Index listing submitted by **September 28, 11:59 PM PT**
- [ ] No artificial usage: installs and tokens come from real people only
