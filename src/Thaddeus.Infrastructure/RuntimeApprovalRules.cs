using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Runtime
{
    private const string ApprovalRulesSetting = "approval-rules-v1";

    public ApprovalRule[] ApprovalRules()
    {
        ApprovalRule[] saved;
        try { saved = store.Setting(ApprovalRulesSetting) is { } json ? Wire.Unpack<ApprovalRule[]>(json) : []; }
        catch { saved = []; }
        return saved.Where(rule => rule.Scope.Length is > 0 and <= 160 && rule.Label.Length is > 0 and <= 160 &&
                rule.Decision is "allow" or "deny")
            .DistinctBy(rule => rule.Scope, StringComparer.Ordinal).OrderBy(rule => rule.Label, StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
    }

    public ApprovalRule[] RemoveApprovalRule(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 160) throw new ArgumentException("Choose a remembered approval rule.");
        var rules = ApprovalRules().Where(rule => !string.Equals(rule.Scope, scope, StringComparison.Ordinal)).ToArray();
        store.Setting(ApprovalRulesSetting, Wire.Pack(rules));
        return rules;
    }

    private void RememberApprovalRule(Run run, Approval approval, string decision)
    {
        if (decision is not ("allow" or "deny")) throw new ArgumentException("A remembered decision must be always allow or always deny.");
        var policy = DescribeApprovalPolicy(run, approval);
        var next = ApprovalRules().Where(rule => !string.Equals(rule.Scope, policy.Scope, StringComparison.Ordinal)).ToList();
        next.Add(new(policy.Scope, policy.Label, decision, clock.GetUtcNow()));
        store.Setting(ApprovalRulesSetting, Wire.Pack(next.OrderBy(rule => rule.Label, StringComparer.OrdinalIgnoreCase).ToArray()));
        run.ApprovalPolicy = policy with { Decision = decision };
        store.Save(run, "approval.policy.remembered", new { policy.Scope, policy.Label, decision,
            appliesToFutureReviews = true, currentApproval = approval.Id });
    }

    private void PrepareApprovalPolicy(Run run)
    {
        if (run.Approval is not { Decision: "pending" } approval) return;
        var described = DescribeApprovalPolicy(run, approval);
        var rule = ApprovalRules().SingleOrDefault(item => string.Equals(item.Scope, described.Scope, StringComparison.Ordinal));
        run.ApprovalPolicy = described with { Decision = rule?.Decision ?? "ask" };
        store.Save(run, rule == null ? "approval.policy.ask" : "approval.policy.matched", new
        {
            described.Scope,
            described.Label,
            decision = rule?.Decision ?? "ask",
            exactReviewRetained = true
        });
        if (rule == null) return;
        _ = Task.Run(async () =>
        {
            try { await Decide(run.Id, approval.Id, approval.Digest, rule.Decision == "allow"); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
            {
                var current = store.Get(run.Id);
                if (current?.State == RunState.AwaitingApproval)
                    store.Save(current, "approval.policy.failed", new { described.Scope, classification = error.GetType().Name,
                        message = "The remembered rule could not be applied. Review this action manually." });
            }
        });
    }

    private ApprovalPolicy DescribeApprovalPolicy(Run run, Approval approval)
    {
        if (IsSoulApproval(approval)) return new("profile:soul", "Update Soul from Chat", "ask");
        if (IsUserApproval(approval)) return new("profile:user", "Update User profile from Chat", "ask");
        if (IsTodoBatchApproval(approval)) return new("todo:create-batch", "Create To-dos from supplied material", "ask");
        if (IsInboxWatchApproval(run, approval))
        {
            var proposal = ParseApprovedInboxWatch(approval);
            return Bound("inbox-watch", "Create read-only inbox watches with " + proposal.Watch.Email.Tool.ConnectorName,
                DelegationEmailConversation.ToolVersion(proposal.Watch.Email.Tool));
        }
        if (IsBriefDelegationApproval(run, approval))
        {
            var proposal = ParseApprovedBrief(approval);
            return Bound("brief", "Schedule recurring email and calendar briefs",
                DelegationEmailConversation.ToolVersion(proposal.Brief.Email.Tool),
                DelegationEmailConversation.ToolVersion(proposal.Brief.Calendar.Tool));
        }
        if (IsEmailDelegationApproval(run, approval))
        {
            var proposal = ParseApprovedEmail(approval);
            return Bound("email", "Schedule email with " + proposal.Email.SenderConnection,
                DelegationEmailConversation.ToolVersion(proposal.Email.Tool));
        }
        if (IsDelegationApproval(run, approval)) return new("reminder:schedule", "Schedule reminders and notifications", "ask");
        if (IsDelegationManagementApproval(approval)) return new("delegation:" + approval.Action.Name,
            approval.Action.Name switch
            {
                DelegationManagementConversation.CancelTool => "Cancel delegated work",
                DelegationManagementConversation.RescheduleTool => "Reschedule reminders",
                DelegationManagementConversation.EditEmailTool => "Edit scheduled emails",
                DelegationManagementConversation.PauseBriefTool => "Pause recurring briefs",
                DelegationManagementConversation.ResumeBriefTool => "Resume recurring briefs",
                _ => "Edit recurring briefs"
            }, "ask");
        if (IsConnectedApproval(run, approval, out var tool))
            return Bound("connected-tool", $"Use {tool.ConnectorName} · {tool.RemoteName}", tool.ConnectorId, tool.RemoteName, tool.ConnectionVersion);
        if (approval.Action.Name == "knowledge.write") return new("knowledge:write-plan", "Write generated plans", "ask");
        return Bound("action", "Use " + approval.Action.Name, approval.Action.Name, approval.Action.Path);
    }

    private static ApprovalPolicy Bound(string kind, string label, params string[] binding) =>
        new(kind + ":" + Wire.Hash(string.Join("\n", binding)), label.Length <= 160 ? label : label[..160], "ask");

    public static bool ApprovalSettingsIntent(string message) => !string.IsNullOrWhiteSpace(message) &&
        System.Text.RegularExpressions.Regex.IsMatch(message,
            @"^\s*(?:(?:show|open|manage|change|review)\s+(?:my\s+)?(?:remembered\s+)?(?:approval|permission)\s+(?:rules|settings|preferences)|what\s+(?:approval|permission)s?\s+(?:do\s+you\s+remember|are\s+remembered))\s*[?.!]*\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public Run PrepareApprovalSettings(string message, ProviderSnapshot provider)
    {
        lock (conversationGate)
        {
            var run = Build(new(message, [], "plans/", [new("Remembered approval settings opened", "deterministic")],
                new(ModelCalls: 0, ToolCalls: 0, Seconds: 30, Repairs: 0, MaxOutputTokens: 128, MaxTotalTokens: 0), provider, "conversation"));
            run.SettingsSection = "approvals";
            run.ConversationContext = ConversationHistory();
            run.State = RunState.Succeeded;
            run.Summary = "Approval settings ready · no model call";
            run.DraftText = "I’ve opened your remembered approval choices. Remove any rule to make me ask again next time.";
            var now = clock.GetUtcNow();
            store.Save(run, "conversation.approval-settings.accepted", new { section = run.SettingsSection, modelCalls = 0 },
                new(run.Id + "-user", "user", message, now));
            store.Save(run, "conversation.approval-settings.ready", new { section = run.SettingsSection, modelCalls = 0 },
                new(run.Id + "-assistant", "assistant", run.DraftText, now));
            return run;
        }
    }
}
