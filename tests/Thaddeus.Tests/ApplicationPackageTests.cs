using System.Text.Json.Nodes;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Tests;

public sealed class ApplicationPackageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus package ' $-" + Guid.NewGuid().ToString("N"));
    internal static string CreatePackage(string root, int? schema = null)
    {
        Directory.CreateDirectory(Path.Combine(root, "wwwroot"));
        var files = new List<object>();
        var executable = OperatingSystem.IsWindows() ? "Thaddeus.Host.exe" : "Thaddeus.Host";
        var launcher = OperatingSystem.IsWindows() ? "launch-host.ps1" : OperatingSystem.IsMacOS() ? "Start Thaddeus.command" : "start-thaddeus.sh";
        foreach (var name in new[] { executable, launcher, "wwwroot/index.html", "Thaddeus.Host.runtimeconfig.json" })
        {
            var content = "Inert fixture, never executed: " + name;
            File.WriteAllText(Path.Combine(root, name), content);
            files.Add(new { path = name, size = new FileInfo(Path.Combine(root, name)).Length, sha256 = Wire.Hash(content) });
        }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(root, executable), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(root, "package-manifest.json"), Wire.Pack(new { schemaVersion = 1, kind = "portable-development-package",
            runtime = ApplicationPackage.NativeRuntime, sourceHead = new string('a', 40), published = DateTimeOffset.UtcNow,
            application = ApplicationPackage.Capabilities with { StudySchemaVersion = schema ?? Store.CurrentSchemaVersion }, files }));
        return root;
    }
    internal static void Rewrite(string root, Action<JsonObject> mutate)
    {
        var file = Path.Combine(root, "package-manifest.json"); var value = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        mutate(value); File.WriteAllText(file, value.ToJsonString());
    }
    [Fact] public async Task ExactPackageVerifiesWithoutExecutingOrChangingAnyFiles()
    {
        CreatePackage(root); var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllText);
        var receipt = await ApplicationPackage.Verify(root);
        Assert.Equal(root, receipt.Directory); Assert.Equal(4, receipt.Files); Assert.False(receipt.PublisherVerified);
        Assert.Equal(Store.CurrentSchemaVersion, receipt.StudySchemaVersion);
        Assert.Equal(receipt, await ApplicationPackage.Verify(root, expectedManifestSha256: receipt.ManifestSha256));
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllText(file.Key));
        Assert.Equal(before.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        ApplicationPackage.RequireStudyCompatibility(receipt, Store.CurrentSchemaVersion);
        Assert.Throws<InvalidOperationException>(() => ApplicationPackage.RequireStudyCompatibility(receipt, Store.CurrentSchemaVersion + 1));
    }
    [Theory] [InlineData("../elsewhere")] [InlineData("wwwroot/index.html:stream")] [InlineData("worker/../../private")] [InlineData("CON.txt")]
    public async Task TraversalAndAmbiguousFileNamesAreRefused(string path)
    {
        CreatePackage(root); Rewrite(root, json => json["files"]![0]!["path"] = path);
        await Assert.ThrowsAsync<ArgumentException>(() => ApplicationPackage.Verify(root));
    }
    [Theory] [InlineData("schema")] [InlineData("runtime")] [InlineData("missing")] [InlineData("duplicate")]
    public async Task UnsupportedOrAmbiguousManifestCannotBecomeAVerifiedPackage(string mutation)
    {
        CreatePackage(root);
        Rewrite(root, json =>
        {
            if (mutation == "schema") json["application"]!["minimumStudySchemaVersion"] = 999;
            else if (mutation == "runtime") json["runtime"] = "wrong-platform";
            else if (mutation == "missing") json.Remove("application");
            else json["files"]!.AsArray().Add(json["files"]![0]!.DeepClone());
        });
        await Assert.ThrowsAsync<ArgumentException>(() => ApplicationPackage.Verify(root));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ChangedOrUndeclaredPayloadIsRefused(bool undeclared)
    {
        CreatePackage(root);
        var file = Path.Combine(root, undeclared ? "private-launch.json" : "wwwroot/index.html");
        File.WriteAllText(file, "Changed or private bytes.");
        await Assert.ThrowsAsync<IOException>(() => ApplicationPackage.Verify(root));
        Assert.Equal("Changed or private bytes.", File.ReadAllText(file));
    }
    [Fact] public async Task ChangedReviewAndCancellationCannotVerify()
    {
        CreatePackage(root); var receipt = await ApplicationPackage.Verify(root);
        Rewrite(root, json => json["published"] = DateTimeOffset.UtcNow.AddHours(1));
        await Assert.ThrowsAsync<IOException>(() => ApplicationPackage.Verify(root, expectedManifestSha256: receipt.ManifestSha256));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ApplicationPackage.Verify(root, new CancellationToken(true)));
    }
    [Fact] public void StudyCopiesRequireThePayloadAndReserve()
    {
        StorageSpace.Require(root, 100, StorageSpace.Reserve + 100);
        Assert.Throws<IOException>(() => StorageSpace.Require(root, 100, StorageSpace.Reserve + 99));
        Assert.Throws<IOException>(() => StorageSpace.Require(root, 0, StorageSpace.Reserve - 1));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
