using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class WorkerBundleArchiveTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-archive-" + Guid.NewGuid().ToString("N"));
    private string Host => Path.Combine(root, "host");
    private string Target => Path.Combine(root, "combined.zip");
    private QemuPinnedFile Pin(string relative, string content)
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content);
        return new(path, Wire.Hash(content));
    }
    private QemuInstallation Prepare(string runtime = "win-x64")
    {
        var files = new List<object>();
        foreach (var name in new[] { runtime == "win-x64" ? "Thaddeus.Host.exe" : "Thaddeus.Host", "wwwroot/index.html", runtime == "win-x64" ? "Start Thaddeus.cmd" : "start-thaddeus.sh" })
        { var pin = Pin("host/" + name, "Inert host fixture " + name); files.Add(new { path = name, sha256 = pin.Sha256, size = new FileInfo(pin.Path).Length }); }
        Pin("host/package-manifest.json", Wire.Pack(new { schemaVersion = 1, kind = "portable-development-package", runtime, sourceHead = "fixture", files }));
        var executable = Pin("inputs/runtime/qemu", "Inert QEMU bytes"); var image = Pin("inputs/runtime/image", "Inert image-tool bytes");
        var manifest = Pin("inputs/runtime-manifest.json", Wire.Pack(new QemuRuntimeManifest(1, runtime == "win-x64" ? "qemu-windows-runtime" : "qemu-linux-x64-runtime", "11.1.0",
            [new("qemu", executable.Sha256), new("image", image.Sha256)])));
        return new(executable, image, Pin("inputs/kernel", "Kernel fixture"), Pin("inputs/initrd", "Initrd fixture"), Pin("inputs/root.ext4", "Guest fixture"),
            new(Path.Combine(root, "inputs/runtime"), manifest));
    }
    private void RewriteManifest(Action<JsonObject> mutate)
    {
        var path = Path.Combine(Host, "package-manifest.json"); var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); mutate(json); File.WriteAllText(path, json.ToJsonString());
    }
    [Theory] [InlineData("win-x64")] [InlineData("linux-x64")]
    public async Task OneArchiveContainsExactHostAndRelocatableWorkerWithoutStagingInputCopies(string runtime)
    {
        var installation = Prepare(runtime); var originalManifest = File.ReadAllText(Path.Combine(Host, "package-manifest.json"));
        var progress = new List<WorkerArchiveProgress>();
        var receipt = await WorkerBundleArchive.Create(installation, Host, Target, runtime, progress: progress.Add);
        Assert.False(receipt.SignedRelease); Assert.False(receipt.IsolationQualified); Assert.Equal(11, receipt.Files);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Target))), receipt.ArchiveSha256);
        Assert.Equal(originalManifest, File.ReadAllText(Path.Combine(Host, "package-manifest.json"))); Assert.False(Directory.Exists(Path.Combine(Host, "worker")));
        Assert.Equal(new[] { "host", "inputs" }, Directory.GetDirectories(root).Select(Path.GetFileName).Order().ToArray());
        var destination = Path.Combine(root, "relocated estate"); ZipFile.ExtractToDirectory(Target, destination);
        var package = Path.Combine(destination, "thaddeus-" + runtime);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(package, "package-manifest.json")))!;
        Assert.Equal(receipt.HostManifestSha256, manifest["bundledWorker"]!["originalHostManifestSha256"]!.GetValue<string>());
        foreach (var file in manifest["files"]!.AsArray())
        {
            var bytes = File.ReadAllBytes(Path.Combine(package, file!["path"]!.GetValue<string>()));
            Assert.Equal(file["size"]!.GetValue<long>(), bytes.LongLength); Assert.Equal(file["sha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        using var descriptor = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "worker/installation.json")));
        var resolved = BundledWorkerInstallation.Resolve(descriptor.RootElement, Path.Combine(package, "worker"), runtime);
        using var lease = await QemuRuntimeLease.Open(resolved.RuntimePackage!, resolved.Executable, resolved.ImageTool, default,
            runtime == "win-x64" ? "qemu-windows-runtime" : "qemu-linux-x64-runtime");
        Assert.Equal(2, lease.FileCount); Assert.Equal("Guest fixture", File.ReadAllText(resolved.BaseDisk.Path));
        File.WriteAllText(resolved.BaseDisk.Path, "Only the extracted copy changes"); Assert.Equal("Guest fixture", File.ReadAllText(installation.BaseDisk.Path));
        Assert.DoesNotContain(root, manifest.ToJsonString()); Assert.DoesNotContain(root, descriptor.RootElement.GetRawText());
        Assert.Equal("verified", progress[^1].Phase); Assert.Equal(receipt.LogicalBytes, progress[^1].Bytes);
        using var zip = ZipFile.OpenRead(Target);
        if (runtime == "linux-x64")
            foreach (var file in new[] { "Thaddeus.Host", "start-thaddeus.sh", "worker/runtime/qemu", "worker/runtime/image" })
                Assert.Equal(0x1ed, (zip.GetEntry("thaddeus-linux-x64/" + file)!.ExternalAttributes >> 16) & 0x1ff);
    }
    [Theory] [InlineData("host")] [InlineData("guest")] [InlineData("runtime")] [InlineData("extra")]
    public async Task ChangedInputsNeverPublishAndOwnedIncompleteArchiveIsRemoved(string which)
    {
        var installation = Prepare();
        var file = which switch { "host" => Path.Combine(Host, "wwwroot/index.html"), "guest" => installation.BaseDisk.Path, "runtime" => installation.Executable.Path, _ => Path.Combine(Host, "private-note.txt") };
        File.WriteAllText(file, "Changed source input");
        await Assert.ThrowsAnyAsync<IOException>(() => WorkerBundleArchive.Create(installation, Host, Target, "win-x64"));
        Assert.False(File.Exists(Target)); Assert.Empty(Directory.GetFiles(root, "*.incomplete-*")); Assert.Equal("Changed source input", File.ReadAllText(file));
    }
    [Theory] [InlineData("../outside")] [InlineData("C:/private")] [InlineData("worker/fake")] [InlineData("wwwroot/index.html:ads")]
    public async Task MalformedArchiveNamesFailBeforeOutput(string name)
    {
        var installation = Prepare(); RewriteManifest(json => json["files"]![0]!["path"] = name);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => WorkerBundleArchive.Create(installation, Host, Target, "win-x64")); Assert.False(File.Exists(Target));
    }
    [Fact] public async Task CancellationAndSpaceFailureCleanOutputWithoutTouchingSourcesOrPreviousArchives()
    {
        var installation = Prepare(); using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkerBundleArchive.Create(installation, Host, Target, "win-x64", stop.Token,
            progress => { if (progress.Phase == "packing") stop.Cancel(); }));
        Assert.False(File.Exists(Target)); Assert.Empty(Directory.GetFiles(root, "*.incomplete-*"));
        await Assert.ThrowsAsync<IOException>(() => WorkerBundleArchive.CreateCore(installation, Host, Target, "win-x64", default, availableSpace: () => 0));
        var checks = 0;
        await Assert.ThrowsAsync<IOException>(() => WorkerBundleArchive.CreateCore(installation, Host, Target, "win-x64", default,
            availableSpace: () => ++checks < 3 ? 30L * 1024 * 1024 * 1024 : 0));
        Assert.True(checks >= 3); Assert.False(File.Exists(Target)); Assert.Empty(Directory.GetFiles(root, "*.incomplete-*"));
        File.WriteAllText(Target, "Keep the previous archive.");
        await Assert.ThrowsAsync<IOException>(() => WorkerBundleArchive.Create(installation, Host, Target, "win-x64")); Assert.Equal("Keep the previous archive.", File.ReadAllText(Target));
    }
    [Fact] public async Task CorruptionAfterWritingIsDetectedByReopeningTheArchive()
    {
        var installation = Prepare();
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkerBundleArchive.CreateCore(installation, Host, Target, "win-x64", default, testFault: _ =>
            File.WriteAllText(Directory.GetFiles(root, "*.incomplete-*").Single(), "Damaged archive")));
        Assert.False(File.Exists(Target)); Assert.Empty(Directory.GetFiles(root, "*.incomplete-*")); Assert.Equal("Guest fixture", File.ReadAllText(installation.BaseDisk.Path));
    }
    [Fact] public async Task ValidZipWithChangedContentFailsTheIndependentEntryHashCheck()
    {
        var installation = Prepare();
        await Assert.ThrowsAsync<IOException>(() => WorkerBundleArchive.CreateCore(installation, Host, Target, "win-x64", default, testFault: _ =>
        {
            using var zip = ZipFile.Open(Directory.GetFiles(root, "*.incomplete-*").Single(), ZipArchiveMode.Update);
            var entry = zip.GetEntry("thaddeus-win-x64/wwwroot/index.html")!; var length = entry.Length;
            using var stream = entry.Open(); stream.SetLength(0); stream.Write(new byte[checked((int)length)]);
        }));
        Assert.False(File.Exists(Target)); Assert.Empty(Directory.GetFiles(root, "*.incomplete-*"));
    }
    [Fact] public async Task OutputCannotBeInsideAnInputTreeAndWrongTargetIsRefused()
    {
        var installation = Prepare();
        foreach (var output in new[] { Path.Combine(Host, "out.zip"), Path.Combine(installation.RuntimePackage!.Root, "out.zip") })
            await Assert.ThrowsAsync<ArgumentException>(() => WorkerBundleArchive.Create(installation, Host, output, "win-x64"));
        await Assert.ThrowsAsync<ArgumentException>(() => WorkerBundleArchive.Create(installation, Host, Target, "osx-arm64"));
        await Assert.ThrowsAsync<ArgumentException>(() => WorkerBundleArchive.Create(installation, Host, Target, "linux-x64"));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
