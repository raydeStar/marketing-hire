using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thaddeus.Core;

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static string Pack<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Unpack<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)!;
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
public enum RunState { Queued, Running, AwaitingApproval, Paused, Succeeded, Failed, Cancelled, NeedsAttention, Denied, AwaitingInput }
public record Criterion(string Description, string Kind, string Status = "unverified");
public record Budget(int ModelCalls = 3, int ToolCalls = 8, int MaxOutputTokens = 4096, int Seconds = 180, int Repairs = 1, int MaxTotalTokens = 64000, bool RequireCertifiedTokenBound = false);
public record TokenQuote(int? InputUpperBound, bool OutputBoundCertified, string Basis, int? OutputUpperBound = null);
public record ProviderSnapshot(string Kind = "scripted", string Model = "fictional-weekly-v1", string Reasoning = "high", string? Endpoint = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CredentialId = null);
public record Goal(string Objective, string[] ReadScope, string WriteScope, Criterion[] Criteria, Budget Limits, ProviderSnapshot Provider, string Kind = "plan", PublicWebScope? Web = null, MemorySelection[]? Memories = null);
public record EvidenceRef(string Path, string Hash, string Content);
public record ToolRequest(string Name, string Path, string? Content = null);
public record ToolResult(string Name, bool Success, string Summary, EvidenceRef? Evidence = null);
public record Attempt(int Number, string Classification, string Summary);
public record ValidationResult(bool Passed, string[] Checks, string[] Unverified);
public record Approval(string Id, string RunId, ToolRequest Action, string Digest, string ResourceVersion, DateTimeOffset Expires, string Decision = "pending");
public record RunEvent(int SchemaVersion, string EventId, string RunId, long Sequence, DateTimeOffset Timestamp, string Type, JsonElement Data, long Cursor = 0);
public record ModelReply(ToolRequest? Action, string? Text, int? InputTokens = null, int? OutputTokens = null);
public record ModelAttachment(string Id, string Name, string MediaType, string Content);
public record Observation(Goal Goal, IReadOnlyList<EvidenceRef> Evidence, string? Failure, int Round, IReadOnlyList<ChatMessage>? History = null, ArtifactChatContext? Artifacts = null, ModelAttachment[]? Attachments = null, bool SuggestIdeas = false, ConversationWebContext? Web = null, ConnectedToolContext? ConnectedTools = null, DelegationToolContext? Delegation = null, TodoBatchToolContext? Todos = null, SoulDocument? Soul = null, UserDocument? User = null);
public record ConversationWebContext(string[] Urls, CapabilityReceipt[] Receipts, bool CanFetch);
public record ConnectedToolDefinition(string ConnectorId, string ConnectorName, string RemoteName, string ModelName,
    string Description, JsonElement InputSchema, string Effect, string ConnectionVersion);
public record ConnectedToolContext(ConnectedToolDefinition[] Tools, CapabilityReceipt[] Receipts, bool CanCall);
public record DelegationJobSummary(string Id, int Version, string Kind, string Title, string State, string ScheduleKind,
    DateTimeOffset? NextRunUtc, string TimeZone, string? LocalTime, bool CancellationRequested,
    string? Sender = null, string? Target = null, string? Subject = null, string? Body = null,
    string? BriefEmailAccount = null, string? BriefCalendarAccount = null, string? BriefSelectionRule = null,
    string? BriefEmailArguments = null, string? BriefCalendarArguments = null);
public record DelegationToolContext(CapabilityReceipt[] Receipts, bool CanPropose, bool CanManage,
    DateTimeOffset RequestedAt, string TimeZone, DelegationJobSummary[] Jobs);
