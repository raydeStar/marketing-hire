using System.Runtime.Versioning;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

internal sealed record QemuHostResourceState(OwnedProcessResourceObservation? Windows = null,
    LinuxResourceObservation? Linux = null, string[]? ExecutableMappings = null);

internal interface IQemuHostProcess : IAsyncDisposable
{
    int Id { get; }
    Stream Input { get; }
    Stream Output { get; }
    Stream Error { get; }
    Task<OwnedProcessExit> Completion { get; }
    void Stop(string reason);
    void ValidateRuntime();
    QemuHostResourceState ObserveResources();
}

[SupportedOSPlatform("windows10.0")]
internal sealed class WindowsQemuProcess(WindowsJobProcess process) : IQemuHostProcess
{
    public int Id => process.Id;
    public Stream Input => process.Input;
    public Stream Output => process.Output;
    public Stream Error => process.Error;
    public Task<OwnedProcessExit> Completion => process.Completion;
    public void Stop(string reason) => process.Stop(reason);
    public void ValidateRuntime() { }
    public QemuHostResourceState ObserveResources() => new(Windows: process.ObserveResources());
    public ValueTask DisposeAsync() => process.DisposeAsync();
}

internal sealed class LinuxQemuProcess(LinuxSystemdProcess process, LinuxQemuRuntime runtime) : IQemuHostProcess
{
    public int Id => process.Id;
    public Stream Input => process.Input;
    public Stream Output => process.Output;
    public Stream Error => process.Error;
    public Task<OwnedProcessExit> Completion => process.Completion;
    public void Stop(string reason) => process.Stop(reason);
    public void ValidateRuntime() { runtime.VerifyExecutableMappings(Id); process.ObserveResources(); }
    public QemuHostResourceState ObserveResources() => new(Linux: process.ObserveResources(), ExecutableMappings: runtime.VerifyExecutableMappings(Id));
    public ValueTask DisposeAsync() => process.DisposeAsync();

    public static async Task<LinuxQemuProcess> Start(QemuBootFiles files, IReadOnlyList<string> arguments, SandboxSpec spec,
        string directory, string supervisor, LinuxQemuRuntime runtime, CancellationToken cancellation)
    {
        var request = runtime.Request(files.Executable, new[] { "-no-user-config", "-L", runtime.DataDirectory }.Concat(arguments).ToArray(), directory, TimeSpan.FromMinutes(12), 300000);
        var runtimeRoot = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? throw new IOException("Missing private Linux user runtime.");
        var process = await LinuxSystemdProcess.Start(request, new(((long)spec.MemoryMiB + 1024) * 1024 * 1024, spec.Cpus * 100, 128), supervisor,
            Path.Combine(runtimeRoot, "thad-qemu-" + Guid.NewGuid().ToString("N")), cancellation);
        return new(process, runtime);
    }
}
