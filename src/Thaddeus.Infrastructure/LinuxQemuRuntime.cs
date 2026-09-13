using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Thaddeus.Infrastructure;

/// <summary>Explicit Linux x64 bundle, including the loader, libraries and firmware. No host library fallback is admitted.</summary>
public sealed class LinuxQemuRuntime : IDisposable
{
    private readonly QemuRuntimeLease lease;
    public string Root { get; }
    public QemuPinnedFile Executable { get; }
    public QemuPinnedFile ImageTool { get; }
    public QemuPinnedFile Loader { get; }
    public string DataDirectory => Path.Combine(Root, "share", "qemu");
    private LinuxQemuRuntime(string root, QemuRuntimeLease lease)
    {
        Root = root; this.lease = lease;
        Executable = lease.FilePin("bin/qemu-system-x86_64"); ImageTool = lease.FilePin("bin/qemu-img"); Loader = lease.FilePin("lib/ld-linux-x86-64.so.2");
    }

    public static async Task<LinuxQemuRuntime> Open(QemuRuntimePackage package, QemuPinnedFile executable, QemuPinnedFile imageTool, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64) throw new PlatformNotSupportedException("This runtime bundle requires native Linux x64.");
        if (package.Root.IndexOfAny([':', ';', '$']) >= 0 || package.Root.Any(char.IsControl)) throw new ArgumentException("The runtime path cannot change the library search list.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package.Root));
        RequireReadOnlyMount(root, await File.ReadAllTextAsync("/proc/self/mountinfo", cancellation));
        var lease = await QemuRuntimeLease.Open(package, executable, imageTool, cancellation, "qemu-linux-x64-runtime");
        try
        {
            var runtime = new LinuxQemuRuntime(root, lease);
            if (runtime.Executable != executable || runtime.ImageTool != imageTool) throw new IOException("Linux runtime executable roles differ from the approved bundle.");
            return runtime;
        }
        catch { lease.Dispose(); throw; }
    }

    public OwnedProcessRequest Request(string executable, IReadOnlyList<string> arguments, string directory, TimeSpan lifetime, int outputLimit)
    {
        if (executable != Executable.Path && executable != ImageTool.Path) throw new ArgumentException("Only this bundle's adapter executables may be launched.");
        lease.VerifyInventory();
        return new(Loader.Path, new[] { "--inhibit-cache", "--library-path", Path.Combine(Root, "lib"), executable }.Concat(arguments).ToArray(), directory,
            new Dictionary<string, string> { ["HOME"] = directory, ["TMPDIR"] = directory, ["LC_ALL"] = "C", ["QEMU_MODULE_DIR"] = Path.Combine(Root, "disabled-modules") }, lifetime, outputLimit);
    }

    public string[] VerifyExecutableMappings(int processId)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var mapped = ExecutablePaths(File.ReadAllText($"/proc/{processId}/maps"));
        foreach (var path in mapped)
        {
            var relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            if (lease.FilePin(relative).Path != path) throw new IOException("A Linux adapter mapped executable code outside its approved runtime.");
        }
        if (!mapped.Contains(Loader.Path) || (!mapped.Contains(Executable.Path) && !mapped.Contains(ImageTool.Path)))
            throw new IOException("The Linux adapter's executable mappings do not match its runtime roles.");
        return mapped;
    }

    public static string[] ExecutablePaths(string maps)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in maps.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) throw new IOException("Invalid Linux process map.");
            if (!fields[1].Contains('x')) continue;
            if (fields.Length == 6 && fields[5] is "[vdso]" or "[vsyscall]") continue;
            if (fields.Length != 6 || !fields[5].StartsWith('/') || fields[5].EndsWith(" (deleted)", StringComparison.Ordinal))
                throw new IOException("The Linux adapter has unaccounted executable memory.");
            paths.Add(Unescape(fields[5]));
        }
        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    public static void RequireReadOnlyMount(string root, string mountInfo)
    {
        var mounts = mountInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var halves = line.Split(" - ", 2, StringSplitOptions.None);
            if (halves.Length != 2) throw new IOException("Invalid Linux mount information.");
            var before = halves[0].Split(' '); var after = halves[1].Split(' ');
            if (before.Length < 6 || after.Length < 3) throw new IOException("Invalid Linux mount information.");
            return (Path: Unescape(before[4]), Vfs: before[5], Super: after[2]);
        }).ToArray();
        var covering = mounts.Where(mount => root == mount.Path || root.StartsWith(mount.Path.TrimEnd('/') + "/", StringComparison.Ordinal))
            .OrderByDescending(mount => mount.Path.Length).FirstOrDefault();
        static bool ReadOnly(string? options) => options != null && options.Split(',').Contains("ro") && !options.Split(',').Contains("rw");
        if (!root.StartsWith('/') || !ReadOnly(covering.Vfs) || !ReadOnly(covering.Super) ||
            mounts.Any(mount => mount.Path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal) && (!ReadOnly(mount.Vfs) || !ReadOnly(mount.Super))))
            throw new IOException("Linux runtime storage must be mounted read-only; advisory file locks are insufficient.");
    }
    private static string Unescape(string value) => Regex.Replace(value, @"\\([0-7]{3})", match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString());
    public void Dispose() => lease.Dispose();
}
