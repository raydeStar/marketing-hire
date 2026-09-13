using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed partial class ResearchCoordinatorTests
{
    private async Task<Run> InterruptedCheckpoint(bool artifact = false)
    {
        worker.OnStart = artifact ? Propose : async id => { await Ask(id); };
        // A durable native checkpoint exists, but the controller never receives a confirmed Stop response.
        worker.Failure = "stop";
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        worker.Failure = null;
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, worker);
        runtime.Recover(); await coordinator.Initialize();
        return store.Get(run.Id)!;
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RecoveryRestoresOnlyTheSavedQuestionOrExactArtifactReview(bool artifact)
    {
        var before = await InterruptedCheckpoint(artifact);
        Assert.Equal("attention", before.Research!.Phase);
        var calls = worker.Calls.ToArray(); var frozen = Wire.Pack(before.PreparedContext);
        var review = await coordinator.InspectRecovery(before.Id, before.Version, default);
        Assert.True(review.CanRestore); Assert.True(review.WorkerStopped);
        Assert.Equal(artifact ? "captured-artifact" : "saved-question", review.Checkpoint);
        Assert.Equal(calls.Append("reconcile"), worker.Calls);
        Assert.False(grants.Authenticate(before.Id, worker.Grant));
        var restored = await coordinator.RestoreCheckpoint(before.Id, review.Digest, default);
        await coordinator.Tick(default);
        Assert.Equal(artifact ? RunState.AwaitingApproval : RunState.AwaitingInput, restored.State);
        Assert.Equal(artifact ? "awaiting-approval" : "awaiting-input", restored.Research!.Phase);
        Assert.Equal(Wire.Pack(before.Question), Wire.Pack(restored.Question)); Assert.Equal(before.Approval, restored.Approval);
        Assert.Equal(frozen, Wire.Pack(restored.PreparedContext)); Assert.Equal(0, restored.ModelCalls);
        Assert.Equal(Wire.Pack(before.ExecutionCommands), Wire.Pack(restored.ExecutionCommands));
        Assert.Equal("absent", store.Version("plans/research.md")); Assert.Null(restored.Question?.Answer);
        Assert.Equal(calls.Append("reconcile"), worker.Calls); // Restore and the ordinary pump cannot boot or infer.
        Assert.Contains(store.AllEvents(), entry => entry.Type == "research.recovery.restored");
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreCheckpoint(before.Id, review.Digest, default));
        if (artifact)
        {
            Assert.Equal(Wire.Hash(Content), restored.Research.Review!.Sha256);
            await coordinator.Decide(before.Id, restored.Approval!.Id, restored.Approval.Digest, true, default);
            Assert.Equal(Content, store.Page("plans/research.md")!.Content);
        }
        else
        {
            await coordinator.Answer(before.Id, restored.Question!.Id, "Beginners", default);
            await coordinator.Tick(default);
            Assert.Equal(1, worker.Calls.Count(call => call == "resume"));
        }
    }

    [Theory] [InlineData("answer")] [InlineData("repair")]
    public async Task RecoveryOfSavedContinuationRequiresAnotherExplicitResume(string checkpoint)
    {
        var run = await InterruptedCheckpoint();
        if (checkpoint == "answer") run.Question = run.Question! with { Answer = "Beginners" };
        else run.ArtifactImports.Add(new("capture-1", "import-1", "plans/research.md", "research.md", "absent", [], DateTimeOffset.UtcNow,
            Status: "repair-requested", Repair: new("repair-requested", "proposal-1", "hash", ["Unverified quotation"], 1, 1, "Correct the quotation.")));
        store.Save(run, "fixture.saved-continuation", new { checkpoint });
        var review = await coordinator.InspectRecovery(run.Id, run.Version, default);
        Assert.True(review.CanRestore); Assert.Equal("saved-" + checkpoint, review.Checkpoint);
        var restored = await coordinator.RestoreCheckpoint(run.Id, review.Digest, default); await coordinator.Tick(default);
        Assert.Equal(RunState.Paused, restored.State); Assert.Equal("paused", restored.Research!.Phase);
        Assert.DoesNotContain("wake", worker.Calls); Assert.DoesNotContain("resume", worker.Calls);
    }

    [Theory]
    [InlineData("command")] [InlineData("model")] [InlineData("reservation")] [InlineData("checkpoint")]
    [InlineData("checkpoint-type")] [InlineData("context")] [InlineData("session")] [InlineData("missing-capture")]
    [InlineData("expired-approval")] [InlineData("approved")]
    public async Task RecoveryCannotTurnUncertaintyOrMissingEvidenceIntoAReadyTask(string defect)
    {
        var run = await InterruptedCheckpoint(artifact: defect is "missing-capture" or "expired-approval" or "approved");
        switch (defect)
        {
            case "command": run.ExecutionCommands[0] = run.ExecutionCommands[0] with { Status = "interrupted-outcome-unknown" }; break;
            case "model": run.ModelDispatches.Add(new("unknown-model", "hash", DateTimeOffset.UtcNow, "outcome-unknown", 100)); break;
            case "reservation": run.ReservedTokens = 100; break;
            case "checkpoint": run.ExecutionCommands.RemoveAt(run.ExecutionCommands.Count - 1); break;
            case "checkpoint-type": run.ExecutionCommands[^1] = run.ExecutionCommands[^1] with { Observation = new("accepted", null, JsonSerializer.SerializeToElement(new { thaddeusFilesystemCheckpoint = 42 })) }; break;
            case "context": run.PreparedContext = run.PreparedContext! with { ContentHash = "changed" }; break;
            case "session": run.Execution = run.Execution! with { SessionKey = "agent:another:session" }; break;
            case "missing-capture": run.ArtifactChecks.Clear(); break;
            case "expired-approval": run.Approval = run.Approval! with { Expires = DateTimeOffset.UtcNow.AddMinutes(-1) }; break;
            case "approved": run.Approval = run.Approval! with { Decision = "approved" }; break;
        }
        store.Save(run, "fixture.unresolved-evidence", new { defect }); var opens = worker.Opens;
        var review = await coordinator.InspectRecovery(run.Id, run.Version, default);
        Assert.False(review.CanRestore); Assert.False(review.WorkerStopped); Assert.Null(review.Checkpoint);
        Assert.Equal(opens, worker.Opens); Assert.Equal("attention", store.Get(run.Id)!.Research!.Phase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreCheckpoint(run.Id, review.Digest, default));
        Assert.Equal("absent", store.Version("plans/research.md"));
    }

    [Fact] public async Task RecoveryRequiresPhysicalStoppedOwnershipAndDoesNotLeakHostErrors()
    {
        var run = await InterruptedCheckpoint(); worker.Failure = "reconcile";
        var review = await coordinator.InspectRecovery(run.Id, run.Version, default);
        Assert.False(review.CanRestore); Assert.False(review.WorkerStopped);
        Assert.DoesNotContain("Sensitive", review.Summary);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreCheckpoint(run.Id, review.Digest, default));
        Assert.DoesNotContain("wake", worker.Calls);
    }

    [Theory] [InlineData("digest")] [InlineData("version")] [InlineData("expired")]
    public async Task RecoveryReviewMustBeExactCurrentAndUnexpired(string stale)
    {
        var run = await InterruptedCheckpoint();
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.InspectRecovery(run.Id, run.Version - 1, default));
        var review = await coordinator.InspectRecovery(run.Id, run.Version, default);
        if (stale != "digest")
        {
            var changed = store.Get(run.Id)!;
            if (stale == "expired") changed.Research = changed.Research! with { Recovery = review with { Version = changed.Version + 1, Expires = DateTimeOffset.UtcNow.AddSeconds(-1) } };
            store.Save(changed, "fixture.new-receipt", new { stale });
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreCheckpoint(run.Id, stale == "digest" ? "wrong" : review.Digest, default));
        Assert.Equal("attention", store.Get(run.Id)!.Research!.Phase); Assert.DoesNotContain("wake", worker.Calls);
    }

    [Fact] public async Task CancellingDuringRecoveryInspectionCannotBeOverwrittenByItsLateReceipt()
    {
        var run = await InterruptedCheckpoint();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.OnReconcile = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var inspection = coordinator.InspectRecovery(run.Id, run.Version, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await coordinator.Cancel(run.Id).WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inspection);
        Assert.Equal(RunState.Cancelled, store.Get(run.Id)!.State);
        Assert.Null(store.Get(run.Id)!.Research!.Recovery); Assert.DoesNotContain("wake", worker.Calls);
    }

    [Fact] public void ResearchStateFromBeforeRecoveryControlsStillDeserializes()
    {
        var previous = Wire.Unpack<ResearchState>("{\"phase\":\"attention\",\"message\":\"Stopped\",\"workerRetained\":true}");
        Assert.Null(previous.Recovery); Assert.True(previous.WorkerRetained);
    }
}
