using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ResearchCoordinatorTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-research-" + Guid.NewGuid().ToString("N"));
    private readonly Store store;
    private readonly Runtime runtime;
    private readonly WorkerAuthorization grants;
    private readonly Fixture worker;
    private ResearchCoordinator coordinator;
    private static readonly ProviderSnapshot Provider = new("compatible", "fixture-model", "high", "http://127.0.0.1:5181/v1");
    private const string Content = "# Research\nSource: notes/source.md\nA fictional source.\nUnresolved: research quality needs human review.";
    public ResearchCoordinatorTests()
    {
        store = new(root); store.Write("notes/source.md", "A fictional source.", "absent");
        runtime = new(store, _ => throw new Exception("The host must never run a second model loop."), new PlanValidator(), new EvidencePolicy());
        grants = new(store); worker = new(store);
        coordinator = new(store, runtime, grants, worker);
    }
    private Task<Run> Submit() => coordinator.Submit(new("Research a fictional source", ["notes/source.md"]), Provider, default);
    [Theory] [InlineData("queued")] [InlineData("question")] [InlineData("resume-queued")]
    public async Task ForgottenMemoryCannotStartOrReopenAWorker(string point)
    {
        var source = store.Page("notes/source.md")!;
        var entry = store.Remember(Guid.NewGuid().ToString("N"), new("This is fictional source material.", new(source.Path, source.Version, source.Content), "absent"));
        worker.OnStart = async id => { await Ask(id); };
        var run = await coordinator.Submit(new("Research with one selected memory", [], Memories: [new(entry.Id, entry.Version)]), Provider, default);
        Assert.Equal(PolicyProfile.NativeEvidence, run.Profile); Assert.Single(run.PreparedContext!.Memories!);
        if (point != "queued") { await coordinator.Tick(default); await coordinator.Tick(default); }
        if (point == "resume-queued") await coordinator.Answer(run.Id, "question-1", "Beginners", default);
        store.ForgetMemory(entry.Id, entry.Version);
        if (point == "question")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Answer(run.Id, "question-1", "Beginners", default));
            Assert.Null(store.Get(run.Id)!.Question!.Answer);
        }
        else await coordinator.Tick(default);
        Assert.DoesNotContain("wake", worker.Calls);
        if (point == "queued") Assert.Empty(worker.Calls);
        Assert.Equal(0, store.Get(run.Id)!.ModelCalls);
    }
    private Task<CapabilityResult> Call(string id, string operation, string name, object arguments) =>
        runtime.Call(id, new(operation, name, JsonSerializer.SerializeToElement(arguments)), default);
    private Task<CapabilityResult> Ask(string id) => Call(id, "question-1", "thaddeus_ask_user", new { question = "Which audience?", choices = new[] { "Beginners" } });
    private async Task Propose(string id)
    {
        Assert.False((await Call(id, "read-1", "thaddeus_read_note", new { path = "notes/source.md" })).IsError);
        Assert.False((await Call(id, "import-1", "thaddeus_propose_import", new { path = "plans/research.md", content = Content, artifact = "research.md",
            citations = new[] { new { source = "notes/source.md", version = store.Version("notes/source.md"), quote = "A fictional source." } } })).IsError);
    }

    [Fact] public async Task UnqualifiedFactoryCannotCreateRunOrOpenWorker()
    {
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, new UnavailableResearchFactory());
        await Assert.ThrowsAsync<InvalidOperationException>(Submit);
        Assert.Empty(store.List()); Assert.Empty(store.Chats()); Assert.Equal(0, worker.Opens);
    }

    [Fact] public async Task DurableQuestionStopsWorkerBeforeAnswerAndContinuationKeepsFrozenContext()
    {
        worker.OnStart = async id => { await Ask(id); };
        var run = await Submit(); var frozen = run.PreparedContext!;
        await Assert.ThrowsAsync<InvalidOperationException>(Submit);
        await coordinator.Tick(default);
        Assert.Equal("working", store.Get(run.Id)!.Research!.Phase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Answer(run.Id, "question-1", "Beginners", default));
        await coordinator.Tick(default);
        Assert.Equal("awaiting-input", store.Get(run.Id)!.Research!.Phase);
        Assert.Equal(new[] { "prepare", "start", "quiesce", "stop" }, worker.Calls);
        var oldGrant = worker.Grant; Assert.False(grants.Authenticate(run.Id, oldGrant));
        await coordinator.Answer(run.Id, "question-1", "Beginners", default);
        Assert.Equal("resume-queued", store.Get(run.Id)!.Research!.Phase);
        await coordinator.Tick(default);
        Assert.Equal(new[] { "prepare", "start", "quiesce", "stop", "wake", "resume" }, worker.Calls);
        Assert.NotEqual(oldGrant, worker.Grant); Assert.False(grants.Authenticate(run.Id, oldGrant));
        Assert.Equal(frozen.ContentHash, store.Get(run.Id)!.PreparedContext!.ContentHash);
        Assert.Equal(Provider, store.Get(run.Id)!.Goal.Provider);
        Assert.Equal(RunState.Running, store.Get(run.Id)!.State); Assert.Null(store.Get(run.Id)!.Validation);
    }

    [Fact] public async Task ExactArtifactReadbackAndStoppedWorkerRequiredForImportThenRetirementAllowsNextTask()
    {
        worker.OnStart = Propose;
        var run = await Submit(); await coordinator.Tick(default);
        var approval = store.Get(run.Id)!.Approval!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Decide(run.Id, approval.Id, approval.Digest, true, default));
        Assert.Equal("absent", store.Version("plans/research.md"));
        await coordinator.Tick(default);
        Assert.Equal(new[] { "prepare", "start", "quiesce", "readback", "stop" }, worker.Calls);
        Assert.Equal(Wire.Hash(Content), store.Get(run.Id)!.Research!.Review!.Sha256);
        await coordinator.Decide(run.Id, approval.Id, approval.Digest, true, default);
        Assert.Equal(Content, store.Page("plans/research.md")!.Content);
        Assert.Contains(store.Get(run.Id)!.Goal.Criteria, criterion => criterion.Status == "unverified");
        await coordinator.Tick(default);
        Assert.Equal("finished", store.Get(run.Id)!.Research!.Phase); Assert.True(coordinator.HasRetainedWork);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DeletePersonalData(default));
        Assert.NotNull(store.Get(run.Id)); Assert.Equal(1, worker.Retirements);
        Assert.NotEqual(run.Id, (await Submit()).Id);
    }

    [Theory] [InlineData("artifact")] [InlineData("quiesce")] [InlineData("stop")] [InlineData("json")]
    public async Task UncertainReviewCannotImportOrAutomaticallyReplay(string failure)
    {
        worker.OnStart = Propose; worker.Failure = failure;
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        var saved = store.Get(run.Id)!;
        Assert.Equal("attention", saved.Research!.Phase); Assert.NotNull(saved.Research.FailureCode);
        Assert.Null(saved.Research.Review); Assert.False(grants.Authenticate(run.Id, worker.Grant));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Decide(run.Id, saved.Approval!.Id, saved.Approval.Digest, true, default));
        await coordinator.Tick(default); Assert.Equal(1, worker.Calls.Count(call => call == "start"));
        Assert.Equal("absent", store.Version("plans/research.md"));
    }

    [Fact] public async Task CleanupFailureIsDurableAndBlocksAnotherWorker()
    {
        worker.OnStart = Propose; worker.Failure = "artifact"; worker.FailDispose = true;
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        Assert.Equal("cleanup-attention", store.Get(run.Id)!.Research!.Phase);
        Assert.EndsWith("cleanup-unconfirmed", store.Get(run.Id)!.Research!.FailureCode);
        await Assert.ThrowsAsync<InvalidOperationException>(Submit);
        worker.FailDispose = false;
    }

    [Fact] public async Task RestartBeforeFirstDispatchRequiresExplicitResume()
    {
        var run = await Submit(); runtime.Recover(); await coordinator.Initialize(); await coordinator.Tick(default);
        Assert.Equal("paused", store.Get(run.Id)!.Research!.Phase); Assert.Equal(0, worker.Opens);
        await coordinator.Resume(run.Id, default); await coordinator.Tick(default);
        Assert.Equal(1, worker.Opens); Assert.Equal(RunState.Running, store.Get(run.Id)!.State);
    }

    [Fact] public async Task UnknownNativeAcknowledgementIsNeverReplayedAfterRestart()
    {
        worker.OnStart = async id => { await Ask(id); throw new IOException("Lost native acknowledgement"); };
        var run = await Submit(); await coordinator.Tick(default);
        runtime.Recover(); await coordinator.Initialize(); await coordinator.Tick(default);
        Assert.Equal("attention", store.Get(run.Id)!.Research!.Phase);
        Assert.Single(store.Get(run.Id)!.ExecutionCommands); Assert.Equal(1, worker.Calls.Count(call => call == "start"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Resume(run.Id, default));
    }

    [Fact] public async Task RestartAtSavedQuestionReconcilesThenContinuesOnlyAfterAnswer()
    {
        worker.OnStart = async id => { await Ask(id); };
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        await coordinator.DisposeAsync(); coordinator = new(store, runtime, grants, worker);
        runtime.Recover(); await coordinator.Initialize(); await coordinator.Tick(default);
        Assert.Equal(1, worker.Opens);
        await coordinator.Answer(run.Id, "question-1", "Beginners", default); await coordinator.Tick(default);
        Assert.Equal(2, worker.Opens); Assert.Contains("reconcile", worker.Calls);
        Assert.Equal(1, worker.Calls.Count(call => call == "resume"));
    }

    [Fact] public async Task CancellationInterruptsProvisioningWithoutWaitingForControllerLock()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.OnPrepare = async cancellation => { entered.SetResult(); await Task.Delay(Timeout.Infinite, cancellation); };
        var run = await Submit(); var tick = coordinator.Tick(default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await coordinator.Cancel(run.Id).WaitAsync(TimeSpan.FromSeconds(3)); await tick;
        await coordinator.Tick(default);
        Assert.Equal("finished", store.Get(run.Id)!.Research!.Phase);
        Assert.Equal(RunState.Cancelled, store.Get(run.Id)!.State); Assert.DoesNotContain("start", worker.Calls);
        Assert.False(grants.Authenticate(run.Id, worker.Grant)); Assert.Equal("absent", store.Version("plans/research.md"));
    }

    [Fact] public async Task CancelBeforeProvisioningLeavesNoWorkspaceAndPermitsDataDeletion()
    {
        var run = await Submit(); await coordinator.Cancel(run.Id); await coordinator.Tick(default);
        Assert.Equal(0, worker.Opens); Assert.False(coordinator.HasRetainedWork);
        await coordinator.DeletePersonalData(default); Assert.Empty(store.List());
    }

    [Fact] public async Task RestartBetweenAnswerCommitAndQueueKeepsAnswerAndRequiresExplicitContinuation()
    {
        worker.OnStart = async id => { await Ask(id); };
        var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        await runtime.AnswerQuestion(run.Id, "question-1", "Beginners", default);
        runtime.Recover(); await coordinator.Initialize(); await coordinator.Tick(default);
        Assert.Equal("paused", store.Get(run.Id)!.Research!.Phase); Assert.DoesNotContain("resume", worker.Calls);
        await coordinator.Resume(run.Id, default); await coordinator.Tick(default);
        Assert.Equal(1, worker.Calls.Count(call => call == "resume"));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task RestartAfterDecisionCommitRetiresWithoutRepeatingTheDecision(bool allow)
    {
        worker.OnStart = Propose; var run = await Submit(); await coordinator.Tick(default); await coordinator.Tick(default);
        var approval = store.Get(run.Id)!.Approval!;
        await runtime.Decide(run.Id, approval.Id, approval.Digest, allow);
        runtime.Recover(); await coordinator.Initialize(); await coordinator.Tick(default);
        Assert.Equal("finished", store.Get(run.Id)!.Research!.Phase);
        Assert.Equal(allow ? RunState.Succeeded : RunState.Denied, store.Get(run.Id)!.State);
        Assert.Equal(allow ? 1 : 0, store.Revisions("plans/research.md").Count);
        Assert.Equal(1, worker.Retirements);
    }

    private sealed class Fixture(Store store) : IResearchWorkerFactory, IResearchWorker, IExecutionBackend
    {
        public ResearchAvailability Availability => new(true, "controlled-fixture", "test", "No real worker or model is used.");
        public IExecutionBackend Execution => this;
        public readonly List<string> Calls = [];
        public int Opens, Retirements;
        public string? Grant, Failure;
        public bool FailDispose;
        private string id = "";
        public Func<string, Task> OnStart = _ => Task.CompletedTask;
        public Func<CancellationToken, Task> OnPrepare = _ => Task.CompletedTask;
        public IResearchWorker Open(Run run) { Opens++; id = run.Id; return this; }
        public async Task Prepare(Run run, string grant, CancellationToken cancellation)
        {
            Assert.Equal("provisioning", store.Get(id)!.Research!.Phase); Assert.True(store.Get(id)!.Research!.WorkerRetained);
            Calls.Add("prepare"); Grant = grant; await OnPrepare(cancellation);
        }
        public Task Wake(Run run, string grant, CancellationToken cancellation)
        { Assert.Equal("resuming", store.Get(id)!.Research!.Phase); Calls.Add("wake"); Grant = grant; return Task.CompletedTask; }
        public Task Reconcile(Run run, CancellationToken cancellation) { Calls.Add("reconcile"); return Task.CompletedTask; }
        public Task Retire(Run run, CancellationToken cancellation) { Calls.Add("retire"); Retirements++; return Task.CompletedTask; }
        public Task Stop(Run run, CancellationToken cancellation)
        { Calls.Add("stop"); if (Failure == "stop") throw new IOException("Shutdown unconfirmed"); return Task.CompletedTask; }
        public Task<SandboxText> ReadArtifact(Run run, string path, CancellationToken cancellation)
        {
            Calls.Add("readback"); if (Failure == "json") throw new JsonException("Malformed worker response");
            var content = Failure == "artifact" ? "Different artifact" : Content;
            return Task.FromResult(new SandboxText(path, content, Wire.Hash(content)));
        }
        private static ExecutionObservation Ack(string id) => new("accepted", id, JsonSerializer.SerializeToElement(new { runId = id }));
        public async Task<ExecutionObservation> Start(ExecutionStart request, CancellationToken cancellation)
        { Calls.Add("start"); Assert.Equal("outcome-unknown", store.Get(id)!.ExecutionCommands.Last().Status); await OnStart(id); return Ack("native-1"); }
        public Task<ExecutionObservation> Resume(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation)
        { Calls.Add("resume"); Assert.Contains("Beginners", message); return Task.FromResult(Ack("native-2")); }
        public Task<ExecutionObservation> Cancel(ExecutionIdentity identity, CancellationToken cancellation)
        { Calls.Add("quiesce"); if (Failure == "quiesce") throw new IOException("Stop unconfirmed"); return Task.FromResult(new ExecutionObservation("no-active-run", null, JsonSerializer.SerializeToElement(new { ok = true }))); }
        public Task<ExecutionObservation> Inspect(ExecutionIdentity identity, CancellationToken cancellation) => Task.FromResult(Ack("native-1"));
        public Task<ExecutionObservation> Steer(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { if (FailDispose) throw new IOException("Cleanup unconfirmed"); return ValueTask.CompletedTask; }
    }
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        worker.FailDispose = false; await coordinator.DisposeAsync(); store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true);
    }
}
