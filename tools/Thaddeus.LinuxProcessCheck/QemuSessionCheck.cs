using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

internal static class QemuSessionCheck
{
    public static async Task<int> Run(string installationFile, string evidenceDirectory)
    {
        var checks = new List<object>();
        void Require(bool valid, string message) { if (!valid) throw new IOException(message); }
        void Record(string name, object evidence) { checks.Add(new { name, evidence }); Console.WriteLine("SESSION_CHECK " + Wire.Pack(new { name, evidence })); }
        QemuWorkerSession? session = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var fixture = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            PrivateWorkerDirectory.Create(evidenceDirectory);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(installationFile, deadline.Token));
            var installation = json.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
            foreach (var pin in new[] { installation.Kernel, installation.Initrd, installation.BaseDisk })
            {
                await using var file = new FileStream(pin.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Require(Convert.ToHexStringLower(await SHA256.HashDataAsync(file, deadline.Token)) == pin.Sha256, "Guest boot input changed: " + pin.Path);
            }
            using var windowsRuntime = OperatingSystem.IsWindowsVersionAtLeast(10)
                ? await QemuRuntimeLease.Open(installation.RuntimePackage!, installation.Executable, installation.ImageTool, deadline.Token) : null;
            using var linuxRuntime = OperatingSystem.IsLinux()
                ? await LinuxQemuRuntime.Open(installation.RuntimePackage!, installation.Executable, installation.ImageTool, deadline.Token) : null;
            var overlay = Path.Combine(evidenceDirectory, "worker.qcow2");
            string[] imageArguments = ["create", "-f", "qcow2", "-F", "raw", "-b", installation.BaseDisk.Path, overlay];
            OwnedProcessExit imageExit;
            if (OperatingSystem.IsLinux() && linuxRuntime != null)
            {
                LinuxQemuRuntime.RequireReadOnlyMount(Path.GetDirectoryName(installation.BaseDisk.Path)!, await File.ReadAllTextAsync("/proc/self/mountinfo", deadline.Token));
                var request = linuxRuntime.Request(installation.ImageTool.Path, imageArguments, evidenceDirectory, TimeSpan.FromSeconds(30), 10000);
                await using var image = await LinuxSystemdProcess.Start(request, new(268435456, 100, 64), Environment.ProcessPath!,
                    Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")!, "image-" + Guid.NewGuid().ToString("N")), deadline.Token);
                await Task.WhenAll(image.Output.CopyToAsync(Stream.Null, deadline.Token), image.Error.CopyToAsync(Console.OpenStandardError(), deadline.Token));
                imageExit = await image.Completion;
            }
            else if (OperatingSystem.IsWindowsVersionAtLeast(10))
            {
                await using var image = WindowsJobProcess.Start(new(installation.ImageTool.Path, imageArguments, evidenceDirectory,
                    QemuWorkerSession.HostEnvironment(evidenceDirectory), TimeSpan.FromSeconds(30), 10000, new(268435456, 1000, 1)));
                await Task.WhenAll(image.Output.CopyToAsync(Stream.Null, deadline.Token), image.Error.CopyToAsync(Console.OpenStandardError(), deadline.Token));
                imageExit = await image.Completion;
            }
            else throw new PlatformNotSupportedException();
            Require(imageExit.Succeeded, "Private overlay creation failed.");
            Record("private-overlay", imageExit);
            fixture.Start(1);
            var runId = Guid.NewGuid().ToString("N"); var nonce = Guid.NewGuid().ToString("N");
            var port = ((IPEndPoint)fixture.LocalEndpoint).Port;
            var received = FixtureRequest(fixture, runId, nonce, deadline.Token);
            var spec = new SandboxSpec("thaddeus-" + runId, "thaddeus/fixture@sha256:" + installation.BaseDisk.Sha256, 1, 1536);
            var files = new QemuBootFiles(installation.Executable.Path, installation.Kernel.Path, installation.Initrd.Path, installation.BaseDisk.Path, overlay);
            var boot = Path.Combine(evidenceDirectory, "boot");
            if (OperatingSystem.IsLinux() && linuxRuntime != null)
                session = await QemuWorkerSession.StartLinux(files, spec, new(runId, port), boot, linuxRuntime, Environment.ProcessPath!, deadline.Token);
            else if (OperatingSystem.IsWindowsVersionAtLeast(10))
                session = await QemuWorkerSession.Start(files, spec, new(runId, port), boot, deadline.Token);
            else throw new PlatformNotSupportedException();
            Record("native-guest-and-authenticated-channels", session.Observation);
            if (OperatingSystem.IsLinux()) Require(session.Observation.LinuxHostResources != null && session.Observation.ExecutableMappings?.Length > 2, "Missing native Linux resource or mapped-code observations.");
            var version = await session.Execute(["openclaw", "--version"], null, deadline.Token);
            Require(version.ExitCode == 0 && version.Output.Contains("2026.9.4", StringComparison.Ordinal), "The actual OpenClaw version did not match.");
            Record("pinned-openclaw", version);
            var content = "A raven wrote this inside the Linux estate. " + nonce;
            var guestCheck = await session.Execute(["python3", "-c", "import os,pathlib,hashlib,json; p=pathlib.Path('/home/agent/native-check.txt'); p.write_text(" + JsonSerializer.Serialize(content) + "); print(json.dumps({'uid':os.getuid(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'networks':sorted(os.listdir('/sys/class/net')),'hostPathExists':os.path.exists('/host'),'mounts':pathlib.Path('/proc/mounts').read_text()}))"], null, deadline.Token);
            Require(guestCheck.ExitCode == 0, "Guest file check failed.");
            using var guest = JsonDocument.Parse(guestCheck.Output);
            Require(guest.RootElement.GetProperty("uid").GetInt32() == 1000 && guest.RootElement.GetProperty("sha256").GetString() == Wire.Hash(content) &&
                guest.RootElement.GetProperty("networks").GetArrayLength() == 1 && guest.RootElement.GetProperty("networks")[0].GetString() == "lo" &&
                !guest.RootElement.GetProperty("hostPathExists").GetBoolean() && !guest.RootElement.GetProperty("mounts").GetString()!.Contains("9p") &&
                !guest.RootElement.GetProperty("mounts").GetString()!.Contains("virtiofs"), "Guest identity, file or device boundary differed.");
            Record("guest-files-and-network-inventory", guest.RootElement.Clone());
            var script = "const good=await fetch('http://127.0.0.1:5182/worker/" + runId + "/mcp',{method:'POST',headers:{'content-type':'text/plain'},body:'" + nonce + "'});const bad=await fetch('http://127.0.0.1:5182/host-secret');console.log(JSON.stringify({status:good.status,body:await good.json(),denied:bad.status}));";
            var routed = await session.Execute(["node", "--input-type=module", "-e", script], null, deadline.Token);
            Require(routed.ExitCode == 0, "Guest broker request failed.");
            using var route = JsonDocument.Parse(routed.Output);
            Require(route.RootElement.GetProperty("status").GetInt32() == 200 && route.RootElement.GetProperty("body").GetProperty("nonce").GetString() == nonce && route.RootElement.GetProperty("denied").GetInt32() == 502, "Broker round trip or denied path differs.");
            Record("fixed-broker-route-and-denied-host-path", new { response = route.RootElement.Clone(), hostReceived = await received });
            var stopped = await session.Stop(deadline.Token); session = null;
            Require(stopped.Outcome.Succeeded && stopped.GuestShutdown, "Guest shutdown was not confirmed.");
            Record("independent-guest-shutdown", stopped);
            var receipt = new { passed = true, checks, os = Environment.OSVersion.ToString(), modelCalls = 0, gpuDevices = 0 };
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "verified.json"), Wire.Pack(receipt), deadline.Token);
            Console.WriteLine("SESSION_VERIFIED " + Wire.Pack(receipt)); return 0;
        }
        catch (Exception error)
        {
            var receipt = new { passed = false, checks, error = error.ToString(), modelCalls = 0, gpuDevices = 0 };
            if (Directory.Exists(evidenceDirectory)) await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "failed.json"), Wire.Pack(receipt));
            Console.WriteLine("SESSION_VERIFIED " + Wire.Pack(receipt)); return 1;
        }
        finally { if (session != null) await session.DisposeAsync(); }
    }

    private static async Task<object> FixtureRequest(TcpListener listener, string runId, string nonce, CancellationToken cancellation)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellation);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var request = await reader.ReadLineAsync(cancellation); var length = -1; var count = 0;
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellation)))
        {
            if (++count > 30 || line.Length > 8192) throw new IOException("Fixture header bound exceeded.");
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..].Trim());
        }
        if (request != $"POST /worker/{runId}/mcp HTTP/1.1" || length != nonce.Length) throw new IOException("The host received an unexpected route or body length.");
        var body = new char[length]; await reader.ReadBlockAsync(body, cancellation);
        if (new string(body) != nonce) throw new IOException("The broker body changed in transit.");
        var payload = Wire.Pack(new { nonce });
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n{payload}"), cancellation);
        listener.Stop(); // A second request cannot accidentally reach another host endpoint.
        return new { request, bodyMatches = true };
    }
}
