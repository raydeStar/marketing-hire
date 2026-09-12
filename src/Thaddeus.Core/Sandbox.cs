namespace Thaddeus.Core;

public enum CheckState { Passed, Failed, Unverified }
public record BoundaryCheck(string Id, CheckState State, string Detail);
public record SandboxInspection(string Backend, string RequiredVersion, string? ObservedVersion,
    DateTimeOffset ObservedAt, string Status, string Summary, IReadOnlyList<BoundaryCheck> Checks);
public record SandboxSpec(string Id, string Image, int Cpus = 2, int MemoryMiB = 4096);
public record SandboxCommandResult(int ExitCode, string Output, string Error);
public record SandboxText(string Path, string Content, string Sha256);

// The estate may change its foundations; its doors must keep the same locks.
public interface ISandboxBackend
{
    Task<SandboxInspection> Inspect(CancellationToken cancellation);
    Task Create(SandboxSpec spec, CancellationToken cancellation);
    Task Stop(string id, CancellationToken cancellation);
    Task Remove(string id, CancellationToken cancellation);
    Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation);
    Task PutText(string id, string path, string content, CancellationToken cancellation);
    Task<SandboxText> GetText(string id, string path, CancellationToken cancellation);
}
