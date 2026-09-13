using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Thaddeus.Infrastructure;

internal static class ProcessResourceChecks
{
    private const long MiB = 1024 * 1024;

    internal static async Task<bool> TryHelper(string[] args)
    {
        if (args.FirstOrDefault() is not ("--memory-check" or "--process-check" or "--cpu-check")) return false;
        Console.WriteLine("{\"ready\":true}");
        if (await Console.In.ReadLineAsync() != "go") throw new InvalidOperationException("Missing resource fixture start signal.");
        if (args[0] == "--memory-check")
        {
            var allocations = new List<nint>(); var error = 0;
            try
            {
                // Bounded virtual commit, not a host exhaustion test. Even the unlimited control stops at 384 MiB.
                for (var i = 0; i < 12; i++)
                {
                    var allocation = Native.VirtualAlloc(0, (nuint)(32 * MiB), 0x1000 | 0x2000, 0x04);
                    if (allocation == 0) { error = Marshal.GetLastWin32Error(); break; }
                    allocations.Add(allocation);
                }
                Console.WriteLine(JsonSerializer.Serialize(new { allocatedBytes = allocations.Count * 32 * MiB, denied = error != 0, nativeError = error }));
                await Console.In.ReadLineAsync(); // Keep the original job alive for an independent host observation.
            }
            finally { foreach (var allocation in allocations) Native.VirtualFree(allocation, 0, 0x8000); }
        }
        else if (args[0] == "--process-check")
        {
            Process? child = null; var denied = false; var error = 0;
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("--idle");
                try
                {
                    child = Process.Start(start) ?? throw new InvalidOperationException("Child process was not returned.");
                    denied = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == null;
                }
                catch (Win32Exception exception) { denied = true; error = exception.NativeErrorCode; }
                Console.WriteLine(JsonSerializer.Serialize(new { denied, nativeError = error, childId = child?.Id }));
                await Console.In.ReadLineAsync();
            }
            finally
            {
                if (child != null) { if (!child.HasExited) child.Kill(); await child.WaitForExitAsync(); child.Dispose(); }
            }
        }
        else
        {
            var watch = Stopwatch.StartNew();
            var threads = Enumerable.Range(0, 2).Select(_ => new Thread(() => { while (watch.Elapsed < TimeSpan.FromSeconds(6)) Thread.SpinWait(256); })).ToArray();
            foreach (var thread in threads) thread.Start();
            foreach (var thread in threads) thread.Join();
            Console.WriteLine(JsonSerializer.Serialize(new { elapsedMs = watch.ElapsedMilliseconds }));
            await Console.In.ReadLineAsync();
        }
        return true;
    }

    internal static async Task<object[]> Run(string root, string executable, IReadOnlyDictionary<string, string> environment)
    {
        var results = new List<object>();
        var observationNumber = 0;
        OwnedProcessRequest Request(string helper, OwnedProcessResourceLimits? limits) =>
            new(executable, [helper], root, environment, TimeSpan.FromSeconds(30), 10000, limits);
        async Task<(JsonElement reply, OwnedProcessResourceObservation before, OwnedProcessResourceObservation after, double cpuMs, long privateBytes)> Observe(OwnedProcessRequest request)
        {
            await using var worker = WindowsJobProcess.Start(request);
            using var output = new StreamReader(worker.Output);
            using var input = new StreamWriter(worker.Input) { AutoFlush = true };
            using var process = Process.GetProcessById(worker.Id); _ = process.Handle;
            using var errors = new StreamReader(worker.Error); var errorText = errors.ReadToEndAsync();
            var ready = await output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (ready != "{\"ready\":true}") throw new InvalidOperationException("Resource helper did not initialize: " + await errorText);
            var before = worker.ObserveResources(); var cpu = process.TotalProcessorTime;
            await input.WriteLineAsync("go");
            var raw = await output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) ?? throw new InvalidOperationException("Resource result missing.");
            var after = worker.ObserveResources(); process.Refresh(); var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            var privateBytes = process.PrivateMemorySize64;
            await input.WriteLineAsync("exit");
            var outcome = await worker.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            if (!outcome.Succeeded || await errorText != "") throw new InvalidOperationException("Resource helper did not complete cleanly.");
            await File.WriteAllTextAsync(Path.Combine(root, $"resource-observation-{++observationNumber}.json"), JsonSerializer.Serialize(new
            { request = request.Resources, helper = request.Arguments[0], before, after, result = JsonSerializer.Deserialize<JsonElement>(raw), cpuMs, privateBytes, outcome }));
            return (JsonSerializer.Deserialize<JsonElement>(raw), before, after, cpuMs, privateBytes);
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var limits = new OwnedProcessResourceLimits(256 * MiB, 10000, 1);
        foreach (var bounded in new[] { false, true })
        {
            var (reply, before, after, _, privateBytes) = await Observe(Request("--memory-check", bounded ? limits : null));
            Require(reply.GetProperty("denied").GetBoolean() == bounded &&
                (bounded ? reply.GetProperty("allocatedBytes").GetInt64() < 256 * MiB && privateBytes <= 256 * MiB && privateBytes >= reply.GetProperty("allocatedBytes").GetInt64()
                         : reply.GetProperty("allocatedBytes").GetInt64() == 384 * MiB), "Committed-memory boundary/control failed.");
            results.Add(new { name = bounded ? "job-memory-limit-denies-commit" : "bounded-unlimited-memory-control", before, after, privateBytes, observed = reply, passed = true });
        }
        foreach (var count in new[] { 2, 1 })
        {
            var (reply, before, after, _, _) = await Observe(Request("--process-check", limits with { ActiveProcesses = count }));
            Require(reply.GetProperty("denied").GetBoolean() == (count == 1), "Active-process boundary/control failed.");
            results.Add(new { name = count == 1 ? "job-process-limit-denies-descendant" : "job-process-positive-control", before, after, observed = reply, passed = true });
        }
        var rate = Math.Clamp((int)Math.Ceiling(5000.0 / Environment.ProcessorCount), 1, 10000);
        var (measured, configured, finished, cpuMs, _) = await Observe(Request("--cpu-check", limits with { CpuRate = rate }));
        var elapsed = measured.GetProperty("elapsedMs").GetDouble();
        var nominalCpuMs = elapsed * Environment.ProcessorCount * rate / 10000.0;
        // Scheduler accounting has interval jitter. Record both values; do not turn this into a throughput benchmark.
        Require(configured.CpuControlFlags == 5 && configured.CpuRate == rate && cpuMs > 0 && cpuMs <= nominalCpuMs + 1200,
            "Queried CPU hard cap or bounded execution observation failed.");
        results.Add(new { name = "job-cpu-hard-cap", configured, finished, elapsedMs = elapsed, cpuMs,
            hostLogicalProcessors = Environment.ProcessorCount, nominalCpuMs, timingToleranceMs = 1200, passed = true });
        foreach (var invalid in new[] { limits with { CommittedMemoryBytes = 0 }, limits with { CpuRate = 0 }, limits with { CpuRate = 10001 }, limits with { ActiveProcesses = 0 } })
        {
            try { await using var unexpected = WindowsJobProcess.Start(Request("--idle", invalid)); throw new InvalidOperationException("Invalid resource limit was accepted."); }
            catch (ArgumentException) { }
        }
        results.Add(new { name = "invalid-resource-limits-refused-before-launch", passed = true });
        return results.ToArray();
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool VirtualFree(nint address, nuint size, uint freeType);
    }
}
