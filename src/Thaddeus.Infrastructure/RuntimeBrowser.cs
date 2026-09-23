using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private readonly object browserAdmissionGate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> browserStops = new();
    public bool BrowserAvailable => browserSession?.Available == true;
    private BrowserToolContext? BrowserObservation(Run run) => run.BrowserAllowance == null ? null : new(run.BrowserAllowance, run.Browser);
    private static bool BrowserIsClosed(BrowserTaskState task) => task.Phase is "closed" or "interrupted";
    private static bool BrowserUnknown(BrowserTaskState task) => task.Receipts.Any(receipt => receipt.State == "outcome-unknown");
    private static string BrowserBinding(BrowserTaskState task) => Wire.Hash(Wire.Pack(new {
        task.SessionId, task.Epoch, task.Scope, task.AuthorizedUntil, page = task.Page?.Version, task.PendingAction
    }));
    private static string BrowserDigest(string runId, string approvalId, ToolRequest action, string version, DateTimeOffset expiry) =>
        Wire.Hash(Wire.Pack(new { runId, approvalId, action, version, expiry, authority = "exact-browser-v1" }));
    private void ReviewBrowser(Run run, ToolRequest action)
    {
        var task = run.Browser!;
        var expiry = clock.GetUtcNow().AddMinutes(15);
        if (task.AuthorizedUntil is { } until && until < expiry) expiry = until;
        var id = Guid.NewGuid().ToString("N"); var version = BrowserBinding(task);
        run.ApprovalPolicy = null;
        run.Approval = new(id, run.Id, action, BrowserDigest(run.Id, id, action, version, expiry), version, expiry);
        run.State = RunState.AwaitingApproval; run.DraftText = "";
        run.Summary = task.Phase == "review" ? "Review the website task and its allowance before Chrome opens" : "Review this exact browser action";
        store.Save(run, "browser.review.requested", new { run.Approval, task.SessionId, task.Phase });
    }
    private async Task<bool> HandleBrowserAction(Run run, ToolRequest action, CancellationToken cancellation)
    {
        if (action.Name == BrowserConversation.StartTool)
        {
            if (!BrowserAvailable || run.BrowserAllowance == null || run.Browser != null)
                throw new InvalidOperationException("Chrome is unavailable or this reply already has a browser task.");
            var proposal = BrowserConversation.Parse<BrowserConversation.Proposal>(action);
            var scope = BrowserTaskPolicy.ValidateScope(new(proposal.Objective, proposal.StartUrl, proposal.Hosts, run.BrowserAllowance));
            lock (browserAdmissionGate)
            {
                if (store.List().Any(other => other.Id != run.Id && other.Browser is { } task && !BrowserIsClosed(task)))
                    throw new InvalidOperationException("Close the existing browser task before reviewing another.");
                run.Browser = new() { Scope = scope };
                ReviewBrowser(run, new(BrowserConversation.StartTool, "", Wire.Pack(scope)));
            }
            return true;
        }
        if (run.Browser is not { } active) return false;
        if (action.Name != BrowserConversation.ActionTool || active.Phase != "working" || active.Page == null)
            throw new InvalidOperationException("Only the reviewed browser task's advertised actions are available.");
        AssertBrowserAuthority(run);
        var proposed = BrowserConversation.Parse<BrowserAction>(action);
        BrowserTaskPolicy.ValidateAction(active.Scope, active.Page, proposed);
        if (BrowserTaskPolicy.NeedsReview(proposed))
        {
            active.PendingAction = proposed; active.Phase = "review-action";
            ReviewBrowser(run, new(BrowserConversation.ActionTool, "", Wire.Pack(proposed)));
        }
        else await DispatchBrowser(run, proposed, cancellation);
        return true;
    }
    private void AssertBrowserAuthority(Run run)
    {
        var task = run.Browser ?? throw new InvalidOperationException("No browser task exists.");
        if (task.AuthorizedUntil == null || task.AuthorizedUntil <= clock.GetUtcNow() || BrowserUnknown(task))
            throw new InvalidOperationException("Browser authorization expired or an external outcome is unknown. Inspect the receipts and start a newly reviewed task.");
        var elapsed = task.ActiveSeconds + (task.ActiveSince is { } since ? Math.Max(0, (clock.GetUtcNow() - since).TotalSeconds) : 0);
        if (elapsed >= task.Scope.Limits.Seconds)
            throw new BudgetException("The browser task's active time allowance is exhausted.");
        if (browserSession == null) throw new InvalidOperationException("The browser runtime is unavailable.");
    }
    private async Task PrepareBrowserExecution(Run run, CancellationToken cancellation)
    {
        var task = run.Browser!; AssertBrowserAuthority(run);
        if (task.Phase == "opening")
        {
            ReserveTool(run, new("browser.open", task.Scope.StartUrl));
            task.Page = await browserSession!.Start(task.SessionId, task.Scope, cancellation);
            task.Phase = "working";
            store.Save(run, "browser.opened", new { task.SessionId, task.Page });
        }
        else if (task.Phase == "action-approved")
        {
            var approval = run.Approval;
            if (approval == null || approval.Decision != "approved" || approval.Expires <= clock.GetUtcNow() ||
                approval.ResourceVersion != BrowserBinding(task) || task.PendingAction == null)
                throw new InvalidOperationException("Browser approval changed or expired before dispatch. No action was sent.");
            await DispatchBrowser(run, task.PendingAction, cancellation);
        }
        else if (task.Phase == "resuming")
        {
            ReserveTool(run, new("browser.snapshot", ""));
            task.Page = await browserSession!.Observe(task.SessionId, cancellation);
            task.Phase = "working";
            store.Save(run, "browser.resumed", new { task.SessionId, task.Page });
        }
    }
    private async Task DispatchBrowser(Run run, BrowserAction action, CancellationToken cancellation)
    {
        var task = run.Browser!; AssertBrowserAuthority(run);
        // Read back before consuming approval. A changed page earns a new review, not a hopeful click.
        if (run.ToolCalls >= run.Goal.Limits.ToolCalls) throw new BudgetException("Browser action allowance exhausted before reading or dispatch.");
        var current = await browserSession!.Observe(task.SessionId, cancellation);
        BrowserTaskPolicy.ValidateAction(task.Scope, current, action);
        ReserveTool(run, new(BrowserConversation.ActionTool, "", Wire.Pack(action)));
        var receipt = new BrowserActionReceipt(Guid.NewGuid().ToString("N"), action, "outcome-unknown", clock.GetUtcNow(),
            "Dispatch recorded; the actual result is not yet known.");
        task.Receipts.Add(receipt); task.PendingAction = null;
        if (run.Approval is { Decision: "approved" } approval && BrowserTaskPolicy.NeedsReview(action))
            run.Approval = approval with { Decision = "consumed" };
        store.Save(run, "browser.dispatch", receipt);
        // Never replay this intent after cancellation, transport loss, or host restart.
        task.Page = await browserSession.Act(task.SessionId, action, cancellation);
        task.Receipts[^1] = receipt with { State = "completed", Summary = "Browser operation returned a current page.", Page = task.Page };
        task.Phase = "working";
        store.Save(run, "browser.result", task.Receipts[^1]);
    }
    private Task<Run> DecideBrowser(Run run, string approvalId, string digest, bool allow, string? remember)
    {
        var task = run.Browser!; var approval = run.Approval;
        if (remember != null) throw new ArgumentException("Browser reviews apply once to this task and page; they cannot be remembered.");
        if (run.State != RunState.AwaitingApproval || approval == null || approval.Decision != "pending" || approval.Id != approvalId ||
            approval.Digest != digest || approval.Expires <= clock.GetUtcNow() || approval.ResourceVersion != BrowserBinding(task) ||
            digest != BrowserDigest(run.Id, approvalId, approval.Action, approval.ResourceVersion, approval.Expires) ||
            task.Phase is not ("review" or "review-action"))
            throw new InvalidOperationException("Browser approval is stale, changed, expired or already decided.");
        if (!allow)
        {
            run.Approval = approval with { Decision = "denied" };
            task.PendingAction = null; task.Epoch++;
            task.Phase = task.Phase == "review" ? "closed" : "paused";
            run.State = RunState.Denied; run.Summary = "Browser action denied · no action dispatched";
            store.Save(run, "browser.denied", run.Approval);
            return Task.FromResult(run);
        }
        if (task.Phase == "review")
        {
            if (!BrowserAvailable) throw new InvalidOperationException("Install Chrome and use a package containing the browser runtime.");
            // This separately displayed allowance is explicitly reviewed; proposal usage remains charged.
            run.Goal = run.Goal with { Limits = task.Scope.Limits };
            task.AuthorizedUntil = clock.GetUtcNow().AddHours(2);
            task.Phase = "opening";
        }
        else { AssertBrowserAuthority(run); task.Phase = "action-approved"; }
        run.Approval = approval with { Decision = "approved" }; run.State = RunState.Paused;
        run.Summary = "Browser review accepted · preparing the authorized operation";
        store.Save(run, "browser.approved", new { run.Approval, task.AuthorizedUntil, task.Scope.Limits });
        _ = Task.Run(() => Execute(run.Id));
        return Task.FromResult(run);
    }
    public async Task<Run> ControlBrowser(string id, string command)
    {
        if (command is not ("pause" or "takeover" or "resume" or "close")) throw new ArgumentException("Unknown browser control.");
        if (command != "resume") browserStops[id] = 0;
        if (command != "resume" && cancellations.TryRemove(id, out var cancellation))
            try { await cancellation.CancelAsync(); } catch (ObjectDisposedException) { }
        await Gate(id).WaitAsync();
        try
        {
            var run = store.Get(id) ?? throw new ArgumentException("Run not found.");
            var task = run.Browser ?? throw new InvalidOperationException("This task has no browser session.");
            if (command == "resume")
            {
                if (task.Phase is not ("paused" or "takeover") || browserSession?.IsOpen(task.SessionId) != true)
                    throw new InvalidOperationException("This browser cannot resume. Close this task and review a new one.");
                if (run.ChargedTokens >= run.Goal.Limits.MaxTotalTokens)
                    throw new InvalidOperationException("This Chrome task's token allowance is exhausted. Close Chrome and start a new reviewed task.");
                AssertBrowserAuthority(run); task.Phase = "resuming"; run.State = RunState.Paused;
                run.Summary = "Resuming from a fresh page; previous action approval is invalid";
                store.Save(run, "browser.resume.requested", new { task.SessionId });
                _ = Task.Run(() => Execute(id)); return run;
            }
            if (run.Approval is { } approval) run.Approval = approval with { Decision = "cancelled" };
            task.PendingAction = null; task.Epoch++;
            if (command == "close")
            {
                if (browserSession != null) await browserSession.Close(task.SessionId);
                task.Phase = "closed"; task.AuthorizedUntil = null;
                if (run.State != RunState.Succeeded) run.State = BrowserUnknown(task) ? RunState.NeedsAttention : RunState.Cancelled;
                run.Summary = BrowserUnknown(task) ? "Browser closed · an external outcome remains unknown; inspect receipts" : "Browser closed · results retained";
            }
            else
            {
                if (task.Phase is "review" or "closed" or "interrupted" or "finished")
                    throw new InvalidOperationException("This browser task is not running. Start a newly reviewed task.");
                task.Phase = command == "takeover" ? "takeover" : "paused";
                run.State = BrowserUnknown(task) ? RunState.NeedsAttention : RunState.Paused;
                run.Summary = BrowserUnknown(task) ? "Stopped · an external outcome is unknown; no automatic retry" :
                    browserSession?.IsOpen(task.SessionId) != true ? "Stopped · browser connection closed; start a newly reviewed task" :
                    command == "takeover" ? "Your turn in Chrome · AI is stopped until you resume" : "Browser task paused · AI is stopped";
            }
            store.Save(run, "browser.control", new { command, task.Phase, task.Epoch, run.Summary });
            return run;
        }
        finally { browserStops.TryRemove(id, out _); Gate(id).Release(); }
    }
    private void PauseBrowserClock(Run run)
    {
        if (run.Browser is not { ActiveSince: { } since } task) return;
        task.ActiveSeconds += Math.Max(0, (clock.GetUtcNow() - since).TotalSeconds); task.ActiveSince = null;
        store.Save(run, "browser.active-time", new { task.ActiveSeconds });
    }
    private void RecoverBrowser(Run run)
    {
        if (run.Browser is not { } task || BrowserIsClosed(task)) return;
        PauseBrowserClock(run);
        run.ChargedTokens += run.ReservedTokens; run.ReservedTokens = 0;
        for (var i = 0; i < run.ModelStages.Count; i++)
            if (run.ModelStages[i].Status == "reserved") run.ModelStages[i] = run.ModelStages[i] with { Status = "interrupted" };
        task.Phase = "interrupted"; task.AuthorizedUntil = null; task.PendingAction = null; task.Epoch++;
        if (run.Approval is { } approval) run.Approval = approval with { Decision = "cancelled" };
        if (run.State != RunState.Succeeded) run.State = RunState.NeedsAttention;
        run.Summary = BrowserUnknown(task) ? "Host restarted · browser outcome unknown; inspect receipts, do not replay" :
            "Host restarted · browser authorization ended; start a newly reviewed task";
        store.Save(run, "browser.recovered", new { task.Phase, run.Summary });
    }
}
