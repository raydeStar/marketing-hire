using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Thaddeus.Infrastructure;

[assembly: SupportedOSPlatform("windows10.0")]

if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("This check requires Windows 10 or newer.");
var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
if (!Path.GetFileNameWithoutExtension(executable).Equals("Thaddeus.ProcessCheck", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Run the built apphost executable, or use dotnet run; the helper must launch its exact binary.");
var minimalEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    ["WINDIR"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    ["DOTNET_ROOT"] = RuntimeEnvironment.GetRuntimeDirectory().Split(Path.DirectorySeparatorChar + "shared" + Path.DirectorySeparatorChar)[0],
    ["THADDEUS_EXPLICIT_FIXTURE"] = "declared-fixture-value"
};
OwnedProcessRequest Self(string directory, string[] arguments, TimeSpan? lifetime = null, int outputLimit = 100000) =>
    new(executable, arguments, directory, minimalEnvironment, lifetime ?? TimeSpan.FromMinutes(2), outputLimit);
Process Unowned(params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    return Process.Start(start) ?? throw new InvalidOperationException("Helper did not start.");
}
async Task<JsonElement> ReadLine(Stream stream)
{
    var bytes = new List<byte>(); var buffer = new byte[1];
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (bytes.Count < 100000 && await stream.ReadAsync(buffer, deadline.Token) > 0)
    {
        if (buffer[0] == 10) return JsonSerializer.Deserialize<JsonElement>(bytes.ToArray());
        bytes.Add(buffer[0]);
    }
    throw new InvalidOperationException("Helper did not return a bounded JSON line.");
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static async Task Gone(Process process) { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Require(process.HasExited, "Owned descendant survived."); }
static Process Hold(int id) { var process = Process.GetProcessById(id); _ = process.Handle; return process; }

// Internal fixture roles only. None receives an agent command or accesses product data.
if (args.FirstOrDefault() == "--idle") { Console.WriteLine(JsonSerializer.Serialize(new { pid = Environment.ProcessId })); await Task.Delay(Timeout.Infinite); return; }
if (args.FirstOrDefault() == "--echo")
{
    var input = await Console.In.ReadToEndAsync();
    Console.Error.Write("fixture-diagnostic");
    Console.WriteLine(JsonSerializer.Serialize(new { arguments = args.Skip(1), input, cwd = Environment.CurrentDirectory,
        ambient = Environment.GetEnvironmentVariable("THADDEUS_PARENT_CANARY"), declared = Environment.GetEnvironmentVariable("THADDEUS_EXPLICIT_FIXTURE") })); return;
}
if (args.FirstOrDefault() == "--flood") { while (true) Console.Write(new string('f', 4096)); }
if (args.FirstOrDefault() == "--probe-handle")
{
    Console.WriteLine(JsonSerializer.Serialize(new { inherited = FixtureNative.SetEvent((nint)long.Parse(args[1])) })); return;
}
if (args.FirstOrDefault() == "--descendant")
{
    using var child = Unowned("--idle");
    Require(await child.StandardOutput.ReadLineAsync() != null, "Descendant did not become ready.");
    Console.WriteLine(JsonSerializer.Serialize(new { pid = Environment.ProcessId, descendant = child.Id }));
    if (await Console.In.ReadLineAsync() == "exit") return;
    await Task.Delay(Timeout.Infinite); return;
}
if (args.FirstOrDefault() == "--owner")
{
    var ready = Path.GetFullPath(args[1]);
    OwnedProcessRequest request = Self(Path.GetDirectoryName(ready)!, ["--descendant"]);
    if (args.Length == 3)
    {
        var qemu = Path.GetFullPath(args[2]);
        Require(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(qemu))) == "47d57a6072e0bb3bd98f87926eb129eb1736dfe818c67b3b81ef7ce4edd0b3cd", "QEMU executable pin mismatch.");
        request = new(qemu, ["-machine", "q35", "-accel", "whpx", "-cpu", "qemu64,-svm", "-m", "512", "-smp", "1",
            "-nodefaults", "-nic", "none", "-display", "none", "-monitor", "none", "-qmp", "stdio", "-S"], Path.GetDirectoryName(ready)!, minimalEnvironment, TimeSpan.FromMinutes(2));
    }
    await using var worker = WindowsJobProcess.Start(request);
    var observed = await ReadLine(worker.Output);
    Require(args.Length == 3 ? observed.TryGetProperty("QMP", out _) : observed.TryGetProperty("descendant", out _), "Owned worker did not initialize.");
    await File.WriteAllTextAsync(ready, JsonSerializer.Serialize(new { owner = Environment.ProcessId, worker = worker.Id, observed }));
    await worker.Completion; return;
}

if (args.Length is < 1 or > 2) throw new ArgumentException("Usage: ProcessCheck FRESH_ARTIFACT_DIRECTORY [PINNED_QEMU_EXE]");
var root = Path.GetFullPath(args[0]);
if (Directory.Exists(root) || File.Exists(root)) throw new InvalidOperationException("Use a fresh check directory.");
Store.AssertNoLinks(Path.GetDirectoryName(root)!); Directory.CreateDirectory(root);
var cases = new List<object>(); var passed = false;
var originalCanary = Environment.GetEnvironmentVariable("THADDEUS_PARENT_CANARY");
try
{
    Environment.SetEnvironmentVariable("THADDEUS_PARENT_CANARY", "must-not-reach-child");
    var literals = new[] { "", "space and\ttab", "quote\"inside", "trail\\", "double\\\\\"quote", "line\nbreak", "$(literal) `literal` ; & | >", "raven · 鴉" };
    await using (var worker = WindowsJobProcess.Start(Self(root, ["--echo", .. literals])))
    {
        const string input = "literal input with $() and `characters`\n";
        await worker.Input.WriteAsync(Encoding.UTF8.GetBytes(input)); worker.Input.Close();
        using var error = new StreamReader(worker.Error); var errorText = error.ReadToEndAsync();
        var value = await ReadLine(worker.Output);
        Require((await worker.Completion.WaitAsync(TimeSpan.FromSeconds(15))).Succeeded, "Literal round trip failed.");
        Require(value.GetProperty("arguments").EnumerateArray().Select(v => v.GetString()).SequenceEqual(literals), "Argument quoting changed literal data.");
        Require(value.GetProperty("input").GetString() == input && value.GetProperty("cwd").GetString() == root &&
            value.GetProperty("ambient").ValueKind == JsonValueKind.Null && value.GetProperty("declared").GetString() == "declared-fixture-value" &&
            await errorText == "fixture-diagnostic", "Stream, directory or environment isolation failed.");
        cases.Add(new { name = "literal-streams-explicit-environment", worker = worker.Id, passed = true });
    }
    using (var marker = new EventWaitHandle(false, EventResetMode.ManualReset))
    {
        Require(FixtureNative.SetHandleInformation(marker.SafeWaitHandle, 1, 1), "Could not prepare the inherited-handle negative control.");
        await using var worker = WindowsJobProcess.Start(Self(root, ["--probe-handle", marker.SafeWaitHandle.DangerousGetHandle().ToInt64().ToString()]));
        var result = await ReadLine(worker.Output); await worker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        // The same numeric value can name a different handle in the child. The host event is the authority.
        Require(!marker.WaitOne(0), "Unlisted host event handle reached the worker.");
        cases.Add(new { name = "unlisted-inheritable-handle-denied", childSetEventResult = result.GetProperty("inherited").GetBoolean(), passed = true });
    }
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try { await using var unexpected = WindowsJobProcess.Start(Self(root, ["--idle"]), cancelled.Token); throw new InvalidOperationException("Cancelled startup ran."); }
        catch (OperationCanceledException) { cases.Add(new { name = "pre-cancelled-start-refused", passed = true }); }
    }
    try
    {
        await using var unexpected = WindowsJobProcess.Start(Self(root, []) with { Executable = Path.Combine(root, "missing.exe") });
        throw new InvalidOperationException("Missing executable ran.");
    }
    catch (Win32Exception) { cases.Add(new { name = "failed-launch-no-fallback", passed = true }); }
    foreach (var mode in new[] { "normal-root-exit", "cancel", "lifetime", "dispose" })
    {
        using var cancellation = new CancellationTokenSource();
        var worker = WindowsJobProcess.Start(Self(root, ["--descendant"], mode == "lifetime" ? TimeSpan.FromSeconds(5) : null), cancellation.Token);
        await using (worker)
        {
            var value = await ReadLine(worker.Output);
            using var descendant = Hold(value.GetProperty("descendant").GetInt32());
            if (mode == "normal-root-exit") await worker.Input.WriteAsync(Encoding.UTF8.GetBytes("exit\n"));
            else if (mode == "cancel") cancellation.Cancel();
            else if (mode == "dispose") await worker.DisposeAsync();
            var outcome = await worker.Completion.WaitAsync(TimeSpan.FromSeconds(10)); await Gone(descendant);
            Require(outcome.Succeeded == (mode == "normal-root-exit"), "Process termination was misclassified as success or failure.");
            Require(mode != "cancel" || worker.StopReason == "cancelled", "Cancellation reason missing.");
            Require(mode != "lifetime" || worker.StopReason == "lifetime", "Lifetime deadline missing.");
            cases.Add(new { name = mode + "-reaps-descendant", worker = worker.Id, descendant = descendant.Id, outcome, passed = true });
        }
    }
    await using (var worker = WindowsJobProcess.Start(Self(root, ["--flood"], outputLimit: 1024)))
    {
        try { await worker.Output.CopyToAsync(Stream.Null).WaitAsync(TimeSpan.FromSeconds(10)); throw new InvalidOperationException("Output overflow accepted."); }
        catch (IOException) { }
        var outcome = await worker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Require(worker.StopReason == "output-limit" && !outcome.Succeeded, "Output limit did not terminate worker as a failed outcome.");
        cases.Add(new { name = "output-limit-stops-worker", passed = true });
    }
    foreach (var kind in args.Length == 2 ? new[] { "helper", "qemu" } : new[] { "helper" })
    {
        var ready = Path.Combine(root, kind + "-owner-ready.json");
        using var sibling = Unowned("--idle");
        using var owner = Unowned(kind == "qemu" ? ["--owner", ready, Path.GetFullPath(args[1])] : ["--owner", ready]);
        Process? worker = null, descendant = null;
        try
        {
            Require(await sibling.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) != null, "Unrelated control did not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(ready))
            {
                if (owner.HasExited) throw new InvalidOperationException("Owner failed: " + await owner.StandardError.ReadToEndAsync());
                await Task.Delay(50, timeout.Token);
            }
            // The helper creates the file only after startup. Wait for its write handle to close before reading.
            JsonElement observation = default;
            while (observation.ValueKind == JsonValueKind.Undefined)
            {
                try { observation = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(ready, timeout.Token)); }
                catch (Exception ex) when (ex is IOException or JsonException) { await Task.Delay(20, timeout.Token); }
            }
            worker = Hold(observation.GetProperty("worker").GetInt32());
            if (kind == "helper") descendant = Hold(observation.GetProperty("observed").GetProperty("descendant").GetInt32());
            owner.Kill(entireProcessTree: false); await Gone(owner); await Gone(worker);
            if (descendant != null) await Gone(descendant);
            Require(!sibling.HasExited, "Unrelated process was terminated.");
            cases.Add(new { name = kind + "-abrupt-owner-death", owner = owner.Id, worker = worker.Id, descendant = descendant?.Id,
                unrelatedProcess = sibling.Id, unrelatedStillRunning = true, passed = true });
        }
        finally
        {
            if (!owner.HasExited) owner.Kill(entireProcessTree: false);
            if (worker is { HasExited: false }) worker.Kill(entireProcessTree: true);
            if (descendant is { HasExited: false }) descendant.Kill();
            if (!sibling.HasExited) sibling.Kill();
            await Gone(owner); await Gone(sibling); worker?.Dispose(); descendant?.Dispose();
        }
    }
    passed = true;
}
finally
{
    Environment.SetEnvironmentVariable("THADDEUS_PARENT_CANARY", originalCanary);
    await File.WriteAllTextAsync(Path.Combine(root, "ownership-receipt.json"), JsonSerializer.Serialize(new
    {
        schemaVersion = 1, observedAt = DateTimeOffset.UtcNow, operatingSystem = RuntimeInformation.OSDescription,
        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), framework = RuntimeInformation.FrameworkDescription,
        passed, productionBackendQualified = false, cases
    }, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"{cases.Count} Windows ownership checks passed. No machinery escaped the estate. Evidence: {root}");

internal static class FixtureNative
{
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetHandleInformation(SafeWaitHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetEvent(nint handle);
}
