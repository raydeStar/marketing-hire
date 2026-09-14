using System.Text.Json;

namespace Thaddeus.Core;

public record ExecutionIdentity(string Backend, string SandboxId, string SessionKey, string RuntimeVersion,
    string? RuntimeRunId = null, long LastWorkerSequence = 0);
public record UserQuestion(string Id, string Text, string[] Choices, DateTimeOffset Created, string? Answer = null, DateTimeOffset? Answered = null);
public record CapabilityCall(string OperationId, string Name, JsonElement Arguments);
public record CapabilityReceipt(string OperationId, string RequestHash, string Name, string Authority,
    DateTimeOffset Recorded, JsonElement Result, bool IsError = false);
public record CapabilityResult(JsonElement Value, bool IsError = false);
public record CapabilityDefinition(string Name, string Description, JsonElement InputSchema);
public interface ICapabilityBroker
{
    IReadOnlyList<CapabilityDefinition> Tools { get; }
    IReadOnlyList<CapabilityDefinition> ToolsFor(string runId);
    Task<CapabilityResult> Call(string runId, CapabilityCall call, CancellationToken cancellation);
}

public record PolicyProfile(string Id, int Version, bool SourceContext, bool ValidateEvidence, int RepairLimit, bool MemoryContext = false, int ProposalEvidenceVersion = 0)
{
    public static readonly PolicyProfile Baseline = new("openclaw-baseline", 1, false, false, 0);
    public static readonly PolicyProfile Evidence = new("thaddeus-evidence", 1, true, true, 1);
    public static readonly PolicyProfile EvidenceMemory = new("thaddeus-evidence", 2, true, true, 1, true);
    public static readonly PolicyProfile NativeEvidence = new("thaddeus-evidence", 3, true, true, 1, true, 1);
    public static readonly PolicyProfile NativeUnchecked = new("thaddeus-evidence-unchecked", 3, true, false, 0, true, 1);
    public static readonly PolicyProfile ArtifactEvidence = new("thaddeus-evidence", 4, true, true, 1, true, 2);
    public static readonly PolicyProfile ArtifactUnchecked = new("thaddeus-evidence-unchecked", 4, true, false, 0, true, 2);
    // These controls only change experiments. Permission and credential rules have no off switch here.
    public string Digest => ProposalEvidenceVersion != 0 ? Wire.Hash(Wire.Pack(new { Id, Version, SourceContext, ValidateEvidence, RepairLimit, MemoryContext, ProposalEvidenceVersion })) :
        Version == 1 ? Wire.Hash(Wire.Pack(new { Id, Version, SourceContext, ValidateEvidence, RepairLimit })) :
        Wire.Hash(Wire.Pack(new { Id, Version, SourceContext, ValidateEvidence, RepairLimit, MemoryContext }));
    public void Validate()
    {
        if (this != Baseline && this != Evidence && this != EvidenceMemory && this != NativeEvidence && this != NativeUnchecked && this != ArtifactEvidence && this != ArtifactUnchecked) throw new ArgumentException("Choose a registered, versioned policy profile.");
    }
}

public record ExecutionStart(string RunId, ExecutionIdentity Identity, string Objective, ProviderSnapshot Provider, Budget Limits);
public record ExecutionObservation(string Status, string? RuntimeRunId, JsonElement Report, string Authority = "worker-reported");
public record ExecutionCommand(string Id, string Kind, string RequestHash, DateTimeOffset Requested,
    string Status = "outcome-unknown", ExecutionObservation? Observation = null);
public interface IExecutionBackend
{
    Task<ExecutionObservation> Start(ExecutionStart request, CancellationToken cancellation);
    Task<ExecutionObservation> Steer(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation);
    Task<ExecutionObservation> Cancel(ExecutionIdentity identity, CancellationToken cancellation);
    Task<ExecutionObservation> Inspect(ExecutionIdentity identity, CancellationToken cancellation);
    Task<ExecutionObservation> Resume(ExecutionIdentity identity, string message, string operationId, CancellationToken cancellation);
}
