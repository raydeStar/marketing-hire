using Microsoft.Data.Sqlite;
using Thaddeus.Core;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thaddeus.Infrastructure;

public sealed class Store : IRunStore, IToolExecutor, IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();
    private readonly FileStream lease;
    public string Root { get; }
    public Store(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        AssertNoLinks(Root);
        // One host owns the ledger. Two butlers carrying the same tray is rarely helpful.
        lease = new FileStream(Path.Combine(Root, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "ledger.sqlite") }.ToString());
        db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        Exec("""
            CREATE TABLE IF NOT EXISTS runs(id TEXT PRIMARY KEY, version INTEGER NOT NULL, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS events(cursor INTEGER PRIMARY KEY AUTOINCREMENT, runId TEXT NOT NULL, seq INTEGER NOT NULL, body TEXT NOT NULL, UNIQUE(runId,seq));
            CREATE TABLE IF NOT EXISTS pages(path TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS revisions(id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT NOT NULL, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, body TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS chats(id TEXT PRIMARY KEY, body TEXT NOT NULL);
            """);
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
    public void Save(Run run, string type, object data)
    {
        lock (gate)
        {
            var existing = Get(run.Id);
            if (existing != null && existing.Version != run.Version) throw new InvalidOperationException("Run changed; refresh before acting.");
            using var tx = db.BeginTransaction();
            run.Version++; run.Updated = DateTimeOffset.UtcNow;
            Exec("INSERT INTO runs VALUES($id,$v,$b) ON CONFLICT(id) DO UPDATE SET version=$v,body=$b", ("$id", run.Id), ("$v", run.Version), ("$b", Wire.Pack(run)));
            var seq = long.Parse(Query("SELECT CAST(COALESCE(MAX(seq),0)+1 AS TEXT) FROM events WHERE runId=$id", ("$id", run.Id))[0]);
            var evt = new RunEvent(1, Guid.NewGuid().ToString("N"), run.Id, seq, run.Updated, type, JsonSerializer.SerializeToElement(data, Wire.Json));
            Exec("INSERT INTO events(runId,seq,body) VALUES($id,$s,$b)", ("$id", run.Id), ("$s", seq), ("$b", Wire.Pack(evt)));
            tx.Commit();
        }
    }
    public IReadOnlyList<RunEvent> Events(long after = 0, string? runId = null)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT cursor,body FROM events WHERE cursor>$c AND ($r IS NULL OR runId=$r) ORDER BY cursor LIMIT 2000";
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
    {
        lock (gate)
        {
            if (content.Length is 0 or > 100_000) throw new ArgumentException("Page must contain 1–100,000 characters.");
            var file = SafePath(path);
            if (Version(path) != expectedVersion) throw new InvalidOperationException("Resource changed. The old approval cannot authorize this write.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var page = new Page(path, content, Wire.Hash(content), DateTimeOffset.UtcNow);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, content);
            File.Move(temp, file, true);
            using var tx = db.BeginTransaction();
            Exec("INSERT INTO pages VALUES($p,$b) ON CONFLICT(path) DO UPDATE SET body=$b", ("$p", path), ("$b", Wire.Pack(page)));
            Exec("INSERT INTO revisions(path,body) VALUES($p,$b)", ("$p", path), ("$b", Wire.Pack(page)));
            tx.Commit();
            return page;
        }
    }
    public string? Setting(string key) { lock (gate) return Query("SELECT body FROM settings WHERE key=$k", ("$k", key)).FirstOrDefault(); }
    public void Setting(string key, string value) { lock (gate) Exec("INSERT INTO settings VALUES($k,$b) ON CONFLICT(key) DO UPDATE SET body=$b", ("$k", key), ("$b", value)); }
    public void Chat(ChatMessage message) { lock (gate) Exec("INSERT INTO chats VALUES($i,$b)", ("$i", message.Id), ("$b", Wire.Pack(message))); }
    public IReadOnlyList<ChatMessage> Chats() { lock (gate) return Query("SELECT body FROM chats ORDER BY rowid").Select(Wire.Unpack<ChatMessage>).ToArray(); }
    public void DeletePersonalData()
    {
        lock (gate)
        {
            foreach (var p in Pages()) File.Delete(SafePath(p.Path));
            Exec("DELETE FROM runs; DELETE FROM events; DELETE FROM pages; DELETE FROM revisions; DELETE FROM chats;");
            Exec("PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");
        }
    }
    public void Dispose() { db.Dispose(); lease.Dispose(); }
}
