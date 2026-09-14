using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Infrastructure;

public sealed record ApplicationCapabilities(int FormatVersion, string Runtime, int MinimumStudySchemaVersion,
    int StudySchemaVersion, int GuardedLaunchVersion);
public sealed record VerifiedApplicationPackage(string Directory, string Runtime, string SourceHead,
    DateTimeOffset Published, int MinimumStudySchemaVersion, int StudySchemaVersion,
    int Files, long Bytes, string ManifestSha256, bool PublisherVerified = false);

/// <summary>Verifies the selected application without executing it. A hash checks the suitcase, not the traveller's identity.</summary>
public static class ApplicationPackage
{
    private const int MaxFiles = 8192, MaxManifest = 4_000_000;
    private const long MaxBytes = 18L * 1024 * 1024 * 1024;
    public static string NativeRuntime => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public static ApplicationCapabilities Capabilities => new(1, NativeRuntime, 0, Store.CurrentSchemaVersion, 1);

    public static async Task<VerifiedApplicationPackage> Verify(string directory, CancellationToken cancellation = default,
        string? expectedManifestSha256 = null)
    {
        try { return await VerifyCore(directory, cancellation, expectedManifestSha256); }
        catch (Exception error) when (error is KeyNotFoundException or FormatException or OverflowException)
        { throw new ArgumentException("The selected application manifest is incomplete or invalid.", error); }
    }
    private static async Task<VerifiedApplicationPackage> VerifyCore(string directory, CancellationToken cancellation, string? expectedManifestSha256)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose the full path to an extracted application folder.");
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); Store.AssertNoLinks(directory);
        if (!System.IO.Directory.Exists(directory)) throw new ArgumentException("The selected application folder is missing.");
        var manifestPath = Path.Combine(directory, "package-manifest.json"); Store.AssertNoLinks(manifestPath);
        await using var manifestFile = OpenOrdinary(manifestPath);
        if (manifestFile.Length is < 1 or > MaxManifest) throw new IOException("The application manifest exceeds its limit.");
        var bytes = new byte[checked((int)manifestFile.Length)]; await manifestFile.ReadExactlyAsync(bytes, cancellation);
        var manifestHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (expectedManifestSha256 != null && manifestHash != expectedManifestSha256)
            throw new IOException("The reviewed application manifest changed. Select and review the package again.");
        using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 32 }); Unique(json.RootElement);
        var manifest = json.RootElement;
        if (manifest.GetProperty("schemaVersion").GetInt32() != 1 || manifest.GetProperty("kind").GetString() != "portable-development-package" ||
            manifest.GetProperty("runtime").GetString() != NativeRuntime) throw new ArgumentException("Choose a portable application for this computer's operating system and architecture.");
        if (!manifest.TryGetProperty("application", out var declaration))
            throw new ArgumentException("This older package does not declare study compatibility. Use a package with guided version support.");
        var capability = declaration.Deserialize<ApplicationCapabilities>(Thaddeus.Core.Wire.Json);
        if (capability == null || capability.FormatVersion != 1 || capability.Runtime != NativeRuntime || capability.MinimumStudySchemaVersion < 0 ||
            capability.StudySchemaVersion < capability.MinimumStudySchemaVersion || capability.StudySchemaVersion > 1000 || capability.GuardedLaunchVersion != 1)
            throw new ArgumentException("This package's compatibility declaration is unsupported.");
        var head = manifest.GetProperty("sourceHead").GetString() ?? "";
        if (!Regex.IsMatch(head, "\\A(?:[a-f0-9]{40}|[a-f0-9]{64})\\z")) throw new ArgumentException("The package source identity is invalid.");
        var published = manifest.GetProperty("published").GetDateTimeOffset();
        var files = manifest.GetProperty("files");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() is < 1 or > MaxFiles)
            throw new ArgumentException("The application inventory exceeds its limit.");
        var pins = new List<(string Path, long Size, string Hash)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in files.EnumerateArray())
        {
            var relative = file.GetProperty("path").GetString() ?? ""; Relative(relative);
            var size = file.GetProperty("size").GetInt64(); var hash = file.GetProperty("sha256").GetString() ?? "";
            if (!names.Add(relative) || relative.Equals("package-manifest.json", StringComparison.OrdinalIgnoreCase) || size < 0 || size > MaxBytes ||
                !Regex.IsMatch(hash, "\\A[a-f0-9]{64}\\z")) throw new ArgumentException("The package has an invalid or repeated file entry.");
            total = checked(total + size); if (total > MaxBytes) throw new IOException("The package exceeds the supported expanded size.");
            pins.Add((relative, size, hash));
        }
        var executable = OperatingSystem.IsWindows() ? "Thaddeus.Host.exe" : "Thaddeus.Host";
        var launcher = OperatingSystem.IsWindows() ? "launch-host.ps1" : OperatingSystem.IsMacOS() ? "Start Thaddeus.command" : "start-thaddeus.sh";
        foreach (var required in new[] { executable, launcher, "wwwroot/index.html", "Thaddeus.Host.runtimeconfig.json" })
            if (!pins.Any(file => file.Path == required)) throw new ArgumentException("The complete packaged application and launcher are required.");
        var expected = pins.Select(pin => pin.Path).Append("package-manifest.json").Order(StringComparer.Ordinal).ToArray();
        if (!Inventory(directory).SequenceEqual(expected)) throw new IOException("The application folder differs from its manifest. Keep private launch profiles and studies outside it.");
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            foreach (var pin in pins)
            {
                cancellation.ThrowIfCancellationRequested();
                var full = Path.Combine(directory, pin.Path); Store.AssertNoLinks(full);
                await using var input = OpenOrdinary(full);
                if (input.Length != pin.Size) throw new IOException("An application file changed size.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
                while (await input.ReadAsync(buffer, cancellation) is var read && read > 0)
                {
                    count += read; if (count > pin.Size) throw new IOException("An application file grew beyond its recorded size.");
                    hash.AppendData(buffer, 0, read);
                }
                if (count != pin.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != pin.Hash)
                    throw new IOException("An application file differs from its recorded hash. Download or extract a fresh copy.");
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        if (!Inventory(directory).SequenceEqual(expected)) throw new IOException("The application inventory changed during verification.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(Path.Combine(directory, executable)) & UnixFileMode.UserExecute) == 0)
            throw new IOException("The selected application is not executable on this computer.");
        return new(directory, NativeRuntime, head, published, capability.MinimumStudySchemaVersion, capability.StudySchemaVersion,
            pins.Count, total, manifestHash);
    }

    public static void RequireStudyCompatibility(VerifiedApplicationPackage package, int schema)
    {
        if (schema < package.MinimumStudySchemaVersion || schema > package.StudySchemaVersion)
            throw new InvalidOperationException("This application cannot read the selected backup's database version. For rollback, choose the backup made before that upgrade.");
    }
    private static FileStream OpenOrdinary(string path)
    {
        var info = new FileInfo(path); info.Refresh();
        // On Unix a FIFO must be rejected before opening it; there may never be a writer.
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Attributes.HasFlag(FileAttributes.Device))
            throw new IOException("Only ordinary package files are supported.");
        if (!OperatingSystem.IsWindows()) StudyBackup.NoLinks(path);
        return new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }
    private static string[] Inventory(string root)
    {
        var files = new List<string>(); var pending = new Stack<(string Directory, int Depth)>(); pending.Push((root, 0)); var visited = 0;
        while (pending.TryPop(out var current))
        {
            if (++visited > MaxFiles || current.Depth > 32) throw new IOException("The package directory structure exceeds its limit.");
            foreach (var item in new DirectoryInfo(current.Directory).EnumerateFileSystemInfos())
            {
                if (item.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Application folders cannot contain filesystem links.");
                if (item is DirectoryInfo) pending.Push((item.FullName, current.Depth + 1));
                else { if (files.Count >= MaxFiles) throw new IOException("The package has too many files."); files.Add(Path.GetRelativePath(root, item.FullName).Replace('\\', '/')); }
            }
        }
        return files.Order(StringComparer.Ordinal).ToArray();
    }
    private static void Relative(string value)
    {
        if (value.Length is < 1 or > 300 || value.Any(c => char.IsControl(c) || "\\:*?\"<>|".Contains(c)) ||
            value.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                Regex.IsMatch(part, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)", RegexOptions.IgnoreCase)))
            throw new ArgumentException("Application entries must be ordinary relative paths.");
    }
    private static void Unique(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in item.EnumerateObject()) { if (!names.Add(field.Name)) throw new ArgumentException("Ambiguous application manifest fields."); Unique(field.Value); }
        }
        else if (item.ValueKind == JsonValueKind.Array) foreach (var value in item.EnumerateArray()) Unique(value);
    }
}
