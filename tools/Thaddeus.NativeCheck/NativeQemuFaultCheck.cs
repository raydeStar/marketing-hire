using System.Runtime.Versioning;
using System.Security.Cryptography;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

// Fixture-only fault injection on new private copies. The original overlay and user data are never mutated.
[SupportedOSPlatform("windows10.0")]
internal static class NativeQemuFaultCheck
{
    public static async Task<object> Run(Store source, string id, QemuInstallation installation, QemuBrokerRoute broker)
    {
        DockerSandboxBackend.ValidateId(id);
        var original = Path.Combine(source.Root, "qemu-" + id, "worker.qcow2"); Store.AssertNoLinks(original);
        using var originalDisk = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read);
        var originalHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(originalDisk));
        var results = new List<object>();
        foreach (var kind in new[] { "corrupt", "missing", "locked" })
        {
            var root = PrivateWorkerDirectory.Create(Path.Combine(source.Root, "recovery-negative-" + kind + "-" + Guid.NewGuid().ToString("N")));
            using var isolated = new Store(root);
            var directory = PrivateWorkerDirectory.Create(Path.Combine(root, "qemu-" + id));
            var disk = Path.Combine(directory, "worker.qcow2");
            if (kind != "missing") File.Copy(original, disk, overwrite: false);
            if (kind == "corrupt")
            {
                // Damage only the new copy's qcow2 magic. Even a polite disk check must decline this invitation.
                using var damaged = new FileStream(disk, FileMode.Open, FileAccess.Write, FileShare.None);
                damaged.Write(new byte[4]); damaged.Flush(flushToDisk: true);
            }
            using var locked = kind == "locked" ? new FileStream(disk, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
            isolated.Setting("sandbox:" + id, Wire.Pack(new SandboxRegistration(new(id, installation.Image), "interrupted", DateTimeOffset.UtcNow)));
            isolated.Setting("qemu-route:" + id, Wire.Pack(broker)); isolated.Setting("active-sandbox", id);
            await using var backend = new QemuSandboxBackend(isolated, installation, broker);
            var refused = false; var executeDenied = false;
            try { await backend.ReconcileStopped(id, default); }
            catch (IOException) { refused = true; }
            try { await backend.Execute(id, ["true"], null, default); }
            catch (InvalidOperationException) { executeDenied = true; }
            var receipt = isolated.Setting("qemu-recovery:" + id) is { } raw ? Wire.Unpack<QemuRecoveryReceipt>(raw) : null;
            if (!refused || !executeDenied || backend.Observation != null || File.Exists(disk) != (kind != "missing") ||
                kind == "corrupt" && (receipt == null || receipt.Status != "recovery-required" || receipt.ImageCheck.Succeeded || !receipt.OverlayUnchanged))
                throw new InvalidOperationException("Recovery fault was not safely rejected: " + kind);
            results.Add(new { kind, root, refused, executeDenied, imageRetained = File.Exists(disk), receipt });
        }
        originalDisk.Position = 0;
        var unchanged = originalHash == Convert.ToHexStringLower(await SHA256.HashDataAsync(originalDisk));
        if (!unchanged) throw new IOException("Recovery fault fixtures changed the original overlay.");
        return new { passed = true, originalUnchanged = unchanged, results };
    }
}
