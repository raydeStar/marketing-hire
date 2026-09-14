using System.Security.Cryptography;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Thaddeus.Packaging;

public sealed record NoticeFile(string Name, string Sha256, string Origin, string File);
public sealed record NoticeComponent(string Ecosystem, string Identity, string License, string Copyright,
    string MetadataSha256, string PackageIntegrity, string PackageContentHash, string Provenance, IReadOnlyList<NoticeFile> Notices);
public sealed record NoticeReport(int SchemaVersion, string Scope, string DependenciesSha256, string WebLockSha256,
    string CatalogSha256, int Files, IReadOnlyList<NoticeComponent> Components);

public static class NoticeBundle
{
    private const int MaxFile = 2_000_000, MaxTotal = 20_000_000;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static readonly Regex NoticeName = new(@"\A(?:licen[cs]e|copying|notice|third.party.notices)(?:[.\-].*)?\z", RegexOptions.IgnoreCase);
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static NoticeReport Create(string source, string package, string assets)
    {
        source = Path.GetFullPath(source); package = Path.GetFullPath(package);
        Ordinary(source); Ordinary(package);
        if (File.Exists(Path.Combine(package, "package-manifest.json"))) throw new IOException("Never alter an already sealed application package.");
        var destination = Path.Combine(package, "ThirdPartyNotices", "Generated");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Use a fresh notice destination.");
        var depsBytes = Read(package, "Thaddeus.Host.deps.json");
        var lockBytes = Read(source, "web/package-lock.json");
        var catalogRoot = Path.Combine(source, "third-party", "nuget");
        var catalogBytes = Read(catalogRoot, "catalog.json");
        using var deps = JsonDocument.Parse(depsBytes); using var web = JsonDocument.Parse(lockBytes);
        using var catalog = JsonDocument.Parse(catalogBytes); using var restored = JsonDocument.Parse(Read(Path.GetDirectoryName(Path.GetFullPath(assets))!, Path.GetFileName(assets)));
        if (catalog.RootElement.GetProperty("schemaVersion").GetInt32() != 1 || web.RootElement.GetProperty("lockfileVersion").GetInt32() != 3)
            throw new ArgumentException("Unsupported notice catalog or npm lock format.");
        var folders = restored.RootElement.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToArray();
        var components = new List<NoticeComponent>(); var content = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        NoticeFile Capture(string root, string relative, string origin, string? expectedHash = null)
        {
            var bytes = Read(root, relative); _ = Utf8.GetString(bytes);
            var hash = Hash(bytes); if (expectedHash != null && hash != expectedHash) throw new IOException("Pinned upstream notice changed: " + relative);
            content.TryAdd(hash, bytes);
            if (content.Values.Sum(value => value.Length) > MaxTotal) throw new IOException("Notice bundle exceeds its bounded size.");
            return new(relative, hash, origin, "texts/" + hash + ".txt");
        }
        foreach (var item in deps.RootElement.GetProperty("libraries").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var type = item.Value.GetProperty("type").GetString();
            if (type == "project") continue;
            if (type is not ("package" or "runtimepack")) throw new ArgumentException("Unknown published dependency type: " + type);
            var identity = item.Name.StartsWith("runtimepack.", StringComparison.Ordinal) ? item.Name[12..] : item.Name;
            var (id, version) = Identity(identity); var relative = id.ToLowerInvariant() + "/" + version.ToLowerInvariant();
            var candidates = folders.Select(folder => Within(folder, relative)).Where(Directory.Exists).ToArray();
            if (candidates.Length != 1) throw new IOException("Expected one restored package directory for " + identity);
            var directory = candidates[0];
            var specBytes = Read(directory, id.ToLowerInvariant() + ".nuspec");
            using var reader = XmlReader.Create(new MemoryStream(specBytes), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxFile });
            var xml = XDocument.Load(reader); var metadata = xml.Root!.Elements().Single(x => x.Name.LocalName == "metadata");
            XElement? Element(string name) => metadata.Elements().SingleOrDefault(x => x.Name.LocalName == name);
            if (!string.Equals(Element("id")?.Value, id, StringComparison.OrdinalIgnoreCase) || Element("version")?.Value != version)
                throw new IOException("Restored NuGet metadata does not match " + identity);
            var license = Element("license") ?? throw new IOException("Missing license declaration: " + identity);
            var licenseType = (string?)license.Attribute("type");
            if (licenseType is not ("expression" or "file")) throw new IOException("Unsupported license declaration: " + identity);
            var integrity = item.Value.TryGetProperty("sha512", out var declared) ? declared.GetString() ?? "" : "";
            // NuGet's content hash is distinct from a signed ZIP's byte checksum.
            // Locked restore supplies the former; preserve and check both identities.
            using var cache = JsonDocument.Parse(Read(directory, ".nupkg.metadata"));
            var restoredContent = cache.RootElement.GetProperty("contentHash").GetString()!;
            if (Convert.FromBase64String(restoredContent).Length != 64 || (integrity.Length > 0 && integrity != "sha512-" + restoredContent))
                throw new IOException("NuGet restore content identity changed: " + identity);
            var archive = Within(directory, id.ToLowerInvariant() + "." + version.ToLowerInvariant() + ".nupkg");
            Ordinary(archive);
            using (var stream = File.OpenRead(archive))
            {
                var actual = Convert.ToBase64String(SHA512.HashData(stream));
                var cached = Utf8.GetString(Read(directory, Path.GetFileName(archive) + ".sha512")).Trim();
                if (actual != cached)
                    throw new IOException("NuGet package archive identity changed: " + identity);
                integrity = "sha512-" + actual;
            }
            using var zip = ZipFile.OpenRead(archive);
            void ArchiveMatches(string relative, byte[] expected)
            {
                var entry = zip.Entries.SingleOrDefault(entry => entry.FullName.Equals(relative, StringComparison.OrdinalIgnoreCase));
                if (entry == null || entry.Length != expected.Length || entry.Length > MaxFile) throw new IOException("NuGet notice is missing from its archive: " + relative);
                using var input = entry.Open(); var bytes = new byte[expected.Length]; input.ReadExactly(bytes);
                if (!bytes.AsSpan().SequenceEqual(expected)) throw new IOException("Extracted NuGet notice differs from its archive: " + relative);
            }
            ArchiveMatches(id.ToLowerInvariant() + ".nuspec", specBytes);
            foreach (var file in RootNotices(directory)) ArchiveMatches(file, Read(directory, file));
            if (licenseType == "file") ArchiveMatches(license.Value, Read(directory, license.Value));
            var notices = RootNotices(directory).Select(file => Capture(directory, file, "NuGet package " + identity)).ToList();
            if (licenseType == "file" && notices.All(file => file.Name != license.Value)) notices.Add(Capture(directory, license.Value, "NuGet license declaration"));
            var matches = catalog.RootElement.GetProperty("components").EnumerateArray()
                .Where(entry => entry.GetProperty("identity").GetString() == relative).ToArray();
            if (matches.Length > 1) throw new IOException("Duplicate upstream notice record: " + identity);
            var provenance = "License/notice files preserved from the restored NuGet package.";
            if (matches.Length == 1)
            {
                var entry = matches[0]; var repository = Element("repository");
                if (licenseType != "expression" || entry.GetProperty("license").GetString() != license.Value ||
                    entry.GetProperty("repository").GetString() != (string?)repository?.Attribute("url") ||
                    entry.GetProperty("packageCommit").GetString() != ((string?)repository?.Attribute("commit") ?? ""))
                    throw new IOException("Upstream notice record does not match this package: " + identity);
                provenance = entry.GetProperty("provenance").GetString()!;
                foreach (var file in entry.GetProperty("files").EnumerateArray())
                    notices.Add(Capture(catalogRoot, file.GetProperty("path").GetString()!, file.GetProperty("url").GetString()!, file.GetProperty("sha256").GetString()));
            }
            if (licenseType != "file" && !notices.Any(file => LicenseName(file.Name)))
                throw new IOException("No full license text for " + identity + "; add a reviewed upstream record before publishing.");
            components.Add(new("nuget", identity, license.Value, Element("copyright")?.Value ?? "", Hash(specBytes), integrity, "sha512-" + restoredContent, provenance, notices));
        }
        var foundVite = false;
        foreach (var item in web.RootElement.GetProperty("packages").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (item.Name == "") continue;
            var vite = item.Name == "node_modules/vite";
            if (!vite && item.Value.TryGetProperty("dev", out var dev) && dev.GetBoolean()) continue;
            if (!item.Name.StartsWith("node_modules/", StringComparison.Ordinal)) throw new IOException("Unexpected npm package path.");
            var directory = Within(source, "web/" + item.Name);
            var bytes = Read(directory, "package.json"); using var npm = JsonDocument.Parse(bytes);
            var name = npm.RootElement.GetProperty("name").GetString()!;
            var version = npm.RootElement.GetProperty("version").GetString()!;
            if (version != item.Value.GetProperty("version").GetString() ||
                !(item.Name == "node_modules/" + name || item.Name.EndsWith("/node_modules/" + name, StringComparison.Ordinal)))
                throw new IOException("Installed npm package differs from its lock: " + item.Name);
            var license = npm.RootElement.GetProperty("license").GetString() ?? throw new IOException("Missing npm license: " + name);
            var notices = RootNotices(directory).Select(file => Capture(directory, file, "npm package " + name + "@" + version)).ToArray();
            if (!notices.Any(file => LicenseName(file.Name))) throw new IOException("Missing full npm license text: " + name);
            components.Add(new("npm", name + "@" + version, license, "", Hash(bytes), item.Value.GetProperty("integrity").GetString()!, "",
                vite ? "Vite includes the browser module-preload helper; its upstream notices are retained in full." : "Conservative production dependency graph, including type-only or tree-shaken entries; not every entry is emitted into the client.", notices));
            foundVite |= vite;
        }
        if (!foundVite || components.Count is < 1 or > 512) throw new IOException("Incomplete or oversized dependency notice graph.");
        var report = new NoticeReport(1, "Published host NuGet/runtime dependencies and npm production graph plus Vite browser helper. Worker images, QEMU, Docker and separately installed software are outside this bundle.",
            Hash(depsBytes), Hash(lockBytes), Hash(catalogBytes), content.Count, components);
        var index = new StringBuilder("THADDEUS THIRD-PARTY NOTICES\n\n" + report.Scope + "\n\nThis bundle does not grant a license to original Thaddeus code. Upstream notices may name additional components; their original text is preserved.\n");
        foreach (var component in components)
        {
            index.Append($"\n{component.Ecosystem}: {component.Identity}\nLicense: {component.License}\n{component.Copyright}\n{component.Provenance}\n");
            foreach (var notice in component.Notices) index.Append($"  {notice.Name}\n  Source: {notice.Origin}\n  Full text: {notice.File}\n  SHA-256: {notice.Sha256}\n");
        }
        var parent = Path.GetDirectoryName(destination)!;
        if (Directory.Exists(parent)) Ordinary(parent); else Directory.CreateDirectory(parent);
        Directory.CreateDirectory(destination);
        try
        {
            Directory.CreateDirectory(Path.Combine(destination, "texts"));
            foreach (var file in content) File.WriteAllBytes(Path.Combine(destination, "texts", file.Key + ".txt"), file.Value);
            File.WriteAllText(Path.Combine(destination, "THIRD-PARTY-NOTICES.txt"), index.ToString(), Utf8);
            File.WriteAllText(Path.Combine(destination, "bundle.json"), JsonSerializer.Serialize(report, Json) + "\n", Utf8);
        }
        catch { Directory.Delete(destination, recursive: true); throw; }
        return report;
    }

    private static (string Id, string Version) Identity(string value)
    {
        var pieces = value.Split('/');
        if (pieces.Length != 2 || !Regex.IsMatch(pieces[0], @"\A[A-Za-z0-9_.-]+\z") || !Regex.IsMatch(pieces[1], @"\A[0-9][A-Za-z0-9_.+-]*\z"))
            throw new ArgumentException("Invalid dependency identity.");
        return (pieces[0], pieces[1]);
    }
    private static string[] RootNotices(string directory) => Directory.EnumerateFiles(directory)
        .Select(Path.GetFileName).Where(name => NoticeName.IsMatch(name!)).Cast<string>().Order(StringComparer.Ordinal).ToArray();
    private static bool LicenseName(string name) => Regex.IsMatch(Path.GetFileName(name), @"\A(?:licen[cs]e|copying)(?:[.\-].*)?\z", RegexOptions.IgnoreCase);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Within(string root, string relative)
    {
        if (Path.IsPathFullyQualified(relative) || relative.Contains('\\') || relative.Split('/').Any(p => p is "" or "." or ".."))
            throw new IOException("Notice paths must be relative ordinary files.");
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(directory, relative));
        if (!full.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new IOException("Notice path escapes its package.");
        return full;
    }
    private static void Ordinary(string full)
    {
        for (string? current = Path.GetFullPath(full); current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked notice paths are refused.");
    }
    private static byte[] Read(string root, string relative)
    {
        var full = Within(root, relative); Ordinary(full);
        using var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!input.CanSeek || input.Length is < 1 or > MaxFile) throw new IOException("Invalid notice input size: " + relative);
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes); return bytes;
    }
}
