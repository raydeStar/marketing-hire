using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed record StudyBackupFile(string Path, long Bytes, string Sha256, DateTimeOffset LastWriteUtc = default);
public sealed record StudyBackupManifest(int FormatVersion, string Kind, DateTimeOffset Created, int DatabaseSchemaVersion, StudyBackupFile[] Files);
public sealed record StudyBackupReceipt(string Operation, string Directory, int DatabaseSchemaVersion, int Files, long Bytes, string ManifestSha256);
public sealed record StudyBackupPreview(DateTimeOffset Created, int DatabaseSchemaVersion, int Files, long Bytes, string ManifestSha256);

/// <summary>Offline study copies. A restore creates another study; the butler never swaps the original out from under you.</summary>
public static class StudyBackup
{
    private const int MaxFiles = 20_000, MaxDepth = 32, ManifestLimit = 8_000_000;
    private const long MaxBytes = 64L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> Volatile = new(StringComparer.OrdinalIgnoreCase)
        { "host.lock", "launcher.lock", "launcher-instance.json", "qemu-owner.lock", "ledger.sqlite-wal", "ledger.sqlite-shm" };

    public static async Task<StudyBackupReceipt> Create(string dataDirectory, string destination, CancellationToken cancellation = default)
    {
        var source = Existing(dataDirectory); var target = Destination(destination, source);
        if (!File.Exists(Path.Combine(source, "ledger.sqlite"))) throw new ArgumentException("Choose an existing Thaddeus data directory.");
        using var launcher = Lease(source, "launcher.lock"); using var host = Lease(source, "host.lock");
        var originals = Inventory(source, excludeTransient: true);
        var wal = new FileInfo(Path.Combine(source, "ledger.sqlite-wal"));
        if (wal.Exists && wal.Length > MaxBytes) throw new IOException("The database journal exceeds the backup limit.");
        var estimate = originals.Sum(relative => new FileInfo(Path.Combine(source, relative)).Length);
        if (estimate > MaxBytes) throw new IOException("The study exceeds the supported backup size.");
        StorageSpace.Require(target, checked(estimate + (wal.Exists ? wal.Length : 0) + 16 * 1024 * 1024));
        var stage = Stage(target); var payload = PrivateWorkerDirectory.Create(Path.Combine(stage, "data"));
        // SQLite folds any durable WAL content into a standalone database; no migration touches the original.
        cancellation.ThrowIfCancellationRequested();
        var schema = SnapshotDatabase(source, payload);
        var entries = new List<StudyBackupFile> { await Describe(payload, "ledger.sqlite", cancellation) };
        foreach (var relative in originals.Where(relative => relative != "ledger.sqlite"))
            entries.Add(await Copy(source, payload, relative, null, cancellation));
        if (!originals.SequenceEqual(Inventory(source, excludeTransient: true))) throw new IOException("The source inventory changed during backup. Keep the incomplete copy and try again after all writers have stopped.");
        var manifest = new StudyBackupManifest(1, "thaddeus-study-backup", DateTimeOffset.UtcNow, schema, entries.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
        Validate(manifest);
        var json = Wire.Pack(manifest);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > ManifestLimit) throw new IOException("The backup manifest exceeds the supported limit.");
        using (var output = new FileStream(Path.Combine(stage, "backup.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { await output.WriteAsync(bytes, cancellation); output.Flush(flushToDisk: true); }
        await VerifyFiles(payload, manifest, cancellation);
        cancellation.ThrowIfCancellationRequested();
        Directory.Move(stage, target);
        return Receipt("backup", target, manifest, Wire.Hash(json));
    }

    public static async Task<StudyBackupPreview> Preview(string backupDirectory, CancellationToken cancellation = default)
    {
        var source = Existing(backupDirectory); var file = Path.Combine(source, "backup.json"); NoLinks(file);
        using var lease = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var json = await ReadManifest(lease, cancellation);
        var manifest = Wire.Unpack<StudyBackupManifest>(json); Validate(manifest);
        return new(manifest.Created, manifest.DatabaseSchemaVersion, manifest.Files.Length, manifest.Files.Sum(entry => entry.Bytes), Wire.Hash(json));
    }

    public static async Task<StudyBackupReceipt> Restore(string backupDirectory, string destination, CancellationToken cancellation = default, string? expectedManifestSha256 = null)
    {
        var source = Existing(backupDirectory); var target = Destination(destination, source);
        var file = Path.Combine(source, "backup.json"); NoLinks(file);
        var info = new FileInfo(file);
        if (!info.Exists || info.Length > ManifestLimit) throw new ArgumentException("Choose a complete, bounded Thaddeus backup.");
        using var manifestLease = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var json = await ReadManifest(manifestLease, cancellation);
        if (expectedManifestSha256 != null && expectedManifestSha256 != Wire.Hash(json))
            throw new InvalidOperationException("The selected backup changed after review. Review it again before restoring.");
        var manifest = Wire.Unpack<StudyBackupManifest>(json); Validate(manifest);
        var payload = Existing(Path.Combine(source, "data"));
        if (!Inventory(payload).SequenceEqual(manifest.Files.Select(entry => entry.Path))) throw new IOException("The backup inventory differs from its manifest.");
        StorageSpace.Require(target, checked(manifest.Files.Sum(entry => entry.Bytes) + 16 * 1024 * 1024));
        var stage = Stage(target);
        foreach (var entry in manifest.Files) await Copy(payload, stage, entry.Path, entry, cancellation);
        var schema = VerifyDatabase(stage);
        if (schema != manifest.DatabaseSchemaVersion) throw new IOException("The backup database version differs from its manifest.");
        await VerifyFiles(stage, manifest, cancellation);
        cancellation.ThrowIfCancellationRequested();
        using (var restored = new Store(stage)) restored.DisarmDelegationsAfterRestore(DateTimeOffset.UtcNow);
        foreach (var transient in new[] { "host.lock", "ledger.sqlite-wal", "ledger.sqlite-shm" })
        {
            var path = Path.Combine(stage, transient);
            if (File.Exists(path)) File.Delete(path);
        }
        Directory.Move(stage, target);
        return Receipt("restore", target, manifest, Wire.Hash(json));
    }

    private static async Task<string> ReadManifest(FileStream stream, CancellationToken cancellation)
    {
        if (stream.Length > ManifestLimit) throw new IOException("The backup manifest exceeds the supported limit.");
        using var text = new StreamReader(stream, leaveOpen: true);
        var content = new StringBuilder(); var buffer = new char[8192];
        while (await text.ReadAsync(buffer, cancellation) is var count && count != 0)
        {
            if (content.Length + count > ManifestLimit) throw new IOException("The backup manifest grew beyond its limit.");
            content.Append(buffer, 0, count);
        }
        return content.ToString();
    }

    private static string Existing(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use an absolute directory path.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); Store.AssertNoLinks(full);
        if (!Directory.Exists(full)) throw new ArgumentException("The source directory does not exist.");
        return full;
    }
    private static string Destination(string path, string source)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use an absolute destination path.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); Store.AssertNoLinks(full);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.Equals(source, comparison) || full.StartsWith(source + Path.DirectorySeparatorChar, comparison) || source.StartsWith(full + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Keep the source and destination in separate directory trees.");
        if (Path.Exists(full)) throw new IOException("The destination already exists. Choose a new folder; nothing will be overwritten.");
        if (!Directory.Exists(Path.GetDirectoryName(full))) throw new ArgumentException("The destination's parent directory must already exist.");
        return full;
    }
    private static string Stage(string target) => PrivateWorkerDirectory.Create(target + ".incomplete-" + Guid.NewGuid().ToString("N"));
    private static FileStream Lease(string directory, string name)
    {
        var path = Path.Combine(directory, name); NoLinks(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("Thaddeus is running or its data is in use. Stop this study's host before making a backup."); }
    }
    private static string[] Inventory(string directory, bool excludeTransient = false)
    {
        var files = new List<string>(); long bytes = 0;
        void Visit(string root, string prefix, int depth)
        {
            if (depth > MaxDepth) throw new IOException("The study contains too many nested directories.");
            foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked files or directories cannot be included in a study backup.");
                var relative = prefix + entry.Name; ValidatePath(relative);
                // The Windows maintenance host still writes here; diagnostics remain with the original estate.
                if (excludeTransient && prefix == "" && entry is DirectoryInfo && entry.Name.Equals("launcher-logs", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry is DirectoryInfo) Visit(entry.FullName, relative + "/", depth + 1);
                else if (!(excludeTransient && prefix == "" && Volatile.Contains(entry.Name)))
                {
                    if (files.Count >= MaxFiles) throw new IOException("The study exceeds the backup file limit.");
                    bytes = checked(bytes + ((FileInfo)entry).Length); if (bytes > MaxBytes) throw new IOException("The study exceeds the 64 GiB backup limit.");
                    files.Add(relative);
                }
            }
        }
        Visit(directory, "", 0);
        return files.Order(StringComparer.Ordinal).ToArray();
    }
    private static void ValidatePath(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1000 || relative.Contains('\\') || relative.Contains(':') || relative.Any(char.IsControl) || Path.IsPathRooted(relative))
            throw new ArgumentException("The backup contains an unsupported file path.");
        foreach (var part in relative.Split('/'))
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 ||
                Regex.IsMatch(part.Split('.')[0], @"\A(?:con|prn|aux|nul|com[1-9]|lpt[1-9])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new ArgumentException("The backup contains an ambiguous or nonportable file path.");
    }
    private static void Validate(StudyBackupManifest? manifest)
    {
        if (manifest is not { FormatVersion: 1, Kind: "thaddeus-study-backup", Files.Length: > 0 and <= MaxFiles } ||
            manifest.DatabaseSchemaVersion is < 0 or > Store.CurrentSchemaVersion) throw new ArgumentException("This backup format or database version is not supported by this host.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file == null) throw new ArgumentException("Invalid backup entry.");
            ValidatePath(file.Path);
            if (!seen.Add(file.Path) || file.Bytes < 0 || file.Bytes > MaxBytes || file.LastWriteUtc.Year < 1601 || file.LastWriteUtc.Offset != TimeSpan.Zero || file.Sha256 == null || !Regex.IsMatch(file.Sha256, @"\A[a-f0-9]{64}\z")) throw new ArgumentException("Invalid or repeated backup entry.");
            total = checked(total + file.Bytes); if (total > MaxBytes) throw new ArgumentException("The backup exceeds the 64 GiB limit.");
            if (Volatile.Contains(file.Path)) throw new ArgumentException("A backup must not restore process locks or database sidecars.");
        }
        if (!seen.Contains("ledger.sqlite") || !manifest.Files.Select(file => file.Path).SequenceEqual(manifest.Files.Select(file => file.Path).Order(StringComparer.Ordinal)))
            throw new ArgumentException("The backup must contain a database and a canonical file inventory.");
    }
    private static async Task<StudyBackupFile> Describe(string directory, string relative, CancellationToken cancellation)
    {
        var full = Path.Combine(directory, relative); NoLinks(full);
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new(relative, stream.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellation)), File.GetLastWriteTimeUtc(full));
    }
    private static async Task<StudyBackupFile> Copy(string source, string target, string relative, StudyBackupFile? expected, CancellationToken cancellation)
    {
        var from = Path.Combine(source, relative); var to = Path.Combine(target, relative);
        NoLinks(from); Store.AssertNoLinks(Path.GetDirectoryName(to)!);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var lastWrite = File.GetLastWriteTimeUtc(from);
        if (input.Length > MaxBytes || expected != null && input.Length != expected.Bytes) throw new IOException("A backup file has an unexpected size.");
        using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
        {
            var length = input.Length; var buffer = new byte[1024 * 1024]; long copied = 0, nextSpaceCheck = 0;
            while (await input.ReadAsync(buffer, cancellation) is var count && count != 0)
            {
                copied += count; if (copied > length) throw new IOException("A source file changed during the copy.");
                if (copied >= nextSpaceCheck) { StorageSpace.Require(to, 1024 * 1024); nextSpaceCheck = copied + 16 * 1024 * 1024; }
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
            }
            if (copied != length) throw new IOException("A source file changed during the copy.");
            output.Flush(flushToDisk: true);
        }
        File.SetLastWriteTimeUtc(to, lastWrite);
        var actual = await Describe(target, relative, cancellation);
        input.Position = 0;
        if (actual.Sha256 != Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellation)) || expected != null && actual != expected)
            throw new IOException("A backup file does not match its recorded bytes. The original study is unchanged.");
        return actual;
    }
    private static async Task VerifyFiles(string directory, StudyBackupManifest manifest, CancellationToken cancellation)
    {
        if (!Inventory(directory).SequenceEqual(manifest.Files.Select(file => file.Path))) throw new IOException("The copied file inventory changed.");
        foreach (var file in manifest.Files) if (await Describe(directory, file.Path, cancellation) != file) throw new IOException("The copied bytes do not match the manifest.");
    }
    private static SqliteConnection Database(string directory, bool create = false) => new(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(directory, "ledger.sqlite"), Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    private static int Check(SqliteConnection db)
    {
        using var version = db.CreateCommand(); version.CommandText = "PRAGMA user_version";
        var schema = Convert.ToInt32(version.ExecuteScalar());
        if (schema is < 0 or > Store.CurrentSchemaVersion) throw new InvalidOperationException("This data belongs to a newer host. Use that host to back it up or restore it.");
        using var check = db.CreateCommand(); check.CommandText = "PRAGMA quick_check";
        if (!Equals(check.ExecuteScalar(), "ok")) throw new IOException("Database integrity verification failed.");
        using var tables = db.CreateCommand(); tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('runs','events','pages','settings')";
        if (Convert.ToInt32(tables.ExecuteScalar()) != 4) throw new IOException("This is not a supported Thaddeus database.");
        return schema;
    }
    private static int SnapshotDatabase(string source, string destination)
    {
        NoLinks(Path.Combine(source, "ledger.sqlite")); NoLinks(Path.Combine(source, "ledger.sqlite-wal")); NoLinks(Path.Combine(source, "ledger.sqlite-shm"));
        using var original = Database(source); original.Open(); var schema = Check(original);
        using (var snapshot = Database(destination, create: true))
        {
            snapshot.Open(); original.BackupDatabase(snapshot);
            // A portable snapshot carries one database file, including committed WAL pages from the source.
            using var journal = snapshot.CreateCommand(); journal.CommandText = "PRAGMA journal_mode=DELETE";
            if (!Equals(journal.ExecuteScalar(), "delete")) throw new IOException("The database snapshot could not be made self-contained.");
        }
        if (VerifyDatabase(destination) != schema) throw new IOException("The database snapshot version changed.");
        return schema;
    }
    private static int VerifyDatabase(string directory) { using var db = Database(directory); db.Open(); return Check(db); }
    internal static void NoLinks(string path)
    {
        Store.AssertNoLinks(path);
        if (!File.Exists(path)) return;
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked files cannot be used for backup or restore.");
        if (OperatingSystem.IsWindows()) return;
        // FileStream's ordinary Unix open can wait for a pipe writer before cancellation is possible.
        // O_RDONLY | O_NONBLOCK | O_NOFOLLOW | O_CLOEXEC, from the supported platforms' fcntl.h.
        // Probe without reading: the ledger may contain files, but it is not accepting plumbing.
        var flags = OperatingSystem.IsLinux() ? 0x800 | 0x20000 | 0x80000 :
            OperatingSystem.IsMacOS() ? 0x4 | 0x100 | 0x1000000 : throw new PlatformNotSupportedException();
        using var handle = new SafeFileHandle(UnixOpen(path, flags), ownsHandle: true);
        if (handle.IsInvalid) throw new IOException("The backup input could not be opened without following a link or waiting on a pipe.");
        using var probe = new FileStream(handle, FileAccess.Read);
        if (!probe.CanSeek) throw new IOException("Non-seekable inputs such as named pipes cannot be included in a study copy.");
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    private static StudyBackupReceipt Receipt(string operation, string directory, StudyBackupManifest manifest, string hash) =>
        new(operation, directory, manifest.DatabaseSchemaVersion, manifest.Files.Length, manifest.Files.Sum(file => file.Bytes), hash);
}
