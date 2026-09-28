# Install HireZero

Run Chip, the HireZero marketing employee, with its web cockpit and persistent
business workspace.

**Release status — September 28, 2026:** this is an early source-install guide.
The source is public; the prebuilt image and one-click installation are being
finalized. The package has local Linux-container checks. Hosted cockpit access
and outside-user acceptance are tracked separately in the release checklist.

## Choose your installation

| Route | Availability |
|---|---|
| One-click through the Agent Index | Pending public image, registration and Plow admission |
| Local Docker package built from source | Available from this public repository; steps below |

After admission, the Agent Index's installation action will be the shortest
route. This guide will be updated with the verified public image and launch URL.
There is no public HireZero image tag to copy yet.

## Before you start

- Docker running Linux containers, with Compose 2.24 or newer.
- A Plow account and a phone able to send the activation message. A free phone
  line must be available on Plow when you create the employee.
- For a source build: Git, Python 3.11+, Node 22/npm and .NET SDK 10.0.203
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

## Build the local package

Clone into a new directory and install the locked frontend dependencies:

```sh
git clone https://github.com/raydeStar/marketing-hire.git hirezero
cd hirezero
npm --prefix web ci
node scripts/build-plow-package.mjs plow-package-local-1
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
printf 'HIREZERO_PLOW_IMAGE=hirezero-marketing:plow-package-local-1\nHIREZERO_PLOW_PORT=5191\n' > .env
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
out of Git, screenshots and shared logs. Leave `AGENT_ID` empty during private
setup; registering a listing is a separate release step.

## Open the cockpit

Open **http://localhost:5191** on the computer running Docker (or the port you
put in `.env`). Give the gateway a moment to start if the page initially says it
is not ready. Follow the local workspace setup shown in the browser.

The Compose file binds to loopback. It is not a public web address, and your
phone cannot reach that URL on its own. Text the employee using the number
assigned to your chosen Plow line. Hosted web access uses Plow's authenticated
ingress and still needs separate release acceptance.

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
2. Build/check the new package, or pull the exact published image once one exists.
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
| No leaderboard usage | Private setup leaves reporting disabled; consult the release checklist before enabling it |

For a bug report, include the image/source version, symptom and redacted error.
Do not attach credentials, private business exports or an unreviewed full log.
More technical details: [Plow package](PLOW_PACKAGE.md) and
[official Plow CLI](https://github.com/plow-pbc/plow-agents).
