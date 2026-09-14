using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed class OpenClawResearchWorker(ISandboxBackend sandbox, SandboxSpec spec, string brokerOrigin,
    Func<CancellationToken, Task> reconcile, Func<CancellationToken, Task> retire,
    Action<int, SandboxCommandResult>? observeGatewayHealth = null,
    Action<SandboxCommandResult>? observeGatewayFailure = null) : IResearchWorker
{
    private readonly OpenClawBackend execution = new(sandbox);
    internal TimeProvider StartupClock { get; init; } = TimeProvider.System;
    public IExecutionBackend Execution => execution;
    private static void Bound(Run run, SandboxSpec spec)
    {
        if (run.Execution?.SandboxId != spec.Id || run.PreparedContext == null || run.Execution.SessionKey != "agent:thaddeus:" + run.Id)
            throw new InvalidOperationException("Worker identity or frozen context is missing.");
    }
    public async Task Prepare(Run run, string grant, CancellationToken cancellation)
    {
        Bound(run, spec);
        await sandbox.Create(spec, cancellation);
        var configured = await sandbox.Execute(spec.Id, ["node", "/opt/thaddeus/bootstrap.mjs"], Wire.Pack(new
        {
            schemaVersion = 1, runId = run.Id, brokerOrigin, model = run.Goal.Provider.Model,
            reasoning = run.Goal.Provider.Reasoning, context = run.PreparedContext, grantToken = grant
        }), cancellation);
        if (configured.ExitCode != 0) throw new IOException("The private worker binding was not confirmed.");
        using var document = JsonDocument.Parse(configured.Output);
        var binding = document.RootElement;
        if (binding.GetProperty("runId").GetString() != run.Id || binding.GetProperty("sessionKey").GetString() != run.Execution!.SessionKey ||
            binding.GetProperty("runtimeVersion").GetString() != OpenClawBackend.PinnedVersion ||
            binding.GetProperty("contentHash").GetString() != run.PreparedContext!.ContentHash)
            throw new IOException("The worker replied with a different execution binding.");
        if (run.Goal.Web?.Search != null && (!binding.TryGetProperty("capabilities", out var capabilities) ||
            capabilities.ValueKind != JsonValueKind.Array || !capabilities.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "thaddeus_search_public_web")))
            throw new IOException("This worker package does not support public search. Update its prepared image before starting search-enabled research.");
        await StartGateway(cancellation);
    }
    public async Task Wake(Run run, string grant, CancellationToken cancellation)
    {
        Bound(run, spec);
        await WorkerControlException.During("boot", async () =>
        {
            var started = await sandbox.Execute(spec.Id, ["true"], null, cancellation);
            if (started.ExitCode != 0) throw new IOException("Saved worker did not start.");
        });
        await WorkerControlException.During("refresh-grant", () => execution.RefreshGrant(run.Execution!, run.PreparedContext!, grant, cancellation));
        await StartGateway(cancellation);
    }
    private async Task StartGateway(CancellationToken cancellation)
    {
        await WorkerControlException.During("gateway-start", async () =>
        {
            var started = await sandbox.Execute(spec.Id, ["python3", "-c", """
            import os, subprocess, stat
            fd = os.open('/home/agent/.openclaw/gateway-console.log', os.O_WRONLY | os.O_CREAT | os.O_APPEND | os.O_NOFOLLOW, 0o600)
            assert stat.S_ISREG(os.fstat(fd).st_mode), 'Invalid gateway log'
            with os.fdopen(fd, 'ab', buffering=0) as log:
                subprocess.Popen(['openclaw','gateway','run'], stdin=subprocess.DEVNULL, stdout=log,
                                 stderr=subprocess.STDOUT, start_new_session=True)
            print('started')
            """], null, cancellation);
            if (started.ExitCode != 0 || started.Output.Trim() != "started") throw new IOException("Gateway startup was not confirmed; no automatic restart.");
        });
        await WorkerControlException.During("gateway-health", async () =>
        {
            var ready = await GatewayReadiness.Wait(async (attempt, token) =>
            {
                var health = await sandbox.Execute(spec.Id, ["openclaw", "gateway", "health", "--json", "--timeout", "2000"], null, token);
                observeGatewayHealth?.Invoke(attempt, health);
                return health;
            }, StartupClock, cancellation);
            if (ready) return;
            if (observeGatewayFailure != null)
            {
                try
                {
                    // Take the witness statement before containment closes the room. No restart or task replay.
                    var diagnostic = await sandbox.Execute(spec.Id, GatewayStartupDiagnostics.Command, null, cancellation);
                    observeGatewayFailure(diagnostic);
                }
                catch (Exception diagnosticError) when (diagnosticError is not OutOfMemoryException)
                {
                    // A failed observation must preserve the original readiness failure and allow containment.
                }
            }
            cancellation.ThrowIfCancellationRequested();
            throw new IOException("Gateway did not become ready within its startup bound.");
        });
    }
    public Task Reconcile(Run run, CancellationToken cancellation) { Bound(run, spec); return reconcile(cancellation); }
    public Task Stop(Run run, CancellationToken cancellation) { Bound(run, spec); return sandbox.Stop(spec.Id, cancellation); }
    public Task Retire(Run run, CancellationToken cancellation) { Bound(run, spec); return retire(cancellation); }
    public Task<SandboxText> ReadArtifact(Run run, string path, CancellationToken cancellation)
    { Bound(run, spec); return sandbox.GetText(spec.Id, path, cancellation); }
    public async ValueTask DisposeAsync() { if (sandbox is IAsyncDisposable disposable) await disposable.DisposeAsync(); }
}

/// <summary>Explicit development composition, never registered by default or selected as a fallback.</summary>
public sealed class QemuResearchFactory(Store store, QemuInstallation installation, int brokerPort, string? linuxSupervisor = null) : IResearchWorkerFactory
{
    private string Backend => NativeWorkerPlatform.Backend == "qemu-whpx" ? "qemu-whpx"
        : NativeWorkerPlatform.Backend == "qemu-kvm" && linuxSupervisor != null ? "qemu-kvm"
        : throw new PlatformNotSupportedException("The research factory requires its supported native process owner.");
    public ResearchAvailability Availability => new(true, Backend, "development-only",
        "Development VM research is enabled for this isolated test host. Production qualification remains open.", true);
    public IResearchWorker Open(Run run)
    {
        _ = Backend;
        var sandbox = new QemuSandboxBackend(store, installation, new(run.Id, brokerPort), linuxSupervisor);
        var spec = new SandboxSpec(run.Execution!.SandboxId, installation.Image);
        // The pinned guest supervisor listens here; the host relay forwards to brokerPort.
        // Those are different computers, even when both addresses say localhost.
        return new OpenClawResearchWorker(sandbox, spec, "http://127.0.0.1:5182",
            async cancellation => { await sandbox.ReconcileStopped(spec.Id, cancellation); },
            cancellation => sandbox.Retire(spec.Id, cancellation),
            (attempt, health) => sandbox.RecordCommandResult("gateway-health", attempt, health),
            sandbox.RecordGatewayStartupFailure);
    }
}
