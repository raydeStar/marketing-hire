using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record QemuRuntimePackage(string Root, QemuPinnedFile Manifest);
public sealed record QemuRuntimeManifest(int SchemaVersion, string Kind, string Version, QemuPinnedFile[] Files);

/// <summary>Checks a host-selected manifest. Unix locks are advisory; Linux execution additionally requires read-only package storage.</summary>
public sealed class QemuRuntimeLease : IDisposable
{
    private readonly List<FileStream> locks = [];
    private readonly string root;
    private readonly Dictionary<string, QemuPinnedFile> expected;
    private bool disposed;
    public int FileCount => expected.Count;

    private QemuRuntimeLease(string root, Dictionary<string, QemuPinnedFile> expected)
    { this.root = root; this.expected = expected; }

    public static async Task<QemuRuntimeLease> Open(QemuRuntimePackage package, QemuPinnedFile executable, QemuPinnedFile imageTool, CancellationToken cancellation,
        string expectedKind = "qemu-windows-runtime")
    {
        if (expectedKind is not ("qemu-windows-runtime" or "qemu-linux-x64-runtime")) throw new ArgumentException("Unsupported runtime target.");
        if (!Path.IsPathFullyQualified(package.Root) || !Path.IsPathFullyQualified(package.Manifest.Path) ||
            !Regex.IsMatch(package.Manifest.Sha256, "\\A[a-f0-9]{64}\\z")) throw new ArgumentException("Invalid runtime package reference.");
        Store.AssertNoLinks(package.Root); Store.AssertNoLinks(package.Manifest.Path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package.Root));
        var manifestStream = new FileStream(package.Manifest.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        QemuRuntimeLease? lease = null;
        try
        {
            if (manifestStream.Length is < 1 or > 1048576 ||
                Convert.ToHexStringLower(await SHA256.HashDataAsync(manifestStream, cancellation)) != package.Manifest.Sha256)
                throw new IOException("Runtime manifest size or digest differs from its trusted pin.");
            manifestStream.Position = 0;
            var manifest = await JsonSerializer.DeserializeAsync<QemuRuntimeManifest>(manifestStream, Wire.Json, cancellation);
            if (manifest is not { SchemaVersion: 1, Version: "11.1.0", Files.Length: > 0 and <= 4096 } || manifest.Kind != expectedKind)
                throw new IOException("Unsupported or incomplete runtime manifest.");
            var expected = new Dictionary<string, QemuPinnedFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                ValidateRelative(file.Path);
                if (!Regex.IsMatch(file.Sha256, "\\A[a-f0-9]{64}\\z") || !expected.TryAdd(file.Path, file))
                    throw new IOException("Runtime manifest has an invalid hash or duplicate/case-colliding path.");
            }
            foreach (var program in new[] { executable, imageTool })
            {
                var relative = Path.GetRelativePath(root, Path.GetFullPath(program.Path)).Replace('\\', '/');
                if (!expected.TryGetValue(relative, out var file) || file.Sha256 != program.Sha256)
                    throw new IOException("Configured executable is outside or differs from the runtime manifest.");
            }
            lease = new(root, expected); lease.locks.Add(manifestStream); lease.VerifyInventory();
            foreach (var file in expected.Values)
            {
                cancellation.ThrowIfCancellationRequested();
                var path = Path.Combine(root, file.Path); Store.AssertNoLinks(path);
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                lease.locks.Add(stream);
                if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation)) != file.Sha256)
                    throw new IOException("Runtime package file differs from its manifest: " + file.Path);
            }
            lease.VerifyInventory(); return lease;
        }
        catch { if (lease != null) lease.Dispose(); else manifestStream.Dispose(); throw; }
    }

    public QemuPinnedFile FilePin(string relative)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!expected.TryGetValue(relative, out var file) || file.Path != relative) throw new IOException("Runtime role is missing from its manifest.");
        return new(Path.Combine(root, file.Path), file.Sha256);
    }

    public void VerifyInventory()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Store.AssertNoLinks(root);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var pending = new Stack<string>(); pending.Push(root);
        var directories = 0;
        while (pending.TryPop(out var directory))
        {
            if (++directories > 4096) throw new IOException("Runtime directory count exceeds its bound.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Runtime package contains a link.");
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (!expected.TryGetValue(relative, out var declared) || declared.Path != relative || !found.Add(relative))
                    throw new IOException("Runtime package has an unexpected or case-colliding file: " + relative);
            }
        }
        if (found.Count != expected.Count) throw new IOException("Runtime package is missing a declared file.");
    }

    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Any(character => char.IsControl(character) || "\\:*?\"<>|".Contains(character)) ||
            path.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                Regex.IsMatch(part, "\\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)", RegexOptions.IgnoreCase)))
            throw new IOException("Runtime manifest path is not a portable relative file path.");
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var stream in locks) stream.Dispose(); locks.Clear();
    }
}
