using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed partial class ResearchCoordinatorTests
{
    [Fact] public async Task GuidanceUsesOwnedWorkerAndReusesRecordedReceiptAfterTheWorkerStops()
    {
        var run = await Submit(); await coordinator.Tick(default);
        var request = new ExecutionGuidance(Guid.NewGuid().ToString("N"), "Keep the existing scope and explain it to beginners.");
        var result = await coordinator.Steer(run.Id, request, default);
        Assert.Equal("native-1", result.Execution!.RuntimeRunId);
        Assert.Equal("native-guidance", result.ExecutionCommands.Last().Observation!.RuntimeRunId);
        Assert.True(grants.Authenticate(run.Id, worker.Grant));
        await Ask(run.Id); await coordinator.Tick(default);
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, worker); await coordinator.Initialize();
        var replay = await coordinator.Steer(run.Id, request, default);
        Assert.Equal("awaiting-input", replay.Research!.Phase);
        Assert.Single(worker.Calls, call => call == "steer"); Assert.Equal(1, worker.Opens);
        Assert.Single(store.Chats(), message => message.Content == request.Message);
    }

    [Fact] public async Task UnknownGuidanceClosesTheGrantAndCannotBeReplayedOrChanged()
    {
        var run = await Submit(); await coordinator.Tick(default); var grant = worker.Grant;
        var request = new ExecutionGuidance(Guid.NewGuid().ToString("N"), "Use a brief summary.");
        worker.OnSteer = _ => throw new IOException("Sensitive worker failure");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Steer(run.Id, request, default));
        Assert.DoesNotContain("Sensitive", error.Message); Assert.False(grants.Authenticate(run.Id, grant));
        Assert.Equal("attention", store.Get(run.Id)!.Research!.Phase);
        await coordinator.Initialize();
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Steer(run.Id, request, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Steer(run.Id, request with { Message = "Another message" }, default));
        Assert.Single(worker.Calls, call => call == "steer"); Assert.Equal(1, worker.Opens);
    }

    [Fact] public async Task CancellationInterruptsGuidanceAndPreservesTheUserStop()
    {
        var run = await Submit(); await coordinator.Tick(default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.OnSteer = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); throw new Exception("Unreachable"); };
        var guidance = coordinator.Steer(run.Id, new(Guid.NewGuid().ToString("N"), "A fictional adjustment."), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await coordinator.Cancel(run.Id).WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => guidance);
        var saved = store.Get(run.Id)!; Assert.Equal(RunState.Cancelled, saved.State);
        Assert.False(grants.Authenticate(run.Id, worker.Grant)); Assert.Equal("cleanup", saved.Research!.Phase);
        Assert.Single(worker.Calls, call => call == "steer");
    }

    [Fact] public async Task InvalidGuidanceDoesNotCloseAHealthyWorkerOrCreateAnIntent()
    {
        var run = await Submit(); await coordinator.Tick(default);
        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.Steer(run.Id, new("invalid", "Message"), default));
        Assert.Equal("working", store.Get(run.Id)!.Research!.Phase); Assert.True(grants.Authenticate(run.Id, worker.Grant));
        Assert.DoesNotContain(worker.Calls, call => call == "steer");
    }
}
