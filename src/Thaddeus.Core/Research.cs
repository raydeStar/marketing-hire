namespace Thaddeus.Core;

public record ResearchAvailability(bool Enabled, string Backend, string Status, string Summary, bool DevelopmentOnly = false);
public record ArtifactReview(string ApprovalId, string Artifact, string Sha256, DateTimeOffset ReadAt);
public record ArtifactImport(string Id, string OperationId, string Path, string Artifact, string ResourceVersion,
    EvidenceCitation[] Citations, DateTimeOffset Requested, string Status = "requested", string? Sha256 = null,
    DateTimeOffset? CapturedAt = null, string? FailureType = null, string? ApprovalId = null, ProposalRepairFeedback? Repair = null);
public record ArtifactCheck(string ApprovalId, string Artifact, string ExpectedSha256, string? ObservedSha256,
    string Status, DateTimeOffset CheckedAt, string? FailureType = null);
public record ResearchRecoveryReview(string Digest, int Version, string? Checkpoint, bool CanRestore, string Summary,
    DateTimeOffset CheckedAt, DateTimeOffset Expires, bool WorkerStopped);
public record ResearchState(string Phase, string Message, ArtifactReview? Review = null, bool WorkerRetained = false, string? FailureCode = null,
    ResearchRecoveryReview? Recovery = null);
public record ResearchRequest(string Objective, string[] ReadScope, PublicWebScope? Web = null, Budget? Limits = null, MemorySelection[]? Memories = null);
public record WorkspaceReview(string RunId, string WorkerId, string Backend, string Status, int Files, long Bytes,
    string Digest, bool CanRemove, string Summary, WorkspaceRemoval? Removal = null);
public record WorkspaceRemoval(string RunId, string WorkerId, string Digest, string Status, DateTimeOffset Requested,
    DateTimeOffset? Verified = null, string? Failure = null);
public interface IResearchWorkspaceStorage
{
    WorkspaceReview Inspect(Run run);
    WorkspaceRemoval Remove(Run run, string digest, CancellationToken cancellation);
}

// Admission comes from a host-owned factory, never a browser flag or an agent's claimed qualification.
public interface IResearchWorkerFactory
{
    ResearchAvailability Availability { get; }
    IResearchWorker Open(Run run);
}

public interface IResearchWorker : IAsyncDisposable
{
    IExecutionBackend Execution { get; }
    Task Prepare(Run run, string grant, CancellationToken cancellation);
    Task Wake(Run run, string grant, CancellationToken cancellation);
    Task Reconcile(Run run, CancellationToken cancellation);
    Task Stop(Run run, CancellationToken cancellation);
    Task Retire(Run run, CancellationToken cancellation);
    Task<SandboxText> ReadArtifact(Run run, string path, CancellationToken cancellation);
}
