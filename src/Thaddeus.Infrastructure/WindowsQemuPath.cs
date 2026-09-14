using System.Runtime.InteropServices;
using System.Text;

namespace Thaddeus.Infrastructure;

internal sealed class WindowsQemuPathException() : IOException(
    "This Windows QEMU build needs paths containing only English letters, numbers and symbols. Windows could not supply a compatible short path. Move the worker package and study to compatible folders, then check again. No Windows setting was changed.");

/// <summary>The pinned Windows QEMU reads command arguments through its legacy code page.</summary>
internal static class WindowsQemuPath
{
    internal static string Existing(string path, Func<string, string?>? shortPath = null)
    {
        if (!OperatingSystem.IsWindows()) return path;
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use an absolute QEMU input path.");
        Store.AssertNoLinks(path);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("A QEMU input path is missing.");
        if (path.All(character => character <= 127)) return path;
        var result = (shortPath ?? NativeShortPath)(path);
        if (string.IsNullOrEmpty(result) || result.Any(character => character > 127) || !Path.IsPathFullyQualified(result))
            throw new WindowsQemuPathException();
        Store.AssertNoLinks(result);
        return result;
    }

    internal static string NewFile(string path)
    {
        if (!OperatingSystem.IsWindows()) return path;
        if (File.Exists(path)) return Existing(path);
        var name = Path.GetFileName(path);
        if (name.Any(character => character > 127)) throw new WindowsQemuPathException();
        return Path.Combine(Existing(Path.GetDirectoryName(path)!), name);
    }

    private static string? NativeShortPath(string path)
    {
        var buffer = new StringBuilder(32768);
        var written = GetShortPathNameW(path, buffer, (uint)buffer.Capacity);
        return written is > 0 and < 32768 ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string path, StringBuilder output, uint capacity);
}
