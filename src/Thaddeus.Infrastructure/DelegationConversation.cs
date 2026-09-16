using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public static class DelegationConversation
{
    public const string ToolName = "delegation_schedule_reminder";

    public const string Instructions = """
        You can propose a durable reminder when the user asks to be reminded at a future time. The host will show the exact reminder and require approval before anything is scheduled.
        The request timestamp and local timezone below are frozen admission facts. Resolve relative dates from that timestamp. If the user did not provide a consequential detail, especially a time of day, ask one concise question instead of calling the tool.
        Use an absolute ISO-8601 dueUtc value and the exact supplied timezone identifier. Do not invent people, message content, dates, or times. Do not claim the reminder exists until a successful tool receipt appears.
        At firing time the durable host delivers the exact approved text without another model call. A sleeping, shut-down, or offline host cannot deliver a local reminder.
        """;

    public static object Schema() => new
    {
        type = "function",
        function = new
        {
            name = ToolName,
            description = "Propose one exact future reminder for owner review and durable host scheduling.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    title = new { type = "string", description = "Short reminder title." },
                    message = new { type = "string", description = "Exact notification text." },
                    dueUtc = new { type = "string", description = "Absolute ISO-8601 UTC instant, such as 2026-09-17T21:00:00Z." },
                    timeZone = new { type = "string", description = "Exact host-supplied timezone identifier." }
                },
                required = new[] { "title", "message", "dueUtc", "timeZone" },
                additionalProperties = false
            }
        }
    };
}

public sealed record ReminderProposal(string Title, string Message, DateTimeOffset DueUtc, string TimeZone);

public sealed partial class Runtime
{
    private DelegationToolContext? DelegationObservation(Run run)
    {
        if (delegations == null || run.DelegationRequestedAt == null || string.IsNullOrWhiteSpace(run.DelegationTimeZone)) return null;
        var receipts = run.Capabilities.Where(receipt => receipt.Authority == "owner-reviewed-delegation" || receipt.Name == DelegationConversation.ToolName ||
            DelegationManagementConversation.ToolNames.Contains(receipt.Name, StringComparer.Ordinal)).ToArray();
        var jobs = DelegationJobSummaries();
        return new(receipts, run.Approval == null && receipts.Length == 0 &&
            run.ToolCalls < run.Goal.Limits.ToolCalls && run.ModelCalls + 1 < run.Goal.Limits.ModelCalls,
            run.Approval == null && receipts.Length == 0 && run.ToolCalls < run.Goal.Limits.ToolCalls &&
                run.ModelCalls < run.Goal.Limits.ModelCalls && jobs.Any(job => CanCancelSummary(job) || CanRescheduleSummary(job)),
            run.DelegationRequestedAt.Value, run.DelegationTimeZone, jobs);
    }

    private static bool CanCancelSummary(DelegationJobSummary job) => !job.CancellationRequested &&
        job.State is not ("cancelled" or "completed" or "succeeded" or "failed" or "unknown" or "missed");
    private static bool CanRescheduleSummary(DelegationJobSummary job) => !job.CancellationRequested && job.Kind == "reminder" &&
        job.ScheduleKind == "once" && job.State == "scheduled";

    private bool HandleDelegationAction(Run run, ToolRequest action)
    {
        if (action.Name != DelegationConversation.ToolName) return false;
        if (delegations == null || run.DelegationRequestedAt == null || string.IsNullOrWhiteSpace(run.DelegationTimeZone))
            throw new ArgumentException("Durable reminders are unavailable on this host.");
        if (run.Approval != null || run.Capabilities.Any(receipt => receipt.Name == DelegationConversation.ToolName) ||
            run.ModelCalls >= run.Goal.Limits.ModelCalls || run.ToolCalls >= run.Goal.Limits.ToolCalls)
            throw new ArgumentException("No reminder proposal allowance remains. Reserve one model call for the final reply.");
        var proposal = ParseReminderProposal(action);
        if (proposal.TimeZone != run.DelegationTimeZone)
            throw new ArgumentException("The reminder timezone changed from the frozen request context. Ask the user to start a new request for another timezone.");
        if (proposal.DueUtc <= run.DelegationRequestedAt.Value)
            throw new ArgumentException("The reminder time must be after the original request timestamp.");
        new DelegationSchedule("once", proposal.DueUtc.ToUniversalTime(), proposal.TimeZone).Validate();
        var exactArguments = JsonSerializer.Serialize(proposal, Wire.Json);
        var exactAction = new ToolRequest(DelegationConversation.ToolName, "owner:windows+in-app", exactArguments);
        var scheduleVersion = Wire.Hash(Wire.Pack(new { kind = "reminder", exactAction, revision = 1 }));
        var expiry = clock.GetUtcNow().AddMinutes(15);
        var approvalId = Guid.NewGuid().ToString("N");
        var digest = DelegationApprovalDigest(run.Id, approvalId, exactAction, scheduleVersion, expiry);
        run.Approval = new(approvalId, run.Id, exactAction, digest, scheduleVersion, expiry);
        run.DraftText = "";
        run.State = RunState.AwaitingApproval;
        run.Summary = $"Review reminder · {proposal.DueUtc:O} · {proposal.TimeZone}";
        store.Save(run, "delegation.reminder.review", new
        {
            approval = run.Approval,
            reminder = proposal,
            target = exactAction.Path,
            authority = "exact-reminder-v1",
            persisted = false,
            dispatched = false
        });
        return true;
    }

    private static string DelegationApprovalDigest(string runId, string approvalId, ToolRequest action, string scheduleVersion, DateTimeOffset expiry) =>
        Wire.Hash(Wire.Pack(new { runId, approvalId, action, authority = "exact-reminder-v1", scheduleVersion, expiry }));

    private static bool IsDelegationApproval(Run run, Approval approval) =>
        run.DelegationRequestedAt != null && approval.Action.Name == DelegationConversation.ToolName &&
        approval.Action.Path == "owner:windows+in-app";

    private static ReminderProposal ParseReminderProposal(ToolRequest action)
    {
        using var parsed = JsonDocument.Parse(action.Content ?? "{}");
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 ||
            !root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("dueUtc", out var due) || due.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("timeZone", out var zone) || zone.ValueKind != JsonValueKind.String ||
            !Regex.IsMatch(due.GetString() ?? "", "(Z|[+-]\\d{2}:\\d{2})$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParse(due.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dueUtc))
            throw new ArgumentException("Malformed reminder proposal; expected only title, message, dueUtc and timeZone with an ISO-8601 due time.");
        var proposal = new ReminderProposal(title.GetString()!.Trim(), message.GetString()!.Trim(), dueUtc.ToUniversalTime(), zone.GetString()!.Trim());
        if (proposal.Title.Length is < 1 or > 200 || proposal.Message.Length is < 1 or > 2000 || proposal.TimeZone.Length is < 1 or > 100)
            throw new ArgumentException("The reminder proposal exceeds its review limits.");
        return proposal;
    }
}
