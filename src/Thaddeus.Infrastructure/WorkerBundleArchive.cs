using System.IO.Compression;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record WorkerArchiveProgress(string Phase, int Files, int TotalFiles, long Bytes, long TotalBytes);
public sealed record WorkerArchiveReceipt(string Archive, string Runtime, int Files, long LogicalBytes, long ArchiveBytes,
    string ArchiveSha256, string HostManifestSha256, string CombinedManifestSha256, string DescriptorSha256,
    string BuilderAssemblySha256, bool SignedRelease = false, bool IsolationQualified = false);

/// <summary>One streamed archive, no second guest disk. The raven packs the estate without renting another estate.</summary>
public static class WorkerBundleArchive
{
    private const long GiB = 1024L * 1024 * 1024, MaxLogicalBytes = 18 * GiB;
    private const int MaxFiles = 8192, MaxManifestBytes = 4_000_000;
    private sealed record Input(string Path, QemuPinnedFile? Pin, byte[]? Content, long Size, string Sha256, int Mode);

    public static Task<WorkerArchiveReceipt> Create(QemuInstallation installation, string hostDirectory, string archivePath,
        string runtime, CancellationToken cancellation = default, Action<WorkerArchiveProgress>? progress = null) =>
        CreateCore(installation, hostDirectory, archivePath, runtime, cancellation, progress);

