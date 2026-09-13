namespace Thaddeus.Core;

public record ResearchAvailability(bool Enabled, string Backend, string Status, string Summary, bool DevelopmentOnly = false);
public record ArtifactReview(string ApprovalId, string Artifact, string Sha256, DateTimeOffset ReadAt);
public record ResearchState(string Phase, string Message, ArtifactReview? Review = null, bool WorkerRetained = false, string? FailureCode = null);
public record ResearchRequest(string Objective, string[] ReadScope, PublicWebScope? Web = null, Budget? Limits = null);

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
