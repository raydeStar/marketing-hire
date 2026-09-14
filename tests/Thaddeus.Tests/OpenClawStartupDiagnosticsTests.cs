using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class OpenClawStartupDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReadinessCapturesOnePrivateObservationWithoutRestartOrMaskingTheCause(bool observationFails)
    {
        var run = new Run { Goal = new("Fixture", [], "plans/", [], new(), new("compatible", "fixture"), "research") };
        run.Execution = new("openclaw", "thaddeus-" + run.Id, "agent:thaddeus:" + run.Id, OpenClawBackend.PinnedVersion);
        run.PreparedContext = new(1, "profile", "personality", [], "Fixture context", Wire.Hash("Fixture context"), DateTimeOffset.UtcNow);
        var binding = Wire.Pack(new { runId = run.Id, sessionKey = run.Execution.SessionKey,
            runtimeVersion = OpenClawBackend.PinnedVersion, contentHash = run.PreparedContext.ContentHash });
        var sandbox = new Sandbox(binding, observationFails);
        var replies = new List<int>(); var diagnostics = new List<SandboxCommandResult>();
        await using var worker = new OpenClawResearchWorker(sandbox, new(run.Execution.SandboxId, "fixture-image"),
            "http://127.0.0.1:5182", _ => Task.CompletedTask, _ => Task.CompletedTask,
            (attempt, _) => replies.Add(attempt), diagnostics.Add);
        var failure = await Assert.ThrowsAsync<WorkerControlException>(() => worker.Prepare(run, "fixture-grant", default));
        Assert.Equal("gateway-health:IOException", failure.FailureCode);
        Assert.DoesNotContain("private-startup-detail", failure.Message);
        Assert.Equal(1, sandbox.Starts); Assert.Equal(10, sandbox.HealthChecks); Assert.Equal(1, sandbox.Observations);
        Assert.Equal(Enumerable.Range(1, 10), replies);
        Assert.Equal(observationFails ? 0 : 1, diagnostics.Count);
        if (!observationFails) Assert.Equal("private-startup-detail", diagnostics[0].Output);
    }

    private sealed class Sandbox(string binding, bool observationFails) : ISandboxBackend
    {
        public int Starts, HealthChecks, Observations;
        public Task Create(SandboxSpec spec, CancellationToken cancellation) => Task.CompletedTask;
        public Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation)
        {
            if (command[0] == "node") return Task.FromResult(new SandboxCommandResult(0, binding, ""));
            if (command.SequenceEqual(GatewayStartupDiagnostics.Command))
            {
                Observations++;
                if (observationFails) throw new IOException("private-startup-detail");
                return Task.FromResult(new SandboxCommandResult(0, "private-startup-detail", ""));
            }
            if (command[0] == "python3") { Starts++; return Task.FromResult(new SandboxCommandResult(0, "started", "")); }
            Assert.Equal(new[] { "openclaw", "gateway", "health", "--json", "--timeout", "2000" }, command);
            HealthChecks++; return Task.FromResult(new SandboxCommandResult(1, "connection refused", ""));
        }
        public Task<SandboxInspection> Inspect(CancellationToken cancellation) => throw new NotSupportedException();
        public Task Stop(string id, CancellationToken cancellation) => throw new NotSupportedException();
        public Task Remove(string id, CancellationToken cancellation) => throw new NotSupportedException();
        public Task PutText(string id, string path, string content, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<SandboxText> GetText(string id, string path, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
