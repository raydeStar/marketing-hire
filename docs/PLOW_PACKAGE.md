# HireZero on Plow: package foundation

The existing .NET host, React cockpit, marketing persona, `hire` tools and work
ledger are packaged beside the Plow OpenClaw base. Live private onboarding is
verified; public-release acceptance is still incomplete. The working
desktop installation and the shared workspace at port 5190 remain untouched.

## Current evidence

- Plow sign-in succeeded after sending the **entire** activation phrase shown by
  the CLI: `Plow Activate: <code>`. Sending only the code does not activate it.
  The account token stays in the WSL user's private `~/.config/plow/token`.
- `artifacts/plow-package-launch-1/receipt.json` identifies the current image and
  every captured source hash. The pinned upstream OpenClaw is 2026.9.6; the base
  image and .NET runtime digests are in `packaging/plow/base.json`.
- `artifacts/plow-port-20260926/results/plow-vault.trx` records 16 focused host
  tests covering direct process execution, cancellation, proxy identity,
  cookie/CSRF protection, and encrypted credential storage. Three Node entrance
  tests passed, along with 24 existing customer sign-in regression tests. The
  real upstream `/opt/plow/probe` passed on foundation 1 with external networking
  disabled; no model was called.
- `artifacts/plow-check-prototype-5/receipt.json` records real Linux host APIs and
  the campaign/Today/first-win browser workflow. The shift was explicitly
  scripted. A container restart retained the owner account, company brief,
  tasks, campaigns and owner direction. The headless vault round trip passed
  before and after restart; its directory and key had modes 0700 and 0600.
  Desktop and phone screenshots were reviewed; layout checker results were empty.
- The check removed its exact container, fictional volume and network. Build
  scratch is removed by the builder; compact hashes, logs and screenshots remain.
  Neither fixture evidence nor a saved login proves live Plow messaging or model
  execution. The latest Dockerfile also disables automatic Index registration
  until `AGENT_ID` is explicitly supplied after listing approval.
- The Plow request meter now has a separate, pinned 2026.9.6 profile for
  `plow/z-ai/glm-5.2`. `artifacts/plow-meter-check-packaged-1/receipt.json`
  records the real Gateway and installed SDK with external networking disabled:
  an unrelated worker is blocked, a refused reservation sends nothing, an
  admitted request produces one synthetic completion and a matching 8-token
  receipt, and replay sends nothing extra. Its ledger and reply are fictional.
  The actual Python ledger's six focused receipt tests and five host readiness
  tests passed separately; these are not evidence of live Plow billing.
- `artifacts/plow-check-meter-1/receipt.json` confirms the updated package still
  passes the real cockpit API/browser workflow and restart-persistence check.
  Its desktop/phone screenshots were reviewed and the layout reports are empty.
  The meter-qualified candidate is `hirezero-marketing:plow-package-meter-1`.
  Older prototype image tags may be removed;
  their compact evidence remains, but replay requires rebuilding those inputs.
- `artifacts/plow-package-onboarding-1/receipt.json` records a small host update
  over meter 1. It captures committed source at `f83c66c`, retaining the existing
  frontend and worker. Plow's installed model status reports its environment
  credential outside `runtimeAuthRoutes`; the cockpit now recognizes that
  credential only when the report has no missing-provider or route errors.
  Eight focused checks cover acceptance and refusal. The private installation
  was updated without resetting its volume, and the browser's onboarding became
  available. `artifacts/plow-onboarding-20260927/live-update.json` records the
  retained owner and empty business state. This is connection/configuration
  evidence, not a live model completion.
- The owner subsequently requested HireZero onboarding. One real website-reading
  request succeeded; the reviewed brief, ethos and proposed objectives were
  saved. The review corrected unsupported public-code/free-product claims and
  an invented deadline. Receipt:
  `artifacts/plow-onboarding-20260927/hirezero-onboarded.json`.
- Launch 1 captures `6208abd`, including the latest shift ceiling, grant closure
  and report-size fixes. Its packaged API/browser/restart workflow passed in
  `artifacts/plow-check-launch-1/receipt.json`; its build scratch and disposable
  container, network and volume were removed. Meter qualification is reused
  for the same pinned runtime and 22 unchanged source files, with the live
  runtime's read-only readiness response recorded alongside the package.
