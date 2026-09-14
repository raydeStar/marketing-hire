using System.Collections;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record HostRequirement(string Id, string Name, CheckState State, string Detail, string? NextStep = null);
public sealed record HostRequirementsReport(string Platform, string Architecture, string? Backend, DateTimeOffset CheckedAt,
    IReadOnlyList<HostRequirement> Checks)
{
    public bool Passed => Backend != null && Checks.Count != 0 && Checks.All(check => check.State == CheckState.Passed);
    public string Summary => Passed ? "This computer passed the prerequisite checks. Check the installed worker package separately before enabling research."
        : "This computer needs attention before it can run the isolated worker. Ordinary chat remains available.";
}

/// <summary>Read-only host prerequisites. The raven inspects the foundation without moving the furniture.</summary>
public sealed class HostRequirements(IHostProcessRunner commands)
{
    public async Task<HostRequirementsReport> Check(bool packagedLinuxEntryPoint, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var platform = NativeWorkerPlatform.Platform; var backend = NativeWorkerPlatform.Backend;
        var checks = new List<HostRequirement>
        {
            new("platform", "Supported computer", backend == null ? CheckState.Failed : CheckState.Passed,
                backend == null ? "This operating system or processor does not have a supported worker in this preview." : "This preview supports the computer's operating system and native x64 processor.",
                backend == null ? "Use ordinary chat here, or connect this browser to a supported Windows x64 or Linux x64 host." : null)
        };
        if (backend == "qemu-whpx") checks.Add(WindowsHypervisor());
        else if (backend == "qemu-kvm")
        {
            checks.Add(new("application", "Packaged application", packagedLinuxEntryPoint ? CheckState.Passed : CheckState.Failed,
                packagedLinuxEntryPoint ? "The running application can own the Linux worker service." : "The study is running through a development or test runner.",
                packagedLinuxEntryPoint ? null : "Open the extracted application with start-thaddeus.sh, then check this computer again."));
            checks.Add(LinuxKvm());
            await LinuxSession(checks, cancellation);
        }
        return new(platform, RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), backend, DateTimeOffset.UtcNow, checks);
    }

    private static HostRequirement WindowsHypervisor()
    {
        const string next = "Enable Windows Hypervisor Platform in Windows Features, then restart when convenient. If it is already enabled, check virtualization settings with your computer maker.";
        try
        {
            var result = WHvGetCapability(0, out var present, sizeof(ulong), out var written);
            var available = result == 0 && written is >= 1 and <= sizeof(ulong) && present == 1;
            return new("virtualization", "Virtualization support", available ? CheckState.Passed : CheckState.Failed,
                available ? "Windows reports that its hypervisor is available. No virtual machine was created." : "Windows virtualization is not available to this application.", available ? null : next);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return new("virtualization", "Virtualization support", CheckState.Failed, "The Windows Hypervisor Platform API is unavailable.", next); }
    }

    private static HostRequirement LinuxKvm()
    {
        try
        {
            using var device = File.OpenHandle("/dev/kvm", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            var version = ioctl(device, 0xae00, 0); // KVM_GET_API_VERSION only: no VM, memory or vCPU allocation.
            return new("virtualization", "Virtualization support", version == 12 ? CheckState.Passed : CheckState.Failed,
                version == 12 ? "This account can access KVM API 12. No virtual machine was created." : "The KVM device did not report the required API version.",
                version == 12 ? null : "Ask the host administrator to check KVM support for this Linux installation.");
        }
        catch (UnauthorizedAccessException)
        { return new("virtualization", "Virtualization support", CheckState.Failed, "KVM is present, but this account cannot access it.", "Ask the host administrator to grant this account KVM access, then sign out and back in before checking again."); }
        catch (IOException)
        { return new("virtualization", "Virtualization support", CheckState.Failed, "A usable KVM device was not found.", "Ask the host administrator to enable hardware virtualization and the Linux KVM driver."); }
    }

    private async Task LinuxSession(List<HostRequirement> checks, CancellationToken cancellation)
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtime) || !Path.IsPathFullyQualified(runtime))
        {
            checks.Add(new("services", "Worker service manager", CheckState.Failed, "This application has no native Linux user session.", "Open the packaged study from your Linux desktop session. A compatible systemd user session is required."));
            return;
        }
        var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, _ => (string?)null);
        environment["XDG_RUNTIME_DIR"] = runtime;
        environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=" + Path.Combine(runtime, "bus");
        environment["PATH"] = "/usr/bin:/bin"; environment["LC_ALL"] = "C";
        Task<HostProcessResult> Query(string[] arguments) => commands.Run(new("/usr/bin/systemctl", arguments, "/", TimeSpan.FromSeconds(3), OutputLimit: 8192, Environment: environment), cancellation);
        var manager = await Query(["--user", "show", "--property=Version"]);
        cancellation.ThrowIfCancellationRequested();
        var version = ManagerVersion(manager);
        checks.Add(new("services", "Worker service manager", version is >= 255 ? CheckState.Passed : CheckState.Failed,
            version is >= 255 ? $"The current user service manager reports systemd {version}." : version is > 0 ? $"This session uses systemd {version}; the worker requires 255 or newer." : "The current user service manager could not be reached or identified.",
            version is >= 255 ? null : "Use a Linux installation with systemd 255 or newer and a working user session. Thaddeus does not upgrade the operating system."));
        var user = geteuid();
        var service = await Query(["show", $"user@{user}.service", "--property=LoadState,ActiveState,ControlGroup,Delegate"]);
        cancellation.ThrowIfCancellationRequested();
        var group = UserControlGroup(service, user);
        try
        {
            var controllers = group == null ? null : await SmallFile("/sys/fs/cgroup" + group + "/cgroup.controllers", cancellation);
            var available = HasControllers(controllers);
            checks.Add(new("resources", "Worker resource controls", available ? CheckState.Passed : CheckState.Failed,
                available ? "CPU, memory and process controllers are available to this user service manager. Each worker still verifies its actual limits when starting." : "The user service manager does not have the required CPU, memory and process controllers.",
                available ? null : "Ask the host administrator to enable cgroup v2 and delegate CPU, memory and process controllers to this account's systemd user manager."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { checks.Add(new("resources", "Worker resource controls", CheckState.Unverified, "The user service resource controls could not be inspected.", "Ask the host administrator to check this account's cgroup v2 delegation.")); }
    }

    internal static int? ManagerVersion(HostProcessResult result)
    {
        if (!result.Succeeded) return null;
        var match = Regex.Match(result.Output.TrimEnd('\r', '\n'), @"\AVersion=([0-9]{3,4})(?:[. -][^\r\n]*)?\z");
        return match.Success && int.TryParse(match.Groups[1].Value, out var version) ? version : null;
    }
    internal static string? UserControlGroup(HostProcessResult result, uint user)
    {
        if (!result.Succeeded) return null;
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
        if (lines.Length != 4 || lines.Any(line => !line.Contains('='))) return null;
        var pairs = lines.Select(line => line.Split('=', 2)).ToArray();
        if (pairs.Select(pair => pair[0]).Distinct(StringComparer.Ordinal).Count() != 4) return null;
        var fields = pairs.ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        if (fields.GetValueOrDefault("LoadState") != "loaded" || fields.GetValueOrDefault("ActiveState") != "active" || fields.GetValueOrDefault("Delegate") != "yes") return null;
        var group = fields.GetValueOrDefault("ControlGroup");
        return group is { Length: > 0 and <= 2048 } && group.StartsWith('/') && !group.Any(char.IsControl) &&
            !group.Split('/').Any(part => part is "." or "..") && group.EndsWith($"/user@{user}.service", StringComparison.Ordinal) ? group : null;
    }
    internal static bool HasControllers(string? value) => value != null && new[] { "cpu", "memory", "pids" }.All(controller => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(controller, StringComparer.Ordinal));
    private static async Task<string> SmallFile(string path, CancellationToken cancellation)
    {
        using var reader = File.OpenText(path); var buffer = new char[8193]; var count = await reader.ReadBlockAsync(buffer, cancellation);
        if (count > 8192) throw new IOException("Host prerequisite observation exceeded its bound.");
        return new(buffer, 0, count);
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("WinHvPlatform.dll", ExactSpelling = true)]
    private static extern int WHvGetCapability(uint capability, out ulong value, uint size, out uint written);
    [DllImport("libc", SetLastError = true)] private static extern int ioctl(SafeFileHandle file, nuint request, nint argument);
    [DllImport("libc")] private static extern uint geteuid();
}
