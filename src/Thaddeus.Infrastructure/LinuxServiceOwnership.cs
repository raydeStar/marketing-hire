using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record LinuxServiceOwnership(int SchemaVersion, string BootId, uint UserId, string Unit)
{
    public static LinuxServiceOwnership Current(string unit)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var result = new LinuxServiceOwnership(1, File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim(), GetEffectiveUserId(), unit);
        result.Validate(); return result;
    }
    public void Validate()
    {
        if (SchemaVersion != 1 || !Guid.TryParseExact(BootId, "D", out _) ||
            !Regex.IsMatch(Unit, @"\Athaddeus-worker-[a-f0-9]{32}\.service\z")) throw new IOException("Invalid durable Linux service owner.");
    }
    public void Save(string directory)
    {
        Validate(); Store.AssertNoLinks(directory);
        var path = Path.Combine(directory, "linux-service-" + Unit[16..48] + ".json");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(System.Text.Encoding.UTF8.GetBytes(Wire.Pack(this))); file.Flush(flushToDisk: true);
    }
    public void AssertStopped()
    {
        Validate(); var current = Current(Unit);
        if (current.UserId != UserId) throw new IOException("The Linux worker belongs to another user.");
        if (current.BootId != BootId) return; // A previous kernel cannot still own a local service.
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtime) || !Path.IsPathFullyQualified(runtime)) throw new IOException("A native user service manager is required for reconciliation.");
        var start = new ProcessStartInfo("/usr/bin/systemctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Clear(); start.Environment["XDG_RUNTIME_DIR"] = runtime;
        start.Environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=" + Path.Combine(runtime, "bus"); start.Environment["LC_ALL"] = "C";
        foreach (var argument in new[] { "--user", "show", Unit, "--property=LoadState,ActiveState,SubState,MainPID,ControlGroup,Job" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not inspect the Linux worker service.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); Task.WhenAll(output, error).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); throw new IOException("Linux service inspection timed out."); }
        if (output.Result.Length > 4096 || error.Result.Length > 4096) throw new IOException("Linux service inspection exceeded its bound.");
        var group = StoppedControlGroup(output.Result);
        if (process.ExitCode != 0) throw new IOException("Linux service inspection failed.");
        if (group.Length != 0)
        {
            if (!group.StartsWith('/') || group.Split('/').Any(part => part is "." or "..") || !group.EndsWith("/" + Unit, StringComparison.Ordinal)) throw new IOException("Linux service control group differs from its owner.");
            var events = "/sys/fs/cgroup" + group + "/cgroup.events";
            try { if (!File.ReadAllLines(events).Contains("populated 0")) throw new IOException("The owned Linux service still has live members."); }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }
        }
    }
    public static string StoppedControlGroup(string output)
    {
        var values = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=', 2)).ToArray();
        if (values.Any(pair => pair.Length != 2) || values.Select(pair => pair[0]).Distinct().Count() != values.Length) throw new IOException("Invalid Linux service observation.");
        var properties = values.ToDictionary(pair => pair[0], pair => pair[1]);
        if (properties.Count != 6 || properties.GetValueOrDefault("LoadState") is not ("loaded" or "not-found") ||
            properties.GetValueOrDefault("ActiveState") is not ("inactive" or "failed") || properties.GetValueOrDefault("SubState") is not ("dead" or "failed") ||
            properties.GetValueOrDefault("MainPID") != "0" || properties.GetValueOrDefault("Job") is not ("" or "0") || !properties.ContainsKey("ControlGroup"))
            throw new IOException("The Linux worker is still active, queued, or could not be inspected.");
        return properties["ControlGroup"];
    }
    public static void AssertWorkspaceStopped(string directory, bool requireRecords = false)
    {
        if (!OperatingSystem.IsLinux()) return;
        Store.AssertNoLinks(directory);
        var boots = Directory.EnumerateDirectories(directory, "boot-*").Take(257).ToArray();
        if (boots.Length > 256) throw new IOException("Too many retained worker boots.");
        var count = 0;
        foreach (var folder in new[] { directory }.Concat(boots))
        {
            Store.AssertNoLinks(folder);
            var files = Directory.EnumerateFiles(folder, "linux-service-*.json").Take(1025).ToArray();
            if (requireRecords && files.Length == 0) throw new IOException("Linux service ownership is missing; stopped-state reconciliation needs inspection.");
            foreach (var file in files)
            {
                Store.AssertNoLinks(file);
                if (++count > 1024 || new FileInfo(file).Length > 4096) throw new IOException("Linux service ownership inventory exceeds its bound.");
                var record = Wire.Unpack<LinuxServiceOwnership>(File.ReadAllText(file)); record.Validate();
                if (Path.GetFileName(file) != "linux-service-" + record.Unit[16..48] + ".json") throw new IOException("Linux service owner filename differs from its identity.");
                record.AssertStopped();
            }
        }
    }
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}
