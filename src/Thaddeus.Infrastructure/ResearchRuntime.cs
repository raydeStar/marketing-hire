using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    internal async Task<Run> RecordWorkspaceRemoval(string id, WorkspaceRemoval removal)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
            if (removal.Status != "removed" || removal.Verified == null || removal.RunId != id || removal.WorkerId != run.Execution?.SandboxId || run.Research?.Phase != "finished")
                throw new InvalidOperationException("Workspace removal has no matching verified receipt.");
            if (!run.Research.WorkerRetained) return run;
            run.Research = run.Research with { WorkerRetained = false, Message = "Private workspace removed · imported notes and task receipts retained" };
            store.Save(run, "research.workspace.removed", new { removal, run.Research }); return run;
        }
        finally { Gate(id).Release(); }
    }
    internal async Task<Run> CreateResearch(ResearchRequest request, ProviderSnapshot provider, CancellationToken cancellation)
    {
        var profile = researchProfile ?? PolicyProfile.NativeEvidence;
        profile.Validate();
        if (profile.ProposalEvidenceVersion != 1) throw new InvalidOperationException("Managed research requires the current versioned import contract.");
        var memories = request.Memories ?? [];
        if (memories.Length > 8 || memories.Any(memory => memory == null) || memories.Select(memory => memory.Id).Distinct(StringComparer.Ordinal).Count() != memories.Length)
            throw new ArgumentException("Select up to eight distinct memories.");
        if (string.IsNullOrWhiteSpace(request.Objective) || request.Objective.Length > 4000 || request.ReadScope == null || request.ReadScope.Length > 12 ||
            request.ReadScope.Distinct(StringComparer.Ordinal).Count() != request.ReadScope.Length ||
            request.ReadScope.Length == 0 && request.Web == null && memories.Length == 0)
            throw new ArgumentException("Describe the research and select notes, memories or public source hosts.");
        if (provider.Kind != "compatible") throw new ArgumentException("Research needs a configured model provider.");
        CompatibleProvider.Endpoint(provider);
        if (string.IsNullOrWhiteSpace(provider.Model) || provider.Model.Length > 200 || provider.Reasoning is not ("low" or "medium" or "high"))
            throw new ArgumentException("Choose an exact model and reasoning setting.");
        if (request.Web != null) PublicWebNetwork.ValidateScope(request.Web);
        foreach (var path in request.ReadScope) { store.SafePath(path); if (store.Page(path) == null) throw new ArgumentException("A selected source no longer exists."); }
        foreach (var memory in memories) store.Recall(memory);
        var limits = request.Limits ?? new(ModelCalls: 6, ToolCalls: 16, Seconds: 600, MaxTotalTokens: 96000);
        if (limits.ModelCalls is < 1 or > 12 || limits.ToolCalls is < 2 or > 30 || limits.Seconds is < 1 or > 600 ||
            limits.Repairs is < 0 or > 2 || limits.MaxOutputTokens is < 128 or > 16000 || limits.MaxTotalTokens is < 1 or > 1000000)
            throw new ArgumentException("Research budget is outside supported limits.");
        var run = new Run
        {
            Goal = new(request.Objective, request.ReadScope, "plans/", [new("Exact approved import", "deterministic"),
                new("Source accuracy and research quality", "user")], limits, provider, "research", request.Web, memories),
            Profile = profile, Research = new("queued", "Research accepted · preparing the isolated workspace")
        };
        run.Execution = new("openclaw", "thaddeus-" + run.Id, "agent:thaddeus:" + run.Id, OpenClawBackend.PinnedVersion);
        store.Save(run, "research.accepted", new { run.Goal, run.Research }, new(run.Id + "-user", "user", request.Objective, DateTimeOffset.UtcNow));
        try { await PrepareExecutionContext(run.Id, cancellation); }
        catch
        {
            await ChangeResearch(run.Id, "attention", "The selected context could not be prepared. No worker was started.", attention: true);
            throw;
        }
        return store.Get(run.Id)!;
    }

    internal async Task<Run> ChangeResearch(string id, string phase, string message, bool attention = false,
        ArtifactReview? review = null, bool? retained = null, string? failureCode = null)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Task not found.");
            var previous = run.Research ?? throw new InvalidOperationException("This task has no research coordinator.");
            run.Research = new(phase, message, review ?? previous.Review, retained ?? previous.WorkerRetained, failureCode);
            if (phase == "queued" && run.State == RunState.Paused && run.ExecutionCommands.Count == 0) run.State = RunState.Queued;
            if (attention && run.State is not (RunState.Succeeded or RunState.Denied or RunState.Cancelled or RunState.Failed))
            { PauseExecutionClock(run); run.State = RunState.NeedsAttention; run.Summary = message; }
            store.Save(run, "research." + phase, new { run.Research }); return run;
        }
        finally { Gate(id).Release(); }
    }
}
