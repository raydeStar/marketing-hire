using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

internal static class DevelopmentWorkerSetup
{
    internal static HostWorkerSetup Create(Store store, string? path, int brokerPort)
    {
        if (string.IsNullOrWhiteSpace(path)) return new(store);
        var windows = NativeWorkerPlatform.Backend == "qemu-whpx";
        if (NativeWorkerPlatform.Backend == null) return new(store, unavailable: "This configured preview worker requires native Windows x64 or Linux x64. This browser can connect to a supported host.");
        try
        {
            var supervisor = windows ? null : LinuxWorkerHost.Supervisor(AppContext.BaseDirectory, Environment.ProcessPath);
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use an absolute host installation configuration path.");
            Store.AssertNoLinks(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is < 1 or > 64000) throw new IOException("Invalid installation configuration size.");
            using var document = JsonDocument.Parse(file, new() { MaxDepth = 8 });
            var installation = document.RootElement.GetProperty("kind").GetString() switch
            {
                "qemu" => document.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!,
                BundledWorkerInstallation.Kind => BundledWorkerInstallation.Resolve(document.RootElement,
                    Path.GetDirectoryName(Path.GetFullPath(path))!, windows ? "win-x64" : "linux-x64"),
                _ => throw new ArgumentException("Unsupported host installation.")
            };
            if (installation?.RuntimePackage == null) throw new ArgumentException("A full runtime package is required.");
            var kind = windows ? "qemu-whpx" : "qemu-kvm";
            var digest = Wire.Hash(Wire.Pack(new { backend = kind, installation, brokerPort }));
            return new(store, new(kind, windows ? "Windows isolated worker" : "Linux isolated worker", digest, true), new QemuResearchFactory(store, installation, brokerPort, supervisor),
                async cancellation =>
                {
                    await using var sandbox = new QemuSandboxBackend(store, installation, new(new string('0', 32), brokerPort), supervisor);
                    return await sandbox.Inspect(cancellation);
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return new(store, unavailable: "The configured worker installation needs attention on this host. Ordinary chat remains available."); }
    }
}
