# Install HireZero

Run Chip, the HireZero marketing employee, with its web cockpit and persistent
business workspace.

**Release status — September 28, 2026:** source and the prebuilt Linux image are
public. The exact image below passed an anonymous pull and local package checks.
Hosted sign-in and onboarding also passed; real hosted work, one-click admission and outside-user acceptance remain
separate steps in the [launch receipt](PLOW_LAUNCH_20260928.md).

## Choose your installation

| Route | Availability |
|---|---|
| Local Docker package using the public image | Available; recommended steps below |
| One-click through the Agent Index | Listing registered; Plow admission and hosted acceptance pending |
| Local Docker package built from source | Available from this public repository; steps below |

After admission, the Agent Index's installation action will be the shortest
route. A listing alone does not prove a working hosted install. The commands
below run the employee and cockpit on your computer.

## Before you start

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

## Get the public package

Clone into a new directory for the Compose configuration, then pull the pinned
release. No GitHub login or local compilation is needed:

```sh
git clone https://github.com/raydeStar/marketing-hire.git hirezero
cd hirezero
HIREZERO_IMAGE=ghcr.io/raydestar/hirezero-marketing@sha256:c9d3e27cf06d81e0738d7ad4619c78fbc391f8a2bdb0f10b2ab61400e02c2cc3
docker pull "$HIREZERO_IMAGE"
```

Continue to [connect Plow](#connect-plow-and-create-a-private-install).
The public package reports actual daily model token counts to the
[HireZero Agent Index entry](https://aiworthusing.com/agent-index/hirezero-marketing)
every five minutes. It does not upload prompts, draft content or your business
brief. Preserve the state volume so an update retains your install identity.

### Optional: build from source

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

## Connect Plow and create a private install

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
Source-built private packages leave `AGENT_ID` empty; they do not report until
you deliberately configure the registered identity. Do not create a second
public listing for an installation of this agent.

## Open the cockpit

Open **http://localhost:5191** on the computer running Docker (or the port you
put in `.env`). Give the gateway a moment to start if the page initially says it
is not ready. Follow the local workspace setup shown in the browser.

The Compose file binds to loopback. It is not a public web address, and your
phone cannot reach that URL on its own. Text the employee using the number
assigned to your chosen Plow line. Hosted web access uses Plow's authenticated
ingress. The hosted setup check passed, while full work acceptance remains separate.

For a hosted installation, use an account-authenticated Plow web launch link.
It opens `https://<agent-id>.plow.run` and establishes a browser session. The raw
VM's `exe.xyz` address is private infrastructure, not the cockpit login. The
API's `POST /v1/agents/{id}/web` needs a phone-code **account login**, distinct
from the CLI's activation credential; its launch ticket expires after one minute.
Do not share that ticket or put it in an issue. The public Index's one-click
action remains unavailable until an organizer admits the image.

**Text for conversation; cockpit for decisions.** Use text to ask questions,
discuss a campaign and give feedback. Review the exact piece, destination and
revision in the cockpit to approve it or send it back. A text reply such as
"looks good" does not record approval or publish anything. A hosted installation
needs both the employee's phone number and an authenticated cockpit link.

## Onboard your business

1. Choose **Learn from my website**, an interview, or the written brief in
   onboarding. Provide your business website, not somebody else's product.
2. Review the proposed brief before saving: offer, audience, goals, voice,
   factual claims and anything the employee must not do. Correct assumptions.
3. Add a few past posts and a true company story if you have them. These help
   Chip learn your voice; you can refine them later.
4. Set one measurable goal and assign one small piece of work. For example:

   > Prepare a launch introduction for our business: one LinkedIn draft and a
   > matching website headline. Use only claims supported by our brief or linked
   > sources. Explain the central angle and flag anything needing my decision.

5. Queue the assignment, then explicitly start a shift when you are ready to
   use model capacity. Queuing the first assignment alone does not start inference.
6. Open the prepared work from the cockpit. Check its sources, grade and
   blockers. Approve the piece or send it back with a specific correction.

For a first run, leave publishing connections unconfigured until you have reviewed
the workflow. Later, read the action label carefully: a combined **approve and
schedule** action schedules a post, while a plain approval records a decision.
Some connections save drafts; a copy/composer route leaves the final post to you.

## Check that the installation works

- The cockpit reconnects and the reviewed business brief survives a refresh.
- A real assignment produces saved work that opens from the campaign package.
- Your phone receives a reply from the assigned employee number.
- Usage is recorded for the actual work; a connected status alone is not proof
  of a successful model request.

The [release checklist](PLOW_RELEASE_CHECKLIST.md) separately tracks live shift,
phone continuity, native multiplayer and outside-user acceptance. An illustrated
demo or scripted package test does not replace those checks.

## Stop and resume

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

## Update without losing your business

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

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| Login never activates | Send the whole current activation phrase, not only the code; try SMS if RCS failed |
| No free phone line | Check `plow-agents lines`; do not take an occupied line |
| Image not found | Confirm the build succeeded and the image name in `.env` matches; WSL and Docker Desktop must use the same engine |
| Port already in use | Choose a free `HIREZERO_PLOW_PORT` and recreate the container; preserve the volume |
| Browser refuses the connection | Run `docker compose ps -a` and inspect startup logs; use the exact `localhost` port in `.env` |
| Cockpit starts but employee is disconnected | Allow gateway startup, then inspect Plow credentials, connection status and provider access |
| Assignment is queued but nothing runs | Explicitly start a shift and inspect any displayed blocker |
| No leaderboard usage | Source-built private packages leave reporting disabled; the registered release reports actual usage every five minutes when `AGENT_ID=hirezero-marketing` is set |

For a bug report, include the image/source version, symptom and redacted error.
Do not attach credentials, private business exports or an unreviewed full log.
More technical details: [Plow package](PLOW_PACKAGE.md) and
[official Plow CLI](https://github.com/plow-pbc/plow-agents).
