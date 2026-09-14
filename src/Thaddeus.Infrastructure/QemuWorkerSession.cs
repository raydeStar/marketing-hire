using System.Collections.Concurrent;
using System.Net;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record QemuBrokerRoute(string RunId, int Port);
public sealed record QemuBootFiles(string Executable, string Kernel, string Initrd, string BaseDisk, string Overlay);
public sealed record QemuObservation(int ProcessId, JsonElement Version, JsonElement Cpus, JsonElement Memory,
    JsonElement Pci, JsonElement Block, JsonElement Guest, string ControlClientCertificateSha256, string ConsoleClientCertificateSha256,
    string ControlTls, string ConsoleTls, OwnedProcessResourceObservation? HostResources = null,
    LinuxResourceObservation? LinuxHostResources = null, string[]? ExecutableMappings = null);
public sealed record QemuTermination(int ProcessId, OwnedProcessExit Outcome, bool GuestShutdown, int RejectedControlConnections, int RejectedConsoleConnections,
    OwnedProcessResourceObservation? HostResourcesBeforeStop = null, LinuxResourceObservation? LinuxHostResourcesBeforeStop = null,
    string[]? ExecutableMappingsBeforeStop = null);

/// <summary>One owned QEMU boot. Guest data never selects a host command, file, port, or URL origin.</summary>
public sealed class QemuWorkerSession : IAsyncDisposable
{
    private readonly IQemuHostProcess process;
    private readonly VmTlsChannel control, console;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim qmpWrite = new(1), controlWrite = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> replies = new(), commands = new();
    private readonly ConcurrentDictionary<string, Task> brokerCalls = new();
    private readonly VmBrokerQuota brokerQuota = new();
    private readonly TaskCompletionSource<JsonElement> greeting = Signal(), ready = Signal();
    private readonly VmBrokerProxy proxy;
    private readonly QemuBrokerRoute route;
    private readonly string bootDirectory;
    private Stream? controlStream;
    private Task[] readers = [];
    private Exception? failure;
    private int stopping, disposed, commandSlots;
    private bool guestShutdown;
    public QemuObservation Observation { get; private set; } = null!;
    public int Id => process.Id;
    public Task<OwnedProcessExit> Completion => process.Completion;
    private static TaskCompletionSource<JsonElement> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private QemuWorkerSession(IQemuHostProcess process, VmTlsChannel control, VmTlsChannel console, QemuBrokerRoute route, string bootDirectory)
    { this.process = process; this.control = control; this.console = console; this.route = route; proxy = new(route); this.bootDirectory = bootDirectory; }

    [SupportedOSPlatform("windows10.0")]
    public static Task<QemuWorkerSession> Start(QemuBootFiles files, SandboxSpec spec, QemuBrokerRoute route, string bootDirectory, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException();
        return StartCore(files, spec, route, bootDirectory, QemuHostTarget.WindowsX64, null, null, cancellation);
    }

    public static Task<QemuWorkerSession> StartLinux(QemuBootFiles files, SandboxSpec spec, QemuBrokerRoute route, string bootDirectory,
        LinuxQemuRuntime runtime, string supervisor, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("This worker session requires native Linux x64.");
        if (files.Executable != runtime.Executable.Path) throw new ArgumentException("VM executable differs from its approved Linux bundle.");
        return StartCore(files, spec, route, bootDirectory, QemuHostTarget.LinuxX64, runtime, supervisor, cancellation);
    }

