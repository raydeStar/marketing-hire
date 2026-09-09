# Remaining work after this development cycle

The application backlog is implemented and verified as recorded in
[the completion audit](COMPLETION_AUDIT.md): live conversation and scoped goals,
aggregate token admission, explicit write reconciliation, modular UI with human
edit activity, and a frozen Luna High comparison with an inconclusive verdict.

The final user-operated step is [phone setup](PHONE_SETUP.md): install/sign in to
Tailscale on both devices, start the prepared HTTPS path, then verify pairing,
approval, installation, offline/reconnect and revocation on the actual phone.
Automated local TLS tests cannot stand in for that device evidence.

Hard token ceilings for the CLI provider and model efficacy are not certified.
Strict token mode refuses that provider. These are explicit capability/evidence
limits, not hidden claims of completion.

Only after this milestone should MCP transport, resumable child runs, scheduling,
services, broader skills/plugins or native apps be considered. They were excluded
from the original product scaffold; no invisible installer or decorative swarm.
