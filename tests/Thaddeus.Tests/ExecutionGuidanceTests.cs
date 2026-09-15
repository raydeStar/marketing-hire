using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed partial class ExecutionControlTests
{
    private static ExecutionGuidance Guidance(string message = "Use a beginner audience.") => new(Guid.NewGuid().ToString("N"), message);
    private async Task<(Run, NativeFixture)> Guidable()
    {
        var run = await Ready(new("compatible", "fixture-model", "high", "https://model.fixture.invalid/v1"));
        var native = new NativeFixture(_ => Task.FromResult(Ack()));
        await runtime.StartExecution(run.Id, native, default);
        run = store.Get(run.Id)!; run.Research = new("working", "Fixture worker running", WorkerRetained: true);
        store.Save(run, "fixture.working", new { }); return (run, native);
    }

    [Fact] public async Task GuidanceIsDurableBeforeDispatchAndPreservesConcurrentQuestionAndFrozenAllowance()
    {
        var (run, native) = await Guidable(); var request = Guidance();
        native.OnSteer = async () =>
        {
            Assert.Equal(request.Message, store.Get(run.Id)!.ExecutionCommands.Last().Message);
            Assert.Equal("outcome-unknown", store.Get(run.Id)!.ExecutionCommands.Last().Status);
            Assert.Single(store.Chats(), message => message.Content == request.Message);
            await Ask(run); return Ack("guided-turn");
        };
        await runtime.SteerExecution(run.Id, request, native, default);
        var saved = store.Get(run.Id)!;
        Assert.Equal(RunState.AwaitingInput, saved.State); Assert.NotNull(saved.Question);
        Assert.Equal(Wire.Pack(run.Goal), Wire.Pack(saved.Goal)); Assert.Equal(Wire.Pack(run.PreparedContext), Wire.Pack(saved.PreparedContext));
        Assert.Equal("native-1", saved.Execution!.RuntimeRunId); Assert.Null(saved.Validation);
        Assert.Equal("guided-turn", saved.ExecutionCommands.Last().Observation!.RuntimeRunId);
        Assert.Equal("acknowledged", saved.ExecutionCommands.Last().Status);
        Assert.Contains(request.Message, native.LastMessage); Assert.Equal(0, saved.ModelCalls);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task GuidanceRetryAcrossRestartNeverDispatchesTwice(bool loseAcknowledgement)
    {
        var (run, native) = await Guidable(); var request = Guidance();
        if (loseAcknowledgement) native.OnSteer = () => throw new IOException("Acknowledgement lost");
        if (loseAcknowledgement) await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SteerExecution(run.Id, request, native, default));
        else await runtime.SteerExecution(run.Id, request, native, default);
        store.Dispose(); store = new(root); runtime = NewRuntime(); runtime.Recover();
        if (loseAcknowledgement)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SteerExecution(run.Id, request, native, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.InspectExecution(run.Id, native, default));
        }
        else await runtime.SteerExecution(run.Id, request, native, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SteerExecution(run.Id, request with { Message = "Changed identity" }, native, default));
        Assert.Equal(1, native.Steers); Assert.Single(store.Chats(), message => message.Content == request.Message);
    }

    [Theory] [InlineData("paused")] [InlineData("time")] [InlineData("calls")] [InlineData("tokens")] [InlineData("orphan-reservation")]
    public async Task UnavailableOrExhaustedTaskCannotAcceptGuidance(string reason)
    {
        var (run, native) = await Guidable();
        switch (reason)
        {
            case "paused": run.State = RunState.Paused; break;
            case "time": run.ExecutionDeadlineStart = DateTimeOffset.UtcNow.AddSeconds(-run.Goal.Limits.Seconds - 1); break;
            case "calls": run.ModelCalls = run.Goal.Limits.ModelCalls; break;
            case "tokens": run.ChargedTokens = run.Goal.Limits.MaxTotalTokens; break;
            default: run.ReservedTokens = 100; break;
        }
        store.Save(run, "fixture.unavailable", new { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SteerExecution(run.Id, Guidance(), native, default));
        Assert.Equal(0, native.Steers); Assert.Empty(store.Chats());
    }

    private sealed class HeldModel : IInferenceTransport
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Cancellation;
        public async Task<InferenceReply> Send(ProviderSnapshot provider, JsonElement body, CancellationToken cancellation)
        {
            Cancellation = cancellation; Entered.TrySetResult(); await Release.Task;
            return new(JsonSerializer.SerializeToElement(new { id = "held-fixture" }), 10, 5);
        }
    }
    private static JsonElement ModelRequest() => JsonSerializer.SerializeToElement(new { model = "fixture-model", messages = new[] { new { role = "user", content = "A fictional protocol request" } } });

    [Fact] public async Task GuidanceDuringInferenceDoesNotWaitForTheModelOrResetItsReservationAndClock()
    {
        var (run, native) = await Guidable(); var model = new HeldModel();
        var inference = runtime.Infer(run.Id, ModelRequest(), model, new ModelAccessGate(), default);
        await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            await runtime.SteerExecution(run.Id, Guidance(), native, default).WaitAsync(TimeSpan.FromSeconds(3));
            var saved = store.Get(run.Id)!;
            Assert.Equal(run.ExecutionDeadlineStart, saved.ExecutionDeadlineStart);
            Assert.Equal(run.Goal.Limits.MaxTotalTokens, saved.ReservedTokens); Assert.Equal(1, saved.ModelCalls);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Infer(run.Id, ModelRequest(), model, new ModelAccessGate(), default));
        }
        finally { model.Release.TrySetResult(); }
        await inference.WaitAsync(TimeSpan.FromSeconds(3));
        var final = store.Get(run.Id)!;
        Assert.Equal(15, final.ChargedTokens); Assert.Equal(0, final.ReservedTokens); Assert.Equal("completed", final.ModelDispatches.Single().Status);
        Assert.Equal("native-1", final.Execution!.RuntimeRunId); Assert.Equal("acknowledged", final.ExecutionCommands.Last().Status);
        Assert.Equal("native-guidance", final.ExecutionCommands.Last().Observation!.RuntimeRunId);
    }

    [Fact] public async Task CancellationWinsOverLateModelCompletionEvenAfterAnotherInferenceWasRefused()
    {
        var (run, _) = await Guidable(); var model = new HeldModel();
        var inference = runtime.Infer(run.Id, ModelRequest(), model, new ModelAccessGate(), default);
        await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Infer(run.Id, ModelRequest(), model, new ModelAccessGate(), default));
            await runtime.Cancel(run.Id).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(model.Cancellation.IsCancellationRequested); Assert.Equal(RunState.Cancelled, store.Get(run.Id)!.State);
        }
        finally { model.Release.TrySetResult(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => inference);
        var saved = store.Get(run.Id)!; Assert.Equal(RunState.Cancelled, saved.State);
        Assert.Equal(run.Goal.Limits.MaxTotalTokens, saved.ChargedTokens); Assert.Equal(0, saved.ReservedTokens);
        Assert.Equal("outcome-unknown", saved.ModelDispatches.Single().Status);
    }

    [Theory] [InlineData(RunState.Cancelled)] [InlineData(RunState.NeedsAttention)] [InlineData(RunState.AwaitingInput)]
    public async Task RestartSettlesAnOutstandingModelReservationWithoutReopeningStoppedWork(RunState state)
    {
        var (run, _) = await Guidable(); run.State = state; run.ReservedTokens = 120;
        run.ModelDispatches.Add(new("interrupted-model", "hash", DateTimeOffset.UtcNow, "dispatched-outcome-unknown", 120));
        store.Save(run, "fixture.interrupted", new { });
        runtime.Recover(); runtime.Recover();
        var saved = store.Get(run.Id)!; Assert.Equal(state, saved.State); Assert.Equal(120, saved.ChargedTokens);
        Assert.Equal(0, saved.ReservedTokens); Assert.Equal("outcome-unknown", saved.ModelDispatches.Single().Status);
        Assert.Single(store.AllEvents(), item => item.Type == "recovery.model.unknown");
    }
}
