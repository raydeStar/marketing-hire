using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Thaddeus.Packaging;

namespace Thaddeus.Tests;

public sealed class NoticeBundleTests
{
    [Fact]
    public void Covers_published_dependencies_and_browser_helper_preserving_exact_text()
    {
        using var fixture = new Fixture();
        var report = fixture.Create();
        Assert.Equal(3, report.Components.Count);
        Assert.Contains(report.Components, component => component.Ecosystem == "nuget" && component.Identity == "Fixture/1.0.0");
        Assert.Contains(report.Components, component => component.Identity == "vite@6.4.3");
        Assert.DoesNotContain(report.Components, component => component.Identity.StartsWith("test-only@"));
        foreach (var notice in report.Components.SelectMany(component => component.Notices))
            Assert.Equal(Fixture.License, File.ReadAllText(Path.Combine(fixture.Generated, notice.File)));
        Assert.Equal(1, report.Files); // Identical text shares bytes, while each attribution remains.
        Assert.Equal(report.DependenciesSha256, Fixture.Hash(File.ReadAllBytes(Path.Combine(fixture.Package, "Thaddeus.Host.deps.json"))));
        Assert.True(File.Exists(Path.Combine(fixture.Generated, "bundle.json")));
    }

    [Fact]
    public void Missing_full_license_stops_before_writing_a_partial_notice_bundle()
    {
        using var fixture = new Fixture();
        File.Move(Path.Combine(fixture.Source, "web/node_modules/fixture/LICENSE"), Path.Combine(fixture.Source, "web/node_modules/fixture/NOTICE"));
        Assert.Contains("Missing full npm license", Assert.Throws<IOException>(() => fixture.Create()).Message);
        Assert.False(Directory.Exists(fixture.Generated));
    }

