# Plow hosting boundary (read-only sprint check, 2026-09-23)

This is a bounded documentation review, not a deployed-host result.

| Question | Finding |
| --- | --- |
| Custom authenticated HTTPS/WebSocket ingress | **Unknown.** The current [Plow agent template](https://github.com/plow-pbc/plow-openclaw-agent) and [plow-agents CLI guide](https://github.com/plow-pbc/plow-agents) document phone-line chat and image deployment, but do not specify an arbitrary inbound HTTPS/WebSocket port for a custom cockpit. OpenClaw itself [supports Gateway WebSocket/RPC external clients](https://docs.openclaw.ai/gateway/external-apps); that does not establish Plow ingress. Do not claim this frontend can be hosted in the Plow agent container until Plow confirms endpoint routing, auth, TLS and port configuration. |
| Persistent storage and boot regeneration | **Confirmed for the template:** agent state uses `/var/lib/plow`; `openclaw.json` and workspace `BOOTSTRAP.md`, `SOUL.md`, `IDENTITY.md`, `USER.md` and rendered `AGENTS.md` are boot-owned. [Template README](https://github.com/plow-pbc/plow-openclaw-agent). The local product uses the existing `dev_state` volume. Hosted volume capacity/backup guarantees remain **unknown**. |
| Hosting/inference cost | The [template](https://github.com/plow-pbc/plow-openclaw-agent) says free hosting requires working usage reporting from the deployed image. Neither it nor the [CLI guide](https://github.com/plow-pbc/plow-agents) provides a charge schedule or account-specific quota. The [Agent Index publish page](https://aiworthusing.com/agent-index/publish) describes registration and reporting, not a price or a credit balance. Hosting limits, model charges and credits for this account are **unknown**. Do not treat the phrase “free hosting” as confirmation of this account's eligibility or unlimited inference. |
| Local/hosted model profile | **Inferred possible, untested hosted:** keep a local Compose/startup profile with `openai/gpt-5.6-luna` and OAuth, and a distinct hosted profile supplied through its own deployment config. OpenClaw's [runtime guide](https://docs.openclaw.ai/providers/openai/runtimes) distinguishes subscription from API-key routes; a successful local profile says nothing about hosted auth availability. No paid fallback is configured in this sprint. |
| Smallest later hosted smoke | Confirm Plow's custom ingress contract and cost/credits first. Then deploy a digest-pinned image with durable task storage, open the authenticated cockpit URL in two browsers/accounts, have one person create a task through agent chat, verify the other sees the same task, refresh both, restart the container without deleting the volume, and verify state and access again. This is a proposed check, **not performed**. |

The local product uses its own loopback ASP.NET host as the browser bridge; no
outbound relay or hosted endpoint has been added. The exact unresolved question
for Plow is: *Can one deployed agent image expose a separate, authenticated
TLS HTTPS and WebSocket service to external browsers, and if so which port,
hostname, auth proxy and persistent-volume configuration are supported?*
