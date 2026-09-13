using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

internal static class QemuBackendCheck
{
    private static readonly string RunId = new('b', 32);
    private static readonly string WorkerId = "thaddeus-" + RunId;
    private static async Task<QemuInstallation> Installation(string file)
    { using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file)); return json.RootElement.GetProperty("installation").Deserialize<QemuInstallation>(Wire.Json)!; }
    private static QemuSandboxBackend Backend(Store store, QemuInstallation installation) => new(store, installation, new(RunId, 5183), OperatingSystem.IsLinux() ? Environment.ProcessPath : null);
    private static string Overlay(string root) => Path.Combine(root, "qemu-" + WorkerId, "worker.qcow2");
    private static void Require(bool valid, string message) { if (!valid) throw new IOException(message); }
    private static async Task<string> Hash(string file) { await using var stream = File.OpenRead(file); return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)); }

    public static async Task<int> Owner(string installationFile, string root, string readyFile)
    {
        try
        {
            using var store = new Store(root);
            var installation = await Installation(installationFile);
            await using var backend = Backend(store, installation);
            var inspection = await backend.Inspect(default);
            await backend.Create(new(WorkerId, installation.Image, 1, 1536), default);
            await backend.PutText(WorkerId, "native-persistent.txt", "A raven survives the owner's abrupt exit.", default);
            var sync = await backend.Execute(WorkerId, ["sync"], null, default); Require(sync.ExitCode == 0, "Guest sync failed.");
            await File.WriteAllTextAsync(readyFile + ".partial", Wire.Pack(new { inspection, observation = backend.Observation }));
            File.Move(readyFile + ".partial", readyFile);
            while (!File.Exists(readyFile + ".stop")) await Task.Delay(50);
            await backend.Stop(WorkerId, default);
            await File.WriteAllTextAsync(readyFile + ".stopped", store.Setting("qemu-termination:" + WorkerId)!);
            await Task.Delay(Timeout.Infinite); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("The fixture owner refused its estate: " + error); return 1; }
    }

    public static async Task<int> Run(string installationFile, string evidence)
    {
        PrivateWorkerDirectory.Create(evidence);
        var root = Path.Combine(evidence, "study"); var ready = Path.Combine(evidence, "owner-ready.json");
        var checks = new List<object>();
        void Record(string name, object result) { checks.Add(new { name, result }); Console.WriteLine("BACKEND_CHECK " + Wire.Pack(new { name, result })); }
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "--backend-owner", installationFile, root, ready }) start.ArgumentList.Add(argument);
        using var owner = Process.Start(start)!;
        var stdout = owner.StandardOutput.ReadToEndAsync(deadline.Token); var stderr = owner.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            for (var attempt = 0; !File.Exists(ready) && attempt < 600 && !owner.HasExited; attempt++) await Task.Delay(100, deadline.Token);
            if (!File.Exists(ready)) throw new IOException("The owner did not report readiness: " + (owner.HasExited ? await stderr : "deadline"));
            using var ownership = JsonDocument.Parse(await File.ReadAllTextAsync(ready, deadline.Token));
            var original = ownership.RootElement.GetProperty("observation").Deserialize<QemuObservation>(Wire.Json)!;
            foreach (var mode in new[] { false, true })
            {
                var refused = false;
                try { using var unexpected = QemuDiskLease.Open(Overlay(root), mode); }
                catch (IOException) { refused = true; }
                Require(refused, "A live QEMU writer admitted a recovery/removal lease.");
            }
            if (OperatingSystem.IsLinux())
            {
                var refused = false;
                try { LinuxServiceOwnership.AssertWorkspaceStopped(Path.GetDirectoryName(Overlay(root))!); }
                catch (IOException) { refused = true; }
                Require(refused, "A live Linux service was declared stopped.");
            }
            Record("live-writer-and-service-refusal", new { original.ProcessId, bothDiskModesRefused = true });
            await File.WriteAllTextAsync(ready + ".stop", "Stop the owned fixture guest and preserve its disk.", deadline.Token);
            for (var attempt = 0; !File.Exists(ready + ".stopped") && attempt < 300 && !owner.HasExited; attempt++) await Task.Delay(100, deadline.Token);
            Require(File.Exists(ready + ".stopped"), "The owned guest did not establish its stopping point.");
            Record("guest-checkpoint-before-host-loss", Wire.Unpack<QemuTermination>(await File.ReadAllTextAsync(ready + ".stopped", deadline.Token)));
            owner.Kill(); await owner.WaitForExitAsync(deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(evidence, "owner.stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(evidence, "owner.stderr.log"), await stderr);
            using var store = new Store(root);
            var installation = await Installation(installationFile);
            await using var backend = Backend(store, installation);
            QemuRecoveryReceipt? recovered = null; string? last = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try { recovered = await backend.ReconcileStopped(WorkerId, deadline.Token); break; }
                catch (IOException error)
                {
                    last = error.Message;
                    if (store.Setting("qemu-recovery:" + WorkerId) is { } failed) { last += " " + failed; break; }
                    await Task.Delay(100, deadline.Token);
                }
            }
            Require(recovered is { Status: "stopped", OverlayUnchanged: true, Booted: false, ReplayedCommands: false }, "Stopped recovery failed: " + last);
            Record("abrupt-owner-death-and-read-only-reconciliation", recovered!);
            if (OperatingSystem.IsLinux())
            {
                using var runtime = await LinuxQemuRuntime.Open(installation.RuntimePackage!, installation.Executable, installation.ImageTool, deadline.Token);
                var before = await Hash(Overlay(root));
                using (var guard = QemuDiskLease.Open(Overlay(root), allowReadOnlyQemu: true))
                {
                    var request = runtime.Request(installation.ImageTool.Path, ["resize", "-f", "qcow2", Overlay(root), "+1M"], evidence, TimeSpan.FromSeconds(10), 10000);
                    await using var writer = await LinuxSystemdProcess.Start(request, new(268435456, 100, 64), Environment.ProcessPath!,
                        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")!, "writer-" + Guid.NewGuid().ToString("N")), deadline.Token);
                    var output = new StreamReader(writer.Output).ReadToEndAsync(); var error = new StreamReader(writer.Error).ReadToEndAsync();
                    var exit = await writer.Completion;
                    Require(!exit.Succeeded && (await error).Contains("lock", StringComparison.OrdinalIgnoreCase), "A future QEMU writer bypassed the inspection lease.");
                    Record("inspection-lease-refuses-new-writer", new { exit, output = await output, error = await error });
                }
                Require(await Hash(Overlay(root)) == before, "The refused writer changed the overlay.");
            }
            var resumed = await backend.GetText(WorkerId, "native-persistent.txt", deadline.Token);
            Require(resumed.Content == "A raven survives the owner's abrupt exit." && backend.Observation!.ProcessId != original.ProcessId, "A new VM did not recover the saved guest file.");
            Record("explicit-restart-preserves-guest-file", new { resumed.Sha256, observation = backend.Observation });
            await backend.Stop(WorkerId, deadline.Token);
            using (var damaged = new Store(Path.Combine(evidence, "damaged-study")))
            {
                await using var inspector = Backend(damaged, installation);
                var directory = PrivateWorkerDirectory.Create(Path.GetDirectoryName(Overlay(damaged.Root))!);
                if (OperatingSystem.IsLinux()) LinuxServiceOwnership.Current("thaddeus-worker-" + Guid.NewGuid().ToString("N") + ".service").Save(directory);
                await File.WriteAllTextAsync(Overlay(damaged.Root), "An intentionally invalid private qcow2 fixture.", deadline.Token);
                damaged.Setting("active-sandbox", WorkerId); damaged.Setting("qemu-host:" + WorkerId, inspector.HostKind);
                damaged.Setting("qemu-route:" + WorkerId, Wire.Pack(new QemuBrokerRoute(RunId, 5183)));
                damaged.Setting("sandbox:" + WorkerId, Wire.Pack(new SandboxRegistration(new(WorkerId, installation.Image, 1, 1536), "interrupted", DateTimeOffset.UtcNow)));
                var before = await Hash(Overlay(damaged.Root)); var refused = false;
                try { await inspector.ReconcileStopped(WorkerId, deadline.Token); }
                catch (IOException) { refused = true; }
                var failure = Wire.Unpack<QemuRecoveryReceipt>(damaged.Setting("qemu-recovery:" + WorkerId)!);
                Require(refused && failure.Status == "recovery-required" && failure.OverlayUnchanged && !failure.Booted && !failure.ReplayedCommands &&
                    await Hash(Overlay(damaged.Root)) == before && inspector.Observation == null, "Damaged-disk recovery repaired, booted, or falsely succeeded.");
                Record("damaged-disk-retained-without-repair-or-boot", failure);
            }
            await backend.Retire(WorkerId, deadline.Token);
            Record("verified-stop-and-retirement", Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + WorkerId)!));
            var run = new Run { Id = RunId, Goal = new("Native lifecycle fixture", [], "plans/", [], new(), new()), State = RunState.Succeeded,
                Execution = new("openclaw", WorkerId, "agent:thaddeus:" + RunId, OpenClawBackend.PinnedVersion), Research = new("finished", "Fixture finished", WorkerRetained: true) };
            store.Save(run, "fixture.finished", new { }); store.Write("plans/retained.md", "Keep this imported note.", "absent");
            var storage = new QemuWorkspaceStorage(store); var review = storage.Inspect(run);
            var removal = storage.Remove(run, review.Digest, deadline.Token);
            Require(removal.Status == "removed" && !Directory.Exists(Path.GetDirectoryName(Overlay(root))) && store.Page("plans/retained.md")!.Content == "Keep this imported note.", "Workspace purge did not preserve the imported note.");
            Record("retired-workspace-purge-preserves-import", removal);
            var result = new { passed = true, checks, os = Environment.OSVersion.ToString(), modelCalls = 0, gpuDevices = 0 };
            await File.WriteAllTextAsync(Path.Combine(evidence, "verified.json"), Wire.Pack(result));
            Console.WriteLine("BACKEND_VERIFIED " + Wire.Pack(result)); return 0;
        }
        catch (Exception error)
        {
            var result = new { passed = false, checks, error = error.ToString(), sqliteExtendedError = error is Microsoft.Data.Sqlite.SqliteException sqlite ? (int?)sqlite.SqliteExtendedErrorCode : null, modelCalls = 0, gpuDevices = 0 };
            await File.WriteAllTextAsync(Path.Combine(evidence, "failed.json"), Wire.Pack(result));
            Console.WriteLine("BACKEND_VERIFIED " + Wire.Pack(result)); return 1;
        }
        finally { if (!owner.HasExited) { owner.Kill(); await owner.WaitForExitAsync(); } }
    }
}
