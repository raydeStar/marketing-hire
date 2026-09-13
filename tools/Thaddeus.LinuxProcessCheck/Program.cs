using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Thaddeus.Infrastructure;

if (args.FirstOrDefault() == "--linux-supervise")
{
    try { return await LinuxServiceSupervisor.Run(args[1], args[2]); }
    catch (Exception error) { Console.Error.WriteLine("The Linux steward refused admission: " + error); return 125; }
}
if (args.FirstOrDefault() == "--vm")
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException();
    var request = JsonSerializer.Deserialize<OwnedProcessRequest>(await File.ReadAllTextAsync(args[1]))!;
    using var installationJson = JsonDocument.Parse(await File.ReadAllTextAsync(args[2]));
    var installation = JsonSerializer.Deserialize<QemuInstallation>(installationJson.RootElement.GetProperty("installation"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    using var runtimeLease = await QemuRuntimeLease.Open(installation.RuntimePackage!, installation.Executable, installation.ImageTool, default);
    if (request.Executable != installation.Executable.Path) throw new IOException("VM executable does not match the pinned runtime.");
    using var kernel = new FileStream(installation.Kernel.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var initrd = new FileStream(installation.Initrd.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (Convert.ToHexStringLower(await SHA256.HashDataAsync(kernel)) != installation.Kernel.Sha256 ||
        Convert.ToHexStringLower(await SHA256.HashDataAsync(initrd)) != installation.Initrd.Sha256) throw new IOException("Linux diagnostic boot inputs changed.");
    await using var process = WindowsJobProcess.Start(request);
    await using var stdout = new FileStream(Path.Combine(request.WorkingDirectory, "vm.stdout.log"), FileMode.Create, FileAccess.Write, FileShare.Read);
    await using var stderr = new FileStream(Path.Combine(request.WorkingDirectory, "vm.stderr.log"), FileMode.Create, FileAccess.Write, FileShare.Read);
    await Task.WhenAll(process.Output.CopyToAsync(stdout), process.Error.CopyToAsync(stderr));
    var outcome = await process.Completion;
    await File.WriteAllTextAsync(Path.Combine(request.WorkingDirectory, "vm-exit.json"), JsonSerializer.Serialize(new { process.Id, outcome, resources = process.InitialResources }));
    return outcome.Succeeded ? 0 : 1;
}
if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The actual checks require Linux. The raven declines to impersonate a kernel.");
var executable = Environment.ProcessPath!;
if (args.FirstOrDefault() == "--child")
{
    switch (args[1])
    {
        case "echo":
            Console.WriteLine(JsonSerializer.Serialize(new { args = args.Skip(2), text = await Console.In.ReadLineAsync(), explicitValue = Environment.GetEnvironmentVariable("EXPLICIT_VALUE"), ambientValue = Environment.GetEnvironmentVariable("THADDEUS_AMBIENT"), cwd = Environment.CurrentDirectory }));
            Console.Error.WriteLine("fixture stderr: the raven heard every syllable"); return 0;
        case "detached":
            var detached = new ProcessStartInfo("/usr/bin/setsid") { UseShellExecute = false };
            foreach (var value in new[] { executable, "--child", "wait" }) detached.ArgumentList.Add(value);
            using (var child = Process.Start(detached)!) await File.WriteAllTextAsync(args[2], child.Id.ToString());
            return 0;
        case "cpu": while (true) { _ = Math.Sqrt(Random.Shared.NextDouble()); }
        case "flood": while (true) Console.Write(new string('x', 4096));
        case "wait": await Task.Delay(Timeout.Infinite); return 0;
    }
    throw new ArgumentException("Unknown inert child fixture.");
}
var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? throw new IOException("No native user runtime.");
var root = Path.Combine(runtime, "check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var limits = new LinuxProcessLimits(268435456, 100, 64);
OwnedProcessRequest Request(string[] values, int seconds = 20, int outputLimit = 65536) => new(executable, values, root,
    new Dictionary<string, string> { ["EXPLICIT_VALUE"] = "literal $VALUE; untouched", ["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1" }, TimeSpan.FromSeconds(seconds), outputLimit);
Task<LinuxSystemdProcess> Start(OwnedProcessRequest request, LinuxProcessLimits? resources = null, CancellationToken cancellation = default) =>
    LinuxSystemdProcess.Start(request, resources ?? limits, executable, Path.Combine(runtime, "w-" + Guid.NewGuid().ToString("N")), cancellation);
if (args.FirstOrDefault() == "--owner")
{
    await using var worker = await Start(Request(["--child", "wait"], seconds: args.Length > 2 ? int.Parse(args[2]) : 20));
    await File.WriteAllTextAsync(args[1] + ".partial", JsonSerializer.Serialize(new { worker.Id, worker.SupervisorId, worker.Unit, worker.Resources }));
    File.Move(args[1] + ".partial", args[1]);
    await Task.Delay(Timeout.Infinite); return 0;
}
var checks = new List<object>();
void Record(string name, object evidence) { checks.Add(new { name, evidence }); Console.WriteLine("LINUX_CHECK " + JsonSerializer.Serialize(new { name, evidence })); }
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
bool Alive(int pid)
{
    try { var stat = File.ReadAllText($"/proc/{pid}/stat"); return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0] != "Z"; }
    catch (IOException) { return false; }
}
async Task Stopped(int pid)
{
    for (var attempt = 0; attempt < 100 && Alive(pid); attempt++) await Task.Delay(50);
    Require(!Alive(pid), "Owned process remains alive: " + pid);
}
async Task<string> WaitFile(string path)
{
    for (var attempt = 0; attempt < 200; attempt++)
    {
        if (File.Exists(path)) { var value = await File.ReadAllTextAsync(path); if (value.Length > 0) return value; }
        await Task.Delay(50);
    }
    throw new TimeoutException("Fixture observation did not arrive.");
}
try
{
    Environment.SetEnvironmentVariable("THADDEUS_AMBIENT", "must not reach adapter");
    await using (var worker = await Start(Request(["--child", "echo", "space and 'quote'", "$HOME; $(literal)", "羽根"])))
    {
        var output = new StreamReader(worker.Output).ReadToEndAsync(); var error = new StreamReader(worker.Error).ReadToEndAsync();
        await worker.Input.WriteAsync(Encoding.UTF8.GetBytes("hello from the owner\n")); await worker.Input.FlushAsync();
        var exit = await worker.Completion;
        using var echo = JsonDocument.Parse(await output);
        Require(exit.Succeeded && echo.RootElement.GetProperty("text").GetString() == "hello from the owner" &&
            echo.RootElement.GetProperty("args")[1].GetString() == "$HOME; $(literal)" && echo.RootElement.GetProperty("args")[2].GetString() == "羽根" &&
            echo.RootElement.GetProperty("explicitValue").GetString() == "literal $VALUE; untouched" && echo.RootElement.GetProperty("ambientValue").ValueKind == JsonValueKind.Null &&
            echo.RootElement.GetProperty("cwd").GetString() == root && (await error).Contains("fixture stderr"), "Argument, stream or environment contract changed.");
        Record("literal-streams-and-explicit-environment", new { worker.Resources, exit });
    }
    var detachedPath = Path.Combine(root, "detached.pid");
    await using (var worker = await Start(Request(["--child", "detached", detachedPath])))
    {
        var exit = await worker.Completion; var descendant = int.Parse(await WaitFile(detachedPath));
        await Stopped(descendant); Require(exit.Succeeded, "Normal root exit did not succeed.");
        Record("detached-descendant-after-normal-exit", new { descendant, exit });
    }
    using (var cancellation = new CancellationTokenSource())
    await using (var worker = await Start(Request(["--child", "wait"]), cancellation: cancellation.Token))
    {
        cancellation.Cancel(); var exit = await worker.Completion; await Stopped(worker.Id);
        Require(!exit.Succeeded && exit.StopReason == "cancelled", "Cancelled process claimed success."); Record("cancellation", exit);
    }
    await using (var worker = await Start(Request(["--child", "wait"], seconds: 3)))
    {
        var exit = await worker.Completion; await Stopped(worker.Id); Require(!exit.Succeeded, "Deadline claimed success."); Record("owner-deadline", exit);
    }
    await using (var worker = await Start(Request(["--child", "cpu"], seconds: 10), limits with { CpuQuotaPercent = 25 }))
    {
        await Task.Delay(2200);
        var statistics = await File.ReadAllTextAsync("/sys/fs/cgroup" + worker.Resources.ControlGroup + "/cpu.stat");
        var throttled = long.Parse(statistics.Split('\n').Single(line => line.StartsWith("nr_throttled ")).Split(' ')[1]);
        Require(throttled > 0, "Kernel did not report CPU throttling.");
        worker.Stop("fixture-complete"); await worker.Completion;
        Record("observed-cpu-throttling", new { worker.Resources, statistics });
    }
    await using (var worker = await Start(Request(["--child", "flood"], outputLimit: 1024)))
    {
        try { await worker.Output.CopyToAsync(Stream.Null); throw new InvalidOperationException("Output bound was not enforced."); }
        catch (IOException) { }
        var exit = await worker.Completion; Require(exit.StopReason == "output-limit" && !exit.Succeeded, "Output overflow claimed success."); Record("output-limit", exit);
    }
    var canaryStart = new ProcessStartInfo(executable) { UseShellExecute = false };
    canaryStart.ArgumentList.Add("--child"); canaryStart.ArgumentList.Add("wait");
    using var canary = Process.Start(canaryStart)!;
    try
    {
        var ownerReport = Path.Combine(root, "owner.json");
        var ownerStart = new ProcessStartInfo(executable) { UseShellExecute = false };
        ownerStart.ArgumentList.Add("--owner"); ownerStart.ArgumentList.Add(ownerReport);
        using var owner = Process.Start(ownerStart)!;
        try
        {
            using var report = JsonDocument.Parse(await WaitFile(ownerReport));
            var workerId = report.RootElement.GetProperty("Id").GetInt32(); var supervisorId = report.RootElement.GetProperty("SupervisorId").GetInt32();
            owner.Kill(); await owner.WaitForExitAsync(); await Stopped(workerId); await Stopped(supervisorId);
            Require(!canary.HasExited, "Unrelated canary was stopped.");
            Record("abrupt-owner-death", new { workerId, supervisorId, canaryAlive = true });
        }
        finally { if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); } }
        var pausedReport = Path.Combine(root, "paused-owner.json");
        var pausedStart = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in new[] { "--owner", pausedReport, "3" }) pausedStart.ArgumentList.Add(argument);
        using var pausedOwner = Process.Start(pausedStart)!;
        try
        {
            using var report = JsonDocument.Parse(await WaitFile(pausedReport));
            var workerId = report.RootElement.GetProperty("Id").GetInt32(); var supervisorId = report.RootElement.GetProperty("SupervisorId").GetInt32();
            Require(FixtureSignals.Kill(pausedOwner.Id, 19) == 0, "Could not pause the owned fixture process."); // SIGSTOP on this fixture PID only.
            await Stopped(workerId); await Stopped(supervisorId);
            Require(!pausedOwner.HasExited && !canary.HasExited, "The deadline check lost its independent owner or canary.");
            Record("service-deadline-with-paused-owner", new { workerId, supervisorId, ownerStillAlive = true, canaryAlive = true });
        }
        finally { if (!pausedOwner.HasExited) { pausedOwner.Kill(); await pausedOwner.WaitForExitAsync(); } }
        await using (var worker = await Start(Request(["--child", "wait"])))
        {
            using var supervisor = Process.GetProcessById(worker.SupervisorId); supervisor.Kill();
            var exit = await worker.Completion; await Stopped(worker.Id);
            Require(!exit.Succeeded && !canary.HasExited, "Supervisor crash did not preserve the ownership boundary.");
            Record("abrupt-supervisor-death", new { worker.Id, exit, canaryAlive = true });
        }
    }
    finally { if (!canary.HasExited) { canary.Kill(); await canary.WaitForExitAsync(); } }
    Console.WriteLine("LINUX_VERIFIED " + JsonSerializer.Serialize(new { passed = true, checks, os = Environment.OSVersion.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() }));
    return 0;
}

catch (Exception error)
{
    Console.WriteLine("LINUX_VERIFIED " + JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() })); return 1;
}

internal static class FixtureSignals
{
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    internal static extern int Kill(int pid, int signal);
}
