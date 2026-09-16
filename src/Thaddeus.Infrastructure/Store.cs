using Microsoft.Data.Sqlite;
using Thaddeus.Core;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Infrastructure;

public sealed partial class Store : IRunStore, IToolExecutor, IDisposable
{
    public const int CurrentSchemaVersion = 8;
    private readonly SqliteConnection db;
    private readonly object gate = new();
    private readonly FileStream lease;
    private bool disposed;
    public string Root { get; }
    private readonly Action<string>? testFault;
    public Store(string root, Action<string>? testFault = null)
    {
        this.testFault = testFault;
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        AssertNoLinks(Root);
        // One host owns the ledger. Two butlers carrying the same tray is rarely helpful.
        lease = new FileStream(Path.Combine(Root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // This connection belongs to the exclusive store lease. A global pool must not keep its file handles after that lease ends.
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "ledger.sqlite"), Pooling = false }.ToString());
        try
        {
            db.Open();
            using var versionCommand = db.CreateCommand(); versionCommand.CommandText = "PRAGMA user_version";
            var version = Convert.ToInt32(versionCommand.ExecuteScalar());
            if (version > CurrentSchemaVersion) throw new InvalidOperationException("This data belongs to a newer Thaddeus version. Use that version or restore a compatible backup.");
            Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
            using var migration = db.BeginTransaction();
            Exec("""
            CREATE TABLE IF NOT EXISTS uploads(id TEXT PRIMARY KEY, body TEXT NOT NULL, content BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS runs(id TEXT PRIMARY KEY, version INTEGER NOT NULL, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS events(cursor INTEGER PRIMARY KEY AUTOINCREMENT, runId TEXT NOT NULL, seq INTEGER NOT NULL, body TEXT NOT NULL, UNIQUE(runId,seq));
            CREATE TABLE IF NOT EXISTS pages(path TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS revisions(id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT NOT NULL, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS chats(id TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS library(id TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS library_changes(cursor INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT NOT NULL UNIQUE, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS feed_subscriptions(id TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS feed_entries(id TEXT PRIMARY KEY, subscription TEXT NOT NULL REFERENCES feed_subscriptions(id), body TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS feed_subscription_entries ON feed_entries(subscription);
            CREATE TABLE IF NOT EXISTS memories(id TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS memory_changes(id TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS artifact_apps(id TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS artifact_revisions(id TEXT PRIMARY KEY, artifactId TEXT NOT NULL REFERENCES artifact_apps(id), body TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS artifact_history ON artifact_revisions(artifactId);
            CREATE TABLE IF NOT EXISTS writes(id TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied TEXT NOT NULL, description TEXT NOT NULL);
            """);
            if (version < 1) Exec("INSERT INTO schema_migrations VALUES(1,$at,$description)",
                ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$description", "Register legacy JSON rows and durable writes without rewriting history"));
            if (version < 2) Exec("INSERT INTO schema_migrations VALUES(2,$at,$description)",
                ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$description", "Additive execution, capability, question and model-dispatch JSON fields; absent fields retain legacy defaults"));
            if (version < 3) Exec("INSERT INTO schema_migrations VALUES(3,$at,$description)",
                ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$description", "Explicit source-linked memory and content-free change receipts; legacy run rows stay intact"));
            if (version < 4) Exec("INSERT INTO schema_migrations VALUES(4,$at,$description)",
                ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$description", "Owner-managed to-do, ideas and saved reading; no conversion of execution history"));
            if (version < 5) Exec("INSERT INTO schema_migrations VALUES(5,$at,$description)",
                ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$description", "Bounded RSS and Atom subscriptions and rotating updates, separate from saved reading"));
            if (version < 6) Exec("INSERT INTO schema_migrations VALUES(6,$at,$description)",
                ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$description", "Persistent declarative artifact apps, entries and bounded revision history"));
            if (version < 7) Exec("INSERT INTO schema_migrations VALUES(7,$at,$description)",
                ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$description", "Generated artifact pages; prevent older readers from discarding page code on edits"));
            if (version < 8) Exec("INSERT INTO schema_migrations VALUES(8,$at,$description)", ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$description", "Bounded uploads and task follow-up metadata; protect new fields from older editors"));
            Exec("PRAGMA user_version=8;");
            migration.Commit();
        }
        catch { db.Dispose(); lease.Dispose(); throw; }
    }
    private void Exec(string sql, params (string Key, object? Value)[] args)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    private List<string> Query(string sql, params (string Key, object? Value)[] args)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        using var reader = cmd.ExecuteReader(); var rows = new List<string>();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return rows;
    }
    public Run? Get(string id) { lock (gate) return Query("SELECT body FROM runs WHERE id=$id", ("$id", id)).Select(Wire.Unpack<Run>).FirstOrDefault(); }
    public IReadOnlyList<Run> List() { lock (gate) return Query("SELECT body FROM runs ORDER BY rowid DESC").Select(Wire.Unpack<Run>).ToArray(); }
    public void Save(Run run, string type, object data) => Save(run, type, data, null);
    public void Save(Run run, string type, object data, ChatMessage? message)
    {
        lock (gate)
        {
            var previousVersion = run.Version;
            try
            {
                using var tx = db.BeginTransaction();
                SaveRunInTransaction(run, type, data, message);
                tx.Commit();
            }
            catch { run.Version = previousVersion; throw; }
        }
    }
    private void SaveRunInTransaction(Run run, string type, object data, ChatMessage? message)
    {
        var existing = Get(run.Id);
        if (existing != null && existing.Version != run.Version) throw new InvalidOperationException("Run changed; refresh before acting.");
        run.Version++; run.Updated = DateTimeOffset.UtcNow;
        Exec("INSERT INTO runs VALUES($id,$v,$b) ON CONFLICT(id) DO UPDATE SET version=$v,body=$b", ("$id", run.Id), ("$v", run.Version), ("$b", Wire.Pack(run)));
        var seq = long.Parse(Query("SELECT CAST(COALESCE(MAX(seq),0)+1 AS TEXT) FROM events WHERE runId=$id", ("$id", run.Id))[0]);
        var evt = new RunEvent(1, Guid.NewGuid().ToString("N"), run.Id, seq, run.Updated, type, JsonSerializer.SerializeToElement(data, Wire.Json));
        Exec("INSERT INTO events(runId,seq,body) VALUES($id,$s,$b)", ("$id", run.Id), ("$s", seq), ("$b", Wire.Pack(evt)));
        if (message != null) Exec("INSERT INTO chats VALUES($i,$b)", ("$i", message.Id), ("$b", Wire.Pack(message)));
    }
    public IReadOnlyList<RunEvent> Events(long after = 0, string? runId = null) => ReadEvents(after, runId, 2000);
    public IReadOnlyList<RunEvent> AllEvents() => ReadEvents(0, null, -1);
    private IReadOnlyList<RunEvent> ReadEvents(long after, string? runId, int limit)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT cursor,body FROM events WHERE cursor>$c AND ($r IS NULL OR runId=$r) ORDER BY cursor LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.Parameters.AddWithValue("$c", after); cmd.Parameters.AddWithValue("$r", (object?)runId ?? DBNull.Value);
            using var reader = cmd.ExecuteReader(); var result = new List<RunEvent>();
            while (reader.Read()) result.Add(Wire.Unpack<RunEvent>(reader.GetString(1)) with { Cursor = reader.GetInt64(0) });
            return result;
        }
    }
    public static void AssertNoLinks(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new ArgumentException("Linked workspace paths are not permitted.");
    }
    public string SafePath(string path)
    {
        if (!Regex.IsMatch(path, @"\A(?:notes|plans)/[a-z0-9][a-z0-9-]{0,90}\.md\z")) throw new ArgumentException("Use a simple Markdown name in notes/ or plans/.");
        if (Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"\A(?:con|prn|aux|nul|com[1-9]|lpt[1-9])\z", RegexOptions.IgnoreCase)) throw new ArgumentException("Reserved device names are not valid knowledge pages.");
        var full = Path.GetFullPath(Path.Combine(Root, "knowledge", path));
        AssertNoLinks(Path.GetDirectoryName(full)!);
        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) throw new ArgumentException("Linked files are not permitted.");
        return full;
    }
    public string Version(string path)
    {
        lock (gate) { var file = SafePath(path); return File.Exists(file) ? Wire.Hash(File.ReadAllText(file)) : "absent"; }
    }
    public Page? Page(string path)
    {
        lock (gate)
        {
            var file = SafePath(path);
            if (!File.Exists(file)) return null;
            var content = File.ReadAllText(file);
            if (content.Length > 100_000) throw new ArgumentException("Page exceeds the 100 KB text limit.");
            return new(path, content, Wire.Hash(content), File.GetLastWriteTimeUtc(file));
        }
    }
    public IReadOnlyList<Page> Pages()
    {
        lock (gate) return Query("SELECT body FROM pages ORDER BY path").Select(Wire.Unpack<Page>).Select(p => Page(p.Path)).OfType<Page>().ToArray();
    }
    public IReadOnlyList<Page> Revisions(string path)
    {
        SafePath(path); lock (gate) return Query("SELECT body FROM revisions WHERE path=$p ORDER BY id DESC", ("$p", path)).Select(Wire.Unpack<Page>).ToArray();
    }
    public ToolResult Read(string path)
    {
        var page = Page(path) ?? throw new ArgumentException("Source page does not exist.");
        return new("knowledge.read", true, "Read scoped Markdown source", new(path, page.Version, page.Content));
    }
    public Page Write(string path, string content, string expectedVersion)
        => WriteCommitted(Guid.NewGuid().ToString("N"), path, content, expectedVersion);
    public Page WriteCommitted(string operationId, string path, string content, string expectedVersion)
    {
        lock (gate)
        {
            if (WriteOperation(operationId) is { } existing)
            {
                if (existing.Page.Path != path || existing.Page.Content != content || existing.ExpectedVersion != expectedVersion) throw new InvalidOperationException("Write identity cannot be reused for a different action.");
                if (Version(path) != existing.Page.Version) throw new InvalidOperationException("Committed write requires explicit reconciliation.");
                return existing.Page;
            }
            if (content.Length is 0 or > 100_000) throw new ArgumentException("Page must contain 1–100,000 characters.");
            var file = SafePath(path);
            if (Version(path) != expectedVersion) throw new InvalidOperationException("Resource changed. The old approval cannot authorize this write.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var page = new Page(path, content, Wire.Hash(content), DateTimeOffset.UtcNow);
            var intent = new WriteOperation(operationId, page, expectedVersion, "committed");
            using (var tx = db.BeginTransaction())
            {
                Exec("INSERT INTO pages VALUES($p,$b) ON CONFLICT(path) DO UPDATE SET body=$b", ("$p", path), ("$b", Wire.Pack(page)));
                Exec("INSERT INTO revisions(path,body) VALUES($p,$b)", ("$p", path), ("$b", Wire.Pack(page)));
                Exec("INSERT INTO writes VALUES($i,$b)", ("$i", operationId), ("$b", Wire.Pack(intent)));
                tx.Commit();
            }
            testFault?.Invoke("after-content-commit");
            Project(intent);
            return page;
        }
    }
    public WriteOperation? WriteOperation(string id)
    {
        lock (gate) return Query("SELECT body FROM writes WHERE id=$i", ("$i", id)).Select(Wire.Unpack<WriteOperation>).FirstOrDefault();
    }
    public IReadOnlyList<WriteOperation> WriteOperations()
    {
        lock (gate) return Query("SELECT body FROM writes ORDER BY rowid DESC").Select(Wire.Unpack<WriteOperation>).ToArray();
    }
    public Page CompleteProjection(string operationId, string observedVersion)
    {
        lock (gate)
        {
            var intent = WriteOperation(operationId) ?? throw new ArgumentException("No committed content to reconcile.");
            var current = Version(intent.Page.Path);
            if (current != observedVersion) throw new InvalidOperationException("Page changed after inspection; refresh reconciliation.");
            if (current != intent.ExpectedVersion && current != intent.Page.Version) throw new InvalidOperationException("Page has conflicting content. Preserve it and start a new explicit edit.");
            if (current != intent.Page.Version) Project(intent);
            else Exec("UPDATE writes SET body=$b WHERE id=$i", ("$b", Wire.Pack(intent with { Status = "projected" })), ("$i", operationId));
            return intent.Page;
        }
    }
    private void Project(WriteOperation intent)
    {
        var file = SafePath(intent.Page.Path);
        if (Version(intent.Page.Path) != intent.ExpectedVersion) throw new InvalidOperationException("Page changed before projection.");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, intent.Page.Content);
        File.Move(temp, file, true);
        testFault?.Invoke("after-projection");
        Exec("UPDATE writes SET body=$b WHERE id=$i", ("$b", Wire.Pack(intent with { Status = "projected" })), ("$i", intent.Id));
    }
    public string? Setting(string key) { lock (gate) return Query("SELECT body FROM settings WHERE key=$k", ("$k", key)).FirstOrDefault(); }
    public void Setting(string key, string value) { lock (gate) Exec("INSERT INTO settings VALUES($k,$b) ON CONFLICT(key) DO UPDATE SET body=$b", ("$k", key), ("$b", value)); }
    public void ProviderSettings(string provider, string credentials)
    {
        lock (gate)
        {
            using var transaction = db.BeginTransaction();
            Setting("provider", provider); Setting("provider-credentials", credentials); transaction.Commit();
        }
    }
    public void Chat(ChatMessage message) { lock (gate) Exec("INSERT INTO chats VALUES($i,$b)", ("$i", message.Id), ("$b", Wire.Pack(message))); }
    public IReadOnlyList<ChatMessage> Chats() { lock (gate) return Query("SELECT body FROM chats ORDER BY rowid").Select(Wire.Unpack<ChatMessage>).ToArray(); }
    public void DeletePersonalData()
    {
        lock (gate)
        {
            var runs = List();
            if (runs.Any(run => run.Research?.WorkerRetained == true)) throw new InvalidOperationException("Remove retained private workspaces before deleting their task records.");
            var workers = runs.Where(run => run.Execution != null).Select(run => run.Execution!.SandboxId).ToHashSet(StringComparer.Ordinal);
            if (Query("SELECT key FROM settings WHERE key LIKE 'sandbox:%'").Any(key => !workers.Contains(key[8..])))
                throw new InvalidOperationException("A worker registration has no matching task. Reconcile its ownership before deleting personal data.");
            // Never erase the ownership ledger while a private disk is still in the attic.
            if (Directory.EnumerateFileSystemEntries(Root, "qemu-*").Any(path => Path.GetFileName(path) != "qemu-owner.lock"))
                throw new InvalidOperationException("Private worker files still exist. Remove them before deleting their ownership records.");
            foreach (var run in runs.Where(run => run.Execution != null))
            {
                var workerId = run.Execution!.SandboxId; DockerSandboxBackend.ValidateId(workerId);
                if (Setting("sandbox:" + workerId) is { } saved && Wire.Unpack<SandboxRegistration>(saved).Status is not ("purged" or "removed"))
                    throw new InvalidOperationException("A registered worker still needs cleanup. Its ownership records must be retained.");
                var directory = Path.Combine(Root, "qemu-" + workerId); AssertNoLinks(directory);
                if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Private worker files still exist. Remove them before deleting their ownership records.");
            }
            foreach (var p in Pages()) File.Delete(SafePath(p.Path));
            foreach (var run in runs)
            {
                foreach (var prefix in new[] { "worker-grant:", "workspace-removal:" }) Exec("DELETE FROM settings WHERE key=$k", ("$k", prefix + run.Id));
                if (run.Execution is not { } execution) continue;
                foreach (var prefix in new[] { "sandbox:", "sandbox-absence:", "qemu-host:", "qemu-route:", "qemu-observation:", "qemu-termination:", "qemu-recovery:" })
                    Exec("DELETE FROM settings WHERE key=$k", ("$k", prefix + execution.SandboxId));
                if (Setting("active-sandbox") == execution.SandboxId) Exec("DELETE FROM settings WHERE key='active-sandbox'");
            }
            Exec("DELETE FROM uploads; DELETE FROM artifact_revisions; DELETE FROM artifact_apps; DELETE FROM runs; DELETE FROM events; DELETE FROM pages; DELETE FROM revisions; DELETE FROM chats; DELETE FROM writes; DELETE FROM memories; DELETE FROM memory_changes; DELETE FROM library; DELETE FROM library_changes; DELETE FROM feed_entries; DELETE FROM feed_subscriptions;");
            ChangedFeeds();
            Setting("upload-revision", Guid.NewGuid().ToString("N"));
            Setting("artifact-revision", Guid.NewGuid().ToString("N"));
            Exec("PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            // Finish an admitted ledger operation before handing back its database and ownership lease.
            db.Dispose(); lease.Dispose(); disposed = true;
        }
    }
}
public record WriteOperation(string Id, Page Page, string ExpectedVersion, string Status);
