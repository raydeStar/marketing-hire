# HireZero · Marketing Lead

**A marketing employee that brings you prepared work and a clear decision.**

HireZero gives solo founders and small teams **Chip**, an OpenClaw marketing
employee. Tell it about your business and it works shifts on your marketing:
research, plans, posts and fixes to your site. Every piece comes back to you with
what it proposes and why, and nothing is posted, sent or spent until you approve it.

**[Get started](https://hirezero.app/account/?intent=start)** · [Watch the demo](https://youtu.be/D6nLXgSkcuQ)
· [How it works](#how-working-with-chip-goes) · [Agent Index](https://aiworthusing.com/agent-index/hirezero-marketing)
· [Website](https://hirezero.app) · [Self-host](docs/INSTALL.md#self-host-with-docker) · [Development](#development)

<a href="docs/media/hirezero/hirezero-promo.mp4"><img src="docs/media/hirezero/promo-preview.webp" width="100%" alt="HireZero promo highlights: the logo reveal, Chip working its shift, a week of posts approved in one tap, and 'So you can get back to building.'"></a>

**[▶ Watch the 68-second promo, with sound](https://youtu.be/D6nLXgSkcuQ)**

*A product promo made from real HireZero UI captures and motion recreations of
its screens, using sample workspace content. How it was made:
[docs/demo/promo](docs/demo/promo/README.md).*

## Get started

HireZero is hosted: Chip works on Plow, and you open your workspace from your
browser. There's nothing to install, and your computer can be off while it works.

1. Go to **[hirezero.app](https://hirezero.app)** and choose **Get started**.
2. **New?** Choose **Request a workspace**. During early access each one is set up
   with you; no card needed. **Have one?** Sign in with your phone number and the
   code it texts you. **Invited?** Open the link your teammate sent.
3. **Answer three questions** (what you sell, who it's for, what you want right now),
   or paste your website, and **start your first shift**.

The full walkthrough, including connecting your channels and site, is in
[Get started with HireZero](docs/INSTALL.md).

**Early preview.** Hosted sign-in, onboarding, shifts and review work today.
One-click setup from the [Agent Index](https://aiworthusing.com/agent-index/hirezero-marketing)
is waiting on organizer admission, so new workspaces are set up by hand for now.

![HireZero cockpit with Chip's conversation and one prepared recommendation](docs/media/hirezero/cockpit-desktop.png)

*Application UI with fictional example content, captured September 27, 2026;
some screens have been refined since. Screenshots illustrate the workflow; they
are not customer results or evidence of a live model run.*

## How working with Chip goes

1. **Teach it your business.** Three answers are enough to start. Add your voice,
   proof points and boundaries whenever you like.
2. **Start a shift and pick what matters.** Its first shift always fixes the page
   people find you by, plus up to two things you tick: a week of posts, market
   research, a campaign plan and more. While it works, the cockpit says what it's
   doing right now.
3. **Get clear next steps.** When the shift ends, each piece says whether it's ready
   for review or what it still needs. The shift report opens the same way.
4. **Decide in one place.** Every piece opens with what it proposes in one sentence
   and what approving means, then **Approve**, **Send back with a note**, or **Not
   doing this**. Your reasons teach it what to do next time.
5. **Choose what goes out.** A connected network posts or schedules on your say-so.
   Otherwise, copy the exact text, download its images, and post it yourself.
6. **Keep going.** Between check-ins it finds its own next piece toward your goal.
   Campaigns plan themselves in one click, and the site check has **Fix for me**.

## The campaign is the unit of work

A launch is more than a pile of posts. A campaign ties its plan and central angle
to every piece made for it, grouped by week and channel, with each piece's status,
grade and the facts it relies on. Approved posts can go out straight from the
campaign's review, and a campaign's status follows its dates.

![Campaign review with its pieces, grades, claims and review actions](docs/media/hirezero/campaign-pieces-desktop.png)

*Example campaign with fictional content. Grades are the employee's own review of
its work, not independent verification or a forecast of results.*

<details>
<summary>See the plan and phone layouts</summary>

![Campaign plan and central angle in the work window](docs/media/hirezero/campaign-desktop.png)

<p>
  <img src="docs/media/hirezero/cockpit-phone.png" width="300" alt="Prepared recommendation in the phone cockpit">
  <img src="docs/media/hirezero/campaign-phone.png" width="300" alt="Campaign plan on a phone">
</p>

These are responsive browser captures with fictional content.

</details>

## You stay in control

- **Approve** records your decision on that exact version. It doesn't post.
- **Approve and schedule**, or **Publish now**, sends it through the connection shown.
- A **draft** connection (your site, Gmail, Buttondown) saves it as a draft there; you
  publish it.
- **Post it yourself** copies the text and opens the network's composer.

Text Chip to talk things through; decisions happen in the cockpit, against the
exact piece. A text like "looks good" doesn't approve or post anything. Grades are
the employee's own review, not a fact check: check the sources it cites.

## Where the pieces run

| Piece | Job |
|---|---|
| Cockpit (React) | Conversation, next steps, decisions, campaigns, Work and settings |
| Host (.NET) | Workspace, permissions, review, approval and publishing |
| OpenClaw employee | The marketing persona and its model-backed work, inside the Plow package |
| Marketing ledger and documents | Assignments, drafts, evidence and decisions |
| Plow | Runs the employee: identity, messaging and the model route |
| hirezero.app | Phone sign-in, workspaces and teammate invitations |

Hosted, Plow runs all of this and hirezero.app signs you in. Self-hosted, the same
package runs in Docker on your computer, which must stay on; see
[Self-host with Docker](docs/INSTALL.md#self-host-with-docker).

## Privacy, costs and current limits

- Your business data lives in your workspace: on Plow when hosted, or in your Docker
  volume when self-hosted. Requests go to the configured model service either way.
- Credentials are created when the employee is set up and kept out of source and
  images. Connected-account secrets are stored encrypted in the workspace.
- The public image reports daily model token counts to the Agent Index. It never
  uploads your brief, prompts or drafts. Source builds report nothing until
  `AGENT_ID` is set.
- HireZero is free for a limited time. Model availability and quotas follow Plow and
  provider terms; no unlimited allowance or hard spending cap is promised.
- Open release gates, such as native multiplayer and outside-user acceptance, are
  tracked in the [release checklist](docs/PLOW_RELEASE_CHECKLIST.md). Workspace
  review roles alone don't prove OpenClaw multiplayer.

## Development

The current product lives on `main`. It builds on the Thaddeus host and the
marketing-hire ledger; historical Thaddeus handoffs describe that earlier product,
not the current HireZero installation.

Use the SDK pinned by `global.json`, Node 22 and the locked web dependencies.
Read [AGENTS.md](AGENTS.md) and [local checks](docs/LOCAL_CHECKS.md) first. Run the
smallest checks covering your change; hosted GitHub Actions are disabled.

```sh
npm --prefix web ci
npm --prefix web run build
```

For package builds and disposable checks, follow [the Plow package guide](docs/PLOW_PACKAGE.md).
Routine checks use scripted fixtures and make no live model calls. Keep compact
receipts and remove owned test resources after their processes exit. If you're
preparing a release, use the [release checklist](docs/PLOW_RELEASE_CHECKLIST.md)
and [submission kit](docs/HACKATHON_SUBMISSION.md).

Further reading: [employee operating model](docs/EMPLOYEE_OPERATING_MODEL.md),
[marketing integration](docs/MARKETING_CONTRACT.md),
[architecture](docs/ARCHITECTURE.md), and
[screenshot provenance](docs/media/hirezero/README.md).

## License and credits

Original HireZero/Thaddeus project code is [MIT licensed](LICENSE).
The marketing hire's own additions are covered by
[their MIT license](business/agent/hire/LICENSE). OpenClaw, the Plow base and
other dependencies retain their respective terms; see
[third-party notices](docs/THIRD_PARTY.md).

The Plow base is used through its documented
[variant-image distribution route](https://github.com/plow-pbc/plow-openclaw-agent#building-a-variant-image).
The project's MIT license does not relicense those upstream files. Preserve
their applicable notices when distributing a derived image.
