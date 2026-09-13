using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Thaddeus.Infrastructure;

/// <summary>Owns a trusted Linux adapter in one transient user service. Agent execution still belongs inside the VM.</summary>
public sealed class LinuxSystemdProcess : IAsyncDisposable
{
    private readonly Process runner;
    private readonly Socket lease;
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationTokenRegistration cancellationRegistration;
    private readonly object disposeGate = new();
    private Task? disposal;
    private string? stopReason;
    public string Unit { get; }
    public int Id { get; }
    public int SupervisorId { get; }
    public LinuxResourceObservation Resources { get; }
    public Stream Input { get; }
    public Stream Output { get; }
    public Stream Error { get; }
    public Task<OwnedProcessExit> Completion { get; }

    private LinuxSystemdProcess(Process runner, Socket lease, LinuxServiceRequest launch, LinuxServiceReady ready, int childId, CancellationToken cancellation)
    {
        this.runner = runner; this.lease = lease; Unit = launch.Unit; Id = childId; SupervisorId = ready.SupervisorId; Resources = ready.Resources;
        Input = runner.StandardInput.BaseStream;
        Output = new BoundedOutput(runner.StandardOutput.BaseStream, launch.Process.OutputLimit, () => Stop("output-limit"));
        Error = new BoundedOutput(runner.StandardError.BaseStream, launch.Process.OutputLimit, () => Stop("output-limit"));
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        cancellationRegistration = lifetime.Token.Register(() => Stop(cancellation.IsCancellationRequested ? "cancelled" : "lifetime"));
        lifetime.CancelAfter(launch.Process.Lifetime);
        Completion = ObserveExit();
    }

