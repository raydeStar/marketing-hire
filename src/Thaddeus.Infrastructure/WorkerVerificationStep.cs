namespace Thaddeus.Infrastructure;

/// <summary>Observed verification work only; no private filenames or estimated percentage.</summary>
public sealed record WorkerVerificationStep(string Stage, int VerifiedFiles = 0, int TotalFiles = 0);
public sealed record HostWorkerCheckProgress(string Id, DateTimeOffset StartedAt, WorkerVerificationStep Step);