- The private install now runs `hirezero-marketing:plow-package-launch-1`:
  `sha256:47e40cd22fc1b39024112addc4bb078d5cda0a6dfc2e397174dbd6853dd03d4d`.
  `artifacts/plow-package-launch-1/private-upgrade.json` verifies the owner,
  HireZero brief, objectives and conversation survived the update. Onboarding 1
  is the retained rollback. No extra inference was requested by these checks.

The meter binds to the running host's AI transport. OpenClaw 2026.9.6 copies bare
plugin dependencies into separate module graphs, so importing a plugin-local AI
package can report readiness while leaving the real transport unguarded. The
profile also recognizes the runner's exact boundary-zero affinity ID. It rejects
other sessions, resumed boundaries, model changes, tools, redirects and repeated
sends. Usage is accepted only after a complete SSE reply with consistent counts;
missing evidence retains unknown usage. Ordinary owner requests retain the host
transport. The desktop subscription profile remains unchanged.

## Package and check

Use installed dependencies. Choose fresh names; the runners refuse existing
evidence directories. They check disk space with a 10 GiB reserve and never
operate on the shared host or existing marketing worker.

```powershell
node scripts/build-plow-package.mjs plow-package-candidate-1
node scripts/check-plow-package.mjs hirezero-marketing:plow-package-candidate-1 plow-check-candidate-1
node --test packaging/plow/entrance.test.mjs
node scripts/check-plow-meter.mjs hirezero-marketing:plow-package-candidate-1 plow-meter-check-candidate-1
```

The check uses port 5183 and refuses an occupied port. It creates labelled,
disposable Docker resources, tests the actual Linux cockpit and restart, and
cleans its own resources on success or failure. Fixture mode disables the Plow
boot, reporter and model route; the test browser blocks external origins. Its
ordinary Docker bridge is not a network-isolation claim.

The separate meter check defaults to mounting current meter sources over the
existing package for small iterations. Add `--packaged` to exercise the image's
own meter and configuration instead. Both modes disable external networking and
replace only the test ledger and model response with fictional fixtures.

The normal image keeps Plow's identity, chat channel, provider, config sync and
five-minute usage reporter. The cockpit takes port 3000; the internal gateway
moves to 18789. Its base startup probe remains intact. A narrow, checked wrapper
extends the base's configuration renderer without replacing its boot pipeline.

Hosted web entry requires Plow's private loopback ingress, its authenticated
`X-Plow-User`, and the exact HTTPS `<vm>.exe.xyz:3000` Host. The first admitted
request pins that origin for the process. The existing cookie/CSRF protocol
still protects API mutations. Local entry is explicitly loopback-only and
replaces browser-supplied identity headers with a fictional local owner.

`Marketing:Transport=direct` executes the existing fixed employee commands
beside the host. Docker remains the default for existing installations. The
per-boot gateway password is read into the child environment at dispatch and
never embedded in command arguments. Plow's headless encrypted vault keeps
connection secrets outside study exports in `/var/lib/plow/credentials`.
Encryption does not isolate credentials from an agent that controls the same
filesystem. The persisted `/var/lib/plow` volume must be retained on upgrade.

## Live onboarding still needs these steps

1. Qualify the worker request meter for Plow's OpenAI-compatible completions
   route against a real provider reply during the explicitly authorized live
   smoke test. Offline runtime admission and receipt handling now pass. The
   worker requests `max_tokens: 4096` but does not claim a verified hard spending
   ceiling; the grant uses measured, post-response accounting. Incompatible
   workers and unmetered fallbacks remain refused.
2. Verify phone messages and cockpit chat share actual conversation continuity.
   They use the same main gateway session key; the host also maintains its own
   display/history and execution gate. A shared session key alone does not
   establish bidirectional history or concurrent-turn exclusion.
3. Resolve shared employee/campaign Gateway operations. Direct transport refuses
   commands addressed to a separate logical Gateway. A real separate runtime or
   equivalent qualified boundary is needed; silently merging those agents would
   change existing safety and collaboration behavior.
