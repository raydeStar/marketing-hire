using System.Runtime.InteropServices;

namespace Thaddeus.Infrastructure;

public static class NativeWorkerPlatform
{
    public static string Platform => OperatingSystem.IsWindowsVersionAtLeast(10) ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unsupported";
    public static string? Backend => Select(Platform, RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture);
    internal static string? Select(string platform, Architecture system, Architecture process) => (platform, system, process) switch
    {
        ("windows", Architecture.X64, Architecture.X64) => "qemu-whpx",
        ("linux", Architecture.X64, Architecture.X64) => "qemu-kvm",
        _ => null
    };
}
