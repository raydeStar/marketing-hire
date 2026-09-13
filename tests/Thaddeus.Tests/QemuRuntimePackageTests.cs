using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class QemuRuntimePackageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-package-" + Guid.NewGuid().ToString("N"));
    private string RuntimeRoot => Path.Combine(root, "runtime");
    private static readonly string[] Names = ["qemu-system-x86_64.exe", "qemu-img.exe", "lib/helper.dll", "share/firmware.bin"];
    private static string Contents(string name) => "inert package fixture: " + name;
    private QemuPinnedFile Pin(string name) => new(Path.Combine(RuntimeRoot, name), Wire.Hash(Contents(name)));
    public QemuRuntimePackageTests()
    {
        foreach (var name in Names)
        {
            var path = Path.Combine(RuntimeRoot, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Contents(name));
        }
    }
    private QemuRuntimePackage Package(QemuPinnedFile[]? entries = null, string kind = "qemu-windows-runtime")
    {
        var manifest = new QemuRuntimeManifest(1, kind, "11.1.0",
            entries ?? Names.Select(name => new QemuPinnedFile(name, Wire.Hash(Contents(name)))).ToArray());
        var content = Wire.Pack(manifest); var path = Path.Combine(root, "manifest.json"); File.WriteAllText(path, content);
        return new(RuntimeRoot, new(path, Wire.Hash(content)));
    }
    private Task<QemuRuntimeLease> Open(QemuRuntimePackage package) => QemuRuntimeLease.Open(package, Pin(Names[0]), Pin(Names[1]), default);

    [Fact] public async Task RuntimeTargetRequiresExplicitMatchingAdmission()
    {
        var package = Package(kind: "qemu-linux-x64-runtime");
        await Assert.ThrowsAsync<IOException>(() => Open(package));
        using var lease = await QemuRuntimeLease.Open(package, Pin(Names[0]), Pin(Names[1]), default, "qemu-linux-x64-runtime");
        Assert.Equal(Pin(Names[2]), lease.FilePin(Names[2]));
        Assert.Throws<IOException>(() => lease.FilePin("../outside"));
        Assert.Throws<IOException>(() => lease.FilePin(Names[2].ToUpperInvariant()));
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => lease.FilePin(Names[0]));
    }

    [Fact] public async Task ExactPackageIsReadLockedAndReleased()
    {
        var package = Package();
        using (var lease = await Open(package))
        {
            Assert.Equal(4, lease.FileCount); lease.VerifyInventory();
            if (OperatingSystem.IsWindows())
                foreach (var path in Names.Select(name => Pin(name).Path).Append(package.Manifest.Path))
                    Assert.Throws<IOException>(() => { using var unexpected = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
        }
        foreach (var path in Names.Select(name => Pin(name).Path).Append(package.Manifest.Path))
        { using var released = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
    }
    [Theory][InlineData("changed")][InlineData("missing")][InlineData("unexpected")]
    public async Task LibraryAndFirmwareChangesAreRefused(string fault)
    {
        var package = Package(); var path = Pin("lib/helper.dll").Path;
        if (fault == "changed") File.WriteAllText(path, "different library bytes");
        else if (fault == "missing") File.Delete(path);
        else File.WriteAllText(Path.Combine(RuntimeRoot, "unexpected.dll"), "new library");
        await Assert.ThrowsAsync<IOException>(() => Open(package));
        using var released = new FileStream(package.Manifest.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
    [Fact] public async Task FileAddedAfterPinningIsRefusedByNextInventoryCheck()
    {
        using var lease = await Open(Package());
        File.WriteAllText(Path.Combine(RuntimeRoot, "unexpected.dll"), "not in the approved bundle");
        Assert.Throws<IOException>(lease.VerifyInventory);
    }
    [Fact] public async Task ChangedManifestCannotBlessChangedFileBytes()
    {
        var trusted = Package(); File.WriteAllText(Pin("lib/helper.dll").Path, "altered");
        var replacement = Names.Select(name => new QemuPinnedFile(name, name == "lib/helper.dll" ? Wire.Hash("altered") : Wire.Hash(Contents(name)))).ToArray();
        Package(replacement);
        await Assert.ThrowsAsync<IOException>(() => Open(trusted));
    }
    [Fact] public async Task ConfiguredExecutableMustBelongToTheExactManifest()
    {
        var package = Package();
        await Assert.ThrowsAsync<IOException>(() => QemuRuntimeLease.Open(package, new(Path.Combine(root, Names[0]), Pin(Names[0]).Sha256), Pin(Names[1]), default));
        await Assert.ThrowsAsync<IOException>(() => QemuRuntimeLease.Open(package, Pin(Names[0]) with { Sha256 = new string('0', 64) }, Pin(Names[1]), default));
    }
    [Fact] public async Task DuplicateAndCaseCollidingManifestPathsAreRefused()
    {
        var files = Names.Select(name => new QemuPinnedFile(name, Wire.Hash(Contents(name)))).ToArray();
        await Assert.ThrowsAsync<IOException>(() => Open(Package([.. files, files[0]])));
        await Assert.ThrowsAsync<IOException>(() => Open(Package([.. files, files[0] with { Path = files[0].Path.ToUpperInvariant() }])));
    }
    [Fact] public async Task InventoryCaseMustMatchTheDeclaredSpelling()
    {
        var files = Names.Select(name => new QemuPinnedFile(name == "lib/helper.dll" ? "lib/HELPER.dll" : name, Wire.Hash(Contents(name)))).ToArray();
        await Assert.ThrowsAsync<IOException>(() => Open(Package(files)));
    }
    [Theory]
    [InlineData("../outside.dll")][InlineData("/outside.dll")][InlineData("lib\\outside.dll")]
    [InlineData("x.dll:stream")][InlineData("lib/../outside.dll")][InlineData("lib//x.dll")]
    [InlineData("lib/x.dll.")][InlineData("lib/x.dll ")][InlineData("lib/CON.txt")]
    public async Task ManifestPathsCannotSelectOutsideFilesOrWindowsAliases(string path)
    {
        var files = Names.Select(name => new QemuPinnedFile(name, Wire.Hash(Contents(name)))).ToArray();
        await Assert.ThrowsAsync<IOException>(() => Open(Package([.. files, new(path, new string('a', 64))])));
    }
    [Fact] public async Task LinkedRuntimeEntryIsRefusedOnUnix()
    {
        if (OperatingSystem.IsWindows()) return; // Windows read-share enforcement is checked separately; CI supplies the portable link fixture.
        var package = Package(); var path = Pin("lib/helper.dll").Path;
        File.Delete(path); File.CreateSymbolicLink(path, Pin(Names[0]).Path);
        await Assert.ThrowsAsync<IOException>(() => Open(package));
    }
    [Fact] public async Task CancellationReleasesManifestAndRuntimeHandles()
    {
        var package = Package(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QemuRuntimeLease.Open(package, Pin(Names[0]), Pin(Names[1]), cancellation.Token));
        using var released = new FileStream(package.Manifest.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
