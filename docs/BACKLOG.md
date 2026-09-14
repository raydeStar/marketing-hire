# Active development backlog

The accepted next-generation scope is recorded in
[the implementation contract](IMPLEMENTATION_PLAN.md). Its six delivery gates
remain open: real sandbox qualification, OpenClaw/broker integration, selective
v1 reuse, resumable research workflow, independent Lab integration and portable
nontechnical setup. Current implementation and scoped native evidence are tracked
in [development status](DEVELOPMENT_STATUS.md); an open release gate does not mean
its entire implementation is missing.

The current captured-file workflow now has repeated native Lab controls and
independent false-success negatives, in addition to its separate single Luna
pilot. Remaining work includes broader worker/security qualification, native
Mac/Linux distribution and version transitions, signing/installation, broader Lab
quality/resource evidence and final UI acceptance. Actual phone setup remains
deferred. The sections below describe the earlier scaffold only.

Native Linux same-build study handoff now passes its bounded process/API check,
including failed-start recovery and child survival after the parent exits.
This leaves native desktop chooser/browser behavior, different-build transitions
and distribution open; see [Linux study handoff](LINUX_STUDY_HANDOFF.md).

## Previous scaffold milestone

The application backlog is implemented and verified as recorded in
[the completion audit](COMPLETION_AUDIT.md): live conversation and scoped goals,
aggregate token admission, explicit write reconciliation, modular UI with human
edit activity, and a frozen Luna High comparison with an inconclusive verdict.

Phone verification is explicitly deferred for now. The later user-operated step is [phone setup](PHONE_SETUP.md): install/sign in to
Tailscale on both devices, start the prepared HTTPS path, then verify pairing,
approval, installation, offline/reconnect and revocation on the actual phone.
Automated local TLS tests cannot stand in for that device evidence.

Hard token ceilings for the CLI provider and model efficacy are not certified.
Strict token mode refuses that provider. These are explicit capability/evidence
limits, not hidden claims of completion.

MCP, OpenClaw execution and guided host setup are now accepted next-generation
work. Scheduling and subagent expansion remain later capabilities; no decorative
or unexercised implementation counts as delivery.

Completed desktop follow-up: replay follows cursor pages and exports include the full event history beyond 2,000 receipts. Regression checks cover 2,005 receipts and interleaved runs.