    internal static async Task<WorkerArchiveReceipt> CreateCore(QemuInstallation installation, string hostDirectory, string archivePath,
        string runtime, CancellationToken cancellation, Action<WorkerArchiveProgress>? progress = null, Func<long>? availableSpace = null,
        Action<string>? testFault = null)
    {
        var kind = runtime switch { "win-x64" => "qemu-windows-runtime", "linux-x64" => "qemu-linux-x64-runtime", _ => throw new ArgumentException("Combined workers support Windows x64 and Linux x64.") };
        if (!Path.IsPathFullyQualified(hostDirectory) || !Path.IsPathFullyQualified(archivePath) || installation.RuntimePackage == null)
            throw new ArgumentException("Use absolute paths to a published host, pinned worker installation and new ZIP archive.");
        var host = Path.TrimEndingDirectorySeparator(Path.GetFullPath(hostDirectory));
        var target = Path.GetFullPath(archivePath); var parent = Path.GetDirectoryName(target)!;
        Store.AssertNoLinks(host); Store.AssertNoLinks(target);
        if (!Directory.Exists(host) || !Directory.Exists(parent) || !target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an existing host folder and output parent, with a new .zip filename.");
        if (Path.Exists(target)) throw new IOException("The archive already exists. It will not be overwritten.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (new[] { host, Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.RuntimePackage.Root)) }
            .Any(root => target.StartsWith(root + Path.DirectorySeparatorChar, comparison)))
            throw new ArgumentException("Keep the archive outside the host and worker input directories.");
        cancellation.ThrowIfCancellationRequested();
        availableSpace ??= () => WorkerBundlePreparation.AvailableSpace(target);
        EnsureSpace(availableSpace(), 8 * 1024 * 1024);
        var manifestPath = Path.Combine(host, "package-manifest.json"); Store.AssertNoLinks(manifestPath);
        await using var manifestLock = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (manifestLock.Length is < 1 or > MaxManifestBytes) throw new IOException("The host manifest exceeds its bound.");
        var originalBytes = new byte[checked((int)manifestLock.Length)]; await manifestLock.ReadExactlyAsync(originalBytes, cancellation);
        using var original = JsonDocument.Parse(originalBytes, new() { MaxDepth = 32 }); Unique(original.RootElement);
        var manifest = JsonNode.Parse(originalBytes) as JsonObject ?? throw new ArgumentException("Invalid host package manifest.");
        if (manifest["schemaVersion"]?.GetValue<int>() != 1 || manifest["kind"]?.GetValue<string>() != "portable-development-package" ||
            manifest["runtime"]?.GetValue<string>() != runtime || manifest["files"] is not JsonArray hostFiles || hostFiles.Count is < 1 or > 2048 ||
            manifest.ContainsKey("bundledWorker")) throw new ArgumentException("Choose an unmodified host-only portable package for this worker platform.");
        var inputs = new List<Input>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in hostFiles)
        {
            var relative = node?["path"]?.GetValue<string>() ?? ""; ValidateRelative(relative);
            if (relative.Equals("package-manifest.json", StringComparison.OrdinalIgnoreCase) || relative.Split('/')[0].Equals("worker", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The source package already contains reserved worker or manifest files.");
            var hash = node?["sha256"]?.GetValue<string>() ?? "";
            if (!HashPattern(hash) || !names.Add(relative)) throw new ArgumentException("The host inventory has an invalid hash or duplicate path.");
            var full = Path.Combine(host, relative); Store.AssertNoLinks(full);
            var length = node?["size"]?.GetValue<long>() ?? -1;
            if (length < 0 || length != new FileInfo(full).Length) throw new IOException("A host file differs from its recorded size.");
            inputs.Add(new(relative, new(full, hash), null, length, hash, Mode(runtime, relative, installation: null)));
        }
        foreach (var required in new[] { runtime == "win-x64" ? "Thaddeus.Host.exe" : "Thaddeus.Host", "wwwroot/index.html", runtime == "win-x64" ? "Start Thaddeus.cmd" : "start-thaddeus.sh" })
            if (!inputs.Any(file => file.Path == required)) throw new ArgumentException("The published host is missing a required launcher or application file.");
        if (inputs.Sum(file => file.Size) > 2 * GiB) throw new IOException("The host exceeds its two-GiB packaging allowance.");
        var originalInventory = Inventory(host); var expectedInventory = inputs.Select(file => file.Path).Append("package-manifest.json").Order(StringComparer.Ordinal).ToArray();
        if (!originalInventory.SequenceEqual(expectedInventory)) throw new IOException("The host folder differs from its declared inventory. Private launch profiles and undeclared files must stay outside it.");
        progress?.Invoke(new("checking-runtime", 0, 0, 0, 0));
        using var runtimeLease = await QemuRuntimeLease.Open(installation.RuntimePackage, installation.Executable, installation.ImageTool, cancellation, kind,
            (done, total) => progress?.Invoke(new("checking-runtime", done, total, 0, 0)));
        var runtimeManifest = Wire.Unpack<QemuRuntimeManifest>(await File.ReadAllTextAsync(installation.RuntimePackage.Manifest.Path, cancellation));
        void AddPin(string relative, QemuPinnedFile pin)
        {
            ValidateRelative(relative); Store.AssertNoLinks(pin.Path);
            if (!HashPattern(pin.Sha256) || !names.Add(relative)) throw new ArgumentException("A worker pin or archive path is invalid.");
            inputs.Add(new(relative, pin, null, new FileInfo(pin.Path).Length, pin.Sha256, Mode(runtime, relative, installation)));
        }
        foreach (var file in runtimeManifest.Files) AddPin("worker/runtime/" + file.Path, runtimeLease.FilePin(file.Path));
        AddPin("worker/runtime-manifest.json", installation.RuntimePackage.Manifest);
        AddPin("worker/guest/kernel", installation.Kernel); AddPin("worker/guest/initrd", installation.Initrd); AddPin("worker/guest/root.ext4", installation.BaseDisk);
        var descriptor = Encoding.UTF8.GetBytes(Wire.Pack(WorkerBundlePreparation.Descriptor(installation, runtime)));
        using (var parsed = JsonDocument.Parse(descriptor)) _ = BundledWorkerInstallation.Resolve(parsed.RootElement, Path.Combine(parent, "worker"), runtime);
        var descriptorHash = Hash(descriptor); names.Add("worker/installation.json");
        inputs.Add(new("worker/installation.json", null, descriptor, descriptor.Length, descriptorHash, 0x1a4));
        var originalHash = Hash(originalBytes);
        manifest["signedRelease"] = false; manifest["isolationQualified"] = false;
        manifest["bundledWorker"] = new JsonObject { ["formatVersion"] = 1, ["descriptor"] = "worker/installation.json", ["descriptorSha256"] = descriptorHash,
            ["originalHostManifestSha256"] = originalHash, ["runtime"] = runtime };
        manifest["files"] = new JsonArray(inputs.OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => (JsonNode)new JsonObject { ["path"] = file.Path, ["size"] = file.Size, ["sha256"] = file.Sha256 }).ToArray());
        var combinedBytes = Encoding.UTF8.GetBytes(manifest.ToJsonString(Wire.Json));
        if (combinedBytes.Length > MaxManifestBytes || inputs.Count + 1 > MaxFiles) throw new IOException("The combined manifest exceeds its bound.");
        inputs.Add(new("package-manifest.json", null, combinedBytes, combinedBytes.Length, Hash(combinedBytes), 0x1a4));
        var logical = inputs.Sum(file => file.Size);
        if (logical > MaxLogicalBytes) throw new IOException("The combined package exceeds its 18-GiB logical allowance.");
        // Budget incompressible output plus headers, without assuming that a sparse input makes a small archive.
        var archiveBound = checked(logical + logical / 100 + 8 * 1024 * 1024);
        EnsureSpace(availableSpace(), archiveBound); cancellation.ThrowIfCancellationRequested();
        var temporary = target + ".incomplete-" + Guid.NewGuid().ToString("N"); var created = false;
        var top = "thaddeus-" + runtime + "/";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
            {
                created = true;
                using (var guarded = new BoundedOutput(output, archiveBound, availableSpace))
                using (var archive = new ZipArchive(guarded, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
                {
                    long packed = 0; var completed = 0;
                    foreach (var input in inputs)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(top + input.Path, CompressionLevel.Fastest);
                        entry.ExternalAttributes = ((0x8000 | input.Mode) << 16) | 0x20;
                        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        await using var destination = entry.Open();
                        await using var source = OpenInput(input);
                        await CopyChecked(source, destination, input, cancellation, count => progress?.Invoke(new("packing", completed, inputs.Count, packed + count, logical)));
                        packed += input.Size; completed++;
                        progress?.Invoke(new("packing", completed, inputs.Count, packed, logical));
                    }
                }
                await output.FlushAsync(cancellation); output.Flush(true);
            }
            testFault?.Invoke("before-archive-verification");
            await Verify(temporary, inputs, top, logical, cancellation, progress);
            runtimeLease.VerifyInventory();
            if (!Inventory(host).SequenceEqual(originalInventory)) throw new IOException("The source host inventory changed during packaging.");
            manifestLock.Position = 0;
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(manifestLock, cancellation)) != originalHash) throw new IOException("The source host manifest changed during packaging.");
            var archiveInfo = new FileInfo(temporary); string archiveHash;
            await using (var archive = File.OpenRead(temporary)) archiveHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(archive, cancellation));
            string builderHash;
            await using (var builder = File.OpenRead(typeof(WorkerBundleArchive).Assembly.Location)) builderHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(builder, cancellation));
            var receipt = new WorkerArchiveReceipt(target, runtime, inputs.Count, logical, archiveInfo.Length, archiveHash, originalHash, Hash(combinedBytes), descriptorHash, builderHash);
            cancellation.ThrowIfCancellationRequested(); Store.AssertNoLinks(target);
            File.Move(temporary, target, overwrite: false); created = false;
            progress?.Invoke(new("verified", inputs.Count, inputs.Count, logical, logical)); return receipt;
        }
        finally
        {
            // Only this invocation's single temporary file is disposable; never erase a prior archive or any input.
            if (created) File.Delete(temporary);
        }
    }

    private static async Task Verify(string file, List<Input> inputs, string top, long logical, CancellationToken cancellation, Action<WorkerArchiveProgress>? progress)
    {
        using var archive = ZipFile.OpenRead(file);
        if (archive.Entries.Count != inputs.Count || archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Count)
            throw new IOException("The archive inventory differs from its plan.");
        long verified = 0; var done = 0;
        foreach (var input in inputs)
        {
            var entry = archive.GetEntry(top + input.Path) ?? throw new IOException("An expected archive entry is missing.");
            if (entry.Length != input.Size || ((entry.ExternalAttributes >> 16) & 0x1ff) != input.Mode) throw new IOException("An archived size or permission differs from its plan.");
            await using var stream = entry.Open();
            await CopyChecked(stream, Stream.Null, input, cancellation, count => progress?.Invoke(new("verifying", done, inputs.Count, verified + count, logical)));
            verified += input.Size; done++; progress?.Invoke(new("verifying", done, inputs.Count, verified, logical));
        }
    }
    private static Stream OpenInput(Input input)
    {
        if (input.Content != null) return new MemoryStream(input.Content, writable: false);
        Store.AssertNoLinks(input.Pin!.Path);
        var stream = new FileStream(input.Pin.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length == input.Size) return stream;
        stream.Dispose(); throw new IOException("A packaging input changed size.");
    }
    private static async Task CopyChecked(Stream source, Stream destination, Input input, CancellationToken cancellation, Action<long>? progress)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024); long count = 0;
        try
        {
            while (await source.ReadAsync(buffer, cancellation) is var read && read > 0)
            {
                count += read; if (count > input.Size) throw new IOException("An archive input grew beyond its declared size.");
                hash.AppendData(buffer, 0, read); await destination.WriteAsync(buffer.AsMemory(0, read), cancellation); progress?.Invoke(count);
            }
            if (count != input.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != input.Sha256)
                throw new IOException("An archived file differs from its supplied pin. The incomplete archive is removed.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    private static int Mode(string runtime, string relative, QemuInstallation? installation)
    {
        if (runtime != "linux-x64") return 0x1a4;
        if (installation == null) return relative is "Thaddeus.Host" or "start-thaddeus.sh" ? 0x1ed : 0x1a4;
        var root = installation.RuntimePackage!.Root;
        return new[] { installation.Executable.Path, installation.ImageTool.Path }.Any(file => "worker/runtime/" + Path.GetRelativePath(root, file).Replace('\\', '/') == relative) ? 0x1ed : 0x1a4;
    }
    private static string[] Inventory(string directory)
    {
        var found = new List<string>(); var directories = new Stack<(string Path, int Depth)>(); directories.Push((directory, 0)); var visited = 0;
        while (directories.TryPop(out var current))
        {
            if (++visited > MaxFiles || current.Depth > 32) throw new IOException("Package directory nesting or count exceeds its bound.");
            foreach (var entry in new DirectoryInfo(current.Path).EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Package inputs cannot contain links.");
                if (entry is DirectoryInfo) directories.Push((entry.FullName, current.Depth + 1));
                else { if (found.Count >= MaxFiles) throw new IOException("Too many package files."); found.Add(Path.GetRelativePath(directory, entry.FullName).Replace('\\', '/')); }
            }
        }
        return found.Order(StringComparer.Ordinal).ToArray();
    }
    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 300 || path.Any(character => char.IsControl(character) || "\\:*?\"<>|".Contains(character)) ||
            path.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                Regex.IsMatch(part, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)", RegexOptions.IgnoreCase)))
            throw new ArgumentException("Package entries must be portable relative paths.");
    }
    private static bool HashPattern(string hash) => Regex.IsMatch(hash, "\\A[a-f0-9]{64}\\z");
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw new ArgumentException("Ambiguous duplicate package fields."); Unique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Unique(item);
    }
    private static void EnsureSpace(long available, long nextBytes)
    { if (available < 10 * GiB + nextBytes) throw new IOException("Archive preparation needs its bounded output plus 10 GiB free. No staged worker copy is created."); }

    private sealed class BoundedOutput(Stream inner, long maximum, Func<long> availableSpace) : Stream
    {
        private long written, nextSpaceCheck;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => written; public override long Position { get => written; set => throw new NotSupportedException(); }
        private void Admit(int bytes)
        {
            if (written + bytes > maximum) throw new IOException("The archive exceeded its bounded output size.");
            if (written >= nextSpaceCheck) { EnsureSpace(availableSpace(), 1024 * 1024); nextSpaceCheck = written + 16 * 1024 * 1024; }
            written += bytes;
        }
        public override void Write(byte[] buffer, int offset, int count) { Admit(count); inner.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Admit(buffer.Length); inner.Write(buffer); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Admit(buffer.Length); await inner.WriteAsync(buffer, cancellationToken); }
        public override void Flush() => inner.Flush(); public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
}
