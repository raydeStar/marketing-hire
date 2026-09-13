using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Thaddeus.Infrastructure;

/// <summary>A trusted service main process. systemd owns all descendants, including if this steward falls over.</summary>
public static class LinuxServiceSupervisor
{
    public static async Task<int> Run(string requestPath, string expectedHash)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (!Path.IsPathFullyQualified(requestPath) || new FileInfo(requestPath).Length > 131072) throw new IOException("Invalid Linux launch request.");
        var bytes = await File.ReadAllBytesAsync(requestPath);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != expectedHash) throw new IOException("Linux launch request changed before service admission.");
        var launch = JsonSerializer.Deserialize<LinuxServiceRequest>(bytes) ?? throw new IOException("Missing Linux launch request.");
        LinuxProcessContract.Validate(launch.Process, launch.Limits);
        var resources = LinuxProcessContract.Observe(launch.Unit);
        LinuxProcessContract.VerifyResources(launch.Unit, launch.Limits, resources);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(launch.LeasePath), setup.Token);
        using var lease = new NetworkStream(socket, ownsSocket: false);
        await LinuxProcessContract.Send(lease, new LinuxServiceReady(launch.Nonce, Environment.ProcessId, resources), setup.Token);
        var admitted = new byte[1]; await lease.ReadExactlyAsync(admitted, setup.Token);
        if (admitted[0] != 1) throw new IOException("The owning host did not admit the Linux worker.");
        var start = new ProcessStartInfo(launch.Process.Executable) { UseShellExecute = false, WorkingDirectory = launch.Process.WorkingDirectory };
        foreach (var argument in launch.Process.Arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var pair in launch.Process.Environment) start.Environment.Add(pair.Key, pair.Value);
        // The child inherits only service stdio. The separate CLOEXEC socket detects owner loss even when stdin is blocked.
        using var child = Process.Start(start) ?? throw new IOException("Linux worker did not start.");
        await LinuxProcessContract.Send(lease, new LinuxChildStarted(child.Id), setup.Token);
        var exit = child.WaitForExitAsync();
        var ownerLoss = lease.ReadAsync(new byte[1]).AsTask();
        if (await Task.WhenAny(exit, ownerLoss) == ownerLoss)
        {
            // Returning also makes systemd kill the entire control group; a detached descendant cannot keep the estate.
            return 125;
        }
        await exit;
        return child.ExitCode is >= 0 and <= 255 ? child.ExitCode : 126;
    }
}