public record TodoBatchSource(string Kind, string Reference, string Version, string Label);
public record TodoBatchToolContext(TodoBatchSource[] Sources, CapabilityReceipt[] Receipts, bool CanPropose);
public record Page(string Path, string Content, string Version, DateTimeOffset Updated);
public record ChatMessage(string Id, string Role, string Content, DateTimeOffset Created);
public record ConversationRetry(string RootId, string SourceId, string OperationId);
public sealed class Run
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required Goal Goal { get; set; }
    public RunState State { get; set; } = RunState.Queued;
    public string Summary { get; set; } = "Ready to begin";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; }
    public int ModelCalls { get; set; }
    public int ToolCalls { get; set; }
    public int Repairs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? Cost { get; set; }
    public List<EvidenceRef> Evidence { get; set; } = [];
    public List<Attempt> Attempts { get; set; } = [];
    public Approval? Approval { get; set; }
    public ValidationResult? Validation { get; set; }
    public string? OutputPath { get; set; }
    public bool DemoFailure { get; set; }
    public bool ValidationEnabled { get; set; } = true;
    public bool JournalDetail { get; set; } = true;
    public string Policy { get; set; } = "evidence";
    public string DraftText { get; set; } = "";
    public string? ConnectionSetup { get; set; }
    public string? ConnectionSetupProduct { get; set; }
    public bool Background { get; set; }
    public string[] UploadIds { get; set; } = [];
    public bool SuggestIdeas { get; set; }
    public string[] ConversationWebUrls { get; set; } = [];
    public ConnectedToolDefinition[] ConnectedTools { get; set; } = [];
    public DateTimeOffset? DelegationRequestedAt { get; set; }
    public string? DelegationTimeZone { get; set; }
    public List<ChatMessage> ConversationContext { get; set; } = [];
    public ConversationRetry? ConversationRetry { get; set; }
    public ArtifactChatContext? ArtifactContext { get; set; }
    public ArtifactResult? ArtifactResult { get; set; }
    public int ChargedTokens { get; set; }
    public int ReservedTokens { get; set; }
    public string TokenAccounting { get; set; } = "No model dispatch yet";
    public ExecutionIdentity? Execution { get; set; }
    public UserQuestion? Question { get; set; }
    public List<CapabilityReceipt> Capabilities { get; set; } = [];
    public PolicyProfile? Profile { get; set; }
    public List<ModelDispatch> ModelDispatches { get; set; } = [];
    public DateTimeOffset? ExecutionDeadlineStart { get; set; }
    public ExecutionContextSnapshot? PreparedContext { get; set; }
    public List<RememberedEntry> MemoryEvidence { get; set; } = [];
    public List<NativeProposalReview> NativeProposals { get; set; } = [];
    public List<ExecutionCommand> ExecutionCommands { get; set; } = [];
    public double ExecutionActiveSeconds { get; set; }
    public ResearchState? Research { get; set; }
    public List<ArtifactCheck> ArtifactChecks { get; set; } = [];
    public List<ArtifactImport> ArtifactImports { get; set; } = [];
}
public interface IConnectedToolBroker
{
    ConnectedToolDefinition[] Snapshot();
    Task<CapabilityResult> Call(ConnectedToolDefinition tool, JsonElement arguments, CancellationToken cancellation);
}
public interface IModelProvider
{
    Task Prepare(CancellationToken cancellation) => Task.CompletedTask;
    Task<ModelReply> Respond(Observation observation, Func<string, Task> onDelta, CancellationToken cancellation);
    TokenQuote Quote(Observation observation) => new(null, false, "Provider has no certified token bound");
}
public interface IRunStore
{
    Run? Get(string id);
    IReadOnlyList<Run> List();
    void Save(Run run, string type, object data);
    IReadOnlyList<RunEvent> Events(long after = 0, string? runId = null);
}
public interface IToolExecutor
{
    ToolResult Read(string path);
    Page Write(string path, string content, string expectedVersion);
    string Version(string path);
}
public interface IValidator { ValidationResult Validate(ToolRequest action, IReadOnlyList<EvidenceRef> evidence); }
public interface IAgentPolicy { bool MayRepair(Run run); }
public sealed class EvidencePolicy : IAgentPolicy
{
    public bool MayRepair(Run run) => run.Policy == "evidence" && run.Repairs < run.Goal.Limits.Repairs;
}
public sealed class PlanValidator : IValidator
{
    public ValidationResult Validate(ToolRequest action, IReadOnlyList<EvidenceRef> evidence)
    {
        var content = action.Content ?? "";
        var checks = new List<string>();
        if (content.StartsWith("# ", StringComparison.Ordinal)) checks.Add("Markdown title present");
        if (evidence.Count > 0 && evidence.All(e => content.Contains(e.Path, StringComparison.Ordinal))) checks.Add("All read source paths referenced");
        if (content.Contains("unresolved", StringComparison.OrdinalIgnoreCase)) checks.Add("Unresolved section present");
        return new(checks.Count == 3, checks.ToArray(), ["Factual accuracy and conflict resolution require human review", "Schedule feasibility is not mechanically proven"]);
    }
}
