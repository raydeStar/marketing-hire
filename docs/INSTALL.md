# Get started with HireZero

HireZero gives you **Chip**, a marketing employee that researches, plans and drafts
your marketing, then brings each piece to you to approve. Nothing is posted, sent or
spent without you.

Most people use it **hosted**: Chip works on Plow, and you open your workspace from
[hirezero.app](https://hirezero.app). There is nothing to install, and your computer
can be off while it works. If you'd rather run everything yourself, see
[Self-host with Docker](#self-host-with-docker).

- [Start by text](#start-by-text)
- [Start in your browser](#start-in-your-browser)
- [Onboard your business](#onboard-your-business)
- [Review what it brings you](#review-what-it-brings-you)
- [Connect your channels and site](#connect-your-channels-and-site)
- [Self-host with Docker](#self-host-with-docker)
- [Troubleshooting](#troubleshooting)

## Start by text

1. Open **[HireZero on the Agent Index](https://aiworthusing.com/agent-index/hirezero-marketing)**
   on your phone and tap **Text this agent**. That sends Plow "Set this up for me";
   Plow sets up your own Chip on its own number, and Chip texts you back.
2. Say hi. Chip asks what you sell, who buys it, the facts it may state, where you
   want to show up and what you want from marketing, and saves your answers as
   your brief.
3. Ask for work in your own words. Chip asks for missing facts, or reply **go** and
   it drafts with named blanks such as `[price]`. It texts you the drafts when
   they're ready.
4. Reply with changes, or approve, schedule, post, start a shift or set working
   hours by text. Each change asks for your "yes" first. For a channel that isn't
   connected, Chip texts you the post at the time you picked, so you can put it up
   yourself.

The web cockpit shows the same employee and work. Ask Chip for its address, or
sign in at hirezero.app.

## Start in your browser

1. Go to **[hirezero.app](https://hirezero.app)** and choose **Get started**.
2. **New here?** Choose **Request a workspace**. During early access each workspace
   is set up with you by hand; you'll hear back when yours is ready. No card needed.
3. **Already have a workspace?** Choose **Sign in to my workspace**, enter your phone
   number and the code it texts you, then **Open your workspace**.
4. **Invited by a teammate?** Open the private link they sent and sign in with the
   phone number they invited. You join their workspace with the campaigns they
   shared; approvals stay with the owner.

| Way in | Status |
|---|---|
| Hosted workspace from hirezero.app | Available; setup is assisted during early access |
| Joining a teammate's workspace | Available by invitation |
| One-click from the [Agent Index](https://aiworthusing.com/agent-index/hirezero-marketing) | Available and verified: tap **Text this agent** |
| Self-host with Docker | Available; [steps below](#self-host-with-docker) |

Model usage runs through Plow; HireZero is free for a limited time. Plow/provider
quotas apply, and no unlimited allowance is promised.

## Onboard your business

The first time you open your workspace, a short onboarding runs.

1. **Tell Chip about your business.** Three answers are enough: what you sell, who
   it's for, and what you want right now. Or paste your website and let it draft
   them, or talk it through. Everything else (voice, proof points, boundaries) is
   optional and can be added later.
2. **Make it sound like you** (optional). Answer one or more short questions in your
   own words: how you started, a customer moment, something you believe. Chip writes
   from these.
3. **Start your first shift.** It always makes the single biggest fix to the page
   people find you by. Tick up to two more, such as a week of posts, market research
   or a campaign plan, and press **Start my first shift**. It works for up to 30
   minutes, and you can stop it any time.

While it works, the cockpit on the right shows what it's doing now ("Now: Writing
'Your first week of posts'") and the next check-in. You can keep chatting; replies
take a little longer during a shift.

**Text for conversation; the cockpit for decisions.** Text or chat with Chip to ask
questions and give direction. Approving or sending work back happens in the cockpit,
against the exact piece. A message like "looks good" doesn't approve or post anything.

## Review what it brings you

When the shift ends, **Your next steps** lists each piece it made, saying whether
it's ready for review or what it still needs. Open one, and a card at the top says:

- **what it proposes**, in one sentence, and **what approving means**;
- **Approve**, **Send back with a note**, or **Not doing this** (with an optional
  reason, so it learns). Anything unfinished offers **Send it back to finish**.

For a post, approving records your decision; it doesn't post. Then either:

- **have it posted for you**, if that network is connected (publish now or schedule), or
- **post it yourself**: **Copy the text** word for word, download the images or videos
  that go with it, and open the network's composer. It can remind you at a time.

**Work** keeps everything in tabs: *To do* (what's in progress and waiting on you),
*Campaigns*, *Calendar*, *Results*, *Listening* and *History*. A campaign with no plan
offers **Have it plan this campaign**; its status follows its dates. The site check
under *Listening* has **Fix for me** for each problem it finds.

## Connect your channels and site

Open **Settings → Connections**. Connecting is optional: without it you copy and
post things yourself.

- **Your website.** A HireZero site (in your site admin, **Settings → AI employee →
  Create a key**, then paste the site address and key) or WordPress (an application
  password). Approved fixes and posts are saved on your site **as drafts** for you to
  publish; nothing goes live on its own.
- **Social networks.** Bluesky, Mastodon, a Facebook Page, Instagram and Threads
  connect from their own settings (each shows the steps). LinkedIn and X sign in
  when your workspace offers them.
- **Email.** Gmail (approved emails land in your drafts) and Buttondown.
- **Your numbers.** Google Analytics, Search Console, Plausible, HubSpot, Meta Ads or
  a CSV, so Chip can tell you what moved and what to try next.

## Self-host with Docker

Prefer to run Chip on your own computer? These steps run the employee and cockpit in
Docker, and that computer must stay on for it to work. A self-hosted workspace is
separate from a hosted one; nothing moves between them.

**Release status — September 28, 2026:** source and the prebuilt Linux image are
public. The v12 image below passed anonymous public registry verification and the
packaged Linux workflow, including restart persistence for work and credentials.
It includes v11's context-budget changes and only offers supported invitation
methods. This rollout does not establish cloud writing quality. Existing
installations retain their current image; hosted updates are operator-assisted.
One-click setup was admitted and the listing verified on September 29. See the
[entry-flow rollout](ENTRY_FLOW_20260928.md) and the
[earlier quality and deployment receipt](PLOW_SHIFT_QUALITY_20260928.md).

### Before you start

- Docker running Linux containers, with Compose 2.24 or newer.
- A Plow account and a phone able to send the activation message. A free phone
  line must be available on Plow when you create the employee.
- Git and Python 3.11+ for the repository files and Plow CLI.
- For a source build only: Node 22/npm and .NET SDK 10.0.203
  (compatible patch roll-forward is allowed by `global.json`).
- Space for the container base, application image and saved work. The package
  builder reserves 4 GiB for its work **plus a 10 GiB free-space floor**; an
  uncached Docker base pull needs additional space.

The commands below use **Bash**. On Windows, run them inside WSL2 with Docker
Desktop integration enabled and install the build prerequisites in that same
environment. The runtime target is Linux amd64; other architectures are not
qualified by the current package checks.

Plow/provider quotas and pricing apply. Confirm the allowance on your account
before starting model-backed work; this guide does not promise free inference.

### Get the public package

Clone into a new directory for the Compose configuration, then pull the pinned
release. No GitHub login or local compilation is needed:

```sh
git clone https://github.com/raydeStar/marketing-hire.git hirezero
cd hirezero
HIREZERO_IMAGE=ghcr.io/raydestar/hirezero-marketing@sha256:1eb79fb6e91fd552b5f49c2cc0cf3493d79e235b487cf23bd8c952090d035b29
docker pull "$HIREZERO_IMAGE"
```

Continue to [connect Plow](#connect-plow-and-create-a-private-install).
The public package reports actual daily model token counts to the
[HireZero Agent Index entry](https://aiworthusing.com/agent-index/hirezero-marketing)
every five minutes. It does not upload prompts, draft content or your business
brief. Preserve the state volume so an update retains your install identity.

#### Optional: build from source

After cloning and entering the repository, use this instead of the public image:

```sh
npm --prefix web ci
node scripts/build-plow-package.mjs plow-package-local-1
HIREZERO_IMAGE=hirezero-marketing:plow-package-local-1
```

The builder produces `hirezero-marketing:plow-package-local-1`. It captures the
source, compiles the web app and host, builds against the pinned Plow base, and
removes its staging files. It does not sign you in, start an employee or register
a listing. Logs and the image ID are in
`artifacts/plow-package-local-1/receipt.json`.

Use a fresh `plow-package-...` name on each build; the builder refuses to overwrite
an existing receipt. Do not run the upstream base's Dockerfile as a substitute:
that would omit the HireZero cockpit and host.

### Connect Plow and create a private install

Install the [official Plow CLI](https://github.com/plow-pbc/plow-agents#1-install-and-log-in),
then sign in and inspect available lines:

```sh
plow-agents login
plow-agents lines
```

Send the **entire activation phrase**, including `Plow Activate:`, to the number
the CLI gives you. If RCS does not activate it, send that phrase as SMS. Login
codes expire; use the current code from your terminal. The activation number is
different from the number your employee will use.

These are **first-install commands**. If the installation directory below already
contains a workspace, skip creation and follow
[updates](#update-without-losing-your-business) instead. Do not overwrite an
existing `.env` or credentials file.

From the `hirezero` repository directory, prepare a new private installation folder:

```sh
install -d -m 700 "$HOME/.local/share/hirezero/install"
cp packaging/plow/compose.yml "$HOME/.local/share/hirezero/install/compose.yml"
cp packaging/plow/.dockerignore "$HOME/.local/share/hirezero/install/.dockerignore"
cd "$HOME/.local/share/hirezero/install"
umask 077
printf 'HIREZERO_PLOW_IMAGE=%s\nHIREZERO_PLOW_PORT=5191\n' "$HIREZERO_IMAGE" > .env
```

Use an unused local port if 5191 is occupied. The Compose project name
is `hirezero-plow`; use a separate project name if you intentionally run a second
business installation on the same Docker engine.

Choose a currently **free** line from `plow-agents lines`. Replace `ln_xxx` in
the following command with that line ID:

```sh
plow-agents deploy --local --line ln_xxx
chmod 600 .env plow-credentials
docker compose ps
docker compose logs --tail=60
```

The deployment command creates an agent credential and starts Compose. Do not
repeat it for ordinary updates: it creates a new agent. Keep `plow-credentials`
out of Git, screenshots and shared logs. The public image already includes
`AGENT_ID=hirezero-marketing` and registers your installation automatically.
Source builds now also default to this registered identity. For private package
qualification, explicitly override `AGENT_ID` to an empty value; fixture mode
disables reporting and model access automatically. Do not create a second
public listing for an installation of this agent.

### Open the self-hosted cockpit

Open **http://localhost:5191** on the computer running Docker (or the port you
put in `.env`). Give the gateway a moment to start if the page initially says it
is not ready. Follow the local workspace setup shown in the browser.

The Compose file binds to loopback. It is not a public web address, and your
phone cannot reach that URL on its own. Text the employee using the number
assigned to your chosen Plow line.

For an older hosted installation, use an account-authenticated Plow web launch link.
It opens `https://<agent-id>.plow.run` and establishes a browser session. The raw
VM's `exe.xyz` address is private infrastructure, not the cockpit login. The
API's `POST /v1/agents/{id}/web` needs a phone-code **account login**, distinct
from the CLI's activation credential; its launch ticket expires after one minute.
Do not share that ticket or put it in an issue. The public Index's one-click
action (**Text this agent**) sets up a new hosted install on the promoted image.

### Stop and resume

From the private installation folder:

```sh
docker compose stop
```

To resume the same employee and saved workspace:

```sh
docker compose up -d --no-build --pull never
```

The host computer and Docker must stay running for the local employee to work.
Stop an active shift in the cockpit before planned maintenance.

### Update without losing your business

These steps apply to an installation whose Docker volume you control. A local
update retains the same workspace; it does not require onboarding again.

1. Finish or stop active work. Back up the installation's named Docker volume
   with your Docker backup procedure; keep its credentials separately and private.
2. Pull the exact new published image, or build/check your source package.
   Keep the previous working image as your rollback.
3. Change only `HIREZERO_PLOW_IMAGE` in the installation's `.env` to the checked
   image tag or immutable image ID.
4. From that installation directory, run:

   ```sh
   docker compose up -d --no-build --pull never
   ```

5. Reopen the cockpit and confirm the brief, saved work and connection survived.

Keep the Compose project name and `state` volume unchanged. **Do not use
`docker compose down -v` for an update**: it deletes the saved volume. Do not
rerun the Plow deployment/mint command. Rollback across a data-format change needs
a compatible backup, not just an older image.

A Git pull changes source files; it does not update a running container. Plow
image promotion changes future installs, not existing installations.

For **Plow-hosted workspaces**, ask for a supported image replacement that keeps
the agent ID, line and `/var/lib/plow` storage. The currently documented CLI has
no such update command. Do not retire and recreate a populated workspace as an
ordinary update: a fresh deployment is a separate workspace. Settings exports
preserve application data, but are not a full VM/vault backup or an automatic
restore. Keep the existing installation until a complete restore or retained-volume
upgrade has been verified.

### Check that a self-hosted installation works

- The cockpit reconnects and the reviewed business brief survives a refresh.
- A real assignment produces saved work that opens from the campaign package.
- Your phone receives a reply from the assigned employee number.
- Usage is recorded for the actual work; a connected status alone is not proof
  of a successful model request.

The [release checklist](PLOW_RELEASE_CHECKLIST.md) separately tracks live shift,
phone continuity, native multiplayer and outside-user acceptance.

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| The sign-in code doesn't arrive | Check the number you entered and ask for a new code; codes expire |
| Signed in, but no workspace | New workspaces are set up with you during early access; choose **Request a workspace** |
| Invited, but can't see the workspace | Sign in with the exact phone number the invitation was sent to; invitations are single-use |
| Chat says it's still writing | Replies can take a minute or two during a shift; the answer appears in the thread by itself |
| A shift made nothing | Open **Work → History → Shift log**; each check-in says what it did and why |
| Nothing lands on my site | Connect it under **Settings → Connections**; until then you apply fixes yourself |
| Self-host: login never activates | Send the whole current activation phrase, not only the code; try SMS if RCS failed |
| Self-host: no free phone line | Check `plow-agents lines`; do not take an occupied line |
| Image not found | Confirm the build succeeded and the image name in `.env` matches; WSL and Docker Desktop must use the same engine |
| Port already in use | Choose a free `HIREZERO_PLOW_PORT` and recreate the container; preserve the volume |
| Browser refuses the connection | Run `docker compose ps -a` and inspect startup logs; use the exact `localhost` port in `.env` |
| Cockpit starts but employee is disconnected | Allow gateway startup, then inspect Plow credentials, connection status and provider access |
| Assignment is queued but nothing runs | Explicitly start a shift and inspect any displayed blocker |
| No leaderboard usage | Confirm `AGENT_ID=hirezero-marketing`, the five-minute reporter and actual model use; fixture mode deliberately reports nothing |

For a bug report, include the image/source version, symptom and redacted error.
Do not attach credentials, private business exports or an unreviewed full log.
More technical details: [Plow package](PLOW_PACKAGE.md) and
[official Plow CLI](https://github.com/plow-pbc/plow-agents).
