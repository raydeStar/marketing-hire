using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class QemuRecoveryDiagnosticsTests
{
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
