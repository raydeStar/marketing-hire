using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Infrastructure;

public sealed record LinuxProcessLimits(long MemoryBytes, int CpuQuotaPercent, int Tasks)
{
    public void Validate()
    {
        if (MemoryBytes is < 67108864 or > 274877906944 || CpuQuotaPercent is < 1 or > 400 || Tasks is < 16 or > 512)
            throw new ArgumentException("Linux worker resource limits are outside the supported bounds.");
    }
}

public sealed record LinuxResourceObservation(string ControlGroup, long MemoryBytes, long SwapBytes,
    long CpuQuotaMicroseconds, long CpuPeriodMicroseconds, int Tasks);
internal sealed record LinuxServiceRequest(string Unit, string LeasePath, string Nonce, OwnedProcessRequest Process, LinuxProcessLimits Limits);
internal sealed record LinuxServiceReady(string Nonce, int SupervisorId, LinuxResourceObservation Resources);
internal sealed record LinuxChildStarted(int ProcessId);

/// <summary>Explicit systemd service contract; these arguments alone are not proof of applied limits.</summary>
public static class LinuxProcessContract
{
    public static void Validate(OwnedProcessRequest request, LinuxProcessLimits limits)
    {
        limits.Validate();
        if (!Path.IsPathFullyQualified(request.Executable) || !Path.IsPathFullyQualified(request.WorkingDirectory) || request.Resources != null ||
            request.Lifetime <= TimeSpan.Zero || request.Lifetime > TimeSpan.FromMinutes(15) || request.OutputLimit is < 1 or > 2_000_000 ||
            request.Arguments.Count > 256 || request.Environment.Count > 64)
            throw new ArgumentException("Linux process paths or limits are invalid; use Linux resource limits explicitly.");
        if (new[] { request.Executable, request.WorkingDirectory }.Concat(request.Arguments).Any(value => value.Contains('\0')) ||
            request.Arguments.Sum(value => (long)Encoding.UTF8.GetByteCount(value) + 1) > 32768 ||
            request.Environment.Any(pair => string.IsNullOrEmpty(pair.Key) || pair.Key.Contains('=') || pair.Key.Contains('\0') || pair.Value.Contains('\0')) ||
            request.Environment.Sum(pair => (long)Encoding.UTF8.GetByteCount(pair.Key) + Encoding.UTF8.GetByteCount(pair.Value) + 2) > 32768)
            throw new ArgumentException("Linux process arguments or explicit environment are invalid.");
    }

    public static IReadOnlyList<string> Arguments(string unit, string supervisor, string requestPath, string requestHash, OwnedProcessRequest request, LinuxProcessLimits limits)
    {
        Validate(request, limits);
        if (!Regex.IsMatch(unit, @"\Athaddeus-worker-[a-f0-9]{32}\.service\z") || !Regex.IsMatch(requestHash, @"\A[a-f0-9]{64}\z") ||
            !Path.IsPathFullyQualified(supervisor) || !Path.IsPathFullyQualified(requestPath) || supervisor.Contains('\0') || requestPath.Contains('\0'))
            throw new ArgumentException("Linux service identity or supervisor paths are invalid.");
        string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        return ["--user", "--pipe", "--wait", "--collect", "--quiet", "--service-type=exec", "--expand-environment=no", "--unit=" + unit,
            "--property=KillMode=control-group", "--property=KillSignal=SIGKILL", "--property=SendSIGKILL=yes", "--property=TimeoutStopSec=2s",
            "--property=RuntimeMaxSec=" + Number((long)Math.Ceiling(request.Lifetime.TotalSeconds)),
            "--property=MemoryMax=" + Number(limits.MemoryBytes), "--property=MemorySwapMax=0",
            "--property=CPUQuota=" + Number(limits.CpuQuotaPercent) + "%", "--property=CPUQuotaPeriodSec=100ms",
            "--property=TasksMax=" + Number(limits.Tasks), "--property=OOMPolicy=kill", "--property=NoNewPrivileges=yes",
            "--property=ProtectControlGroups=yes", "--", supervisor, "--linux-supervise", requestPath, requestHash];
    }

    public static void VerifyResources(string unit, LinuxProcessLimits requested, LinuxResourceObservation observed)
    {
        requested.Validate();
        if (!observed.ControlGroup.EndsWith("/" + unit, StringComparison.Ordinal) || observed.MemoryBytes != requested.MemoryBytes || observed.SwapBytes != 0 ||
            observed.CpuQuotaMicroseconds <= 0 || observed.CpuPeriodMicroseconds != 100000 ||
            observed.CpuQuotaMicroseconds != requested.CpuQuotaPercent * 1000L || observed.Tasks != requested.Tasks)
            throw new IOException("The observed Linux cgroup differs from the requested worker boundary.");
    }

    internal static LinuxResourceObservation Observe(string unit)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var membership = File.ReadAllLines("/proc/self/cgroup").Single(line => line.StartsWith("0::", StringComparison.Ordinal))[3..];
        if (!membership.StartsWith('/') || membership.Split('/').Any(part => part is "." or "..") || !membership.EndsWith("/" + unit, StringComparison.Ordinal))
            throw new IOException("The supervisor is not in its expected unified cgroup service.");
        var root = "/sys/fs/cgroup" + membership;
        long Read(string file) => long.Parse(File.ReadAllText(Path.Combine(root, file)).Trim(), CultureInfo.InvariantCulture);
        var cpu = File.ReadAllText(Path.Combine(root, "cpu.max")).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new(membership, Read("memory.max"), Read("memory.swap.max"), long.Parse(cpu[0], CultureInfo.InvariantCulture),
            long.Parse(cpu[1], CultureInfo.InvariantCulture), checked((int)Read("pids.max")));
    }

    internal static async Task Send<T>(Stream stream, T message, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > 4096) throw new IOException("Linux ownership handshake exceeded its bound.");
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, cancellation); await stream.WriteAsync(bytes, cancellation); await stream.FlushAsync(cancellation);
    }
    internal static async Task<T> Receive<T>(Stream stream, CancellationToken cancellation)
    {
        var prefix = new byte[4]; await stream.ReadExactlyAsync(prefix, cancellation);
        var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length is < 1 or > 4096) throw new IOException("Linux ownership handshake exceeded its bound.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, cancellation);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new IOException("Linux ownership handshake was empty.");
    }
}
