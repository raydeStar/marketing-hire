using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ExecutionControlTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-control-" + Guid.NewGuid().ToString("N"));
    private Store store;
    private Runtime runtime;
    public ExecutionControlTests() { store = new(root); runtime = NewRuntime(); }
    private Runtime NewRuntime() => new(store, _ => throw new Exception("The host must not run another model loop."), new PlanValidator(), new EvidencePolicy());
    private async Task<Run> Ready()
    {
        store.Write("notes/source.md", "A fictional source.", "absent");
        var run = CapabilityTests.CreateWorkerRun(store);
        run.State = RunState.Queued;
        run.Execution = run.Execution! with { SessionKey = "agent:thaddeus:" + run.Id };
        store.Save(run, "fixture.queued", new { });
        await runtime.PrepareExecutionContext(run.Id, default);
        return store.Get(run.Id)!;
    }
    private static ExecutionObservation Ack(string id = "native-1") => new("accepted", id, JsonSerializer.SerializeToElement(new { runId = id }));
    private static ExecutionObservation Idle() => new("no-active-run", null, JsonSerializer.SerializeToElement(new { ok = true }));
    private Task<CapabilityResult> Ask(Run run) => runtime.Call(run.Id, new("question-1", "thaddeus_ask_user",
        JsonSerializer.SerializeToElement(new { question = "Which audience?", choices = new[] { "Developers" } })), default);

    [Fact] public async Task IntentPrecedesNativeDispatchAndAcknowledgementDoesNotOverwriteConcurrentQuestion()
    {
        var run = await Ready();
        var native = new NativeFixture(async _ =>
        {
            Assert.Equal("outcome-unknown", store.Get(run.Id)!.ExecutionCommands.Single().Status);
            await Ask(run); // This would deadlock if the RPC held the broker's lock.
            return Ack();
        });
        await runtime.StartExecution(run.Id, native, default);
        var saved = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingInput, saved.State); Assert.NotNull(saved.Question);
        Assert.Equal("native-1", saved.Execution!.RuntimeRunId);
        Assert.Equal("acknowledged", saved.ExecutionCommands.Single().Status);
        Assert.Null(saved.Validation); Assert.Null(saved.ExecutionDeadlineStart);
        Assert.True(saved.ExecutionActiveSeconds > 0);
    }

    [Fact] public async Task ConcurrentAndPostRestartStartRetriesReturnReceiptWithoutAnotherDispatch()
    {
        var run = await Ready(); var native = new NativeFixture(_ => Task.FromResult(Ack()));
        await Task.WhenAll(runtime.StartExecution(run.Id, native, default), runtime.StartExecution(run.Id, native, default));
        store.Dispose(); store = new(root); runtime = NewRuntime(); runtime.Recover();
        await runtime.StartExecution(run.Id, native, default);
        Assert.Equal(1, native.Starts); Assert.Single(store.Get(run.Id)!.ExecutionCommands);
        Assert.Equal(RunState.NeedsAttention, store.Get(run.Id)!.State);
    }

    [Fact] public async Task LostAcknowledgementCannotBeReplayedAfterRestartEvenWhenQuestionArrived()
    {
        var run = await Ready(); var native = new NativeFixture(async _ => { await Ask(run); throw new IOException("lost acknowledgement"); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartExecution(run.Id, native, default));
        store.Dispose(); store = new(root); runtime = NewRuntime(); runtime.Recover(); runtime.Recover();
        Assert.Equal(RunState.AwaitingInput, store.Get(run.Id)!.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartExecution(run.Id, native, default));
        await runtime.QuiesceExecution(run.Id, native, default);
        await runtime.AnswerQuestion(run.Id, "question-1", "Developers", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ResumeExecution(run.Id, native, default));
        Assert.Equal(1, native.Starts); Assert.Equal(0, native.Resumes);
        Assert.Single(store.AllEvents(), e => e.Type == "execution.recovery.unknown");
    }

    [Fact] public async Task AnswerRequiresNativeStopAndStableResumeIdentityPreventsDuplicateContinuation()
    {
        var run = await Ready(); var native = new NativeFixture(async _ => { await Ask(run); return Ack(); });
        await runtime.StartExecution(run.Id, native, default);
        await runtime.AnswerQuestion(run.Id, "question-1", "Developers", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ResumeExecution(run.Id, native, default));
        await runtime.QuiesceExecution(run.Id, native, default);
        var prior = store.Get(run.Id)!.ExecutionActiveSeconds;
        await runtime.ResumeExecution(run.Id, native, default);
        var operation = native.LastOperation;
        await runtime.ResumeExecution(run.Id, native, default);
        Assert.Equal(1, native.Resumes); Assert.StartsWith("answer-", operation);
        Assert.Contains("Developers", native.LastMessage);
        Assert.Equal(prior, store.Get(run.Id)!.ExecutionActiveSeconds);
        Assert.Equal("native-2", store.Get(run.Id)!.Execution!.RuntimeRunId);
        Assert.Null(store.Get(run.Id)!.Validation);
    }

    [Theory] [InlineData("ok", true)] [InlineData("no-active-run", false)] [InlineData("mystery", true)]
    public async Task UnrecognizedStopCannotAuthorizeContinuation(string status, bool ok)
    {
        var run = await Ready(); var native = new NativeFixture(async _ => { await Ask(run); return Ack(); })
        { StopReply = new(status, null, JsonSerializer.SerializeToElement(new { ok })) };
        await runtime.StartExecution(run.Id, native, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.QuiesceExecution(run.Id, native, default));
        await runtime.AnswerQuestion(run.Id, "question-1", "Developers", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ResumeExecution(run.Id, native, default));
        Assert.Equal(0, native.Resumes);
    }

    [Fact] public async Task UserWaitDoesNotConsumeActiveTimeAndContinuationDoesNotResetItsBudget()
    {
        var run = await Ready(); var native = new NativeFixture(async _ =>
        {
            var current = store.Get(run.Id)!; current.ExecutionDeadlineStart = DateTimeOffset.UtcNow.AddSeconds(-7);
            store.Save(current, "fixture.elapsed", new { }); await Ask(run); return Ack();
        });
        await runtime.StartExecution(run.Id, native, default);
        var saved = store.Get(run.Id)!; Assert.InRange(saved.ExecutionActiveSeconds, 7, 9);
        saved.Created = DateTimeOffset.UtcNow.AddDays(-1); saved.Question = saved.Question! with { Created = saved.Created };
        store.Save(saved, "fixture.waited", new { });
        await runtime.QuiesceExecution(run.Id, native, default);
        await runtime.AnswerQuestion(run.Id, "question-1", "Developers", default);
        saved = store.Get(run.Id)!; saved.ExecutionActiveSeconds = saved.Goal.Limits.Seconds;
        store.Save(saved, "fixture.exhausted", new { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ResumeExecution(run.Id, native, default));
        Assert.Equal(0, native.Resumes);
    }

    [Fact] public async Task SuccessfulWorkerReportDoesNotCreateVerifiedProductCompletion()
    {
        var run = await Ready(); var native = new NativeFixture(_ => Task.FromResult(Ack()));
        await runtime.StartExecution(run.Id, native, default);
        Assert.Equal(RunState.Running, store.Get(run.Id)!.State);
        Assert.Null(store.Get(run.Id)!.Validation); Assert.Null(store.Get(run.Id)!.OutputPath);
    }

    [Fact] public async Task ChangedFrozenProviderCannotReuseAcknowledgedExecutionIdentity()
    {
        var run = await Ready(); var native = new NativeFixture(_ => Task.FromResult(Ack()));
        await runtime.StartExecution(run.Id, native, default);
        var current = store.Get(run.Id)!;
        current.Goal = current.Goal with { Provider = current.Goal.Provider with { Model = "another-model" } };
        store.Save(current, "fixture.changed-provider", new { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartExecution(run.Id, native, default));
        Assert.Equal(1, native.Starts);
    }

    [Fact] public async Task ReadOnlyNativeInspectionRetainsReportWithoutDeclaringSuccessOrReplayingWork()
    {
        var run = await Ready(); var native = new NativeFixture(_ => Task.FromResult(Ack()));
        await runtime.StartExecution(run.Id, native, default);
        runtime.Recover();
        await runtime.InspectExecution(run.Id, native, default);
        Assert.Equal(RunState.NeedsAttention, store.Get(run.Id)!.State);
        Assert.Null(store.Get(run.Id)!.Validation); Assert.Equal(1, native.Starts); Assert.Equal(0, native.Resumes);
        Assert.Single(store.AllEvents(), item => item.Type == "execution.inspected");
    }

    [Fact] public async Task UncertainResumeCannotBeReconciledByInspectingPreviousNativeRun()
    {
        var run = await Ready(); var native = new NativeFixture(async _ => { await Ask(run); return Ack(); });
        await runtime.StartExecution(run.Id, native, default);
        await runtime.QuiesceExecution(run.Id, native, default);
        var current = store.Get(run.Id)!;
        current.ExecutionCommands.Add(new("lost-resume", "resume", "hash", DateTimeOffset.UtcNow));
        store.Save(current, "fixture.lost-resume", new { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.InspectExecution(run.Id, native, default));
        Assert.DoesNotContain(store.AllEvents(), item => item.Type == "execution.inspected");
    }

    private sealed class NativeFixture(Func<ExecutionStart, Task<ExecutionObservation>> start) : IExecutionBackend
    {
        public int Starts, Resumes; public string? LastOperation, LastMessage;
        public ExecutionObservation StopReply = Idle();
        public Task<ExecutionObservation> Start(ExecutionStart request, CancellationToken cancellation) { Starts++; return start(request); }
        public Task<ExecutionObservation> Resume(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation)
        { Resumes++; LastMessage = message; LastOperation = operationId; return Task.FromResult(Ack("native-2")); }
        public Task<ExecutionObservation> Cancel(ExecutionIdentity identity, CancellationToken cancellation) => Task.FromResult(StopReply);
        public Task<ExecutionObservation> Steer(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<ExecutionObservation> Inspect(ExecutionIdentity identity, CancellationToken cancellation) => Task.FromResult(Ack(identity.RuntimeRunId!));
    }
    public void Dispose() { store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