    public static async Task<LinuxSystemdProcess> Start(OwnedProcessRequest request, LinuxProcessLimits limits, string supervisor, string directory, CancellationToken cancellation = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux process ownership requires a native systemd user session.");
        LinuxProcessContract.Validate(request, limits); cancellation.ThrowIfCancellationRequested();
        var unit = "thaddeus-worker-" + Guid.NewGuid().ToString("N") + ".service";
        var leasePath = Path.Combine(directory, "owner.sock");
        if (!Path.IsPathFullyQualified(directory) || Encoding.UTF8.GetByteCount(leasePath) > 103)
            throw new ArgumentException("Use a short private Linux runtime directory for the owner socket.");
        var launch = new LinuxServiceRequest(unit, leasePath, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), request, limits);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(launch);
        var path = Path.Combine(directory, "request.json");
        var arguments = LinuxProcessContract.Arguments(unit, supervisor, path, Convert.ToHexStringLower(SHA256.HashData(bytes)), request, limits);
        PrivateWorkerDirectory.Create(directory);
        await File.WriteAllBytesAsync(path, bytes, cancellation);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(leasePath)); listener.Listen(1);
        Process? runner = null; Socket? lease = null;
        using var setup = CancellationTokenSource.CreateLinkedTokenSource(cancellation); setup.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var start = ManagerCommand("/usr/bin/systemd-run", arguments);
            start.RedirectStandardInput = true;
            runner = Process.Start(start) ?? throw new IOException("systemd did not accept the worker service.");
            var accepting = listener.AcceptAsync(setup.Token).AsTask();
            if (await Task.WhenAny(accepting, runner.WaitForExitAsync(setup.Token)) != accepting)
                throw new IOException("The Linux worker service exited before ownership was established: " + await runner.StandardError.ReadToEndAsync(setup.Token));
            lease = await accepting;
            using var stream = new NetworkStream(lease, ownsSocket: false);
            var ready = await LinuxProcessContract.Receive<LinuxServiceReady>(stream, setup.Token);
            if (ready.Nonce != launch.Nonce || ready.SupervisorId < 1) throw new IOException("The Linux supervisor did not match its owner lease.");
            LinuxProcessContract.VerifyResources(unit, limits, ready.Resources);
            await stream.WriteAsync(new byte[] { 1 }, setup.Token);
            var child = await LinuxProcessContract.Receive<LinuxChildStarted>(stream, setup.Token);
            if (child.ProcessId < 1) throw new IOException("The Linux supervisor did not report its child.");
            await File.WriteAllTextAsync(Path.Combine(directory, "observation.json"), JsonSerializer.Serialize(new { unit, child.ProcessId, ready.SupervisorId, ready.Resources }), setup.Token);
            cancellation.ThrowIfCancellationRequested();
            return new(runner, lease, launch, ready, child.ProcessId, cancellation);
        }
        catch
        {
            lease?.Dispose(); listener.Dispose();
            await StopUnit(unit);
            if (runner != null)
            {
                if (!runner.HasExited) runner.Kill();
                await runner.WaitForExitAsync(); runner.Dispose();
            }
            throw;
        }
    }

    public void Stop(string reason = "stopped")
    {
        Interlocked.CompareExchange(ref stopReason, reason, null);
        lease.Dispose(); // CLOEXEC: neither the service manager nor QEMU owns this end of the lease.
    }

    public LinuxResourceObservation ObserveResources()
    {
        var observed = LinuxProcessContract.ObserveGroup(Unit, Resources.ControlGroup);
        if (observed != Resources) throw new IOException("Linux worker resource controls changed after admission.");
        return observed;
    }

    private async Task<OwnedProcessExit> ObserveExit()
    {
        try
        {
            await runner.WaitForExitAsync();
            var events = "/sys/fs/cgroup" + Resources.ControlGroup + "/cgroup.events";
            if (File.Exists(events) && File.ReadAllLines(events).Contains("populated 1"))
            {
                Stop("service-ended-with-live-members"); await StopUnit(Unit);
                if (File.Exists(events) && File.ReadAllLines(events).Contains("populated 1"))
                    throw new IOException("The owned Linux service still has live members; reconcile it before further work.");
            }
            return new(runner.ExitCode, Volatile.Read(ref stopReason));
        }
        finally { lifetime.CancelAfter(Timeout.InfiniteTimeSpan); lease.Dispose(); }
    }

    private static ProcessStartInfo ManagerCommand(string executable, IReadOnlyList<string> arguments)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtime) || !Path.IsPathFullyQualified(runtime))
            throw new IOException("A native systemd user runtime is required. No host-process fallback was selected.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Clear();
        start.Environment["XDG_RUNTIME_DIR"] = runtime;
        start.Environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=" + Path.Combine(runtime, "bus");
        start.Environment["PATH"] = "/usr/bin:/bin"; start.Environment["LC_ALL"] = "C";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task StopUnit(string unit)
    {
        using var command = Process.Start(ManagerCommand("/usr/bin/systemctl", ["--user", "stop", unit]));
        if (command == null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var output = command.StandardOutput.ReadToEndAsync(timeout.Token); var error = command.StandardError.ReadToEndAsync(timeout.Token);
        try { await command.WaitForExitAsync(timeout.Token); await Task.WhenAll(output, error); }
        catch (OperationCanceledException) { if (!command.HasExited) command.Kill(); throw new IOException("Stopping the owned Linux service timed out."); }
    }

    public ValueTask DisposeAsync() { lock (disposeGate) return new(disposal ??= DisposeCore()); }
    private async Task DisposeCore()
    {
        Stop();
        try { await Completion; }
        finally { cancellationRegistration.Dispose(); lifetime.Dispose(); Input.Dispose(); Output.Dispose(); Error.Dispose(); runner.Dispose(); }
    }

    private sealed class BoundedOutput(Stream inner, int limit, Action exceeded) : Stream
    {
        private int total;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count); Check(read); return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken); Check(read); return read;
        }
        private void Check(int read) { if (Interlocked.Add(ref total, read) > limit) { exceeded(); throw new IOException("Linux worker output exceeded its bound."); } }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
