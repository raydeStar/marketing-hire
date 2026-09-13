using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record QemuPinnedFile(string Path, string Sha256);
public sealed record QemuInstallation(QemuPinnedFile Executable, QemuPinnedFile ImageTool,
    QemuPinnedFile Kernel, QemuPinnedFile Initrd, QemuPinnedFile BaseDisk)
{
    public string Image => "thaddeus-qemu@sha256:" + BaseDisk.Sha256;
    public IEnumerable<QemuPinnedFile> Files => [Executable, ImageTool, Kernel, Initrd, BaseDisk];
}

/// <summary>Explicit development backend. Product admission remains separately gated; no automatic fallback.</summary>
[SupportedOSPlatform("windows10.0")]
public sealed class QemuSandboxBackend(Store store, QemuInstallation installation, QemuBrokerRoute broker) : ISandboxBackend, IAsyncDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1);
    private readonly List<FileStream> pinned = [];
    private QemuWorkerSession? worker;
    private string? owned;
    private bool disposed;
    public QemuObservation? Observation => worker?.Observation;
    private string DirectoryFor(string id) { DockerSandboxBackend.ValidateId(id); return Path.Combine(store.Root, "qemu-" + id); }
    private string Overlay(string id) => Path.Combine(DirectoryFor(id), "worker.qcow2");
    private SandboxRegistration Registration(string id)
    {
        DockerSandboxBackend.ValidateId(id);
        var raw = store.Setting("sandbox:" + id) ?? throw new InvalidOperationException("This host does not own that worker.");
        var value = Wire.Unpack<SandboxRegistration>(raw);
        if (value.Spec.Id != id || value.Spec.Image != installation.Image || store.Setting("qemu-route:" + id) != Wire.Pack(broker))
            throw new InvalidOperationException("Worker package or task binding differs from its registration.");
        return value;
    }
    private void Status(string id, string status) => store.Setting("sandbox:" + id, Wire.Pack(Registration(id) with { Status = status, Updated = DateTimeOffset.UtcNow }));
    private async Task Pin(CancellationToken cancellation)
    {
        if (pinned.Count != 0) return;
        try
        {
            foreach (var file in installation.Files)
            {
                if (!Path.IsPathFullyQualified(file.Path) || !Regex.IsMatch(file.Sha256, @"\A[a-f0-9]{64}\z")) throw new ArgumentException("Invalid QEMU package pin.");
                Store.AssertNoLinks(file.Path);
                var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.Asynchronous | FileOptions.SequentialScan);
                pinned.Add(stream);
                if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation)) != file.Sha256) throw new IOException("QEMU package digest mismatch.");
            }
        }
        catch { foreach (var stream in pinned) stream.Dispose(); pinned.Clear(); throw; }
    }

    public async Task<SandboxInspection> Inspect(CancellationToken cancellation)
    {
        await lifecycle.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this); await Pin(cancellation);
            var result = await HostCommand(installation.Executable.Path, ["--version"], store.Root, cancellation);
            if (!result.Succeeded || !result.Output.StartsWith("QEMU emulator version 11.1.0", StringComparison.Ordinal)) throw new IOException("QEMU version mismatch.");
            return new("qemu-whpx", "11.1.0", "11.1.0", DateTimeOffset.UtcNow, "qualification-required", "The explicit Windows VM backend is available for qualification.",
                [new("pinned-inputs", CheckState.Passed, "Configured executable, image tool, kernel, initrd and base disk hashes match and remain read-locked."),
                 new("release-package", CheckState.Unverified, "Full dependency signing, updates and distribution have not been qualified."),
                 new("production-admission", CheckState.Unverified, "The development backend does not enable production agent execution.")]);
        }
        finally { lifecycle.Release(); }
    }

    public async Task Create(SandboxSpec spec, CancellationToken cancellation)
    {
        DockerSandboxBackend.ValidateSpec(spec);
        if (spec.Image != installation.Image) throw new ArgumentException("Worker image differs from the configured pinned disk.");
        await lifecycle.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (store.Setting("active-sandbox") is { } active && Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + active)!).Status != "removed")
                throw new InvalidOperationException("This host permits one active worker.");
            if (store.Setting("sandbox:" + spec.Id) != null) throw new InvalidOperationException("Worker identities cannot be reused.");
            await Pin(cancellation); var directory = PrivateWorkerDirectory.Create(DirectoryFor(spec.Id)); owned = spec.Id;
            store.Setting("qemu-route:" + spec.Id, Wire.Pack(broker));
            store.Setting("sandbox:" + spec.Id, Wire.Pack(new SandboxRegistration(spec, "creation-unknown", DateTimeOffset.UtcNow)));
            store.Setting("active-sandbox", spec.Id);
            var created = await HostCommand(installation.ImageTool.Path, ["create", "-f", "qcow2", "-F", "raw", "-b", installation.BaseDisk.Path, Overlay(spec.Id)], directory, cancellation);
            if (!created.Succeeded) throw new IOException("Private overlay creation was not confirmed; inspect before any retry.");
            Status(spec.Id, "stopped"); await Boot(spec.Id, cancellation);
        }
        finally { lifecycle.Release(); }
    }

    private async Task Boot(string id, CancellationToken cancellation)
    {
        var registered = Registration(id);
        if (registered.Status != "stopped") throw new InvalidOperationException("Worker state requires reconciliation before another boot.");
        await Pin(cancellation); Store.AssertNoLinks(Overlay(id));
        // Never reopen a disk still held by another worker, and never identify a process by a stale PID.
        using (new FileStream(Overlay(id), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        owned = id; Status(id, "boot-unknown");
        worker = await QemuWorkerSession.Start(new(installation.Executable.Path, installation.Kernel.Path, installation.Initrd.Path, installation.BaseDisk.Path, Overlay(id)),
            registered.Spec, broker, Path.Combine(DirectoryFor(id), "boot-" + Guid.NewGuid().ToString("N")), cancellation);
        Status(id, "running-unqualified"); store.Setting("qemu-observation:" + id, Wire.Pack(worker.Observation));
    }

    public async Task<SandboxCommandResult> Execute(string id, IReadOnlyList<string> command, string? input, CancellationToken cancellation)
    {
        QemuWorkerSession session;
        await lifecycle.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this); var registration = Registration(id);
            if (worker == null) await Boot(id, cancellation);
            if (owned != id || worker!.Completion.IsCompleted || registration.Status is not ("stopped" or "running-unqualified"))
                throw new InvalidOperationException("Worker execution is unavailable; reconcile its state first.");
            session = worker;
        }
        finally { lifecycle.Release(); }
        // Broker calls may arrive while this RPC waits. Do not hold the lifecycle lock over guest execution.
        return await session.Execute(command, input, cancellation);
    }

    public async Task Stop(string id, CancellationToken cancellation)
    {
        await lifecycle.WaitAsync(cancellation);
        try
        {
            var registration = Registration(id); if (registration.Status == "stopped" && worker == null) return;
            if (worker == null || owned != id) throw new InvalidOperationException("This process has no live ownership of that worker.");
            Status(id, "stop-unknown");
            try
            {
                var termination = await worker.Stop(cancellation);
                store.Setting("qemu-termination:" + id, Wire.Pack(termination));
                await File.WriteAllTextAsync(Path.Combine(DirectoryFor(id), "termination-" + termination.ProcessId + ".json"), Wire.Pack(termination), cancellation);
                Status(id, "stopped");
            }
            finally { worker = null; }
        }
        finally { lifecycle.Release(); }
    }

    public async Task Remove(string id, CancellationToken cancellation)
    {
        await lifecycle.WaitAsync(cancellation);
        try
        {
            var registration = Registration(id);
            if (worker != null || registration.Status != "stopped") throw new InvalidOperationException("Stop and reconcile the worker before removal.");
            Store.AssertNoLinks(Overlay(id));
            using (new FileStream(Overlay(id), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(Overlay(id)); Status(id, "removed"); // Retain the host-side evidence directory.
        }
        finally { lifecycle.Release(); }
    }

    public async Task PutText(string id, string path, string content, CancellationToken cancellation)
    {
        DockerSandboxBackend.ValidateArtifactPath(path);
        if (Encoding.UTF8.GetByteCount(content) > 100000) throw new ArgumentException("Text artifact exceeds 100 KB.");
        var result = await Execute(id, ["python3", "-c", DockerSandboxBackend.PutTextProgram], Wire.Pack(new { path, content }), cancellation);
        if (result.ExitCode != 0 || result.Output.Trim() != Wire.Hash(content)) throw new IOException("Worker text copy was not verified.");
    }
    public async Task<SandboxText> GetText(string id, string path, CancellationToken cancellation)
    {
        DockerSandboxBackend.ValidateArtifactPath(path);
        var result = await Execute(id, ["python3", "-c", DockerSandboxBackend.GetTextProgram], Wire.Pack(new { path }), cancellation);
        if (result.ExitCode != 0) throw new IOException("Worker text could not be read safely.");
        var value = Wire.Unpack<SandboxText>(result.Output);
        if (value.Path != path || Encoding.UTF8.GetByteCount(value.Content) > 100000 || Wire.Hash(value.Content) != value.Sha256) throw new IOException("Worker artifact identity or hash mismatch.");
        return value;
    }

    private static async Task<HostProcessResult> HostCommand(string executable, string[] arguments, string directory, CancellationToken cancellation)
    {
        await using var command = WindowsJobProcess.Start(new(executable, arguments, directory, QemuWorkerSession.HostEnvironment(directory), TimeSpan.FromSeconds(15)), cancellation);
        command.Input.Close(); using var output = new StreamReader(command.Output); using var error = new StreamReader(command.Error);
        var readOutput = output.ReadToEndAsync(cancellation); var readError = error.ReadToEndAsync(cancellation);
        var result = await command.Completion; return new(result.ExitCode, await readOutput, await readError, result.StopReason);
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (disposed) return; disposed = true;
            if (worker != null) { if (owned != null) Status(owned, "interrupted"); await worker.DisposeAsync(); worker = null; }
            foreach (var stream in pinned) stream.Dispose(); pinned.Clear();
        }
        finally { lifecycle.Release(); }
    }
}
