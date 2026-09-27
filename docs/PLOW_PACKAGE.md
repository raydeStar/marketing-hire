# HireZero on Plow: package foundation

The existing .NET host, React cockpit, marketing persona, `hire` tools and work
ledger are packaged beside the Plow OpenClaw base. This is a portability
foundation, not a qualified live employee or a public release. The working
desktop installation and the shared workspace at port 5190 remain untouched.

## Current evidence

- Plow sign-in succeeded after sending the **entire** activation phrase shown by
  the CLI: `Plow Activate: <code>`. Sending only the code does not activate it.
  The account token stays in the WSL user's private `~/.config/plow/token`.
- `artifacts/plow-package-prototype-5/receipt.json` identifies the built image and
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

## Package and check

Use installed dependencies. Choose fresh names; the runners refuse existing
evidence directories. They check disk space with a 10 GiB reserve and never
operate on the shared host or existing marketing worker.

```powershell
node scripts/build-plow-package.mjs plow-package-candidate-1
node scripts/check-plow-package.mjs hirezero-marketing:plow-package-candidate-1 plow-check-candidate-1
node --test packaging/plow/entrance.test.mjs
```

The check uses port 5183 and refuses an occupied port. It creates labelled,
disposable Docker resources, tests the actual Linux cockpit and restart, and
cleans its own resources on success or failure. Fixture mode disables the Plow
boot, reporter and model route; the test browser blocks external origins. Its
ordinary Docker bridge is not a network-isolation claim.

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
   route (`plow/z-ai/glm-5.2`) and OpenClaw 2026.9.6. The existing meter only
   qualifies the 2026.9.4 OpenAI subscription Responses transport and refuses
   incompatible worker runs. Do not remove that refusal, enable unmetered
   fallbacks, or present a scripted shift as live work.
2. Verify phone messages and cockpit chat share actual conversation continuity.
   They use the same main gateway session key; the host also maintains its own
   display/history and execution gate. A shared session key alone does not
   establish bidirectional history or concurrent-turn exclusion.
3. Resolve shared employee/campaign Gateway operations. Direct transport refuses
   commands addressed to a separate logical Gateway. A real separate runtime or
   equivalent qualified boundary is needed; silently merging those agents would
   change existing safety and collaboration behavior.
4. Migrate the owner's existing host and `hire` data with verified backups and
   exact source/target manifests. The prototype used fresh fictional data. It
   did not copy, stop, reset or replace the owner's live workspace or `dev_state`.
5. Run an explicitly authorized live owner smoke test on a free Plow line, then
   onboard the business in the preserved cockpit. No real agent line has yet
   been claimed by these checks. Check `lines` again immediately before claiming.

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

For a later approved local live run, set `HIREZERO_PLOW_IMAGE` to the verified
image tag, work from `packaging/plow`, and use the pinned CLI's
`deploy --local --line <currently-free-line>`. It writes `plow-credentials` there
and starts this package's Compose file. The credential file is ignored by Git
and image staging. Do not use `HIREZERO_PLOW_FIXTURE=1` for a real employee. Do
not delete the volume with `down -v` when it holds owner work.

## Public release requirements

Before a public image/listing, resolve the inherited business agent's undeclared
license noted in the root README and confirm Plow base distribution terms. The
repository's MIT license alone does not establish permission for those upstream
files. Review the public-source/image boundary while keeping credentials and
owner workspace data private. Prepare a real demo, an image, install instructions,
usage-reporting proof and the exact commit for verification.

An admin admits the first public image using the account UID, slug and immutable
digest. Later promotions update new installs; they do not update running agents.
The CLI account token must never be sent to the Index or baked into the image.

Official references: [publishing](https://aiworthusing.com/agent-index/publish),
[CLI](https://github.com/plow-pbc/plow-agents),
[OpenClaw base](https://github.com/plow-pbc/plow-openclaw-agent), and
[Index reporter](https://github.com/plow-pbc/agent-index-client).
