using System.Collections.Concurrent;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime(Store store, Func<ProviderSnapshot, IModelProvider> providers, IValidator validator, IAgentPolicy policy, IPublicWebReader? publicWeb = null,
    IProposalEvidenceValidator? proposalEvidence = null, PolicyProfile? researchProfile = null) : ICapabilityBroker
{
    private readonly IProposalEvidenceValidator proposalValidator = proposalEvidence ?? new ProposalEvidenceValidator();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> cancellations = new();
    private readonly object conversationGate = new();
    private SemaphoreSlim Gate(string id) => locks.GetOrAdd(id, _ => new(1, 1));
    public void Recover()
    {
        foreach (var run in store.List())
        {
            if (run.Execution != null && run.ExecutionCommands.Any(command => command.Status == "outcome-unknown"))
            {
                // A saved question may coexist with an unacknowledged RPC. Keep both pieces of evidence.
                for (var i = 0; i < run.ExecutionCommands.Count; i++)
                    if (run.ExecutionCommands[i].Status == "outcome-unknown")
                        run.ExecutionCommands[i] = run.ExecutionCommands[i] with { Status = "interrupted-outcome-unknown" };
                run.Summary = "Execution request outcome is unknown after restart. Inspect native receipts; do not replay it.";
                store.Save(run, "execution.recovery.unknown", new { run.Summary });
            }
            if (run.State == RunState.Running)
            {
                if (run.Execution != null) PauseExecutionClock(run);
                run.ChargedTokens += run.ReservedTokens; run.ReservedTokens = 0;
                for (var i = 0; i < run.ModelDispatches.Count; i++)
                    if (run.ModelDispatches[i].Status == "dispatched-outcome-unknown")
                        run.ModelDispatches[i] = run.ModelDispatches[i] with { Status = "outcome-unknown" };
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
        var run = Build(goal, demoFailure, arm, validation, journal);
        store.Save(run, "goal.created", new { goal, arm, validation, journal, simulated = goal.Provider.Kind == "scripted" });
        return run;
    }
    private Run Build(Goal goal, bool demoFailure = false, string arm = "evidence", bool validation = true, bool journal = true)
    {
        if (string.IsNullOrWhiteSpace(goal.Objective) || goal.Objective.Length > 4000) throw new ArgumentException("Objective must contain 1–4,000 characters.");
        if (goal.Kind is not ("plan" or "conversation")) throw new ArgumentException("Unsupported goal kind.");
        if (goal.Web != null) throw new ArgumentException("Public research is available only through isolated execution admission.");
        if (goal.Memories is { Length: > 0 }) throw new ArgumentException("Selected memory is available through isolated research execution.");
        if (goal.ReadScope.Length > 12 || (goal.Kind == "plan" && goal.ReadScope.Length == 0)) throw new ArgumentException("Select between one and twelve source pages for a plan.");
        if (goal.Kind == "conversation" && goal.ReadScope.Length != 0) throw new ArgumentException("Conversation cannot implicitly read knowledge.");
        foreach (var path in goal.ReadScope) store.SafePath(path);
        if (goal.WriteScope != "plans/") throw new ArgumentException("Agent writes are limited to plans/.");
        var b = goal.Limits;
        if (b.ModelCalls is < 0 or > 12 || b.ToolCalls is < 0 or > 30 || b.Seconds is < 1 or > 600 || b.Repairs is < 0 or > 2 || b.MaxOutputTokens is < 128 or > 16000 || b.MaxTotalTokens is < 0 or > 1_000_000) throw new ArgumentException("Budget outside supported limits.");
        var run = new Run { Goal = goal, DemoFailure = demoFailure, Policy = arm, ValidationEnabled = validation, JournalDetail = journal };
        return run;
    }
    public Run Converse(string message, ProviderSnapshot provider, Budget? limits = null)
    {
        lock (conversationGate)
        {
        if (store.List().Any(r => r.Goal.Kind == "conversation" && r.State is RunState.Running or RunState.Queued)) throw new InvalidOperationException("Wait for the current reply or cancel it before sending another message.");
        var run = Build(new(message, [], "plans/", [new("Response delivered", "deterministic"), new("Factual accuracy", "unverified")], limits ?? new(ModelCalls: 1, ToolCalls: 0), provider, "conversation"));
        // Freeze context at admission: another browser cannot rewrite this turn's past.
        run.ConversationContext = store.Chats().TakeLast(20).ToList();
        store.Save(run, "conversation.accepted", new { goal = run.Goal, contextMessageIds = run.ConversationContext.Select(m => m.Id) }, new(run.Id + "-user", "user", message, DateTimeOffset.UtcNow));
        return run;
        }
    }
    public async Task Execute(string id)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
            if (run.Execution != null) throw new InvalidOperationException("This task is owned by its execution backend. It cannot enter the legacy provider loop.");
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
                    var observation = new Observation(run.Goal, run.Evidence, failure, run.ModelCalls + 1, run.ConversationContext);
                    var quote = provider.Quote(observation);
                    var remaining = run.Goal.Limits.MaxTotalTokens - run.ChargedTokens;
                    if (run.Goal.Limits.RequireCertifiedTokenBound && (quote.InputUpperBound == null || !quote.OutputBoundCertified)) throw new BudgetException("Strict token admission refused: this provider has no certified input/output bound. No inference dispatched.");
                    var reservation = quote.OutputBoundCertified && quote.InputUpperBound is { } inputBound ? checked(inputBound + (quote.OutputUpperBound ?? run.Goal.Limits.MaxOutputTokens)) : remaining;
                    if (reservation < 0 || reservation > remaining || (reservation == 0 && (quote.InputUpperBound != 0 || quote.OutputUpperBound != 0))) throw new BudgetException("Aggregate token budget exhausted before dispatch.");
                    run.ReservedTokens = reservation;
                    run.TokenAccounting = quote.InputUpperBound == null || !quote.OutputBoundCertified ? "Uncertified provider: entire remaining budget reserved; hard remote ceiling not claimed" : quote.Basis;
                    run.ModelCalls++; run.Summary = run.Goal.Kind == "conversation" ? "Composing a reply" : failure == null ? "Drafting a plan from source evidence" : "Repairing the draft within the retry limit";
                    store.Save(run, "model.reserved", new { run.ModelCalls, run.Goal.Provider, run.Goal.Limits.MaxOutputTokens });
                    var lastDelta = DateTimeOffset.MinValue;
                    var reply = await provider.Respond(observation, delta =>
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        if (run.DraftText.Length + delta.Length > 100_000) throw new ArgumentException("Response exceeds the text limit.");
                        run.DraftText += delta;
                        if ((DateTimeOffset.UtcNow - lastDelta).TotalMilliseconds >= 120)
                        {
                            store.Save(run, "model.delta", new { characters = run.DraftText.Length });
                            lastDelta = DateTimeOffset.UtcNow;
                        }
                        return Task.CompletedTask;
                    }, cts.Token);
                    cts.Token.ThrowIfCancellationRequested();
                    run.InputTokens = AddUsage(run.InputTokens, reply.InputTokens, run.ModelCalls);
                    run.OutputTokens = AddUsage(run.OutputTokens, reply.OutputTokens, run.ModelCalls);
                    var reported = reply.InputTokens is { } usedInput && reply.OutputTokens is { } usedOutput && usedInput >= 0 && usedOutput >= 0 ? checked(usedInput + usedOutput) : (int?)null;
                    run.ChargedTokens += reported ?? run.ReservedTokens;
                    run.ReservedTokens = 0;
                    store.Save(run, "model.result", new { reply.Action, reply.Text, reply.InputTokens, reply.OutputTokens });
                    if (run.ChargedTokens > run.Goal.Limits.MaxTotalTokens || reply.OutputTokens > run.Goal.Limits.MaxOutputTokens) throw new BudgetException("Provider exceeded the declared token ceiling. Usage retained; no further action is authorized.");
                    if (run.Goal.Kind == "conversation")
                    {
                        if (reply.Action != null) throw new ArgumentException("Conversation cannot execute tools. Start an explicit scoped goal instead.");
                        if (string.IsNullOrWhiteSpace(reply.Text)) throw new ArgumentException("Provider returned an empty reply.");
                        run.DraftText = reply.Text;
                        run.State = RunState.Succeeded; run.Summary = "Replied · no tools or knowledge writes";
                        run.Validation = new(true, ["Nonempty response delivered"], ["Factual accuracy has not been independently verified"]);
                        store.Save(run, "conversation.completed", run.Validation, new(run.Id + "-assistant", "assistant", reply.Text, DateTimeOffset.UtcNow));
                        return;
                    }
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
            finally
            {
                if (run.ReservedTokens > 0)
                {
                    run.ChargedTokens += run.ReservedTokens; run.ReservedTokens = 0;
                    store.Save(run, "budget.unknown-charge", new { run.ChargedTokens, reason = "Failed or interrupted dispatch usage unknown; reservation retained" });
                }
                cancellations.TryRemove(id, out _);
            }
        }
        finally { Gate(id).Release(); }
    }
    private static int? AddUsage(int? prior, int? next, int calls) => next == null || next < 0 || (calls > 1 && prior == null) ? null : (prior ?? 0) + next;
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
            store.AssertMemoriesCurrent(run);
            AssertProposalReview(run, approval);
            if (approval.Action.Name != "knowledge.write" || !approval.Action.Path.StartsWith(run.Goal.WriteScope, StringComparison.Ordinal) || store.Version(approval.Action.Path) != approval.ResourceVersion || run.Evidence.Any(e => store.Version(e.Path) != e.Hash))
                throw new InvalidOperationException("Resource or source notes changed. Start a new draft; approval is no longer valid.");
            if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; write was not dispatched.");
            run.Approval = approval with { Decision = "approved" }; run.State = RunState.Running;
            run.Summary = "Write intent recorded · execution outcome not yet known";
            store.Save(run, "approval.approved", run.Approval);
            ReserveTool(run, approval.Action);
            try
            {
                var page = store.WriteCommitted(approval.Id, approval.Action.Path, approval.Action.Content!, approval.ResourceVersion);
                var exact = store.Version(page.Path) == Wire.Hash(approval.Action.Content!);
                var validation = run.Execution == null ? validator.Validate(approval.Action, run.Evidence) : new ValidationResult(false, [], ["Source accuracy and overall task completion require independent review"]);
                run.Validation = new(exact, exact ? ["Exact approved content read back and SHA-256 matched", .. (run.ValidationEnabled ? validation.Checks : [])] : [], validation.Unverified);
                run.OutputPath = page.Path;
                run.State = exact ? RunState.Succeeded : RunState.NeedsAttention;
                run.Summary = exact ? run.Execution == null ? "Plan saved · exact write verified; conflict awaits your decision" : "Artifact imported · exact approved content verified; research quality remains unverified" : "Write verification failed · inspect the page";
                run.Goal = run.Goal with { Criteria = [new("Exact approved write", "deterministic", exact ? "verified" : "unverified"), new(run.Execution == null ? "Factual accuracy and conflict decision" : "Research quality and overall task completion", "user", "unverified")] };
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
    public Task Cancel(string id) => CancelCore(id, false);
    internal Task CancelResearch(string id) => CancelCore(id, true);
    private async Task CancelCore(string id, bool researchCancellation)
    {
        if (cancellations.TryRemove(id, out var cts)) await cts.CancelAsync();
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
            var interruptedResearch = researchCancellation && run.Research != null && run.Execution?.Backend == "openclaw" && run.State == RunState.NeedsAttention;
            if (interruptedResearch && run.Approval is { } approval &&
                (approval.Decision is "approved" or "user-action" || store.WriteOperation(approval.Id) != null))
                throw new InvalidOperationException("An approved import has an uncertain outcome. Reconcile or abandon that recorded write before closing the task.");
            // Provisioning may publish attention before cancellation acquires this lock. The user's stop still wins for unapproved work.
            if (run.State is RunState.Queued or RunState.Running or RunState.AwaitingApproval or RunState.Paused or RunState.AwaitingInput || interruptedResearch)
            {
                run.State = RunState.Cancelled; run.Summary = "Cancelled · proposed action will not execute";
                if (run.Approval != null) run.Approval = run.Approval with { Decision = "cancelled" };
                store.Save(run, "run.cancelled", new { run.Summary });
            }
        }
        finally { Gate(id).Release(); }
    }
    public Page EditPage(string path, string content, string version)
    {
        store.SafePath(path);
        var run = new Run { Goal = new("Edit " + path, [], path.Split('/')[0] + "/", [new("Exact user edit", "deterministic")], new(ModelCalls:0), new("user", "direct edit"), "edit"), State = RunState.Running, Summary = "Saving your explicit edit" };
        var action = new ToolRequest("knowledge.write", path, content);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(15);
        run.Approval = new(run.Id, run.Id, action, ApprovalDigest(run.Id, run.Id, action, version, expiry), version, expiry, "user-action");
        store.Save(run, "user.edit", new { action, expectedVersion = version, authority = "Explicit authenticated user edit" });
        try
        {
            var page = store.WriteCommitted(run.Id, path, content, version);
            if (store.Version(path) != Wire.Hash(content)) throw new IOException("User edit read-back did not match committed content.");
            run.ToolCalls = 1; run.OutputPath = path; run.State = RunState.Succeeded;
            run.Summary = "Saved your edit · revision retained";
            run.Validation = new(true, ["Exact user content saved and read back"], ["Content accuracy is the author's responsibility"]);
            store.Save(run, "user.edit.completed", new { page.Path, page.Version, run.Validation });
            return page;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            run.State = store.WriteOperation(run.Id) == null ? RunState.Failed : RunState.NeedsAttention;
            run.Summary = run.State == RunState.Failed ? "Edit rejected · refresh the page and inspect the error" : "Edit content committed; file projection needs reconciliation";
            store.Save(run, "user.edit.stopped", new { classification = ex.GetType().Name, run.Summary });
            throw;
        }
    }
    public object InspectReconciliation(string id)
    {
        var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
        var approval = run.Approval ?? throw new ArgumentException("No write proposal exists for this run. Inspect the trace and start a new task.");
        var actual = store.Version(approval.Action.Path);
        return new { run.Id, run.State, approval.Action, approval.ResourceVersion, observedVersion = actual, exactMatch = actual == Wire.Hash(approval.Action.Content ?? ""), operation = store.WriteOperation(approval.Id), authority = approval.Decision };
    }
    public async Task<Run> Reconcile(string id, string observedVersion, string mode)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
            if (run.State != RunState.NeedsAttention) throw new InvalidOperationException("Only interrupted work can be reconciled.");
            var a = run.Approval ?? throw new ArgumentException("No write proposal to reconcile.");
            if (a.Decision is not ("approved" or "user-action")) throw new InvalidOperationException("An unapproved action cannot be reconciled into execution.");
            var current = store.Version(a.Action.Path);
            if (current != observedVersion) throw new InvalidOperationException("Resource changed since inspection.");
            if (mode == "abandon")
            {
                run.State = RunState.Failed; run.Summary = "Reconciliation closed without further writes; existing content preserved";
                store.Save(run, "reconciliation.abandoned", new { observedVersion }); return run;
            }
            if (mode is not ("verify" or "complete")) throw new ArgumentException("Choose verify, complete, or abandon.");
            if (mode == "complete")
            {
                if (run.Goal.Kind != "edit" && store.Setting("writes") == "off") throw new InvalidOperationException("Agent writes are Off.");
                if (run.Evidence.Any(e => store.Version(e.Path) != e.Hash)) throw new InvalidOperationException("Sources changed; do not complete an old proposal.");
                store.AssertMemoriesCurrent(run);
                AssertProposalReview(run, a);
                store.Save(run, "reconciliation.confirmed", new { mode, observedVersion, a.Action, authority = "Explicit user reconciliation" });
                store.CompleteProjection(a.Id, observedVersion);
            }
            if (store.Version(a.Action.Path) != Wire.Hash(a.Action.Content!)) throw new InvalidOperationException("Content does not match the approved action. No success recorded.");
            if (store.WriteOperation(a.Id) != null) store.CompleteProjection(a.Id, store.Version(a.Action.Path));
            run.State = RunState.Succeeded; run.OutputPath = a.Action.Path;
            run.Summary = "Interrupted write reconciled · exact approved content verified";
            run.Validation = new(true, ["Reconciliation read-back matches approved SHA-256"], ["Factual accuracy remains unverified"]);
            store.Save(run, "reconciliation.verified", new { run.Validation, path = a.Action.Path }); return run;
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