    private static async Task<QemuWorkerSession> StartCore(QemuBootFiles files, SandboxSpec spec, QemuBrokerRoute route, string bootDirectory,
        QemuHostTarget target, LinuxQemuRuntime? runtime, string? supervisor, CancellationToken cancellation)
    {
        DockerSandboxBackend.ValidateSpec(spec);
        if (!System.Text.RegularExpressions.Regex.IsMatch(route.RunId, @"\A[a-f0-9]{32}\z") || route.Port is < 1024 or > 65535)
            throw new ArgumentException("Invalid task broker route.");
        PrivateWorkerDirectory.Create(bootDirectory);
        VmTlsChannel? control = null, console = null; IQemuHostProcess? process = null; QemuWorkerSession? session = null;
        try
        {
            control = new(Path.Combine(bootDirectory, "control")); console = new(Path.Combine(bootDirectory, "console"));
            var arguments = QemuLaunchArguments.Build(target, files, spec,
                new(control.CredentialsDirectory, control.Port), new(console.CredentialsDirectory, console.Port));
            if (target == QemuHostTarget.WindowsX64 && OperatingSystem.IsWindowsVersionAtLeast(10))
            {
                var resources = new OwnedProcessResourceLimits(((long)spec.MemoryMiB + 1024) * 1024 * 1024,
                    Math.Clamp((int)Math.Ceiling(10000.0 * spec.Cpus / Environment.ProcessorCount), 1, 10000), 1);
                process = new WindowsQemuProcess(WindowsJobProcess.Start(new(files.Executable, arguments, bootDirectory, HostEnvironment(bootDirectory), TimeSpan.FromMinutes(12), 300000, resources)));
            }
            else if (target == QemuHostTarget.LinuxX64 && OperatingSystem.IsLinux() && runtime != null && supervisor != null)
                process = await LinuxQemuProcess.Start(files, arguments, spec, bootDirectory, supervisor, runtime, cancellation);
            else throw new PlatformNotSupportedException("The selected VM process owner is unavailable.");
            session = new(process, control, console, route, bootDirectory);
            await session.Initialize(spec, cancellation);
            await File.WriteAllTextAsync(Path.Combine(bootDirectory, "observation.json"), Wire.Pack(session.Observation), cancellation);
            return session;
        }
        catch
        {
            if (session != null) await session.DisposeAsync();
            else { if (process != null) await process.DisposeAsync(); control?.Dispose(); console?.Dispose(); }
            throw;
        }
    }

    public static IReadOnlyDictionary<string, string> HostEnvironment(string privateDirectory) => new Dictionary<string, string>
    {
        ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows), ["WINDIR"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ["TEMP"] = privateDirectory, ["TMP"] = privateDirectory
    };

