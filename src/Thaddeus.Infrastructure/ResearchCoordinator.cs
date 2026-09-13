using System.Collections.Concurrent;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed class UnavailableResearchFactory : IResearchWorkerFactory
{
    public ResearchAvailability Availability => new(false, "docker-sandboxes", "qualification-required",
        "Isolated research is not ready on this host. Check worker setup; ordinary chat remains available.");
    public IResearchWorker Open(Run run) => throw new InvalidOperationException(Availability.Summary);
}

/// <summary>One durable product controller. OpenClaw remains the only model/tool execution loop.</summary>
public sealed class ResearchCoordinator(Store store, Runtime runtime, WorkerAuthorization authorization, IResearchWorkerFactory factory,
    IResearchWorkspaceStorage? workspaceStorage = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> operations = new();
    private IResearchWorker? worker;
    private string? owned;
    private bool disposed;
    public ResearchAvailability Availability => factory.Availability;
    public bool HasRetainedWork => store.List().Any(run => run.Research?.WorkerRetained == true);

    private void NoActiveResearch()
    {
        if (store.List().Any(run => run.Research is { Phase: not "finished" })) throw new InvalidOperationException("Finish or cancel active research before removing stored workspaces.");
    }
    public async Task<WorkspaceReview> InspectWorkspace(string id, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            NoActiveResearch(); var run = Require(id, "finished");
            return (workspaceStorage ?? throw new InvalidOperationException("Workspace storage maintenance is unavailable on this host.")).Inspect(run);
        }
        finally { gate.Release(); }
    }
    public async Task<Run> RemoveWorkspace(string id, string digest, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            NoActiveResearch(); var run = Require(id, "finished");
            var removal = (workspaceStorage ?? throw new InvalidOperationException("Workspace storage maintenance is unavailable on this host.")).Remove(run, digest, cancellation);
            return await runtime.RecordWorkspaceRemoval(id, removal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Workspace removal could not be confirmed. Inspect the workspace and its receipts before another attempt."); }
        finally { gate.Release(); }
    }

    public async Task DeletePersonalData(CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            if (HasRetainedWork) throw new InvalidOperationException("Private research workspaces are retained. Remove them through worker maintenance before deleting the task records.");
            if (store.List().Any(run => run.State is RunState.Running or RunState.Queued || run.Research is { Phase: not "finished" }))
                throw new InvalidOperationException("Cancel active work before deleting data.");
            store.DeletePersonalData();
        }
        finally { gate.Release(); }
    }

    public async Task Initialize()
    {
        foreach (var run in store.List().Where(run => run.Research != null))
        {
            authorization.Revoke(run.Id);
            if (run.Research is { Phase: "finished", WorkerRetained: true } && workspaceStorage != null)
            {
                // Only reconcile an already verified absence. Startup never deletes a file.
                try { if (workspaceStorage.Inspect(run).Removal is { Status: "removed" } removal) await runtime.RecordWorkspaceRemoval(run.Id, removal); }
                catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException) { }
            }
            if (run.Research!.Phase is "provisioning" or "working" or "quiescing" or "resuming")
                await runtime.ChangeResearch(run.Id, "attention", "Host stopped during worker control. Inspect the recorded outcome before continuing.", attention: true);
            else if (run.Research.Phase is "queued" or "resume-queued")
                await runtime.ChangeResearch(run.Id, "paused", "Host restarted before dispatch. Resume this saved intention explicitly.");
            else if (run.Research.Phase == "awaiting-input" && run.State == RunState.Paused && run.Question?.Answer != null)
                await runtime.ChangeResearch(run.Id, "paused", "Your answer was saved before the host stopped. Resume explicitly to continue.");
            else if (run.Research.Phase == "awaiting-approval" && run.State is RunState.Succeeded or RunState.Denied)
                await runtime.ChangeResearch(run.Id, "cleanup", "Your decision was recorded before the host stopped · retiring the workspace");
            else if (run.Research.Phase == "awaiting-approval" && run.State == RunState.NeedsAttention)
                await runtime.ChangeResearch(run.Id, "attention", "The approved import needs reconciliation before further work.");
        }
    }

    public async Task<Run> Submit(ResearchRequest request, ProviderSnapshot provider, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Availability.Enabled) throw new InvalidOperationException(Availability.Summary);
            if (store.List().Any(run => run.Research is { Phase: not "finished" }))
                throw new InvalidOperationException("Finish or cancel the current research task before starting another.");
            return await runtime.CreateResearch(request, provider, cancellation);
        }
        finally { gate.Release(); }
    }

    public async Task<Run> Answer(string id, string questionId, string answer, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            store.AssertMemoriesCurrent(Require(id, "awaiting-input"));
            await runtime.AnswerQuestion(id, questionId, answer, cancellation);
            return await runtime.ChangeResearch(id, "resume-queued", "Your answer is saved · continuation is queued");
        }
        finally { gate.Release(); }
    }

    public async Task<Run> Resume(string id, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var run = Require(id, "paused");
            store.AssertMemoriesCurrent(run);
            if (!Availability.Enabled) throw new InvalidOperationException(Availability.Summary);
            if (run.ExecutionCommands.Any(command => command.Status != "acknowledged")) throw new InvalidOperationException("An earlier command is unresolved; no replay is allowed.");
            if (run.Question is { Answer: not null }) return await runtime.ChangeResearch(id, "resume-queued", "Saved answer queued for continuation");
            if (run.ExecutionCommands.Count != 0 || run.Research!.WorkerRetained) throw new InvalidOperationException("This task needs inspection before continuation.");
            return await runtime.ChangeResearch(id, "queued", "Saved intention queued for its first dispatch");
        }
        finally { gate.Release(); }
    }

    public async Task<Run> Decide(string id, string approvalId, string digest, bool allow, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var run = Require(id, "awaiting-approval");
            if (allow && (run.Research!.Review is not { } review || review.ApprovalId != approvalId ||
                run.Approval?.Id != approvalId || review.Sha256 != Wire.Hash(run.Approval.Action.Content!)))
                throw new InvalidOperationException("The proposal has no matching artifact readback. Nothing was imported.");
            var decided = await runtime.Decide(id, approvalId, digest, allow);
            await runtime.ChangeResearch(id, decided.State == RunState.NeedsAttention ? "attention" : "cleanup",
                decided.State == RunState.NeedsAttention ? "The import needs reconciliation; no automatic retry." : "Decision recorded · retiring the stopped workspace");
            return store.Get(id)!;
        }
        finally { gate.Release(); }
    }

    public async Task Cancel(string id)
    {
        var previous = store.Get(id) ?? throw new ArgumentException("Task not found.");
        if (previous.Research == null) throw new InvalidOperationException("This is not a managed research task.");
        authorization.Revoke(id);
        if (operations.TryGetValue(id, out var operation)) await Interrupt(operation);
        await runtime.CancelResearch(id);
        await gate.WaitAsync();
        try
        {
            authorization.Revoke(id);
            var run = store.Get(id)!;
            if (run.Research!.Phase == "finished") return;
            if (!run.Research.WorkerRetained) { await runtime.ChangeResearch(id, "finished", "Cancelled before worker creation"); return; }
            await ReleaseWorker();
            await runtime.ChangeResearch(id, "cleanup", "Cancelled · reconciling and retiring the private workspace");
        }
        finally { gate.Release(); }
    }

    public async Task<Run> ReconcileImport(string id, string observedVersion, string mode, CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var run = Require(id, "attention");
            if (run.Research!.Review is not { } review || run.Approval is not { Decision: "approved" } approval ||
                review.ApprovalId != approval.Id || review.Sha256 != Wire.Hash(approval.Action.Content!))
                throw new InvalidOperationException("No previously reviewed and approved import is available to reconcile.");
            await runtime.Reconcile(id, observedVersion, mode);
            return await runtime.ChangeResearch(id, "cleanup", "Import reconciliation recorded · retiring the stopped workspace");
        }
        finally { gate.Release(); }
    }

    private Run Require(string id, string phase)
    {
        var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
        if (run.Research?.Phase != phase) throw new InvalidOperationException("The worker is not ready for that action. Refresh the task receipts.");
        return run;
    }
    private IResearchWorker Open(Run run)
    {
        if (worker != null && owned != run.Id) throw new InvalidOperationException("Another research worker is already owned.");
        if (worker == null) { worker = factory.Open(run); owned = run.Id; }
        return worker;
    }

    public async Task Tick(CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var run = store.List().SingleOrDefault(run => run.Research is { Phase: "queued" or "resume-queued" or "working" or "cleanup" });
            if (run == null) return;
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            operation.CancelAfter(TimeSpan.FromMinutes(2)); operations[run.Id] = operation;
            var token = operation.Token;
            try
            {
                if (run.Research!.Phase is "queued" or "resume-queued")
                {
                    store.AssertMemoriesCurrent(run);
                    if (!Availability.Enabled) throw new InvalidOperationException(Availability.Summary);
                    var resume = run.Research.Phase == "resume-queued";
                    var reconnect = worker == null && resume;
                    await runtime.ChangeResearch(run.Id, resume ? "resuming" : "provisioning", resume ? "Reopening the saved workspace" : "Starting the isolated workspace", retained: true);
                    var current = store.Get(run.Id)!; var environment = Open(current);
                    if (reconnect) await environment.Reconcile(current, token);
                    var grant = authorization.Issue(run.Id, TimeSpan.FromMinutes(15));
                    if (resume)
                    {
                        await environment.Wake(current, grant, token);
                        await runtime.ResumeExecution(run.Id, environment.Execution, token);
                    }
                    else
                    {
                        await environment.Prepare(current, grant, token);
                        await runtime.StartExecution(run.Id, environment.Execution, token);
                    }
                    await runtime.ChangeResearch(run.Id, "working", "OpenClaw is working inside the private workspace");
                }
                else if (run.Research.Phase == "working")
                {
                    var environment = Open(run);
                    if (run.State is RunState.AwaitingInput or RunState.AwaitingApproval)
                    {
                        await runtime.ChangeResearch(run.Id, "quiescing", "Saving the worker checkpoint");
                        await runtime.QuiesceExecution(run.Id, environment.Execution, token);
                        ArtifactReview? review = null;
                        var current = store.Get(run.Id)!;
                        if (current.State == RunState.AwaitingApproval)
                        {
                            var approval = current.Approval!;
                            var proposal = current.Capabilities.Single(call => call.Name == "thaddeus_propose_import" && !call.IsError &&
                                call.Result.GetProperty("approvalId").GetString() == approval.Id);
                            var path = proposal.Result.GetProperty("artifact").GetString()!;
                            var artifact = await environment.ReadArtifact(current, path, token);
                            if (artifact.Path != path || artifact.Content != approval.Action.Content || artifact.Sha256 != Wire.Hash(artifact.Content))
                                throw new IOException("Worker artifact does not match the proposed import.");
                            review = new(approval.Id, path, artifact.Sha256, DateTimeOffset.UtcNow);
                        }
                        await environment.Stop(current, token); authorization.Revoke(run.Id);
                        await runtime.ChangeResearch(run.Id, current.State == RunState.AwaitingInput ? "awaiting-input" : "awaiting-approval",
                            current.State == RunState.AwaitingInput ? "Workspace saved · ready for your answer" : "Artifact readback matched · ready for your decision", review: review);
                    }
                    else if (run.State != RunState.Running || run.ExecutionActiveSeconds +
                        (DateTimeOffset.UtcNow - (run.ExecutionDeadlineStart ?? DateTimeOffset.UtcNow)).TotalSeconds >= run.Goal.Limits.Seconds)
                        throw new InvalidOperationException("Native work stopped or reached its time limit without a verified stopping point.");
                }
                else
                {
                    var reconnect = worker == null; var environment = Open(run);
                    if (reconnect) await environment.Reconcile(run, token);
                    await environment.Retire(run, token); await environment.DisposeAsync(); worker = null; owned = null;
                    authorization.Revoke(run.Id);
                    await runtime.ChangeResearch(run.Id, "finished", "Worker stopped · private workspace retained for inspection");
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                authorization.Revoke(run.Id);
                // Record uncertainty before cleanup: even a failed disposal must leave an honest stopping point.
                var phase = store.Get(run.Id)!.Research!.Phase;
                var failureCode = phase + ":" + (error is OperationCanceledException ? "interrupted" : error.GetType().Name);
                await runtime.ChangeResearch(run.Id, run.Research!.Phase == "cleanup" ? "cleanup-attention" : "attention",
                    "Worker control was not confirmed. Inspect the receipts before retrying; no automatic replay.", attention: true, failureCode: failureCode);
                try { await ReleaseWorker(); }
                catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
                {
                    await runtime.ChangeResearch(run.Id, "cleanup-attention", "Worker cleanup was not confirmed. This host cannot admit another research task yet.",
                        attention: true, failureCode: failureCode + ":cleanup-unconfirmed");
                }
            }
            finally { operations.TryRemove(run.Id, out _); }
        }
        finally { gate.Release(); }
    }

    private async Task ReleaseWorker()
    {
        if (worker != null) await worker.DisposeAsync();
        worker = null; owned = null;
    }

    private static async Task Interrupt(CancellationTokenSource operation)
    {
        try { await operation.CancelAsync(); }
        catch (ObjectDisposedException) { /* The operation won the race and already stopped. A polite exit. */ }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var operation in operations.Values) await Interrupt(operation);
        await gate.WaitAsync();
        try
        {
            if (disposed) return; disposed = true;
            if (owned != null) authorization.Revoke(owned);
            if (worker != null) { await worker.DisposeAsync(); worker = null; }
        }
        finally { gate.Release(); }
    }
}
