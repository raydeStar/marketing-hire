using System.Security.Cryptography;
using System.Text.Json;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class WorkerBundlePreparationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-bundle-copy-" + Guid.NewGuid().ToString("N"));
    public WorkerBundlePreparationTests() => Directory.CreateDirectory(root);
    private QemuPinnedFile FilePin(string relative, string content)
    {
        var file = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content); return new(file, Wire.Hash(content));
    }
    private QemuInstallation Installation()
    {
        var executable = FilePin("source/runtime/qemu.exe", "Inert QEMU fixture, never executable.");
        var imageTool = FilePin("source/runtime/image.exe", "Inert image fixture, never executable.");
        var manifest = FilePin("source/runtime-manifest.json", Wire.Pack(new QemuRuntimeManifest(1, "qemu-windows-runtime", "11.1.0",
            [new("qemu.exe", executable.Sha256), new("image.exe", imageTool.Sha256)])));
        return new(executable, imageTool, FilePin("source/kernel", "Kernel fixture"), FilePin("source/initrd", "Initrd fixture"),
            FilePin("source/root.ext4", "Guest fixture"), new(Path.Combine(root, "source/runtime"), manifest));
    }
    [Fact] public async Task CompleteBundleRelocatesWithIndependentVerifiedCopies()
    {
        var source = Installation(); var destination = Path.Combine(root, "bundle");
        var receipt = await WorkerBundlePreparation.Create(source, destination, "win-x64", default);
        Assert.Equal(7, receipt.Files);
        var bytes = File.ReadAllBytes(Path.Combine(destination, "installation.json"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), receipt.DescriptorSha256);
        using var json = JsonDocument.Parse(bytes);
        var copied = BundledWorkerInstallation.Resolve(json.RootElement, destination, "win-x64");
        Assert.Equal(source.BaseDisk.Sha256, copied.BaseDisk.Sha256);
        using (var lease = await QemuRuntimeLease.Open(copied.RuntimePackage!, copied.Executable, copied.ImageTool, default)) Assert.Equal(2, lease.FileCount);
        File.WriteAllText(copied.BaseDisk.Path, "Change this independent copy");
        Assert.Equal("Guest fixture", File.ReadAllText(source.BaseDisk.Path));
    }
    [Fact] public async Task FailedPinRetainsPartialCopiesWithoutPublishingAnInstallableDescriptor()
    {
        var source = Installation(); var destination = Path.Combine(root, "incomplete");
        File.WriteAllText(source.BaseDisk.Path, "Changed source before copy");
        await Assert.ThrowsAsync<IOException>(() => WorkerBundlePreparation.Create(source, destination, "win-x64", default));
        Assert.False(File.Exists(Path.Combine(destination, "installation.json")));
        Assert.True(File.Exists(Path.Combine(destination, "guest/root.ext4")));
        Assert.Equal("Changed source before copy", File.ReadAllText(source.BaseDisk.Path));
    }
    [Fact] public async Task RefusesExistingDestinationAndCancelledPreparation()
    {
        var source = Installation(); var existing = Path.Combine(root, "existing"); Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "Keep this estate intact.");
        await Assert.ThrowsAsync<IOException>(() => WorkerBundlePreparation.Create(source, existing, "win-x64", default));
        Assert.Equal("Keep this estate intact.", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var pending = Path.Combine(root, "cancelled");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkerBundlePreparation.Create(source, pending, "win-x64", cancelled.Token));
        Assert.False(Directory.Exists(pending));
    }
    [Fact] public async Task RefusesToCreateFilesInsideTheOriginalRuntime()
    {
        var source = Installation(); var destination = Path.Combine(source.RuntimePackage!.Root, "another-worker");
        await Assert.ThrowsAsync<ArgumentException>(() => WorkerBundlePreparation.Create(source, destination, "win-x64", default));
        Assert.False(Directory.Exists(destination));
    }
    [Fact] public async Task SparseCopyPreservesLeadingMiddleAndTrailingZerosWithoutSharingTheSource()
    {
        var source = Path.Combine(root, "sparse-source"); var target = Path.Combine(root, "sparse-copy");
        await using (var file = new FileStream(source, FileMode.CreateNew, FileAccess.Write))
        {
            file.SetLength(32 * 1024 * 1024 + 3); file.Position = 12 * 1024 * 1024 + 1;
            await file.WriteAsync(new byte[] { 7, 8, 9 });
        }
        var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
        var length = await WorkerBundlePreparation.CopyPinned(new(source, expected), target, 64 * 1024 * 1024, default);
        Assert.Equal(32 * 1024 * 1024 + 3, length); Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
        if (OperatingSystem.IsWindows()) Assert.True((File.GetAttributes(target) & FileAttributes.SparseFile) != 0);
        File.WriteAllText(target, "Only the copy changes.");
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
    }
    [Fact] public async Task LowSpaceRefusesBeforeCreatingACopyAndStopsWhenSpaceFalls()
    {
        var source = Path.Combine(root, "space-source"); var target = Path.Combine(root, "space-copy");
        await using (var file = File.Create(source)) file.SetLength(65 * 1024 * 1024);
        var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)));
        await Assert.ThrowsAsync<IOException>(() => WorkerBundlePreparation.CopyPinned(new(source, expected), target, 100 * 1024 * 1024, default, () => 512L * 1024 * 1024));
        Assert.False(File.Exists(target));
        var checks = 0;
        await Assert.ThrowsAsync<IOException>(() => WorkerBundlePreparation.CopyPinned(new(source, expected), target, 100 * 1024 * 1024, default,
            () => ++checks <= 2 ? 20L * 1024 * 1024 * 1024 : 0));
        Assert.True(File.Exists(target)); Assert.Equal(3, checks);
        Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
    }
    public void Dispose() => Directory.Delete(root, true);
}
