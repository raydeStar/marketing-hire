using System.Collections.Concurrent;
using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime(Store store, Func<ProviderSnapshot, IModelProvider> providers, IValidator validator, IAgentPolicy policy, IPublicWebReader? publicWeb = null,
    IProposalEvidenceValidator? proposalEvidence = null, PolicyProfile? researchProfile = null, IPublicSearch? publicSearch = null,
    TimeSpan? conversationBackgroundDelay = null, IConnectedToolBroker? connectedTools = null, DelegationScheduler? delegations = null,
    TimeProvider? timeProvider = null) : ICapabilityBroker
{
    private readonly IProposalEvidenceValidator proposalValidator = proposalEvidence ?? new ProposalEvidenceValidator();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> cancellations = new();
    private readonly object conversationGate = new();
    private SemaphoreSlim Gate(string id) => locks.GetOrAdd(id, _ => new(1, 1));
    public void Recover()
    {
        foreach (var run in store.List())
        {
            if (run.Execution != null && run.State != RunState.Running && (run.ReservedTokens > 0 ||
                run.ModelDispatches.Any(dispatch => dispatch.Status == "dispatched-outcome-unknown")))
            {
                // Cancellation or guidance can stop work while its model is still in flight. Keep the bill, not a free replay.
                run.ChargedTokens += run.ReservedTokens; run.ReservedTokens = 0;
                for (var i = 0; i < run.ModelDispatches.Count; i++)
                    if (run.ModelDispatches[i].Status == "dispatched-outcome-unknown")
                        run.ModelDispatches[i] = run.ModelDispatches[i] with { Status = "outcome-unknown" };
                store.Save(run, "recovery.model.unknown", new { run.ChargedTokens, statePreserved = true });
            }
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
    public Run Converse(string message, ProviderSnapshot provider, Budget? limits = null, string? artifactId = null, string? localDate = null, string[]? uploadIds = null, bool suggestIdeas = false)
    {
        lock (conversationGate)
        {
        if (store.List().Any(r => r.Goal.Kind == "conversation" && !r.Background && r.State is RunState.Running or RunState.Queued)) throw new InvalidOperationException("This reply is still in the foreground. A slow reply moves into the background when a slot is free; you can also cancel it.");
        var run = Build(new(message, [], "plans/", [new("Response delivered", "deterministic"), new("Factual accuracy", "unverified")], limits ?? new(ModelCalls: 2, ToolCalls: 2, Seconds: 600), provider, "conversation"));
        run.UploadIds = uploadIds ?? []; store.Attachments(run.UploadIds); run.SuggestIdeas = suggestIdeas;
        if (!suggestIdeas)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(message,
                "(?<![a-z0-9-])notes/[a-z0-9][a-z0-9-]{0,90}\\.md(?![a-z0-9-])", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var path = match.Value.ToLowerInvariant();
                var page = store.Page(path);
                if (page != null && run.Evidence.All(item => item.Path != page.Path))
                    run.Evidence.Add(new(page.Path, page.Version, page.Content));
            }
        }
        if (!suggestIdeas && publicWeb != null) run.ConversationWebUrls = ConversationWeb.Links(message);
        if (!suggestIdeas && connectedTools != null) run.ConnectedTools = connectedTools.Snapshot();
        if (!suggestIdeas && delegations != null)
        {
            run.DelegationRequestedAt = clock.GetUtcNow();
            run.DelegationTimeZone = TimeZoneInfo.Local.Id;
        }
        // Freeze context at admission: another browser cannot rewrite this turn's past.
        run.ConversationContext = ConversationHistory();
        run.ArtifactContext = store.ArtifactContext(artifactId, localDate ?? DateTime.Now.ToString("yyyy-MM-dd"));
        store.Save(run, "conversation.accepted", new { goal = run.Goal, contextMessageIds = run.ConversationContext.Select(m => m.Id) }, new(run.Id + "-user", "user", message, DateTimeOffset.UtcNow));
        return run;
        }
    }
    public static string? ConnectionSetupIntent(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var explicitConnection = System.Text.RegularExpressions.Regex.IsMatch(message,
            @"\b(connect|link|integrate|hook\s+up)\b.*\b(google|gmail|calendar|email|mail|mcp|outlook|github|slack|notion|dropbox|service|account|tool)\b|\bset\s+up\b.*\b(google|gmail|mcp|outlook|github|slack|notion|dropbox)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!explicitConnection) return null;
        return System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(google|gmail)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            ? "google" : "mcp";
    }
    public static string? GoogleCapabilityIntent(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var options = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        if (System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(calendar|events?|schedule)\b", options) &&
            System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(check|read|show|list|summari[sz]e|what(?:'s| is)|connected|linked|auth(?:ed|orized)?)\b", options))
            return "calendar";
        if (!System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(gmail|e-?mail|inbox|mail)\b", options)) return null;
        if (System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(send|sending|compose|email\s+for\s+me|mail\s+for\s+me)\b", options))
            return "gmail-send";
        if (System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(check|read|search|show|list|summari[sz]e|inbox|unread|connected|linked|auth(?:ed|orized)?)\b", options))
            return "gmail-read";
        return null;
    }
    public static bool ConnectionStatusFollowUpIntent(string message) => !string.IsNullOrWhiteSpace(message) &&
        System.Text.RegularExpressions.Regex.IsMatch(message,
            @"^\s*(?:did\s+(?:that|it)\s+work|am\s+i\s+(?:connected|linked|authed|authorized)|is\s+(?:it|google|gmail)\s+(?:connected|linked|authorized))\s*[?.!]*\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public string? RecentGoogleConnectionProduct() => store.List().Take(3)
        .FirstOrDefault(run => run.ConnectionSetup == "google")?.ConnectionSetupProduct;

    public Run PrepareConnectionSetup(string message, ProviderSnapshot provider, string target, string? requestedProduct = null, bool missing = false)
    {
        if (target is not ("google" or "mcp")) throw new ArgumentException("Unsupported connection setup target.");
        lock (conversationGate)
        {
            var run = Build(new(message, [], "plans/", [new("Secure connection setup displayed", "deterministic")],
                new(ModelCalls: 0, ToolCalls: 0, Seconds: 30, Repairs: 0, MaxOutputTokens: 128, MaxTotalTokens: 0), provider, "conversation"));
            run.ConnectionSetup = target;
            run.ConnectionSetupProduct = target == "google" ? requestedProduct ?? GoogleConnectionProduct(message) : null;
            run.ConversationContext = ConversationHistory();
            run.State = RunState.Succeeded;
            run.Summary = missing ? "Connection needed · secure setup available" : "Secure connection setup ready · no model call";
            run.DraftText = target == "google"
                ? missing
                    ? $"I don’t have {GoogleConnectionLabel(run.ConnectionSetupProduct)} connected yet. You can link it below, or close the card and keep chatting. Credentials stay securely on this computer and never enter our conversation."
                    : $"I’ve opened a secure Google connection card below for {GoogleConnectionLabel(run.ConnectionSetupProduct)}. Choose one or more permissions, then sign in on Google. Thaddeus keeps the connection securely on this computer; credentials never enter our conversation."
                : "I’ve opened a secure connection card below. The endpoint and credential fields go directly to this host; secrets are not added to our conversation or sent to the model.";
            run.TokenAccounting = "No model dispatch. Connection credentials are accepted only by the host settings endpoint.";
            run.Validation = new(true, ["Setup request handled locally", "No model or connector action was run"], []);
            var now = clock.GetUtcNow();
            store.Save(run, "connection.setup.accepted", new { target, product = run.ConnectionSetupProduct, missing, modelCalls = 0, credentialsAcceptedInChat = false },
                new(run.Id + "-user", "user", message, now));
            store.Save(run, "connection.setup.ready", new { target, product = run.ConnectionSetupProduct, modelCalls = 0, credentialsAcceptedInChat = false },
                new(run.Id + "-assistant", "assistant", run.DraftText, now));
            return run;
        }
    }
    public Run PrepareGoogleConnectionStatus(string message, ProviderSnapshot provider, string[] accounts, string[] products)
    {
        lock (conversationGate)
        {
            var run = Build(new(message, [], "plans/", [new("Google connection status read from this host", "deterministic")],
                new(ModelCalls: 0, ToolCalls: 0, Seconds: 30, Repairs: 0, MaxOutputTokens: 128, MaxTotalTokens: 0), provider, "conversation"));
            var capabilities = products.Select(GoogleConnectionLabel).Distinct(StringComparer.Ordinal).ToArray();
            run.ConversationContext = ConversationHistory();
            run.State = RunState.Succeeded;
            run.Summary = "Google connection confirmed · no external action run";
            run.DraftText = $"Yes. Google is connected{(accounts.Length == 0 ? "" : " as " + string.Join(", ", accounts))}. Available access: {string.Join(", ", capabilities)}. I haven’t read or changed anything just to answer this question.";
            run.TokenAccounting = "No model dispatch. Connection status was read from the host catalog; no Google request was made.";
            run.Validation = new(true, ["Connection status read locally", "No external action was run"], []);
            var now = clock.GetUtcNow();
            store.Save(run, "connection.status.accepted", new { provider = "google", accounts, products, modelCalls = 0 },
                new(run.Id + "-user", "user", message, now));
            store.Save(run, "connection.status.ready", new { provider = "google", accounts, products, modelCalls = 0 },
                new(run.Id + "-assistant", "assistant", run.DraftText, now));
            return run;
        }
    }
    private static string GoogleConnectionProduct(string message)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(message, @"\bcalendar\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "calendar";
        if (System.Text.RegularExpressions.Regex.IsMatch(message, @"\b(send|sending|email\s+for\s+me|mail\s+for\s+me)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "gmail-send";
        return "gmail-read";
    }
    private static string GoogleConnectionLabel(string? product) => product switch
    {
        "calendar" => "calendar reading",
        "gmail-send" => "approved email sending",
        _ => "read-only mail"
    };
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
                    var observation = new Observation(run.Goal, run.Evidence, failure, run.ModelCalls + 1, run.ConversationContext, run.ArtifactContext, store.Attachments(run.UploadIds, true), run.SuggestIdeas, WebObservation(run), ConnectedObservation(run), DelegationObservation(run), TodoBatchObservation(run), store.Soul(), store.User(), store.Identity());
                    var quote = provider.Quote(observation);
                    var remaining = run.Goal.Limits.MaxTotalTokens - run.ChargedTokens;
                    if (run.Goal.Limits.RequireCertifiedTokenBound && (quote.InputUpperBound == null || !quote.OutputBoundCertified)) throw new BudgetException("Strict token admission refused: this provider has no certified input/output bound. No inference dispatched.");
                    var reservation = quote.OutputBoundCertified && quote.InputUpperBound is { } inputBound ? checked(inputBound + (quote.OutputUpperBound ?? run.Goal.Limits.MaxOutputTokens)) : remaining;
                    if (reservation < 0 || reservation > remaining || (reservation == 0 && (quote.InputUpperBound != 0 || quote.OutputUpperBound != 0))) throw new BudgetException("Aggregate token budget exhausted before dispatch.");
                    await provider.Prepare(cts.Token); cts.Token.ThrowIfCancellationRequested();
                    run.ReservedTokens = reservation;
                    run.TokenAccounting = quote.InputUpperBound == null || !quote.OutputBoundCertified ? "Uncertified provider: entire remaining budget reserved; hard remote ceiling not claimed" : quote.Basis;
                    run.ModelCalls++; run.Summary = run.Goal.Kind == "conversation" ? "Composing a reply" : failure == null ? "Drafting a plan from source evidence" : "Repairing the draft within the retry limit";
                    store.Save(run, "model.reserved", new { run.ModelCalls, run.Goal.Provider, run.Goal.Limits.MaxOutputTokens });
                    var lastDelta = DateTimeOffset.MinValue;
                    var responseTask = provider.Respond(observation, delta =>
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
                    if (run.Goal.Kind == "conversation" && !run.Background)
                    {
                        // The reply keeps its own ledger; the butler need not block the drawing room.
                        using var handoff = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        var delay = Task.Delay(conversationBackgroundDelay ?? TimeSpan.FromSeconds(8), handoff.Token);
                        if (await Task.WhenAny(responseTask, delay) != responseTask && !cts.IsCancellationRequested)
                        {
                            lock (conversationGate)
                            {
                                if (store.List().Count(r => r.Background && r.State is RunState.Running or RunState.Queued) < 2)
                                {
                                    run.Background = true;
                                    store.Save(run, "conversation.background", new { message = "Work continues in the background; chat is available.", maxBackgroundTasks = 2 });
                                }
                            }
                        }
                        await handoff.CancelAsync();
                    }
                    var reply = await responseTask;
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
                        if (reply.Action != null)
                        {
                            if (run.SuggestIdeas) { HandleIdeaAction(run, reply.Action); return; }
                            if (reply.Action.Name == ConversationWeb.ToolName) { await HandleWebAction(run, reply.Action, cts.Token); continue; }
                            if (HandleSoulAction(run, reply.Action) || HandleUserAction(run, reply.Action)) { PrepareApprovalPolicy(run); return; }
                            if (HandleTodoBatchAction(run, reply.Action)) { PrepareApprovalPolicy(run); return; }
                            if (HandleInboxWatchAction(run, reply.Action)) { PrepareApprovalPolicy(run); return; }
                            if (HandleDelegationBriefAction(run, reply.Action)) { PrepareApprovalPolicy(run); return; }
                            if (HandleDelegationEmailAction(run, reply.Action)) { PrepareApprovalPolicy(run); return; }
                            if (HandleDelegationManagementAction(run, reply.Action)) { PrepareApprovalPolicy(run); return; }
                            if (HandleDelegationAction(run, reply.Action)) { PrepareApprovalPolicy(run); return; }
                            if (HandleConnectedAction(run, reply.Action)) { PrepareApprovalPolicy(run); return; }
                            if (HandleAppAction(run, reply.Action)) continue;
                            return;
                        }
                        if (run.SuggestIdeas) throw new ArgumentException("The model returned no saved suggestions. No ideas were added.");
                        if (string.IsNullOrWhiteSpace(reply.Text)) throw new ArgumentException("Provider returned an empty reply.");
                        run.DraftText = reply.Text;
                        run.State = RunState.Succeeded; run.Summary = run.Capabilities.Count == 0 ? "Replied · no tools or knowledge writes" : "Replied · tool receipts recorded in the log";
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
                    store.Save(run, "approval.requested", run.Approval);
                    PrepareApprovalPolicy(run);
                    return;
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
    public async Task<Run> Decide(string id, string approvalId, string digest, bool allow, string? remember = null)
    {
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
            var approval = run.Approval;
            ConnectedToolDefinition connectedTool = null!;
            var soul = approval != null && IsSoulApproval(approval);
            var user = approval != null && IsUserApproval(approval);
            var todoBatch = approval != null && IsTodoBatchApproval(approval);
            var delegationManagement = approval != null && IsDelegationManagementApproval(approval);
            var inboxWatch = approval != null && IsInboxWatchApproval(run, approval);
            var briefDelegation = approval != null && IsBriefDelegationApproval(run, approval);
            var emailDelegation = approval != null && IsEmailDelegationApproval(run, approval);
            var delegation = approval != null && IsDelegationApproval(run, approval);
            var connected = approval != null && IsConnectedApproval(run, approval, out connectedTool);
            var expectedDigest = soul
                ? SoulApprovalDigest(id, approvalId, approval!.Action, approval.ResourceVersion, approval.Expires)
                : user
                ? UserApprovalDigest(id, approvalId, approval!.Action, approval.ResourceVersion, approval.Expires)
                : delegationManagement
                ? DelegationManagementApprovalDigest(id, approvalId, approval!.Action, approval.ResourceVersion, approval.Expires)
                : todoBatch
                ? TodoBatchApprovalDigest(id, approvalId, approval!.Action, approval.ResourceVersion, approval.Expires)
                : inboxWatch
                ? InboxWatchApprovalDigest(id, approvalId, approval!.Action, approval.ResourceVersion, approval.Expires)
                : briefDelegation
                ? BriefDelegationApprovalDigest(id, approvalId, approval!.Action, approval.ResourceVersion, approval.Expires)
                : emailDelegation
                ? EmailDelegationApprovalDigest(id, approvalId, approval!.Action, approval.ResourceVersion, approval.Expires)
                : delegation
                ? DelegationApprovalDigest(id, approvalId, approval!.Action, approval.ResourceVersion, approval.Expires)
                : connected
                ? ConnectedApprovalDigest(id, approvalId, approval!.Action, connectedTool.ConnectionVersion, approval.Expires)
                : approval == null ? "" : ApprovalDigest(id, approval.Id, approval.Action, approval.ResourceVersion, approval.Expires);
            if (run.State != RunState.AwaitingApproval || approval == null || approval.Decision != "pending" || approval.Id != approvalId || approval.Digest != digest || approval.Expires < clock.GetUtcNow() || approval.Digest != expectedDigest)
                throw new InvalidOperationException("Approval is stale, changed, expired, or already decided. Refresh the receipts.");
            if (remember != null)
            {
                if (remember is not ("allow" or "deny") || (remember == "allow") != allow)
                    throw new ArgumentException("The remembered approval choice must match this decision.");
                RememberApprovalRule(run, approval, remember);
            }
            if (!allow)
            {
                run.Approval = approval with { Decision = "denied" }; run.State = RunState.Denied;
                run.Summary = soul ? "Soul edit denied · personality unchanged" : user ? "User profile update denied · profile unchanged" : delegationManagement ? "Delegated-work change denied · nothing changed" : todoBatch ? "To-do batch denied · nothing created" : inboxWatch ? "Inbox watch denied · nothing scheduled or read" : briefDelegation ? "Recurring brief denied · nothing scheduled or read" : emailDelegation ? "Scheduled email denied · nothing scheduled or sent" : delegation ? "Reminder denied · nothing scheduled" : connected ? "Connected action denied · no request sent" : "Write denied · nothing saved";
                store.Save(run, "approval.denied", run.Approval); return run;
            }
            if (soul)
            {
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; the Soul was not updated.");
                if (store.Soul().Version != approval.ResourceVersion)
                    throw new InvalidOperationException("The Soul changed after review. Refresh before approving a replacement.");
                run.Approval = approval with { Decision = "approved" }; run.State = RunState.Running;
                run.Summary = "Approval recorded · updating the exact Soul text";
                store.Save(run, "approval.approved", new { approval = run.Approval, authority = "exact-soul-edit-v1", permissionsChanged = false });
                ReserveTool(run, approval.Action);
                var saved = store.UpdateSoul(approval.Action.Content!, approval.ResourceVersion, "chat", approval.Id);
                var exact = saved.Version == Wire.Hash(approval.Action.Content!);
                run.State = exact ? RunState.Succeeded : RunState.NeedsAttention;
                run.Summary = exact ? "Soul updated · exact approved content verified" : "Soul update needs review · read-back did not match";
                run.Validation = new(exact, exact ? ["Exact approved Soul content read back and SHA-256 matched", "Personality changed without changing tool permissions"] : [],
                    exact ? [] : ["Read-back did not match the approved Soul content"]);
                var reply = exact ? "I’ve updated my Soul with the exact personality change you approved. A small alteration in the manor, and no permissions smuggled in beneath the silverware." : "The Soul edit could not be verified. Please inspect it in Settings.";
                store.Save(run, "soul.updated", new { soul = saved, approval = run.Approval, run.Validation },
                    new(run.Id + "-assistant", "assistant", reply, clock.GetUtcNow()));
                return run;
            }
            if (user)
            {
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; the User profile was not updated.");
                if (store.User().Version != approval.ResourceVersion)
                    throw new InvalidOperationException("The User profile changed after review. Refresh before approving a replacement.");
                run.Approval = approval with { Decision = "approved" }; run.State = RunState.Running;
                run.Summary = "Approval recorded · updating the exact User profile text";
                store.Save(run, "approval.approved", new { approval = run.Approval, authority = "exact-user-profile-edit-v1", permissionsChanged = false });
                ReserveTool(run, approval.Action);
                var saved = store.UpdateUser(approval.Action.Content!, approval.ResourceVersion, "chat", approval.Id);
                var exact = saved.Version == Wire.Hash(approval.Action.Content!);
                run.State = exact ? RunState.Succeeded : RunState.NeedsAttention;
                run.Summary = exact ? "User profile updated · exact approved content verified" : "User profile update needs review · read-back did not match";
                run.Validation = new(exact, exact ? ["Exact approved User profile read back and SHA-256 matched", "Profile context changed without changing tool permissions"] : [],
                    exact ? [] : ["Read-back did not match the approved User profile"]);
                var reply = exact ? "I’ve updated your profile with the exact information you approved. Filed neatly, with no speculative marginalia." : "The User profile update could not be verified. Please inspect it in Settings.";
                store.Save(run, "user.updated", new { user = saved, approval = run.Approval, run.Validation },
                    new(run.Id + "-assistant", "assistant", reply, clock.GetUtcNow()));
                return run;
            }
            if (delegationManagement)
            {
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; no delegated work was changed.");
                var current = store.DelegationJobs().SingleOrDefault(job => job.Id == approval.Action.Path) ?? throw new InvalidOperationException("The delegated job no longer exists.");
                if (DelegationManagementVersion(current) != approval.ResourceVersion)
                    throw new InvalidOperationException("The delegated job changed after review. Refresh before changing it.");
                run.Approval = approval with { Decision = "approved" }; run.State = RunState.Running;
                run.Summary = "Approval recorded · applying the exact delegated-work change";
                store.Save(run, "approval.approved", new { approval = run.Approval, authority = "exact-delegation-management-v1", changed = false });
                ReserveTool(run, approval.Action);
                DelegationJob changed;
                if (approval.Action.Name == DelegationManagementConversation.CancelTool)
                {
                    var proposal = ParseDelegationCancel(approval.Action);
                    changed = store.CancelDelegation(proposal.JobId, proposal.Version, clock.GetUtcNow());
                }
                else if (approval.Action.Name == DelegationManagementConversation.RescheduleTool)
                {
                    var proposal = ParseDelegationReschedule(approval.Action);
                    changed = store.RescheduleReminder(proposal.JobId, proposal.Version, proposal.DueUtc, proposal.TimeZone, clock.GetUtcNow());
                }
                else if (approval.Action.Name == DelegationManagementConversation.EditEmailTool)
                {
                    var proposal = ParseDelegationEmailEdit(approval.Action);
                    var currentEmail = ReadScheduledEmail(current) ?? throw new InvalidOperationException("The scheduled email payload is unreadable.");
                    changed = store.EditScheduledEmail(proposal.JobId, proposal.Version, ReplaceEmail(currentEmail, proposal), clock.GetUtcNow());
                }
                else if (approval.Action.Name == DelegationManagementConversation.PauseBriefTool)
                {
                    var proposal = ParseDelegationBriefState(approval.Action);
                    changed = store.PauseBrief(proposal.JobId, proposal.Version, clock.GetUtcNow());
                }
                else if (approval.Action.Name == DelegationManagementConversation.ResumeBriefTool)
                {
                    var proposal = ParseDelegationBriefState(approval.Action);
                    changed = store.ResumeBrief(proposal.JobId, proposal.Version, clock.GetUtcNow());
                }
                else
                {
                    var proposal = ParseDelegationBriefEdit(approval.Action);
                    var currentBrief = ReadScheduledBrief(current) ?? throw new InvalidOperationException("The recurring brief payload is unreadable.");
                    changed = store.EditBrief(proposal.JobId, proposal.Version, ReplaceBrief(currentBrief, proposal), clock.GetUtcNow());
                }
                var readBack = store.DelegationJobs().Single(job => job.Id == changed.Id && job.Version == changed.Version);
                var result = JsonSerializer.SerializeToElement(new { job = readBack, verifiedByReadBack = true }, Wire.Json);
                run.Capabilities.Add(new("delegation-management-" + approval.Id, Wire.Hash(Wire.Pack(approval.Action)), approval.Action.Name,
                    "owner-reviewed-delegation-management", clock.GetUtcNow(), result, false,
                    JsonSerializer.SerializeToElement(new { approval.Action.Path, approval.Action.Content }, Wire.Json)));
                var cancelled = approval.Action.Name == DelegationManagementConversation.CancelTool;
                var rescheduled = approval.Action.Name == DelegationManagementConversation.RescheduleTool;
                var editedEmail = approval.Action.Name == DelegationManagementConversation.EditEmailTool;
                var pausedBrief = approval.Action.Name == DelegationManagementConversation.PauseBriefTool;
                var resumedBrief = approval.Action.Name == DelegationManagementConversation.ResumeBriefTool;
                var reply = cancelled ? $"Cancelled {readBack.Title}." : rescheduled
                    ? $"Rescheduled {readBack.Title} for {readBack.NextRunUtc:O} ({readBack.Schedule.TimeZone})."
                    : editedEmail ? $"Updated the scheduled email {readBack.Title}. The replacement payload has not been sent yet."
                    : pausedBrief ? $"Paused {readBack.Title}. No new occurrence will run until you resume it."
                    : resumedBrief ? $"Resumed {readBack.Title}. Its next run is {readBack.NextRunUtc:O} ({readBack.Schedule.TimeZone})."
                    : $"Updated {readBack.Title}. The replacement scope will apply to its next occurrence.";
                run.State = RunState.Succeeded;
                run.Summary = cancelled ? $"Cancelled delegated work · {readBack.Title}" : rescheduled
                    ? $"Rescheduled reminder · {readBack.Title}" : editedEmail ? $"Updated scheduled email · {readBack.Title}"
                    : pausedBrief ? $"Paused recurring brief · {readBack.Title}" : resumedBrief ? $"Resumed recurring brief · {readBack.Title}"
                    : $"Updated recurring brief · {readBack.Title}";
                run.Validation = new(true, ["Exact approved change applied", "Delegated job verified by ID and version read-back", "Grant version changed with the job"], []);
                var eventType = cancelled ? "delegation.job.cancelled.by-chat" : rescheduled ? "delegation.job.rescheduled.by-chat" : editedEmail
                    ? "delegation.email.edited.by-chat" : pausedBrief ? "delegation.brief.paused.by-chat" : resumedBrief
                    ? "delegation.brief.resumed.by-chat" : "delegation.brief.edited.by-chat";
                store.Save(run, eventType,
                    new { receipt = run.Capabilities[^1], run.Validation }, new(run.Id + "-assistant", "assistant", reply, clock.GetUtcNow()));
                return run;
            }
            if (todoBatch)
            {
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; no To-dos were created.");
                var proposal = ParseTodoBatch(approval.Action);
                if (!TodoSources(run).Any(source => source.Reference == proposal.SourceReference && source.Version == proposal.SourceVersion))
                    throw new InvalidOperationException("The supplied reading changed or is unavailable. Start a new extraction before writing To-dos.");
                run.Approval = approval with { Decision = "approved" }; run.State = RunState.Running;
                run.Summary = "Approval recorded · creating the exact To-do batch";
                store.Save(run, "approval.approved", new { approval = run.Approval, authority = "exact-todo-batch-v1", written = false });
                ReserveTool(run, approval.Action);
                var saved = store.CreateTodoBatch(approval.Id, proposal);
                var readBack = saved.Items.Select(item => store.Library().Single(current => current.Id == item.Id && current.Version == item.Version)).ToArray();
                var result = JsonSerializer.SerializeToElement(new { saved.Operation.Id, source = proposal.SourceReference,
                    items = readBack.Select(item => new { item.Id, item.Title, item.Due, item.Version }), verifiedByReadBack = true }, Wire.Json);
                run.Capabilities.Add(new("todo-batch-" + approval.Id, saved.Operation.InputHash, TodoBatchConversation.ToolName,
                    "owner-reviewed-todo-batch", clock.GetUtcNow(), result, false, JsonSerializer.SerializeToElement(proposal, Wire.Json)));
                var unresolved = proposal.Items.Count(item => !string.IsNullOrWhiteSpace(item.Ambiguity));
                var reply = $"Created {readBack.Length} editable To-do{(readBack.Length == 1 ? "" : "s")} from {proposal.SourceReference}." +
                    (unresolved == 0 ? "" : $" {unresolved} item{(unresolved == 1 ? " keeps" : "s keep")} an unresolved detail in its notes.");
                run.State = RunState.Succeeded; run.Summary = $"Created {readBack.Length} source-linked To-do{(readBack.Length == 1 ? "" : "s")} · read-back verified";
                run.Validation = new(true, ["Exact approved batch created", "Created items verified by ID and version read-back", "Source reference retained"],
                    unresolved == 0 ? [] : ["Unresolved source details remain visibly attached to the affected To-dos"]);
                store.Save(run, "todo.batch.completed", new { receipt = run.Capabilities[^1], run.Validation },
                    new(run.Id + "-assistant", "assistant", reply, clock.GetUtcNow()));
                return run;
            }
            if (delegation)
            {
                if (delegations == null) throw new InvalidOperationException("The durable delegation scheduler is unavailable.");
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; the reminder was not scheduled.");
                var proposal = ParseReminderProposal(approval.Action);
                run.Approval = approval with { Decision = "approved" };
                run.State = RunState.Running;
                run.Summary = "Approval recorded · persisting the reminder";
                store.Save(run, "approval.approved", new { approval = run.Approval, authority = "exact-reminder-v1", dispatched = false });
                ReserveTool(run, approval.Action);
                var created = delegations.CreateReminder(proposal.Title, proposal.Message, proposal.DueUtc, proposal.TimeZone,
                    requestedAt: run.DelegationRequestedAt, sourceRunId: run.Id, immediate: proposal.Immediate);
                var result = JsonSerializer.SerializeToElement(new
                {
                    created.Job.Id,
                    created.Job.Title,
                    created.Job.State,
                    created.Job.NextRunUtc,
                    created.Job.Schedule.TimeZone,
                    target = created.Job.Action.Target,
                    grantId = created.Grant.Id,
                    scheduleVersion = created.Job.ScheduleVersion,
                    persisted = true
                }, Wire.Json);
                run.Capabilities.Add(new("delegation-" + created.Job.Id, Wire.Hash(Wire.Pack(new { approval.Action, approval.ResourceVersion })),
                    DelegationConversation.ToolName, "owner-reviewed-delegation", clock.GetUtcNow(), result, false,
                    JsonSerializer.SerializeToElement(proposal, Wire.Json)));
                run.State = RunState.Paused;
                run.Summary = $"Reminder scheduled · {created.Job.NextRunUtc:O}";
                store.Save(run, "delegation.reminder.scheduled", new { job = created.Job, grant = created.Grant, receipt = run.Capabilities[^1] });
                _ = Task.Run(() => Execute(id));
                return run;
            }
            if (emailDelegation)
            {
                if (delegations == null || connectedTools == null) throw new InvalidOperationException("Durable connected email is unavailable on this host.");
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; the email was not scheduled.");
                var proposal = ParseApprovedEmail(approval);
                var current = connectedTools.Snapshot().SingleOrDefault(tool =>
                    tool.ConnectorId == proposal.Email.Tool.ConnectorId &&
                    tool.RemoteName == proposal.Email.Tool.RemoteName &&
                    tool.ModelName == proposal.Email.Tool.ModelName &&
                    DelegationEmailConversation.ToolVersion(tool) == DelegationEmailConversation.ToolVersion(proposal.Email.Tool));
                if (current == null) throw new InvalidOperationException("The reviewed email connector changed or is unavailable. Reconnect it and start a new schedule.");
                run.Approval = approval with { Decision = "approved" };
                run.State = RunState.Running;
                run.Summary = "Approval recorded · persisting the exact email";
                store.Save(run, "approval.approved", new { approval = run.Approval, authority = "exact-email-v1", credentialsExposed = false, sent = false });
                ReserveTool(run, approval.Action);
                var created = delegations.CreateEmail(proposal, requestedAt: run.DelegationRequestedAt, sourceRunId: run.Id);
                var result = JsonSerializer.SerializeToElement(new
                {
                    created.Job.Id,
                    created.Job.Title,
                    created.Job.State,
                    created.Job.NextRunUtc,
                    created.Job.Schedule.TimeZone,
                    sender = proposal.Email.SenderConnection,
                    recipient = proposal.Email.Recipient,
                    grantId = created.Grant.Id,
                    scheduleVersion = created.Job.ScheduleVersion,
                    persisted = true,
                    sent = false
                }, Wire.Json);
                run.Capabilities.Add(new("delegation-email-" + created.Job.Id,
                    Wire.Hash(Wire.Pack(new { approval.Action, approval.ResourceVersion })), approval.Action.Name,
                    "owner-reviewed-delegation", clock.GetUtcNow(), result, false,
                    JsonSerializer.SerializeToElement(proposal, Wire.Json)));
                run.State = RunState.Paused;
                run.Summary = $"Email scheduled · {created.Job.NextRunUtc:O}";
                store.Save(run, "delegation.email.scheduled", new { job = created.Job, grant = created.Grant, receipt = run.Capabilities[^1], sent = false });
                _ = Task.Run(() => Execute(id));
                return run;
            }
            if (briefDelegation)
            {
                if (delegations == null || connectedTools == null) throw new InvalidOperationException("Durable recurring briefs are unavailable on this host.");
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; the brief was not scheduled.");
                var proposal = ParseApprovedBrief(approval);
                var current = connectedTools.Snapshot();
                if (!current.Any(tool => DelegationEmailConversation.ToolVersion(tool) == DelegationEmailConversation.ToolVersion(proposal.Brief.Email.Tool)) ||
                    !current.Any(tool => DelegationEmailConversation.ToolVersion(tool) == DelegationEmailConversation.ToolVersion(proposal.Brief.Calendar.Tool)))
                    throw new InvalidOperationException("A reviewed brief connector changed or is unavailable. Reconnect it and start a new schedule.");
                run.Approval = approval with { Decision = "approved" }; run.State = RunState.Running;
                run.Summary = "Approval recorded · persisting the recurring brief";
                store.Save(run, "approval.approved", new { approval = run.Approval, authority = "exact-brief-v1", credentialsExposed = false, sourceMutation = false });
                ReserveTool(run, approval.Action);
                var created = delegations.CreateBrief(proposal, requestedAt: run.DelegationRequestedAt, sourceRunId: run.Id);
                var result = JsonSerializer.SerializeToElement(new
                {
                    created.Job.Id,
                    created.Job.Title,
                    created.Job.State,
                    created.Job.NextRunUtc,
                    created.Job.Schedule.TimeZone,
                    created.Job.Schedule.LocalTime,
                    destination = created.Job.Action.Target,
                    grantId = created.Grant.Id,
                    scheduleVersion = created.Job.ScheduleVersion,
                    proposal.Brief.EmailSelectionRule,
                    emailAccount = proposal.Brief.Email.Tool.ConnectorName,
                    calendarAccount = proposal.Brief.Calendar.Tool.ConnectorName,
                    persisted = true,
                    sourceMutation = false
                }, Wire.Json);
                run.Capabilities.Add(new("delegation-brief-" + created.Job.Id,
                    Wire.Hash(Wire.Pack(new { approval.Action, approval.ResourceVersion })), approval.Action.Name,
                    "owner-reviewed-delegation", clock.GetUtcNow(), result, false,
                    JsonSerializer.SerializeToElement(proposal, Wire.Json)));
                run.State = RunState.Paused;
                run.Summary = $"Weekday brief scheduled · next {created.Job.NextRunUtc:O}";
                store.Save(run, "delegation.brief.scheduled", new { job = created.Job, grant = created.Grant, receipt = run.Capabilities[^1], sourceMutation = false });
                _ = Task.Run(() => Execute(id));
                return run;
            }
            if (inboxWatch)
            {
                if (delegations == null || connectedTools == null) throw new InvalidOperationException("Durable inbox watches are unavailable on this host.");
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; the inbox watch was not scheduled.");
                var proposal = ParseApprovedInboxWatch(approval);
                var current = connectedTools.Snapshot();
                if (!current.Any(tool => DelegationEmailConversation.ToolVersion(tool) == DelegationEmailConversation.ToolVersion(proposal.Watch.Email.Tool)))
                    throw new InvalidOperationException("The reviewed mail connector changed or is unavailable. Reconnect it and start a new inbox watch.");
                run.Approval = approval with { Decision = "approved" }; run.State = RunState.Running;
                run.Summary = "Approval recorded · persisting the read-only inbox watch";
                store.Save(run, "approval.approved", new { approval = run.Approval, authority = "exact-inbox-watch-v1", credentialsExposed = false, sourceMutation = false });
                ReserveTool(run, approval.Action);
                var created = delegations.CreateInboxWatch(proposal, requestedAt: run.DelegationRequestedAt, sourceRunId: run.Id);
                var result = JsonSerializer.SerializeToElement(new
                {
                    created.Job.Id, created.Job.Title, created.Job.State, created.Job.NextRunUtc,
                    intervalMinutes = created.Job.Schedule.IntervalMinutes, created.Job.Schedule.TimeZone,
                    destination = created.Job.Action.Target, grantId = created.Grant.Id,
                    scheduleVersion = created.Job.ScheduleVersion, proposal.Watch.ImportanceInstruction,
                    mailAccount = proposal.Watch.Email.Tool.ConnectorName, persisted = true, sourceMutation = false,
                    authorizationExpires = created.Grant.Expires
                }, Wire.Json);
                run.Capabilities.Add(new("delegation-inbox-watch-" + created.Job.Id,
                    Wire.Hash(Wire.Pack(new { approval.Action, approval.ResourceVersion })), approval.Action.Name,
                    "owner-reviewed-delegation", clock.GetUtcNow(), result, false, JsonSerializer.SerializeToElement(proposal, Wire.Json)));
                run.State = RunState.Paused; run.Summary = $"Inbox watch scheduled · first check {created.Job.NextRunUtc:O}";
                store.Save(run, "delegation.inbox-watch.scheduled", new { job = created.Job, grant = created.Grant,
                    receipt = run.Capabilities[^1], sourceMutation = false });
                _ = Task.Run(() => Execute(id));
                return run;
            }
            if (connected)
            {
                if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new InvalidOperationException("Tool budget exhausted; the connected action was not dispatched.");
                run.Approval = approval with { Decision = "approved" };
                run.State = RunState.Running;
                run.Summary = $"Approved · contacting {connectedTool.ConnectorName}";
                store.Save(run, "approval.approved", new { approval = run.Approval, connectedTool.ConnectorId, connectedTool.RemoteName, credentialsExposed = false });
                _ = Task.Run(() => ExecuteConnectedApproval(id, approval.Id));
                return run;
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
        if (cancellations.TryRemove(id, out var cts))
        {
            try { await cts.CancelAsync(); }
            catch (ObjectDisposedException) { /* Completion won the race; the saved task still honors cancellation. */ }
        }
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
                if (run.Execution != null) PauseExecutionClock(run);
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
                AssertProposalReview(run, a, reconciling: true);
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
    private static string SafeError(Exception ex) => ex is HttpRequestException http ? http.StatusCode switch
    {
        System.Net.HttpStatusCode.TooManyRequests => "Provider is busy or rate-limited (429). No automatic retry was started.",
        System.Net.HttpStatusCode.GatewayTimeout or System.Net.HttpStatusCode.RequestTimeout => "Provider timed out. No completion was saved and no automatic retry was started.",
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "Provider rejected authentication. Check the saved connection in Settings.",
        System.Net.HttpStatusCode.BadGateway => "Provider bridge failed before returning a usable reply (502). Inspect its diagnostic receipt; no automatic retry was started.",
        _ => "Provider request failed. Inspect the connection and provider diagnostics; no automatic retry was started."
    } : ex is IOException ? "Storage or provider I/O failed. Inspect configuration." : ex.Message;
}
public sealed class BudgetException(string message) : Exception(message);
