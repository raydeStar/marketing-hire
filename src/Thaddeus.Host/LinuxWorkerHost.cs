using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

/// <summary>The packaged host also serves as systemd's trusted process steward, before any study is opened.</summary>
public static class LinuxWorkerHost
{
    public static bool Supported => NativeWorkerPlatform.Backend == "qemu-kvm";

    public static string Supervisor(string packageDirectory, string? processPath)
    {
        if (!Supported) throw new PlatformNotSupportedException("The Linux worker currently requires a native Linux x64 host.");
        var expected = Path.Combine(Path.GetFullPath(packageDirectory), "Thaddeus.Host");
        Store.AssertNoLinks(expected);
        if (processPath == null || !Path.GetFullPath(processPath).Equals(expected, StringComparison.Ordinal) || !File.Exists(expected))
            throw new IOException("Start the Linux study with its published Thaddeus.Host executable. A dotnet or test runner cannot supervise workers.");
        return expected;
    }

    public static async Task<int> Run(string[] arguments)
    {
        try
        {
            if (!Supported || arguments is not ["--linux-supervise", var request, var hash])
                throw new ArgumentException("Use the native Linux x64 service entry point with one request path and its hash.");
            return await LinuxServiceSupervisor.Run(request, hash);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("The Linux steward refused admission: " + error.Message);
            return 125;
        }
    }
}