    private async Task Initialize(SandboxSpec spec, CancellationToken cancellation)
    {
        using var setup = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token); setup.CancelAfter(TimeSpan.FromSeconds(50));
        var controlAccept = control.Accept(setup.Token); var consoleAccept = console.Accept(setup.Token);
        readers = [Guard(() => VmJsonFrames.Read(process.Output, QmpMessage, 300000, lifetime.Token)), Guard(() => Drain(process.Error, "qemu-stderr.log", 100000, lifetime.Token)),
            Guard(async () => { using var stream = await consoleAccept; await Drain(stream, "console.log", 300000, lifetime.Token); })];
        _ = process.Completion.ContinueWith(_ => { if (Volatile.Read(ref stopping) == 0) Fail(new IOException("Owned QEMU process exited.")); }, TaskScheduler.Default);
        controlStream = await controlAccept;
        readers = [.. readers, Guard(() => VmJsonFrames.Read(controlStream, GuestMessage, 2200000, lifetime.Token))];
        var version = await greeting.Task.WaitAsync(setup.Token);
        await Qmp("qmp_capabilities", setup.Token);
        var cpus = await Qmp("query-cpus-fast", setup.Token); var memory = await Qmp("query-memory-size-summary", setup.Token);
        var pci = await Qmp("query-pci", setup.Token); var block = await Qmp("query-block", setup.Token);
        if (cpus.GetArrayLength() != spec.Cpus || memory.GetProperty("base-memory").GetInt64() != (long)spec.MemoryMiB * 1024 * 1024 ||
            pci.EnumerateArray().Any(bus => bus.GetProperty("devices").EnumerateArray().Any(device => device.TryGetProperty("class_info", out var info) && (info.GetProperty("class").GetInt32() >> 8) == 2)))
            throw new InvalidOperationException("Observed VM resources or devices differ from the requested boundary.");
        process.ValidateRuntime(); // Admit vCPU execution only after the Linux executable mappings and cgroup match.
        await Qmp("cont", setup.Token); var guest = await ready.Task.WaitAsync(setup.Token);
        if (guest.GetProperty("uid").GetInt32() != 1000) throw new InvalidOperationException("The guest steward did not start as the worker user.");
        var resources = process.ObserveResources();
        Observation = new(Id, version, cpus, memory, pci, block, guest, control.ClientCertificateSha256, console.ClientCertificateSha256,
            control.NegotiatedProtocol!, console.NegotiatedProtocol!, resources.Windows, resources.Linux, resources.ExecutableMappings);
    }

    private async Task Guard(Func<Task> action)
    {
        try { await action(); if (Volatile.Read(ref stopping) == 0) Fail(new IOException("VM channel closed unexpectedly.")); }
        catch (Exception ex) { if (Volatile.Read(ref stopping) == 0) Fail(ex); }
    }
    private void Fail(Exception error)
    {
        if (Volatile.Read(ref stopping) == 0)
        {
            if (Interlocked.CompareExchange(ref failure, error, null) == null) RecordFailure(bootDirectory, error);
            process.Stop("transport-failed");
        }
        lifetime.Cancel(); greeting.TrySetException(error); ready.TrySetException(error);
        foreach (var pending in replies.Values.Concat(commands.Values)) pending.TrySetException(error);
    }

    internal static void RecordFailure(string directory, Exception error)
    {
        try
        {
            var path = Path.Combine(directory, "transport-failure.json"); Store.AssertNoLinks(path);
            var detail = error.Message.Length <= 2000 ? error.Message : error.Message[..2000];
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { failureType = error.GetType().Name, error.HResult, detail, at = DateTimeOffset.UtcNow }, Wire.Json);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            file.Write(bytes); file.Flush(true);
        }
        catch (Exception diagnosticError) when (diagnosticError is IOException or UnauthorizedAccessException)
        {
            // Diagnostics cannot prevent containment. Preserve the first account; do not overwrite it with the echo.
        }
    }

    private void QmpMessage(JsonElement message)
    {
        if (message.TryGetProperty("QMP", out var version)) greeting.TrySetResult(version.Clone());
        else if (message.TryGetProperty("id", out var id))
        {
            if (!replies.TryGetValue(id.GetString()!, out var pending)) throw new IOException("Uncorrelated QMP response.");
            if (message.TryGetProperty("error", out _)) pending.TrySetException(new IOException("QMP command failed."));
            else pending.TrySetResult(message.GetProperty("return").Clone());
        }
        else if (message.TryGetProperty("event", out var name) && name.GetString() == "SHUTDOWN") guestShutdown = message.GetProperty("data").GetProperty("guest").GetBoolean();
    }
    private void GuestMessage(JsonElement message)
    {
        switch (message.GetProperty("type").GetString())
        {
            case "ready": if (!ready.TrySetResult(message.Clone())) throw new IOException("Repeated guest greeting."); break;
            case "result":
                if (!commands.TryGetValue(message.GetProperty("id").GetString()!, out var pending) || !pending.TrySetResult(message.Clone())) throw new IOException("Uncorrelated guest command response.");
                break;
            case "request":
                var id = message.GetProperty("id").GetString()!;
                var lease = brokerQuota.Admit(id);
                var task = Forward(id, message.Clone(), lease); brokerCalls[id] = task;
                _ = task.ContinueWith(_ => brokerCalls.TryRemove(id, out var removed), TaskScheduler.Default);
                break;
            default: throw new IOException("Unsupported guest frame.");
        }
    }

    private async Task Forward(string id, JsonElement message, IDisposable lease)
    {
        using var admission = lease;
        try
        {
            var response = await proxy.Forward(message, lifetime.Token);
            await Send(controlStream!, controlWrite, new { type = "response", id, response.status, response.body, response.contentType }, lifetime.Token);
        }
        catch (Exception) when (!lifetime.IsCancellationRequested)
        {
            try { await Send(controlStream!, controlWrite, new { type = "response", id, status = 502, body = "", contentType = "application/json" }, lifetime.Token); }
            catch (Exception error) { Fail(error); }
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
    }
    private async Task<JsonElement> Qmp(string operation, CancellationToken cancellation)
    {
        var id = Guid.NewGuid().ToString("N"); var pending = Signal(); replies[id] = pending;
        try { await Send(process.Input, qmpWrite, new { execute = operation, id }, cancellation); return await pending.Task.WaitAsync(cancellation); }
        finally { replies.TryRemove(id, out _); }
    }

    public async Task<SandboxCommandResult> Execute(IReadOnlyList<string> command, string? input, CancellationToken cancellation)
    {
        if (lifetime.IsCancellationRequested || Volatile.Read(ref stopping) != 0 || process.Completion.IsCompleted)
            throw new InvalidOperationException("The VM stopped or lost its channel. Reconcile it before issuing another command.");
        if (command.Count is < 1 or > 128 || command.Any(a => a.Contains('\0') || a.Length > 100000) || Encoding.UTF8.GetByteCount(input ?? "") > 200000)
            throw new ArgumentException("Invalid guest command envelope.");
        if (Interlocked.Increment(ref commandSlots) > 4) { Interlocked.Decrement(ref commandSlots); throw new InvalidOperationException("Guest command concurrency limit reached."); }
        var id = Guid.NewGuid().ToString("N"); var pending = Signal(); commands[id] = pending;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            process.ValidateRuntime();
            await Send(controlStream!, controlWrite, new { type = "execute", id, command, input }, deadline.Token);
            var result = await pending.Task.WaitAsync(deadline.Token);
            var output = result.GetProperty("output").GetString()!; var error = result.GetProperty("error").GetString()!;
            if (Encoding.UTF8.GetByteCount(output) > 1200000 || Encoding.UTF8.GetByteCount(error) > 1200000) throw new IOException("Guest result exceeds its bound.");
            return new(result.GetProperty("exitCode").GetInt32(), output, error);
        }
        catch (Exception error) { Fail(error); throw; } // An interrupted command is uncertain; do not leave it running or replay it.
        finally { commands.TryRemove(id, out _); Interlocked.Decrement(ref commandSlots); }
    }

    public async Task<QemuTermination> Stop(CancellationToken cancellation)
    {
        Interlocked.Exchange(ref stopping, 1);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var resources = process.ObserveResources();
            await Send(controlStream!, controlWrite, new { type = "shutdown" }, deadline.Token);
            var outcome = await process.Completion.WaitAsync(deadline.Token);
            await Task.WhenAll(readers).WaitAsync(deadline.Token);
            if (!outcome.Succeeded || !guestShutdown || failure != null) throw new IOException("Guest shutdown was not independently confirmed.");
            return new(Id, outcome, guestShutdown, control.RejectedConnections, console.RejectedConnections, resources.Windows, resources.Linux, resources.ExecutableMappings);
        }
        catch { process.Stop("shutdown-unconfirmed"); throw; }
        finally { await DisposeAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Interlocked.Exchange(ref stopping, 1); lifetime.Cancel(); await process.DisposeAsync();
        control.Dispose(); console.Dispose(); proxy.Dispose();
        await Task.WhenAll(readers);
        try { await Task.WhenAll(brokerCalls.Values); } catch (OperationCanceledException) { }
    }

    private static async Task Send(Stream stream, SemaphoreSlim gate, object message, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Wire.Json);
        if (bytes.Length > 2200000) throw new IOException("VM frame exceeds its bound.");
        await gate.WaitAsync(cancellation);
        try { await stream.WriteAsync(bytes, cancellation); await stream.WriteAsync(new byte[] { 10 }, cancellation); await stream.FlushAsync(cancellation); }
        finally { gate.Release(); }
    }
    private async Task Drain(Stream stream, string name, int limit, CancellationToken cancellation)
    {
        await using var log = new FileStream(Path.Combine(bootDirectory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var buffer = new byte[8192]; var total = 0; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            var keep = Math.Min(count, limit - total);
            await log.WriteAsync(buffer.AsMemory(0, keep), cancellation); await log.FlushAsync(cancellation);
            if ((total += count) > limit) throw new IOException("VM diagnostics exceeded their bound.");
        }
    }
}