    [Fact]
    public void Changed_installed_version_cannot_borrow_the_locked_attribution()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Source, "web/node_modules/fixture/package.json"), "{\"name\":\"fixture\",\"version\":\"2.0.0\",\"license\":\"MIT\"}");
        Assert.Contains("differs from its lock", Assert.Throws<IOException>(() => fixture.Create()).Message);
        Assert.False(Directory.Exists(fixture.Generated));
    }

    [Fact]
    public void Extracted_NuGet_license_must_match_the_verified_archive()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Nuget, "fixture/1.0.0/LICENSE.txt"), "An altered local attribution.");
        Assert.Throws<IOException>(() => fixture.Create());
        Assert.False(Directory.Exists(fixture.Generated));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Archive_checksum_and_restore_content_hash_have_separate_bindings(bool mismatch)
    {
        using var fixture = new Fixture();
        var contentHash = Convert.ToBase64String(SHA512.HashData(Encoding.UTF8.GetBytes("Fictional restored content identity")));
        File.WriteAllText(Path.Combine(fixture.Nuget, "fixture/1.0.0/.nupkg.metadata"), JsonSerializer.Serialize(new { version = 2, contentHash }));
        if (mismatch)
        {
            Assert.Contains("restore content identity changed", Assert.Throws<IOException>(() => fixture.Create()).Message);
            Assert.False(Directory.Exists(fixture.Generated));
            return;
        }
        var deps = Path.Combine(fixture.Package, "Thaddeus.Host.deps.json"); var json = JsonNode.Parse(File.ReadAllText(deps))!;
        json["libraries"]!["Fixture/1.0.0"]!["sha512"] = "sha512-" + contentHash; File.WriteAllText(deps, json.ToJsonString());
        var component = fixture.Create().Components.Single(item => item.Ecosystem == "nuget");
        Assert.Equal("sha512-" + contentHash, component.PackageContentHash);
        Assert.NotEqual(component.PackageContentHash, component.PackageIntegrity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pinned_upstream_notices_require_exact_identity_and_hash(bool wrongIdentity)
    {
        using var fixture = new Fixture();
        var catalog = Path.Combine(fixture.Source, "third-party/nuget");
        File.WriteAllText(Path.Combine(catalog, "LICENSE"), Fixture.License);
        File.WriteAllText(Path.Combine(catalog, "catalog.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, components = new[] { new { identity = "fixture/1.0.0", license = "MIT",
                repository = wrongIdentity ? "https://example.invalid/different" : "https://example.invalid/fixture",
                packageCommit = "fixture-commit", provenance = "Fictional test data", files = new[] {
                    new { path = "LICENSE", url = "https://example.invalid/fixture/LICENSE", sha256 = new string('0', 64) } } } }
        }));
        var error = Assert.Throws<IOException>(() => fixture.Create());
        Assert.Contains(wrongIdentity ? "does not match" : "Pinned upstream notice changed", error.Message);
        Assert.False(Directory.Exists(fixture.Generated));
    }

    [Fact]
    public void Existing_application_and_completed_notice_bundle_are_never_overwritten()
    {
        using var fixture = new Fixture();
        fixture.Create(); var before = File.ReadAllBytes(Path.Combine(fixture.Generated, "bundle.json"));
        Assert.Throws<IOException>(() => fixture.Create());
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(fixture.Generated, "bundle.json")));
        File.WriteAllText(Path.Combine(fixture.Package, "package-manifest.json"), "{}");
        Assert.Contains("already sealed", Assert.Throws<IOException>(() => fixture.Create()).Message);
    }

    private sealed class Fixture : IDisposable
    {
        internal const string License = "Fictional test notice. These bytes grant no rights to product code.\n";
        private readonly string root = Path.Combine(Path.GetTempPath(), "thaddeus-notices-" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(root, "source");
        internal string Package => Path.Combine(root, "package");
        internal string Nuget => Path.Combine(root, "nuget");
        internal string Generated => Path.Combine(Package, "ThirdPartyNotices/Generated");
        internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        internal NoticeReport Create() => NoticeBundle.Create(Source, Package, Path.Combine(root, "assets.json"));

        internal Fixture()
        {
            Directory.CreateDirectory(Package);
            var directory = Path.Combine(Nuget, "fixture/1.0.0"); Directory.CreateDirectory(directory);
            var spec = "<package><metadata><id>Fixture</id><version>1.0.0</version><license type=\"expression\">MIT</license><repository url=\"https://example.invalid/fixture\" commit=\"fixture-commit\"/><copyright>Fictional owner</copyright></metadata></package>";
            File.WriteAllText(Path.Combine(directory, "fixture.nuspec"), spec, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "LICENSE.txt"), License, new UTF8Encoding(false));
            var archive = Path.Combine(directory, "fixture.1.0.0.nupkg");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                foreach (var file in new[] { "fixture.nuspec", "LICENSE.txt" }) zip.CreateEntryFromFile(Path.Combine(directory, file), file);
            var integrity = Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(archive)));
            File.WriteAllText(archive + ".sha512", integrity);
            File.WriteAllText(Path.Combine(directory, ".nupkg.metadata"), JsonSerializer.Serialize(new { version = 2, contentHash = integrity }));
            File.WriteAllText(Path.Combine(root, "assets.json"), JsonSerializer.Serialize(new { packageFolders = new Dictionary<string, object> { [Nuget] = new { } } }));
            File.WriteAllText(Path.Combine(Package, "Thaddeus.Host.deps.json"), JsonSerializer.Serialize(new { libraries = new Dictionary<string, object>
                { ["Thaddeus.Host/1.0.0"] = new { type = "project" }, ["Fixture/1.0.0"] = new { type = "package", sha512 = "sha512-" + integrity } } }));
            Directory.CreateDirectory(Path.Combine(Source, "third-party/nuget"));
            File.WriteAllText(Path.Combine(Source, "third-party/nuget/catalog.json"), "{\"schemaVersion\":1,\"components\":[]}");
            var packages = new Dictionary<string, object> { [""] = new { name = "fictional-client" } };
            foreach (var (name, version, dev) in new[] { ("fixture", "1.0.0", false), ("vite", "6.4.3", true), ("test-only", "1.0.0", true) })
            {
                var npm = Path.Combine(Source, "web/node_modules", name); Directory.CreateDirectory(npm);
                File.WriteAllText(Path.Combine(npm, "package.json"), JsonSerializer.Serialize(new { name, version, license = "MIT" }));
                File.WriteAllText(Path.Combine(npm, "LICENSE"), License);
                packages["node_modules/" + name] = new { version, dev, integrity = "fixture-install-integrity" };
            }
            File.WriteAllText(Path.Combine(Source, "web/package-lock.json"), JsonSerializer.Serialize(new { lockfileVersion = 3, packages }));
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal) || !Path.GetFileName(resolved).StartsWith("thaddeus-notices-"))
                throw new IOException("Unexpected fixture cleanup location.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
