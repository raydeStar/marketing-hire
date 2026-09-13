using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ArtifactImportTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-artifact-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Runtime runtime;
    private readonly WorkerAuthorization grants;
    private readonly Worker worker = new();
    private ResearchCoordinator coordinator;
    private bool failProjection;
    private const string Source = "A fictional workshop lasts 45 minutes.";
    private const string Content = "# Café workshop\r\n\r\n‘A fictional workshop lasts 45 minutes.’\r\nSource: notes/source.md\r\n";
    public ArtifactImportTests()
    {
        store = new(root, stage => { if (failProjection && stage == "after-content-commit") throw new IOException("Injected projection interruption"); });
        store.Write("notes/source.md", Source, "absent");
        runtime = new(store, _ => throw new Exception("No host model loop"), new PlanValidator(), new EvidencePolicy());
        grants = new(store); coordinator = new(store, runtime, grants, worker);
        worker.OnRun = id => Propose(id);
    }
    private Task<Run> Submit(Budget? limits = null) => coordinator.Submit(new("Summarize the workshop", ["notes/source.md"], Limits: limits),
        new("compatible", "fixture", "high", "http://127.0.0.1:5181/v1"), default);
    private object Arguments(string quote = Source) => new { path = "plans/report.md", artifact = "report.md",
        citations = new[] { new EvidenceCitation("notes/source.md", store.Version("notes/source.md"), quote) } };
    private Task<CapabilityResult> Call(string id, string operation, object args) =>
        runtime.Call(id, new(operation, "thaddeus_propose_import", JsonSerializer.SerializeToElement(args, Wire.Json)), default);
    private async Task Propose(string id, string quote = Source)
    {
        var result = await Call(id, "import-" + store.Get(id)!.ArtifactImports.Count, Arguments(quote));
        Assert.False(result.IsError); Assert.Equal("awaiting-artifact-review", result.Value.GetProperty("status").GetString());
        Assert.False(result.Value.TryGetProperty("approvalId", out _));
    }
    [Fact] public async Task DefaultContractCapturesExactUnicodeBytesAndRequiresStoppedWorkerApproval()
    {
        worker.OnStop = async id =>
        {
            var approval = store.Get(id)!.Approval!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(id, approval.Id, approval.Digest, true));
            Assert.Equal("absent", store.Version("plans/report.md"));
        };
        var run = await Submit(); Assert.Equal(PolicyProfile.ArtifactEvidence, run.Profile);
        var tool = runtime.ToolsFor(run.Id).Single(tool => tool.Name == "thaddeus_propose_import");
        Assert.False(tool.InputSchema.GetProperty("properties").TryGetProperty("content", out _));
        await coordinator.Tick(default);
        Assert.Null(store.Get(run.Id)!.Approval); Assert.Equal(RunState.Paused, store.Get(run.Id)!.State);
        var request = Assert.Single(store.Get(run.Id)!.ArtifactImports);
        var replay = await Call(run.Id, request.OperationId, Arguments());
        Assert.Equal(request.Id, replay.Value.GetProperty("importId").GetString());
        Assert.Single(store.Get(run.Id)!.ArtifactImports);
        await coordinator.Tick(default);
        var captured = store.Get(run.Id)!; var approval = captured.Approval!;
        Assert.Equal(new[] { "prepare", "start", "quiesce", "readback", "stop" }, worker.Calls);
        Assert.Equal(Content, approval.Action.Content); Assert.Equal(Wire.Hash(Content), captured.ArtifactImports[0].Sha256);
        Assert.Equal("ready-for-approval", captured.ArtifactImports[0].Status); Assert.Equal("absent", approval.ResourceVersion);
        Assert.False(grants.Authenticate(run.Id, worker.Grant));
        worker.Content = "Later private workspace changes cannot replace the captured content.";
        await coordinator.Decide(run.Id, approval.Id, approval.Digest, true, default); await coordinator.Tick(default);
        Assert.Equal(Content, store.Page("plans/report.md")!.Content); Assert.Single(store.Revisions("plans/report.md"));
        Assert.Equal("finished", store.Get(run.Id)!.Research!.Phase); Assert.Equal(0, captured.ModelCalls);
    }
    [Theory] [InlineData("copy")] [InlineData("path")] [InlineData("artifact")] [InlineData("fields")]
    public async Task InvalidReferenceDoesNotCreateCaptureOrApproval(string defect)
    {
        worker.OnRun = async id =>
        {
            var args = JsonSerializer.SerializeToNode(Arguments(), Wire.Json)!;
            if (defect == "copy") args["content"] = "A second model-authored copy is forbidden";
            if (defect == "path") args["path"] = "notes/overwrite.md";
            if (defect == "artifact") args["artifact"] = "../report.md";
            if (defect == "fields") args["citations"]![0]!["hidden"] = "extra";
            Assert.True((await Call(id, "bad-reference", args)).IsError);
        };
        var run = await Submit(); await coordinator.Tick(default);
        Assert.Empty(store.Get(run.Id)!.ArtifactImports); Assert.Null(store.Get(run.Id)!.Approval);
        Assert.DoesNotContain("readback", worker.Calls);
    }
    [Theory] [InlineData("missing", "unavailable")] [InlineData("json", "unavailable")]
    [InlineData("path", "identity-mismatch")] [InlineData("hash", "identity-mismatch")]
    [InlineData("empty", "invalid-content")] [InlineData("large", "invalid-content")]
    public async Task UnverifiableCaptureStopsAndSurvivesCancellationAndRestart(string failure, string status)
    {
        worker.Failure = failure;
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        var failed = store.Get(run.Id)!; var capture = Assert.Single(failed.ArtifactImports);
        Assert.Equal(status, capture.Status); Assert.Equal(RunState.NeedsAttention, failed.State);
        Assert.Null(failed.Approval); Assert.Empty(failed.NativeProposals); Assert.DoesNotContain("private sensitive detail", Wire.Pack(failed));
        Assert.Equal("attention", failed.Research!.Phase); Assert.False(grants.Authenticate(run.Id, worker.Grant));
        await coordinator.Cancel(run.Id); await coordinator.DisposeAsync();
        coordinator = new(store, runtime, grants, worker); runtime.Recover(); await coordinator.Initialize(); await coordinator.Tick(default);
        Assert.Equal(Wire.Pack(capture), Wire.Pack(Assert.Single(store.Get(run.Id)!.ArtifactImports)));
        Assert.Equal("finished", store.Get(run.Id)!.Research!.Phase); Assert.Equal("absent", store.Version("plans/report.md"));
        Assert.DoesNotContain("wake", worker.Calls);
    }
    [Theory] [InlineData("destination")] [InlineData("source")] [InlineData("writes")]
    public async Task ChangedAuthorityBetweenRequestAndCaptureCannotCreateApproval(string change)
    {
        var run = await Submit(); await coordinator.Tick(default);
        if (change == "destination") store.Write("plans/report.md", "Keep the user's work", "absent");
        if (change == "source") store.Write("notes/source.md", "Changed source", store.Version("notes/source.md"));
        if (change == "writes") store.Setting("writes", "off");
        await coordinator.Tick(default);
        Assert.Null(store.Get(run.Id)!.Approval); Assert.Equal(RunState.NeedsAttention, store.Get(run.Id)!.State);
        Assert.Equal(0, store.Get(run.Id)!.Repairs); Assert.DoesNotContain("wake", worker.Calls);
    }
    [Fact] public async Task BoundedCorrectionUsesNativeContinuationWithRecordedFeedbackAndUnchangedBudgets()
    {
        worker.OnRun = id => Propose(id, worker.Messages.Count == 0 ? "Invented quotation" : Source);
        var run = await Submit(); var originalContext = run.PreparedContext; var originalGoal = run.Goal;
        await coordinator.Tick(default); await coordinator.Tick(default);
        var failed = store.Get(run.Id)!; Assert.Null(failed.Approval); Assert.Equal("resume-queued", failed.Research!.Phase);
        var feedback = Assert.Single(failed.ArtifactImports).Repair!; Assert.Equal("repair-requested", feedback.Status);
        Assert.Equal(Content, Assert.Single(failed.NativeProposals).Content);
        await coordinator.Tick(default); await coordinator.Tick(default);
        var ready = store.Get(run.Id)!;
        Assert.Equal(new[] { "repair-dispatched", "ready-for-approval" }, ready.ArtifactImports.Select(item => item.Status));
        Assert.Equal(1, ready.Repairs); Assert.Equal(2, ready.NativeProposals.Count);
        Assert.Contains(feedback.ProposalHash, Assert.Single(worker.Messages));
        Assert.All(feedback.Problems, problem => Assert.Contains(problem, worker.Messages[0]));
        Assert.Equal(Wire.Pack(originalGoal), Wire.Pack(ready.Goal)); Assert.Equal(originalContext!.ContentHash, ready.PreparedContext!.ContentHash);
        Assert.Single(ready.ExecutionCommands, command => command.Kind == "artifact-repair");
        Assert.Equal("awaiting-approval", ready.Research!.Phase); Assert.Equal(0, ready.ModelCalls);
        await coordinator.Decide(run.Id, ready.Approval!.Id, ready.Approval.Digest, true, default);
        Assert.Equal(Content, store.Page("plans/report.md")!.Content);
    }
    [Theory] [InlineData("repairs")] [InlineData("model")] [InlineData("tokens")] [InlineData("tools")] [InlineData("time")]
    public async Task ExhaustedAllowanceDoesNotQueueCorrection(string bound)
    {
        worker.OnRun = id => Propose(id, "Invented quotation");
        var run = await Submit(); await coordinator.Tick(default);
        var current = store.Get(run.Id)!;
        if (bound == "repairs") current.Repairs = current.Goal.Limits.Repairs;
        if (bound == "model") current.ModelCalls = current.Goal.Limits.ModelCalls;
        if (bound == "tokens") current.ChargedTokens = current.Goal.Limits.MaxTotalTokens;
        if (bound == "tools") current.ToolCalls = current.Goal.Limits.ToolCalls - 1;
        if (bound == "time") current.ExecutionActiveSeconds = current.Goal.Limits.Seconds;
        store.Save(current, "fixture.limit", new { bound });
        await coordinator.Tick(default); await coordinator.Tick(default);
        Assert.Equal("repair-exhausted", store.Get(run.Id)!.ArtifactImports[0].Status);
        Assert.Null(store.Get(run.Id)!.Approval); Assert.DoesNotContain("wake", worker.Calls);
    }
    [Fact] public async Task RepeatedFailedFileCannotObtainAnotherCorrection()
    {
        worker.OnRun = id => Propose(id, "Invented quotation");
        var run = await Submit(); for (var i = 0; i < 4; i++) await coordinator.Tick(default);
        Assert.Equal("repeated-failure", store.Get(run.Id)!.ArtifactImports[^1].Status);
        Assert.Null(store.Get(run.Id)!.Approval); Assert.Single(worker.Messages);
    }
    [Fact] public async Task RestartWithSavedCorrectionRequiresExplicitResume()
    {
        worker.OnRun = id => Propose(id, worker.Messages.Count == 0 ? "Invented quotation" : Source);
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, worker);
        runtime.Recover(); await coordinator.Initialize(); await coordinator.Tick(default);
        Assert.Equal("paused", store.Get(run.Id)!.Research!.Phase); Assert.Empty(worker.Messages);
        // A saved pause does not spend active execution time while the user is away.
        var paused = store.Get(run.Id)!; paused.Created = DateTimeOffset.UtcNow.AddDays(-1); store.Save(paused, "fixture.old-created", new { });
        await coordinator.Resume(run.Id, default); await coordinator.Tick(default); await coordinator.Tick(default);
        Assert.Equal("awaiting-approval", store.Get(run.Id)!.Research!.Phase); Assert.Single(worker.Messages);
    }
    [Fact] public async Task LostCorrectionAcknowledgementCannotBeReplayedAfterRestart()
    {
        worker.OnRun = id => Propose(id, "Invented quotation"); worker.Failure = "resume";
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default); await coordinator.Tick(default);
        Assert.Equal("outcome-unknown", store.Get(run.Id)!.ExecutionCommands[^1].Status);
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, worker); runtime.Recover(); await coordinator.Initialize();
        await coordinator.Tick(default); await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Resume(run.Id, default));
        Assert.Single(worker.Messages); Assert.Null(store.Get(run.Id)!.Approval);
    }
    [Fact] public async Task UnconfirmedWorkerStopKeepsCapturedApprovalUnavailableAfterRestart()
    {
        worker.OnStop = _ => throw new IOException("Stop unconfirmed");
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        var approval = store.Get(run.Id)!.Approval!;
        Assert.Equal(Content, approval.Action.Content); Assert.Null(store.Get(run.Id)!.Research!.Review);
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, worker); runtime.Recover(); await coordinator.Initialize();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Decide(run.Id, approval.Id, approval.Digest, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Decide(run.Id, approval.Id, approval.Digest, true, default));
        Assert.Equal("absent", store.Version("plans/report.md")); Assert.Empty(worker.Messages);
    }
    [Fact] public async Task CancelDuringReadbackCannotCreateAnApprovalOrWakeTheWorker()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.BeforeRead = async token => { reading.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); };
        var run = await Submit(); await coordinator.Tick(default);
        var tick = coordinator.Tick(default); await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.Cancel(run.Id).WaitAsync(TimeSpan.FromSeconds(5)); await tick; await coordinator.Tick(default);
        Assert.Equal(RunState.Cancelled, store.Get(run.Id)!.State); Assert.Equal("finished", store.Get(run.Id)!.Research!.Phase);
        Assert.Null(store.Get(run.Id)!.Approval); Assert.DoesNotContain("wake", worker.Calls);
        Assert.False(grants.Authenticate(run.Id, worker.Grant)); Assert.Equal("absent", store.Version("plans/report.md"));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InterruptedApprovedImportReconcilesOnlyItsIntactCapturedBytes(bool tamperCapture)
    {
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        var approval = store.Get(run.Id)!.Approval!; failProjection = true;
        await coordinator.Decide(run.Id, approval.Id, approval.Digest, true, default); failProjection = false;
        Assert.Equal("attention", store.Get(run.Id)!.Research!.Phase); Assert.Equal("approved", store.Get(run.Id)!.Approval!.Decision);
        Assert.Equal("absent", store.Version("plans/report.md")); Assert.Single(store.Revisions("plans/report.md"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Cancel(run.Id));
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, worker); runtime.Recover(); await coordinator.Initialize();
        if (tamperCapture)
        {
            var damaged = store.Get(run.Id)!; damaged.ArtifactImports[^1] = damaged.ArtifactImports[^1] with { Sha256 = Wire.Hash("different") };
            store.Save(damaged, "fixture.damaged-capture", new { });
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReconcileImport(run.Id, "absent", "complete", default));
            Assert.Equal("absent", store.Version("plans/report.md"));
        }
        else
        {
            await coordinator.ReconcileImport(run.Id, "absent", "complete", default); await coordinator.Tick(default);
            Assert.Equal(Content, store.Page("plans/report.md")!.Content); Assert.Equal("finished", store.Get(run.Id)!.Research!.Phase);
        }
        Assert.Single(store.Revisions("plans/report.md")); Assert.Empty(worker.Messages); Assert.Equal(0, store.Get(run.Id)!.ModelCalls);
    }
    [Theory] [InlineData("artifact")] [InlineData("repair")] [InlineData("tampered")]
    public async Task SavedCaptureSurvivesTheGapBeforeReadyForReview(string checkpoint)
    {
        if (checkpoint == "repair") worker.OnRun = id => Propose(id, "This quotation is absent from the source.");
        worker.OnStop = _ => throw new IOException("Interrupted after durable capture");
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, worker);
        runtime.Recover(); await coordinator.Initialize(); run = store.Get(run.Id)!;
        Assert.Equal("attention", run.Research!.Phase); Assert.Null(run.Research.Review);
        if (checkpoint == "tampered")
        {
            run.ArtifactImports[^1] = run.ArtifactImports[^1] with { Sha256 = Wire.Hash("changed") };
            store.Save(run, "fixture.changed-capture", new { });
        }
        var review = await coordinator.InspectRecovery(run.Id, run.Version, default);
        Assert.Equal(checkpoint != "tampered", review.CanRestore);
        if (checkpoint != "tampered")
        {
            var restored = await coordinator.RestoreCheckpoint(run.Id, review.Digest, default); await coordinator.Tick(default);
            Assert.Equal(checkpoint == "repair" ? RunState.Paused : RunState.AwaitingApproval, restored.State);
            Assert.Equal(Wire.Pack(run.ArtifactImports), Wire.Pack(restored.ArtifactImports));
            if (checkpoint == "artifact") Assert.Equal(Wire.Hash(Content), restored.Research!.Review!.Sha256);
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RestoreCheckpoint(run.Id, review.Digest, default));
        Assert.Equal("absent", store.Version("plans/report.md")); Assert.Empty(worker.Messages);
        Assert.DoesNotContain("wake", worker.Calls); Assert.Equal(0, store.Get(run.Id)!.ModelCalls);
    }
    private sealed class Worker : IResearchWorkerFactory, IResearchWorker, IExecutionBackend
    {
        public ResearchAvailability Availability => new(true, "fixture", "test", "No VM or inference");
        public IExecutionBackend Execution => this;
        public List<string> Calls = [], Messages = [];
        public string Content = ArtifactImportTests.Content;
        public string? Failure, Grant;
        private string id = "";
        public Func<string, Task> OnRun = _ => Task.CompletedTask, OnStop = _ => Task.CompletedTask;
        public Func<CancellationToken, Task> BeforeRead = _ => Task.CompletedTask;
        public IResearchWorker Open(Run run) { id = run.Id; return this; }
        public Task Prepare(Run run, string grant, CancellationToken cancellation) { Calls.Add("prepare"); Grant = grant; return Task.CompletedTask; }
        public Task Wake(Run run, string grant, CancellationToken cancellation) { Calls.Add("wake"); Grant = grant; return Task.CompletedTask; }
        public Task Reconcile(Run run, CancellationToken cancellation) => Task.CompletedTask;
        public Task Retire(Run run, CancellationToken cancellation) { Calls.Add("retire"); return Task.CompletedTask; }
        public async Task Stop(Run run, CancellationToken cancellation) { Calls.Add("stop"); await OnStop(id); }
        public async Task<SandboxText> ReadArtifact(Run run, string path, CancellationToken cancellation)
        {
            Calls.Add("readback"); await BeforeRead(cancellation);
            if (Failure == "missing") throw new IOException("private sensitive detail");
            if (Failure == "json") throw new JsonException("private sensitive detail");
            var content = Failure == "empty" ? " " : Failure == "large" ? new string('é', 50001) : Content;
            return new SandboxText(Failure == "path" ? "other.md" : path, content, Failure == "hash" ? "bad" : Wire.Hash(content));
        }
        private static ExecutionObservation Ack() => new("accepted", Guid.NewGuid().ToString("N"), JsonSerializer.SerializeToElement(new { ok = true }));
        public async Task<ExecutionObservation> Start(ExecutionStart request, CancellationToken cancellation) { Calls.Add("start"); await OnRun(id); return Ack(); }
        public async Task<ExecutionObservation> Resume(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation)
        {
            Calls.Add("resume"); Messages.Add(message); if (Failure == "resume") throw new IOException("Acknowledgement lost");
            await OnRun(id); return Ack();
        }
        public Task<ExecutionObservation> Cancel(ExecutionIdentity identity, CancellationToken cancellation)
        { Calls.Add("quiesce"); return Task.FromResult(new ExecutionObservation("no-active-run", null, JsonSerializer.SerializeToElement(new { ok = true, thaddeusFilesystemCheckpoint = "syncfs" }))); }
        public Task<ExecutionObservation> Inspect(ExecutionIdentity identity, CancellationToken cancellation) => throw new NotSupportedException();
        public Task<ExecutionObservation> Steer(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await coordinator.DisposeAsync(); store.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true);
    }
}
