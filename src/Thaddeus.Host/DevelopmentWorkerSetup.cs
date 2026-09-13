using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

internal static class DevelopmentWorkerSetup
{
    internal static HostWorkerSetup Create(Store store, string? path, int brokerPort)
    {
        if (string.IsNullOrWhiteSpace(path)) return new(store);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return new(store, unavailable: "This configured preview worker requires Windows. This browser can connect to a supported host.");
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use an absolute host installation configuration path.");
            Store.AssertNoLinks(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is < 1 or > 64000) throw new IOException("Invalid installation configuration size.");
            using var document = JsonDocument.Parse(file);
            if (document.RootElement.GetProperty("kind").GetString() != "qemu") throw new ArgumentException("Unsupported host installation.");
            var installation = document.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!;
            if (installation?.RuntimePackage == null) throw new ArgumentException("A full runtime package is required.");
            var digest = Wire.Hash(Wire.Pack(new { backend = "qemu-whpx", installation, brokerPort }));
            return new(store, new("qemu-whpx", "Windows isolated worker", digest, true), new QemuResearchFactory(store, installation, brokerPort),
                async cancellation =>
                {
                    if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException();
                    await using var sandbox = new QemuSandboxBackend(store, installation, new(new string('0', 32), brokerPort));
                    return await sandbox.Inspect(cancellation);
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return new(store, unavailable: "The configured worker installation needs attention on this host. Ordinary chat remains available."); }
    }
}
