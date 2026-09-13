using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class SearchWorkerCompatibilityTests
{
    [Theory]
    [InlineData(null, true, false)]
    [InlineData("[]", true, false)]
    [InlineData("[\"thaddeus_fetch_public_page\"]", true, false)]
    [InlineData("\"thaddeus_search_public_web\"", true, false)]
    [InlineData("[\"thaddeus_search_public_web\"]", true, true)]
    [InlineData(null, false, true)]
    public async Task SearchRequiresImageSupportBeforeGatewayStartup(string? capabilities, bool search, bool accepted)
    {
        var run = new Run { Goal = new("Fixture", [], "plans/", [], new(), new("compatible", "fixture"), "research") };
        if (search) run.Goal = run.Goal with { Web = new([], 4, new("brave", new string('a', 32))) };
        run.Execution = new("openclaw", "thaddeus-" + run.Id, "agent:thaddeus:" + run.Id, OpenClawBackend.PinnedVersion);
        run.PreparedContext = new(1, "profile", "personality", [], "Fixture context", Wire.Hash("Fixture context"), DateTimeOffset.UtcNow);
        var binding = new Dictionary<string, object?> { ["runId"] = run.Id, ["sessionKey"] = run.Execution.SessionKey,
            ["runtimeVersion"] = OpenClawBackend.PinnedVersion, ["contentHash"] = run.PreparedContext.ContentHash };
        if (capabilities != null) binding["capabilities"] = JsonDocument.Parse(capabilities).RootElement;
        var sandbox = new Sandbox(Wire.Pack(binding));
        await using var worker = new OpenClawResearchWorker(sandbox, new(run.Execution.SandboxId, "fixture-image"),
            "http://127.0.0.1:5182", _ => Task.CompletedTask, _ => Task.CompletedTask);
        if (accepted) await worker.Prepare(run, "fixture-grant", default);
        else Assert.Contains("does not support public search", (await Assert.ThrowsAsync<IOException>(() => worker.Prepare(run, "fixture-grant", default))).Message);
        Assert.Equal(1, sandbox.Creates); Assert.Equal(accepted ? 1 : 0, sandbox.Starts); Assert.Equal(accepted ? 1 : 0, sandbox.HealthChecks);
    }
    private sealed class Sandbox(string binding) : ISandboxBackend
    {
        public int Creates, Starts, HealthChecks;
        public Task Create(SandboxSpec spec, CancellationToken cancellation) { Creates++; return Task.CompletedTask; }
        public Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation)
        {
            if (command[0] == "node") return Task.FromResult(new SandboxCommandResult(0, binding, ""));
            if (command[0] == "python3") { Starts++; return Task.FromResult(new SandboxCommandResult(0, "started", "")); }
            Assert.Equal(new[] { "openclaw", "gateway", "health", "--json", "--timeout", "2000" }, command);
            HealthChecks++; return Task.FromResult(new SandboxCommandResult(0, "{}", ""));
        }
        public Task<SandboxInspection> Inspect(CancellationToken cancellation) => throw new NotSupportedException();
        public Task Stop(string id, CancellationToken cancellation) => throw new NotSupportedException();
        public Task Remove(string id, CancellationToken cancellation) => throw new NotSupportedException();
        public Task PutText(string id, string path, string content, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<SandboxText> GetText(string id, string path, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
