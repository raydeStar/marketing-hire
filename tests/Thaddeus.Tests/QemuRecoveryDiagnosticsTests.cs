using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class QemuRecoveryDiagnosticsTests
{
    [Fact]
    public void StartupSnapshotIsBoundedAndPreservesTheFirstObservation()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-startup-diagnostic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            QemuWorkerSession.RecordGatewayStartupFailure(root, new(0, new string('x', 30000), new string('e', 4000)));
            var path = Path.Combine(root, "gateway-startup-failure.json"); var first = File.ReadAllBytes(path);
            using var diagnostic = System.Text.Json.JsonDocument.Parse(first);
            Assert.Equal(20000, diagnostic.RootElement.GetProperty("output").GetString()!.Length);
            Assert.Equal(2000, diagnostic.RootElement.GetProperty("error").GetString()!.Length);
            QemuWorkerSession.RecordGatewayStartupFailure(root, new(1, "later", ""));
            Assert.Equal(first, File.ReadAllBytes(path));
            QemuWorkerSession.RecordGatewayStartupFailure(Path.Combine(root, "missing"), new(0, "unavailable", ""));
            Assert.False(Directory.Exists(Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReadinessRepliesAreBoundedPrivateFilesAndCannotOverwriteTheFirstResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-command-diagnostic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            QemuWorkerSession.RecordCommandResult(root, 1, "gateway-health", 1, new(1, new string('o', 5000), new string('e', 5000)));
            var path = Path.Combine(root, "command-result-01.json"); var first = File.ReadAllBytes(path);
            using var diagnostic = System.Text.Json.JsonDocument.Parse(first);
            Assert.Equal("gateway-health", diagnostic.RootElement.GetProperty("operation").GetString());
            Assert.Equal(1, diagnostic.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Equal(2000, diagnostic.RootElement.GetProperty("output").GetString()!.Length);
            Assert.Equal(2000, diagnostic.RootElement.GetProperty("error").GetString()!.Length);
            QemuWorkerSession.RecordCommandResult(root, 1, "gateway-health", 1, new(0, "later", ""));
            Assert.Equal(first, File.ReadAllBytes(path));
            QemuWorkerSession.RecordCommandResult(root, 31, "gateway-health", 31, new(1, "excess", ""));
            QemuWorkerSession.RecordCommandResult(root, 2, "unrelated-command", 2, new(1, "private", ""));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FirstTransportFailureIsBoundedAndCannotOverwriteAnExistingReceipt()
    {
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-transport-diagnostic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            QemuWorkerSession.RecordFailure(root, new IOException(new string('x', 5000)));
            var path = Path.Combine(root, "transport-failure.json");
            var first = File.ReadAllBytes(path);
            using var diagnostic = System.Text.Json.JsonDocument.Parse(first);
            Assert.Equal(2000, diagnostic.RootElement.GetProperty("detail").GetString()!.Length);
            Assert.Equal("IOException", diagnostic.RootElement.GetProperty("failureType").GetString());
            QemuWorkerSession.RecordFailure(root, new InvalidOperationException("Later cleanup must not replace the cause."));
            Assert.Equal(first, File.ReadAllBytes(path));
            QemuWorkerSession.RecordFailure(Path.Combine(root, "missing"), new IOException("An unavailable diagnostic destination must not block containment."));
            Assert.False(Directory.Exists(Path.Combine(root, "missing")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedPinRetainsItsStageWithoutBootingOrRepairingTheDisk(bool recovery)
    {
        if (NativeWorkerPlatform.Backend != "qemu-whpx") return; // Native Linux exercises this contract in its packaged fixture.
        var root = Path.Combine(Path.GetTempPath(), "thaddeus-recovery-diagnostic-" + Guid.NewGuid().ToString("N"));
        using var store = new Store(root);
        var runId = new string('d', 32); var workerId = "thaddeus-" + runId;
        var pin = new QemuPinnedFile(Path.Combine(root, "not-a-runtime"), new string('a', 64));
        var installation = new QemuInstallation(pin, pin, pin, pin, pin);
        var spec = new SandboxSpec(workerId, installation.Image);
        var directory = Path.Combine(root, "qemu-" + workerId); Directory.CreateDirectory(directory);
        var overlay = Path.Combine(directory, "worker.qcow2");
        await File.WriteAllTextAsync(overlay, "Fictional disk; it must never reach QEMU.");
        var before = await File.ReadAllBytesAsync(overlay);
        store.Setting("sandbox:" + workerId, Wire.Pack(new SandboxRegistration(spec, "stopped", DateTimeOffset.UtcNow)));
        store.Setting("qemu-route:" + workerId, Wire.Pack(new QemuBrokerRoute(runId, 5183)));
        store.Setting("active-sandbox", workerId);
        await using var backend = new QemuSandboxBackend(store, installation, new(runId, 5183));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (recovery) await backend.ReconcileStopped(workerId, default);
            else await backend.Execute(workerId, ["true"], null, default);
        });
        using var diagnostic = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Directory.GetFiles(directory, recovery ? "recovery-*.json" : "boot-failure-*.json").Single()));
        Assert.Equal("pin-inputs", diagnostic.RootElement.GetProperty("phase").GetString());
        Assert.Equal("InvalidOperationException", diagnostic.RootElement.GetProperty("failureType").GetString());
        Assert.Equal(recovery ? "recovery-required" : "stopped", Wire.Unpack<SandboxRegistration>(store.Setting("sandbox:" + workerId)!).Status);
        Assert.Equal(before, await File.ReadAllBytesAsync(overlay));
        Assert.Empty(Directory.GetDirectories(directory));
        Assert.Null(backend.Observation); Assert.Null(store.Setting("qemu-recovery:" + workerId));
    }
}
