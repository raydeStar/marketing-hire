# Sandbox qualification

This is an operator/development tool, not the finished end-user onboarding flow.
It uses the production `DockerSandboxBackend`; it does not simulate a worker.
All paths below are private development evidence under the ignored `artifacts/`.
No commands enable product admission automatically.

```powershell
dotnet run --project tools/Thaddeus.WorkerCheck -- inspect artifacts/worker-qualification
dotnet run --project tools/Thaddeus.WorkerCheck -- create artifacts/worker-qualification '<image>@sha256:<digest>'
dotnet run --project tools/Thaddeus.WorkerCheck -- probe artifacts/worker-qualification
dotnet run --project tools/Thaddeus.WorkerCheck -- stop artifacts/worker-qualification
dotnet run --project tools/Thaddeus.WorkerCheck -- remove artifacts/worker-qualification
```

The check directory owns its worker ID. Creation pins the image, asks for two CPUs
and 4 GiB, supplies no workspace path, disables shared skills, and adds the
recursive deny-network rule `**`. Before creating a new worker it checks the CLI,
authentication, inventory and image service. There is no automatic host/container
fallback. Do not point this tool at the running product's data directory.

The probe runs fixed Python before any agent. It records mounts, resource settings,
credential-directory and socket presence, SSH forwarding, GPU-device visibility,
basic outbound probes and the observed OpenClaw version. It checks a bounded text
round trip, stop, automatic restart via exec and artifact persistence. Missing
OpenClaw is explicitly reported for a base-isolation probe. These observations
are evidence to assess, not a complete security verdict. Host-side VM metadata,
actual broker-only egress and native OpenClaw behavior remain separate gates.

Creation failure leaves durable `creation-unknown` ownership. Inspect the process
receipt and native inventory before further action. Only when the entire current
native inventory is empty can this conservative command clear that registration:

```powershell
dotnet run --project tools/Thaddeus.WorkerCheck -- reconcile-absent artifacts/worker-qualification
```

It records the observation and preserves the failed ID; it does not retry creation.
The current Windows failure and evidence are recorded in
[development status](DEVELOPMENT_STATUS.md). Docker's free account is sufficient
for local Sandboxes; a subscription change does not repair its image-service issue.
