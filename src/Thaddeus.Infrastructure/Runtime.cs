using System.Collections.Concurrent;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed class Runtime(Store store, Func<ProviderSnapshot, IModelProvider> providers, IValidator validator, IAgentPolicy policy)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> cancellations = new();
    private SemaphoreSlim Gate(string id) => locks.GetOrAdd(id, _ => new(1, 1));
    public void Recover()
    {
        foreach (var run in store.List())
        {
            if (run.State == RunState.Running)
            {
                run.State = RunState.NeedsAttention;
                run.Summary = "Host stopped during work. Inspect receipts and reconcile any unknown outcome; no automatic retry.";
                store.Save(run, "recovery.unknown", new { run.Summary });
            }
            else if (run.State == RunState.Queued)
            {
                run.State = RunState.Paused; run.Summary = "Host restarted before dispatch. Safe to resume.";
                store.Save(run, "recovery.paused", new { run.Summary });
            }
        }
    }
    public Run Create(Goal goal, bool demoFailure = false, string arm = "evidence", bool validation = true, bool journal = true)
    {
        if (string.IsNullOrWhiteSpace(goal.Objective) || goal.Objective.Length > 4000) throw new ArgumentException("Objective must contain 1–4,000 characters.");
        if (goal.ReadScope.Length is 0 or > 12) throw new ArgumentException("Select between one and twelve source pages.");
        foreach (var path in goal.ReadScope) store.SafePath(path);
        if (goal.WriteScope != "plans/") throw new ArgumentException("Agent writes are limited to plans/.");
        var b = goal.Limits;
        if (b.ModelCalls is < 0 or > 12 || b.ToolCalls is < 0 or > 30 || b.Seconds is < 1 or > 600 || b.Repairs is < 0 or > 2 || b.MaxOutputTokens is < 128 or > 16000) throw new ArgumentException("Budget outside supported limits.");
        var run = new Run { Goal = goal, DemoFailure = demoFailure, Policy = arm, ValidationEnabled = validation, JournalDetail = journal };
        store.Save(run, "goal.created", new { goal, arm, validation, journal, simulated = goal.Provider.Kind == "scripted" });
        return run;
    }
    public async Task Execute(string id)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
            if (run.State is not (RunState.Queued or RunState.Paused)) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(run.Goal.Limits.Seconds));
            cancellations[id] = cts;
            run.State = RunState.Running; run.Summary = "Reading the selected notes";
            store.Save(run, "run.started", new { run.Summary });
            try
            {
                foreach (var path in run.Goal.ReadScope)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (run.Evidence.Any(e => e.Path == path)) continue;
                    ReserveTool(run, new("knowledge.read", path));
                    var result = store.Read(path); run.Evidence.Add(result.Evidence!);
                    store.Save(run, "tool.result", run.JournalDetail ? result : new { result.Name, result.Success, result.Summary });
                }
                var provider = providers(run.Goal.Provider);
                string? failure = null; var seen = new HashSet<string>();
                while (true)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (run.ModelCalls >= run.Goal.Limits.ModelCalls) throw new BudgetException("Model-call budget exhausted before dispatch.");
                    run.ModelCalls++; run.Summary = failure == null ? "Drafting a plan from source evidence" : "Repairing the draft within the retry limit";
                    store.Save(run, "model.reserved", new { run.ModelCalls, run.Goal.Provider, run.Goal.Limits.MaxOutputTokens });
                    var reply = await provider.Respond(new(run.Goal, run.Evidence, failure, run.ModelCalls), _ => Task.CompletedTask, cts.Token);
                    cts.Token.ThrowIfCancellationRequested();
                    run.InputTokens = AddUsage(run.InputTokens, reply.InputTokens, run.ModelCalls);
                    run.OutputTokens = AddUsage(run.OutputTokens, reply.OutputTokens, run.ModelCalls);
                    store.Save(run, "model.result", new { reply.Action, reply.Text, reply.InputTokens, reply.OutputTokens });
                    var action = reply.Action ?? throw new ArgumentException("Provider returned no typed action. A statement of completion is not a receipt.");
                    store.SafePath(action.Path);
                    if (action.Name == "knowledge.read")
                    {
                        if (!run.Goal.ReadScope.Contains(action.Path)) throw new ArgumentException("Read outside the approved scope.");
                        ReserveTool(run, action); var result = store.Read(action.Path);
                        run.Evidence.RemoveAll(e => e.Path == action.Path); run.Evidence.Add(result.Evidence!);
                        store.Save(run, "tool.result", result); continue;
                    }
                    if (action.Name != "knowledge.write" || !action.Path.StartsWith(run.Goal.WriteScope, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(action.Content) || action.Content.Length > 100_000)
                        throw new ArgumentException("Malformed or unavailable tool action.");
                    if (run.DemoFailure && run.Repairs == 0) action = action with { Content = "A deliberately incomplete demo draft." };
                    if (!seen.Add(Wire.Hash(Wire.Pack(action)))) throw new ArgumentException("Duplicate proposal stopped; repeating oneself is not progress.");
                    var validation = validator.Validate(action, run.Evidence);
                    store.Save(run, "validation.draft", validation);
                    if (run.ValidationEnabled && !validation.Passed)
                    {
                        failure = "Include a Markdown title, references to every supplied source path, and an explicit unresolved section. Do not invent a resolution.";
                        run.Attempts.Add(new(run.ModelCalls, "draft_contract", failure));
                        if (!policy.MayRepair(run)) throw new ArgumentException("Draft validation failed; bounded repair is exhausted or disabled.");
                        run.Repairs++; store.Save(run, "repair.chosen", run.Attempts[^1]); continue;
                    }
                    var version = store.Version(action.Path);
                    var expiry = DateTimeOffset.UtcNow.AddMinutes(15);
                    var approvalId = Guid.NewGuid().ToString("N");
                    var digest = ApprovalDigest(run.Id, approvalId, action, version, expiry);
                    run.Approval = new(approvalId, run.Id, action, digest, version, expiry);
                    run.State = RunState.AwaitingApproval;
                    run.Summary = "Plan drafted · one exact page write needs your approval";
                    store.Save(run, "approval.requested", run.Approval); return;
                }
            }
            catch (OperationCanceledException)
            {
                run.State = cts.IsCancellationRequested && cancellations.ContainsKey(id) ? RunState.NeedsAttention : RunState.Cancelled;
                run.Summary = run.State == RunState.Cancelled ? "Cancelled · no proposed write executed" : "Time budget exhausted · stopped";
                store.Save(run, "run.stopped", new { reason = run.Summary });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or HttpRequestException or BudgetException or System.Text.Json.JsonException or IOException)
            {
                run.State = RunState.Failed; run.Summary = SafeError(ex);
                store.Save(run, "run.failed", new { classification = ex.GetType().Name, reason = run.Summary });
            }
            catch (Exception ex)
            {
                run.State = RunState.Failed; run.Summary = "Unexpected provider or runtime failure. No completion is claimed.";
                store.Save(run, "run.failed", new { classification = ex.GetType().Name, reason = run.Summary });
            }
            finally { cancellations.TryRemove(id, out _); }
        }
        finally { Gate(id).Release(); }
    }
    private static int? AddUsage(int? prior, int? next, int calls) => next == null || (calls > 1 && prior == null) ? null : (prior ?? 0) + next;
    public static string ApprovalDigest(string run, string id, ToolRequest action, string version, DateTimeOffset expiry) => Wire.Hash(Wire.Pack(new { run, id, action, scope = "plans/", version, expiry }));
    public async Task<Run> Decide(string id, string approvalId, string digest, bool allow)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
            var approval = run.Approval;
            if (run.State != RunState.AwaitingApproval || approval == null || approval.Decision != "pending" || approval.Id != approvalId || approval.Digest != digest || approval.Expires < DateTimeOffset.UtcNow || approval.Digest != ApprovalDigest(id, approval.Id, approval.Action, approval.ResourceVersion, approval.Expires))
                throw new InvalidOperationException("Approval is stale, changed, expired, or already decided. Refresh the receipts.");
            if (!allow)
            {
                run.Approval = approval with { Decision = "denied" }; run.State = RunState.Denied; run.Summary = "Write denied · nothing saved";
                store.Save(run, "approval.denied", run.Approval); return run;
            }
            if (store.Setting("writes") == "off") throw new InvalidOperationException("Knowledge writes are currently Off.");
            if (approval.Action.Name != "knowledge.write" || !approval.Action.Path.StartsWith(run.Goal.WriteScope, StringComparison.Ordinal) || store.Version(approval.Action.Path) != approval.ResourceVersion || run.Evidence.Any(e => store.Version(e.Path) != e.Hash))
                throw new InvalidOperationException("Resource or source notes changed. Start a new draft; approval is no longer valid.");
            if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; write was not dispatched.");
            run.Approval = approval with { Decision = "approved" }; run.State = RunState.Running;
            run.Summary = "Write intent recorded · execution outcome not yet known";
            store.Save(run, "approval.approved", run.Approval);
            ReserveTool(run, approval.Action);
            try
            {
                var page = store.Write(approval.Action.Path, approval.Action.Content!, approval.ResourceVersion);
                var exact = store.Version(page.Path) == Wire.Hash(approval.Action.Content!);
                var validation = validator.Validate(approval.Action, run.Evidence);
                run.Validation = new(exact, exact ? ["Exact approved content read back and SHA-256 matched", .. (run.ValidationEnabled ? validation.Checks : [])] : [], validation.Unverified);
                run.OutputPath = page.Path;
                run.State = exact ? RunState.Succeeded : RunState.NeedsAttention;
                run.Summary = exact ? "Plan saved · exact write verified; conflict awaits your decision" : "Write verification failed · inspect the page";
                run.Goal = run.Goal with { Criteria = [new("Exact approved write", "deterministic", exact ? "verified" : "unverified"), new("Factual accuracy and conflict decision", "user", "unverified")] };
                store.Save(run, "tool.result", new ToolResult("knowledge.write", exact, run.Summary, new(page.Path, page.Version, page.Content)));
                store.Save(run, "validation.outcome", run.Validation);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
            {
                run.State = RunState.NeedsAttention; run.Summary = "Write outcome requires reconciliation. No automatic retry.";
                store.Save(run, "write.unknown", new { classification = ex.GetType().Name, run.Summary });
            }
            return run;
        }
        finally { Gate(id).Release(); }
    }
    public async Task Cancel(string id)
    {
        if (cancellations.TryRemove(id, out var cts)) await cts.CancelAsync();
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
            if (run.State is RunState.Queued or RunState.Running or RunState.AwaitingApproval or RunState.Paused)
            {
                run.State = RunState.Cancelled; run.Summary = "Cancelled · proposed action will not execute";
                if (run.Approval != null) run.Approval = run.Approval with { Decision = "cancelled" };
                store.Save(run, "run.cancelled", new { run.Summary });
            }
        }
        finally { Gate(id).Release(); }
    }
    private void ReserveTool(Run run, ToolRequest action)
    {
        if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new BudgetException("Tool-call budget exhausted before dispatch.");
        run.ToolCalls++; store.Save(run, "tool.request", action);
    }
    private static string SafeError(Exception ex) => ex is HttpRequestException ? "Provider request failed. Check the endpoint and server-side credentials." : ex is IOException ? "Storage or provider I/O failed. Inspect configuration." : ex.Message;
}
public sealed class BudgetException(string message) : Exception(message);