4. Fresh workspace and business onboarding are complete. Keep the desktop
   workspace and `dev_state` intact; nothing was migrated. The owner asked Claw
   to learn HireZero, and the corrected brief is saved in the new volume.
5. The private agent holds `ln_p1`; Plow's channel connected, with heartbeat and
   cron disabled. The cockpit is open at `http://localhost:5192` (5191 was
   occupied). One real onboarding request succeeded; a real campaign shift and
   a phone reply still need acceptance. No Index listing was registered. This
   runtime and its `hirezero-plow_state` volume are owner resources now, not
   disposable fixtures. See [release sequence](PLOW_RELEASE_CHECKLIST.md).

The approved listing metadata is:

```text
slug: hirezero-marketing
name: HireZero · Marketing Lead
blurb: The marketing hire that wrote its own listing. Evidence-backed campaigns, shipped only with your approval.
```

Keep `AGENT_ID` empty during private qualification. Supplying
`AGENT_ID=hirezero-marketing` enables the inherited reporter to register the
listing and report this agent's usage; it must not collect the developer's
unrelated Codex or Claude work. Review the concrete release before that step.

The current local installation lives in WSL at
`~/.local/share/hirezero-plow/install`, with mode-0600 `plow-credentials` and a
private directory. Its `.env` pins the image ID and port, and its Compose file
uses project `hirezero-plow`. The account credential remains separate. For a
new installation, copy this package's `compose.yml` and `.dockerignore` into a
private directory, set `HIREZERO_PLOW_IMAGE`, then use the pinned CLI's
`deploy --local --line <currently-free-line>` there. The command mints a new
agent; do not repeat it to update this existing one. Do not use
`HIREZERO_PLOW_FIXTURE=1` for a real employee or delete owner state with `down -v`.

## Code updates and business data

The local Compose entrance defaults to port 5191, leaving 5183 for disposable
checks and 5190 for the shared workspace. `HIREZERO_PLOW_PORT` overrides it.
Keep the installation's Compose project name and `state` volume stable.
Rebuild and check a new application image, set `HIREZERO_PLOW_IMAGE` to its
verified tag or immutable ID in the installation's `.env`, then run
`docker compose up -d --no-build --pull never`. Compose replaces the container
and retains `/var/lib/plow`; there is a brief interruption during restart.
Do not run the mint/deploy command again for an ordinary code update.

`artifacts/plow-check-upgrade-1/receipt.json` proves a different-image upgrade
from foundation 1 to meter 1 and rollback using the same fictional volume.
Owner identity, business profile, tasks, campaigns, owner direction and the
vault key survived; both images passed the vault health check. This pair has
compatible data formats. Future schema changes require their own migration
and backup checks; rollback is not automatically safe across all versions.
The check disables Plow boot and inference, so it does not prove a live phone
connection survives an update. Repeat for a changed persistence contract with:

```powershell
node scripts/check-plow-package.mjs hirezero-marketing:plow-package-OLD plow-check-upgrade-FRESH hirezero-marketing:plow-package-NEW
```

Pushing code alone does not change the running app. Plow image promotion changes
future installs, not running agents; local installs need the explicit Compose
update above. Hosted in-place updates remain unqualified by this local check.

## Public release requirements

The [Plow base's variant instructions](https://github.com/plow-pbc/plow-openclaw-agent#building-a-variant-image)
explicitly describe building and pushing customized images. Use that distribution
route and preserve upstream files and notices. HireZero's MIT license covers its
own code; it does not relicense the Plow base or other dependencies. Review the
public-source/image boundary while keeping credentials and
owner workspace data private. Prepare a real demo, an image, install instructions,
usage-reporting proof and the exact commit for verification.

An admin admits the first public image using the account UID, slug and immutable
digest. Later promotions update new installs; they do not update running agents.
The CLI account token must never be sent to the Index or baked into the image.

Official references: [publishing](https://aiworthusing.com/agent-index/publish),
[CLI](https://github.com/plow-pbc/plow-agents),
[OpenClaw base](https://github.com/plow-pbc/plow-openclaw-agent), and
[Index reporter](https://github.com/plow-pbc/agent-index-client).
