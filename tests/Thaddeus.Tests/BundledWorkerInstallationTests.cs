using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class BundledWorkerInstallationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-bundle-" + Guid.NewGuid().ToString("N"));
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public BundledWorkerInstallationTests() => Directory.CreateDirectory(root);
    private static JsonObject Document() => JsonNode.Parse(Wire.Pack(new BundledWorkerDocument(1, BundledWorkerInstallation.Kind, "win-x64",
        new(new("runtime/qemu.exe", Hash), new("runtime/image.exe", Hash), new("guest/kernel", Hash),
            new("guest/initrd", Hash), new("guest/root.ext4", Hash), new("runtime", new("runtime-manifest.json", Hash))))))!.AsObject();
    private QemuInstallation Resolve(JsonObject json, string runtime = "win-x64", string? directory = null)
    {
        using var document = JsonDocument.Parse(json.ToJsonString());
        return BundledWorkerInstallation.Resolve(document.RootElement, directory ?? root, runtime);
    }
    [Fact] public void RelocationUsesOnlyTheNewBundleRootAndPreservesEveryPin()
    {
        var first = Resolve(Document());
        var moved = Path.Combine(root, "another computer é"); Directory.CreateDirectory(moved);
        var second = Resolve(Document(), directory: moved);
        Assert.Equal(first.Image, second.Image);
        Assert.All(second.Files, file => { Assert.StartsWith(moved + Path.DirectorySeparatorChar, file.Path); Assert.Equal(Hash, file.Sha256); });
        Assert.Equal(Path.Combine(moved, "runtime"), second.RuntimePackage!.Root);
        Assert.NotEqual(first.Executable.Path, second.Executable.Path);
        Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories)); // Resolving cannot install, execute or enroll anything.
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/notepad.exe")]
    [InlineData("\\\\server\\share\\worker")]
    [InlineData("guest\\root.ext4")]
    [InlineData("guest//root.ext4")]
    [InlineData("guest/./root.ext4")]
    [InlineData("guest/root.ext4:stream")]
    [InlineData("guest/root.ext4.")]
    [InlineData("guest/root.ext4 ")]
    [InlineData("guest/CON.txt")]
    [InlineData("guest/worker\n")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesPathsThatCouldEscapeOrChangeMeaningOnAnotherPlatform(string? path)
    {
        var json = Document(); json["installation"]!["baseDisk"]!["path"] = path;
        Assert.Throws<ArgumentException>(() => Resolve(json));
    }
    [Fact] public void RefusesAnExecutableOutsideTheDeclaredRuntime()
    {
        var json = Document(); json["installation"]!["executable"]!["path"] = "guest/qemu.exe";
        Assert.Throws<ArgumentException>(() => Resolve(json));
    }
    [Fact] public void RefusesRuntimeRootTraversalAndInvalidPins()
    {
        var json = Document(); json["installation"]!["runtimePackage"]!["root"] = "..";
        Assert.Throws<ArgumentException>(() => Resolve(json));
        json = Document(); json["installation"]!["kernel"]!["sha256"] = "unverified";
        Assert.Throws<ArgumentException>(() => Resolve(json));
        json = Document(); json["installation"]!["runtimePackage"] = null;
        Assert.Throws<ArgumentException>(() => Resolve(json));
    }
    [Fact] public void RefusesWrongPlatformFormatAndExtraExecutableArguments()
    {
        Assert.Throws<ArgumentException>(() => Resolve(Document(), "linux-x64"));
        Assert.Throws<ArgumentException>(() => Resolve(Document(), "osx-arm64"));
        var json = Document(); json["schemaVersion"] = 2;
        Assert.Throws<ArgumentException>(() => Resolve(json));
        json = Document(); json["installation"]!["executable"]!["arguments"] = "--unrestricted";
        Assert.Throws<JsonException>(() => Resolve(json));
    }
    [Theory]
    [InlineData("schemaVersion", "SchemaVersion")]
    [InlineData("path", "Path")]
    public void RefusesDuplicateFieldsAtEitherLevel(string key, string duplicate)
    {
        var json = Document().ToJsonString();
        var marker = '"' + key + "\":";
        var location = json.IndexOf(marker, StringComparison.Ordinal);
        var value = key == "path" ? "\"guest/other\"" : "1";
        json = json.Insert(location, '"' + duplicate + "\":" + value + ",");
        using var document = JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(() => BundledWorkerInstallation.Resolve(document.RootElement, root, "win-x64"));
    }
    [Fact] public void UnixLinkedBundleDirectoryIsRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.Combine(root, "actual"));
        Directory.CreateSymbolicLink(Path.Combine(root, "runtime"), Path.Combine(root, "actual"));
        Assert.Throws<ArgumentException>(() => Resolve(Document()));
    }
    public void Dispose() => Directory.Delete(root, true);
}
