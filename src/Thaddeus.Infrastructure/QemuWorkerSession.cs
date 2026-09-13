using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record QemuBrokerRoute(string RunId, int Port);
public sealed record QemuBootFiles(string Executable, string Kernel, string Initrd, string BaseDisk, string Overlay);
public sealed record QemuObservation(int ProcessId, JsonElement Version, JsonElement Cpus, JsonElement Memory,
    JsonElement Pci, JsonElement Block, JsonElement Guest, string ControlClientCertificateSha256, string ConsoleClientCertificateSha256,
    string ControlTls, string ConsoleTls);
public sealed record QemuTermination(int ProcessId, OwnedProcessExit Outcome, bool GuestShutdown, int RejectedControlConnections, int RejectedConsoleConnections);

/// <summary>One owned QEMU boot. Guest data never selects a host command, file, port, or URL origin.</summary>
[SupportedOSPlatform("windows10.0")]
public sealed class QemuWorkerSession : IAsyncDisposable
{
    private readonly WindowsJobProcess process;
    private readonly VmTlsChannel control, console;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim qmpWrite = new(1), controlWrite = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> replies = new(), commands = new();
    private readonly ConcurrentDictionary<string, Task> brokerCalls = new();
    private readonly HashSet<string> brokerIds = [];
    private readonly TaskCompletionSource<JsonElement> greeting = Signal(), ready = Signal();
    private readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) }) { Timeout = Timeout.InfiniteTimeSpan };
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

    private QemuWorkerSession(WindowsJobProcess process, VmTlsChannel control, VmTlsChannel console, QemuBrokerRoute route, string bootDirectory)
    { this.process = process; this.control = control; this.console = console; this.route = route; this.bootDirectory = bootDirectory; }

    public static async Task<QemuWorkerSession> Start(QemuBootFiles files, SandboxSpec spec, QemuBrokerRoute route, string bootDirectory, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException();
        if (!System.Text.RegularExpressions.Regex.IsMatch(route.RunId, @"\A[a-f0-9]{32}\z") || route.Port is < 1024 or > 65535)
            throw new ArgumentException("Invalid task broker route.");
        PrivateWorkerDirectory.Create(bootDirectory);
        VmTlsChannel? control = null, console = null; WindowsJobProcess? process = null; QemuWorkerSession? session = null;
        try
        {
            control = new(Path.Combine(bootDirectory, "control")); console = new(Path.Combine(bootDirectory, "console"));
            var arguments = new List<string> { "-name", spec.Id, "-machine", "q35", "-accel", "whpx", "-cpu", "qemu64,-svm",
                "-m", spec.MemoryMiB.ToString(), "-smp", spec.Cpus.ToString(), "-nodefaults", "-nic", "none", "-display", "none", "-monitor", "none", "-no-reboot", "-qmp", "stdio", "-S" };
            foreach (var (name, channel) in new[] { ("console", console), ("control", control) })
            {
                arguments.AddRange(["-object", JsonSerializer.Serialize(new Dictionary<string, object> { ["qom-type"] = "tls-creds-x509", ["id"] = "tls-" + name,
                    ["endpoint"] = "client", ["dir"] = channel.CredentialsDirectory, ["verify-peer"] = true }),
                    "-chardev", $"socket,id={name},host=127.0.0.1,port={channel.Port},tls-creds=tls-{name}"]);
            }
            arguments.AddRange(["-serial", "chardev:console", "-device", "virtio-serial-pci,id=transport", "-device", "virtserialport,chardev=control,name=org.thaddeus.control",
                "-kernel", files.Kernel, "-initrd", files.Initrd, "-append", "console=ttyS0,115200 root=/dev/vda rootfstype=ext4 rootflags=rw modules=virtio_blk,ext4 init=/opt/thaddeus/vm/init quiet"]);
            foreach (var block in new object[] {
                new Dictionary<string, object> { ["driver"] = "file", ["filename"] = files.BaseDisk, ["node-name"] = "base-file", ["read-only"] = true },
                new Dictionary<string, object> { ["driver"] = "raw", ["file"] = "base-file", ["node-name"] = "base", ["read-only"] = true },
                new Dictionary<string, object> { ["driver"] = "file", ["filename"] = files.Overlay, ["node-name"] = "overlay-file" },
                new Dictionary<string, object> { ["driver"] = "qcow2", ["file"] = "overlay-file", ["backing"] = "base", ["node-name"] = "worker" } })
            {
                arguments.AddRange(["-blockdev", JsonSerializer.Serialize(block)]);
            }
            arguments.AddRange(["-device", "virtio-blk-pci,drive=worker"]);
            process = WindowsJobProcess.Start(new(files.Executable, arguments, bootDirectory, HostEnvironment(bootDirectory), TimeSpan.FromMinutes(12), 300000));
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
        readers = [Guard(() => ReadFrames(process.Output, QmpMessage, 300000, lifetime.Token)), Guard(() => Drain(process.Error, "qemu-stderr.log", 100000, lifetime.Token)),
            Guard(async () => { using var stream = await consoleAccept; await Drain(stream, "console.log", 300000, lifetime.Token); })];
        _ = process.Completion.ContinueWith(_ => { if (Volatile.Read(ref stopping) == 0) Fail(new IOException("Owned QEMU process exited.")); }, TaskScheduler.Default);
        controlStream = await controlAccept;
        readers = [.. readers, Guard(() => ReadFrames(controlStream, GuestMessage, 2200000, lifetime.Token))];
        var version = await greeting.Task.WaitAsync(setup.Token);
        await Qmp("qmp_capabilities", setup.Token);
        var cpus = await Qmp("query-cpus-fast", setup.Token); var memory = await Qmp("query-memory-size-summary", setup.Token);
        var pci = await Qmp("query-pci", setup.Token); var block = await Qmp("query-block", setup.Token);
        if (cpus.GetArrayLength() != spec.Cpus || memory.GetProperty("base-memory").GetInt64() != (long)spec.MemoryMiB * 1024 * 1024 ||
            pci.EnumerateArray().Any(bus => bus.GetProperty("devices").EnumerateArray().Any(device => device.TryGetProperty("class_info", out var info) && (info.GetProperty("class").GetInt32() >> 8) == 2)))
            throw new InvalidOperationException("Observed VM resources or devices differ from the requested boundary.");
        await Qmp("cont", setup.Token); var guest = await ready.Task.WaitAsync(setup.Token);
        if (guest.GetProperty("uid").GetInt32() != 1000) throw new InvalidOperationException("The guest steward did not start as the worker user.");
        Observation = new(Id, version, cpus, memory, pci, block, guest, control.ClientCertificateSha256, console.ClientCertificateSha256,
            control.NegotiatedProtocol!, console.NegotiatedProtocol!);
    }

    private async Task Guard(Func<Task> action)
    {
        try { await action(); if (Volatile.Read(ref stopping) == 0) Fail(new IOException("VM channel closed unexpectedly.")); }
        catch (Exception ex) { if (Volatile.Read(ref stopping) == 0) Fail(ex); }
    }
    private void Fail(Exception error)
    {
        if (Volatile.Read(ref stopping) == 0) { Interlocked.CompareExchange(ref failure, error, null); process.Stop("transport-failed"); }
        lifetime.Cancel(); greeting.TrySetException(error); ready.TrySetException(error);
        foreach (var pending in replies.Values.Concat(commands.Values)) pending.TrySetException(error);
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
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"\Ahttp-[1-9][0-9]{0,5}\z") || !brokerIds.Add(id) || brokerIds.Count > 200 || brokerCalls.Count >= 8)
                    throw new IOException("Guest broker request limit or identity violated.");
                var task = Forward(id, message.Clone()); brokerCalls[id] = task;
                _ = task.ContinueWith(_ => brokerCalls.TryRemove(id, out var removed), TaskScheduler.Default);
                break;
            default: throw new IOException("Unsupported guest frame.");
        }
    }

    private async Task Forward(string id, JsonElement message)
    {
        try
        {
            var response = await ForwardHttp(message, lifetime.Token);
            await Send(controlStream!, controlWrite, new { type = "response", id, response.status, response.body, response.contentType }, lifetime.Token);
        }
        catch (Exception) when (!lifetime.IsCancellationRequested)
        {
            try { await Send(controlStream!, controlWrite, new { type = "response", id, status = 502, body = "", contentType = "application/json" }, lifetime.Token); }
            catch (Exception error) { Fail(error); }
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
    }
    private async Task<(int status, string body, string contentType)> ForwardHttp(JsonElement message, CancellationToken cancellation)
    {
        var path = message.GetProperty("path").GetString(); var method = message.GetProperty("method").GetString();
        if (path != $"/worker/{route.RunId}/mcp" && path != $"/worker/{route.RunId}/v1/chat/completions" || method is not ("GET" or "POST" or "DELETE"))
            throw new IOException("Guest broker route denied.");
        var encoded = message.GetProperty("body").GetString()!; if (encoded.Length > 200000) throw new IOException("Guest request exceeds its bound.");
        var body = Convert.FromBase64String(encoded); if (body.Length > 150000 || method == "GET" && body.Length != 0) throw new IOException("Invalid guest request body.");
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri($"http://127.0.0.1:{route.Port}" + path));
        if (method != "GET") request.Content = new ByteArrayContent(body);
        var total = 0;
        foreach (var header in message.GetProperty("headers").EnumerateObject())
        {
            total += header.Name.Length + header.Value.GetRawText().Length;
            if (total > 16000) throw new IOException("Guest headers exceed their bound.");
            if (header.Name is not ("authorization" or "content-type" or "accept" or "mcp-protocol-version" or "mcp-session-id")) continue;
            var value = header.Value.GetString()!; if (value.Length > 8192 || value.Contains('\r') || value.Contains('\n')) throw new IOException("Invalid guest header.");
            if (header.Name == "content-type") { if (request.Content != null) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value); }
            else request.Headers.Add(header.Name, value);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        await using var content = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await content.ReadAsync(buffer, deadline.Token)) > 0)
        { if (bytes.Length + count > 1500000) throw new IOException("Broker response exceeds its bound."); bytes.Write(buffer, 0, count); }
        return ((int)response.StatusCode, Convert.ToBase64String(bytes.ToArray()), response.Content.Headers.ContentType?.ToString() ?? "application/json");
    }

    private async Task<JsonElement> Qmp(string operation, CancellationToken cancellation)
    {
        var id = Guid.NewGuid().ToString("N"); var pending = Signal(); replies[id] = pending;
        try { await Send(process.Input, qmpWrite, new { execute = operation, id }, cancellation); return await pending.Task.WaitAsync(cancellation); }
        finally { replies.TryRemove(id, out _); }
    }

    public async Task<SandboxCommandResult> Execute(IReadOnlyList<string> command, string? input, CancellationToken cancellation)
    {
        if (command.Count is < 1 or > 128 || command.Any(a => a.Contains('\0') || a.Length > 100000) || Encoding.UTF8.GetByteCount(input ?? "") > 200000)
            throw new ArgumentException("Invalid guest command envelope.");
        if (Interlocked.Increment(ref commandSlots) > 4) { Interlocked.Decrement(ref commandSlots); throw new InvalidOperationException("Guest command concurrency limit reached."); }
        var id = Guid.NewGuid().ToString("N"); var pending = Signal(); commands[id] = pending;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
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
            await Send(controlStream!, controlWrite, new { type = "shutdown" }, deadline.Token);
            var outcome = await process.Completion.WaitAsync(deadline.Token);
            await Task.WhenAll(readers).WaitAsync(deadline.Token);
            if (!outcome.Succeeded || !guestShutdown || failure != null) throw new IOException("Guest shutdown was not independently confirmed.");
            return new(Id, outcome, guestShutdown, control.RejectedConnections, console.RejectedConnections);
        }
        catch { process.Stop("shutdown-unconfirmed"); throw; }
        finally { await DisposeAsync(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Interlocked.Exchange(ref stopping, 1); lifetime.Cancel(); await process.DisposeAsync();
        control.Dispose(); console.Dispose(); http.Dispose();
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
    private static async Task ReadFrames(Stream stream, Action<JsonElement> receive, int limit, CancellationToken cancellation)
    {
        using var frame = new MemoryStream(); var buffer = new byte[16384]; int count, frames = 0;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            var start = 0;
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] != 10) continue;
                if (frame.Length + i - start > limit || ++frames > 5000) throw new IOException("VM frame or message count exceeds its bound.");
                frame.Write(buffer, start, i - start);
                using var document = JsonDocument.Parse(frame.GetBuffer().AsMemory(0, (int)frame.Length), new() { MaxDepth = 32 });
                receive(document.RootElement); frame.SetLength(0); start = i + 1;
            }
            if (frame.Length + count - start > limit) throw new IOException("VM frame exceeds its bound.");
            frame.Write(buffer, start, count - start);
        }
        if (frame.Length != 0) throw new IOException("Truncated VM frame.");
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
