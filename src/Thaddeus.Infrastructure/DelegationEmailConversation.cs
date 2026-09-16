using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public static class DelegationEmailConversation
{
    public const string Instructions = """
        A connected MCP email-send tool may be scheduled only when it is explicitly advertised below. Credentials stay in the host broker.
        Compose the exact message now. Never guess a recipient, sender identity, ETA, excuse, address, date, or time. If a relationship such as "my boss" is not already resolved to an exact address in the conversation, ask for it. A draft-only request never authorizes sending.
        The host will show the sender connection, actual recipient, subject, body, absolute send time, timezone and exact tool arguments for one approval. Do not claim the email is scheduled before the scheduling receipt, sent before the provider receipt, or read by the recipient at all.
        """;

    public sealed record Shape(ConnectedToolDefinition Tool, string RecipientField, string? SubjectField, string BodyField);

    public static string ToolName(ConnectedToolDefinition tool) => "delegation_email_" + Wire.Hash(tool.ModelName)[..16];

    public static string ToolVersion(ConnectedToolDefinition tool) => Wire.Hash(Wire.Pack(new
    {
        tool.ConnectorId,
        tool.RemoteName,
        tool.ModelName,
        tool.ConnectionVersion,
        inputSchema = CanonicalJson(tool.InputSchema)
    }));

    private static string CanonicalJson(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
            return;
        }
        element.WriteTo(writer);
    }

    public static Shape[] Eligible(ConnectedToolDefinition[] tools) => tools.Select(ShapeFor).OfType<Shape>().ToArray();

    public static object Schema(Shape shape) => new
    {
        type = "function",
        function = new
        {
            name = ToolName(shape.Tool),
            description = $"Schedule one exact email through {shape.Tool.ConnectorName} / {shape.Tool.RemoteName}. Requires exact owner review; credentials remain on the host.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    dueUtc = new { type = "string", description = "Absolute ISO-8601 UTC send time." },
                    timeZone = new { type = "string", description = "Exact host-supplied timezone identifier." },
                    arguments = shape.Tool.InputSchema
                },
                required = new[] { "dueUtc", "timeZone", "arguments" }, additionalProperties = false
            }
        }
    };

    private static Shape? ShapeFor(ConnectedToolDefinition tool)
    {
        var identity = (tool.RemoteName + " " + tool.Description).ToLowerInvariant();
        if (tool.Effect == "read external data" || !(identity.Contains("email") || identity.Contains("mail")) ||
            !(identity.Contains("send") || identity.Contains("deliver"))) return null;
        if (tool.InputSchema.ValueKind != JsonValueKind.Object ||
            !tool.InputSchema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) return null;
        var names = properties.EnumerateObject().Select(property => property.Name).ToArray();
        var recipient = Field(names, "to", "recipient", "recipientEmail", "email", "emailAddress");
        var body = Field(names, "body", "message", "content", "text", "html");
        if (recipient == null || body == null) return null;
        return new(tool, recipient, Field(names, "subject", "title"), body);
    }

    private static string? Field(string[] names, params string[] candidates) =>
        candidates.Select(candidate => names.FirstOrDefault(name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(name => name != null);
}

public sealed record ScheduledEmailPayload(ConnectedToolDefinition Tool, JsonElement Arguments, string SenderConnection,
    string Recipient, string? Subject, string Body);
public sealed record EmailDelegationProposal(ScheduledEmailPayload Email, DateTimeOffset DueUtc, string TimeZone);

public sealed partial class Runtime
{
    private bool HandleDelegationEmailAction(Run run, ToolRequest action)
    {
        var shape = DelegationEmailConversation.Eligible(run.ConnectedTools)
            .SingleOrDefault(candidate => DelegationEmailConversation.ToolName(candidate.Tool) == action.Name);
        if (shape == null) return false;
        if (delegations == null || connectedTools == null || run.DelegationRequestedAt == null ||
            string.IsNullOrWhiteSpace(run.DelegationTimeZone)) throw new ArgumentException("Durable connected email is unavailable on this host.");
        if (run.Approval != null || run.Capabilities.Any(receipt => receipt.Authority == "owner-reviewed-delegation") ||
            run.ModelCalls >= run.Goal.Limits.ModelCalls || run.ToolCalls >= run.Goal.Limits.ToolCalls)
            throw new ArgumentException("No email proposal allowance remains. Reserve one model call for the final reply.");
        var proposal = ParseEmailDelegation(action, shape);
        if (proposal.TimeZone != run.DelegationTimeZone)
            throw new ArgumentException("The email timezone changed from the frozen request context. Start a new request for another timezone.");
        if (proposal.DueUtc <= run.DelegationRequestedAt.Value) throw new ArgumentException("The email send time must be after the original request timestamp.");
        new DelegationSchedule("once", proposal.DueUtc, proposal.TimeZone).Validate();
        var exactAction = new ToolRequest(action.Name, proposal.Email.Recipient, Wire.Pack(proposal));
        var scheduleVersion = Wire.Hash(Wire.Pack(new { kind = "email", exactAction, toolVersion = DelegationEmailConversation.ToolVersion(shape.Tool), revision = 1 }));
        var expiry = clock.GetUtcNow().AddMinutes(15);
        var approvalId = Guid.NewGuid().ToString("N");
        run.Approval = new(approvalId, run.Id, exactAction,
            EmailDelegationApprovalDigest(run.Id, approvalId, exactAction, scheduleVersion, expiry), scheduleVersion, expiry);
        run.DraftText = ""; run.State = RunState.AwaitingApproval;
        run.Summary = $"Review scheduled email · {proposal.Email.Recipient} · {proposal.DueUtc:O}";
        store.Save(run, "delegation.email.review", new { approval = run.Approval, proposal, authority = "exact-email-v1", credentialsExposed = false, persisted = false, sent = false });
        return true;
    }

    private static EmailDelegationProposal ParseEmailDelegation(ToolRequest action, DelegationEmailConversation.Shape shape)
    {
        using var parsed = JsonDocument.Parse(action.Content ?? "{}"); var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3 ||
            !root.TryGetProperty("dueUtc", out var due) || due.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("timeZone", out var zone) || zone.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object ||
            !Regex.IsMatch(due.GetString() ?? "", "(Z|[+-]\\d{2}:\\d{2})$", RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParse(due.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dueUtc))
            throw new ArgumentException("Malformed scheduled email proposal.");
        var recipient = RequiredString(arguments, shape.RecipientField, 500);
        var body = RequiredString(arguments, shape.BodyField, 20_000);
        var subject = shape.SubjectField == null ? null : OptionalString(arguments, shape.SubjectField, 500);
        var timeZone = zone.GetString()!.Trim();
        if (timeZone.Length is < 1 or > 100 || arguments.GetRawText().Length > 40_000)
            throw new ArgumentException("The scheduled email exceeds its review limits.");
        var payload = new ScheduledEmailPayload(shape.Tool, arguments.Clone(), shape.Tool.ConnectorName, recipient, subject, body);
        return new(payload, dueUtc.ToUniversalTime(), timeZone);
    }

    private static EmailDelegationProposal ParseApprovedEmail(Approval approval)
    {
        var proposal = Wire.Unpack<EmailDelegationProposal>(approval.Action.Content ?? "{}");
        if (proposal.Email.Recipient != approval.Action.Path || proposal.Email.Arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The approved email payload is malformed.");
        return proposal;
    }

    private static string RequiredString(JsonElement arguments, string name, int limit)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > limit)
            throw new ArgumentException($"The email tool needs a bounded string '{name}' field.");
        return value.GetString()!.Trim();
    }
    private static string? OptionalString(JsonElement arguments, string name, int limit)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > limit)
            throw new ArgumentException($"The email tool's '{name}' field is invalid.");
        return value.GetString()!.Trim();
    }

    private static string EmailDelegationApprovalDigest(string runId, string approvalId, ToolRequest action, string scheduleVersion, DateTimeOffset expiry) =>
        Wire.Hash(Wire.Pack(new { runId, approvalId, action, authority = "exact-email-v1", scheduleVersion, expiry }));

    private static bool IsEmailDelegationApproval(Run run, Approval approval) =>
        DelegationEmailConversation.Eligible(run.ConnectedTools).Any(shape => DelegationEmailConversation.ToolName(shape.Tool) == approval.Action.Name);
}
