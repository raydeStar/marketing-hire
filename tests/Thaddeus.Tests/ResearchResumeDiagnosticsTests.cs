using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed partial class ResearchCoordinatorTests
{
    [Theory]
    [InlineData(1, "boot", false)]
    [InlineData(2, "refresh-grant", false)]
    [InlineData(3, "gateway-start", false)]
    [InlineData(4, "gateway-health", false)]
    [InlineData(1, "boot", true)]
    [InlineData(2, "refresh-grant", true)]
    [InlineData(3, "gateway-start", true)]
    [InlineData(4, "gateway-health", true)]
    public async Task ResumeFailureRetainsItsExactStepWithoutLeakingOutputOrDispatchingAgain(int failedCommand, string step, bool interrupted)
    {
        worker.OnStart = async id => { await Ask(id); };
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        var transport = new ResumeCommandFailure(failedCommand, interrupted);
        worker.OnWake = (saved, grant, cancellation) =>
        {
            transport.Grant = grant;
            var native = new OpenClawResearchWorker(transport, new(saved.Execution!.SandboxId, "unused-test-image"),
                "http://127.0.0.1:5182", _ => Task.CompletedTask, _ => Task.CompletedTask);
            return native.Wake(saved, grant, cancellation);
        };
        await coordinator.Answer(run.Id, "question-1", "Beginners", default);
        await coordinator.Tick(default);
        var failed = store.Get(run.Id)!;
        Assert.Equal(RunState.NeedsAttention, failed.State);
        Assert.Equal("attention", failed.Research!.Phase);
        Assert.Equal("resuming:" + step + ":" + (interrupted ? "interrupted" : "IOException"), failed.Research.FailureCode);
        Assert.Equal(failedCommand, transport.Calls);
        Assert.False(grants.Authenticate(run.Id, transport.Grant));
        Assert.DoesNotContain("resume", worker.Calls);
        Assert.DoesNotContain(ResumeCommandFailure.PrivateText, Wire.Pack(failed));
        Assert.DoesNotContain(transport.Grant!, Wire.Pack(failed));
        Assert.DoesNotContain(ResumeCommandFailure.PrivateText, Wire.Pack(store.AllEvents()));
        await coordinator.Tick(default);
        Assert.Equal(failedCommand, transport.Calls);
        Assert.DoesNotContain("resume", worker.Calls);
        Assert.Equal("absent", store.Version("plans/research.md"));
    }

    private sealed class ResumeCommandFailure(int failedCommand, bool interrupted) : ISandboxBackend
    {
        public const string PrivateText = "Private prompt and credential detail must stay out of exported receipts.";
        public int Calls;
        public string? Grant;
        public Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation)
        {
            Calls++;
            if (Calls == failedCommand)
            {
                if (interrupted) throw new OperationCanceledException(PrivateText);
                if (failedCommand == 4) throw new IOException(PrivateText);
                return Task.FromResult(new SandboxCommandResult(1, PrivateText, PrivateText));
            }
            return Task.FromResult(new SandboxCommandResult(0, Calls switch { 1 => "", 2 => Wire.Hash(Grant!), 3 => "started", _ => "{}" }, ""));
        }
        public Task<SandboxInspection> Inspect(CancellationToken cancellation) => throw new NotSupportedException();
        public Task Create(SandboxSpec spec, CancellationToken cancellation) => throw new NotSupportedException();
        public Task Stop(string id, CancellationToken cancellation) => throw new NotSupportedException();
        public Task Remove(string id, CancellationToken cancellation) => throw new NotSupportedException();
        public Task PutText(string id, string path, string content, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<SandboxText> GetText(string id, string path, CancellationToken cancellation) => throw new NotSupportedException();
    }
}
