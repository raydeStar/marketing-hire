using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Store
{
    private static void MemoryId(string id)
    {
        if (id == null || !Regex.IsMatch(id, @"\A[a-f0-9]{32}\z")) throw new ArgumentException("Memory identity is invalid.");
    }
    private RememberedEntry? Memory(string id) => Query("SELECT body FROM memories WHERE id=$id", ("$id", id)).Select(Wire.Unpack<RememberedEntry>).SingleOrDefault();
    public RememberedEntry[] MemoryRecords() { lock (gate) return Query("SELECT body FROM memories ORDER BY rowid").Select(Wire.Unpack<RememberedEntry>).ToArray(); }
    public MemoryChange[] MemoryChanges() { lock (gate) return Query("SELECT body FROM memory_changes ORDER BY rowid").Select(Wire.Unpack<MemoryChange>).ToArray(); }
    public string? MemoryCursor() { lock (gate) return Query("SELECT id FROM memory_changes ORDER BY rowid DESC LIMIT 1").SingleOrDefault(); }
    public MemoryView[] Memories() { lock (gate) return Query("SELECT body FROM memories WHERE json_extract(body,'$.forgotten')=0 ORDER BY rowid")
        .Select(Wire.Unpack<RememberedEntry>).Select(entry => new MemoryView(entry, SourceStatus(entry))).ToArray(); }
    private string SourceStatus(RememberedEntry entry)
    {
        if (entry.Forgotten || entry.Source == null) return "forgotten";
        var source = Page(entry.Source.Path);
        return source == null ? "source-missing" : source.Version == entry.Source.Version && source.Content.Contains(entry.Source.Quote, StringComparison.Ordinal) ? "current" : "source-changed";
    }
    public RememberedEntry Remember(string id, RememberRequest request)
    {
        lock (gate)
        {
            MemoryId(id);
            if (string.IsNullOrWhiteSpace(request.Statement) || request.Statement.Length > 1000 || request.Source == null ||
                string.IsNullOrWhiteSpace(request.Source.Quote) || request.Source.Quote.Length > 2000)
                throw new ArgumentException("Use a statement up to 1,000 characters and an exact source quotation up to 2,000 characters.");
            SafePath(request.Source.Path);
            var page = Page(request.Source.Path) ?? throw new InvalidOperationException("The source note no longer exists.");
            if (request.Source.Version != page.Version || !page.Content.Contains(request.Source.Quote, StringComparison.Ordinal))
                throw new InvalidOperationException("The source or quotation changed. Open the current note and review it again.");
            var current = Memory(id);
            if (current?.Forgotten == true) throw new InvalidOperationException("This entry was forgotten. Create a new entry to remember it again.");
            if ((current?.Version ?? "absent") != request.Version) throw new InvalidOperationException("This memory changed. Refresh it before saving.");
            if (current == null && Memories().Length >= 256) throw new InvalidOperationException("The memory library has reached 256 active entries. Forget unused entries first.");
            var updated = DateTimeOffset.UtcNow;
            var entry = new RememberedEntry(id, Wire.Hash(Wire.Pack(new { id, request.Statement, request.Source, updated })), request.Statement, request.Source, updated);
            SaveMemory(entry, current == null ? "remembered" : "corrected"); return entry;
        }
    }
    public RememberedEntry ForgetMemory(string id, string version)
    {
        lock (gate)
        {
            MemoryId(id); var current = Memory(id) ?? throw new ArgumentException("Memory not found.");
            if (current.Forgotten) return current;
            if (current.Version != version) throw new InvalidOperationException("This memory changed. Review it before forgetting.");
            // Keep a content-free tombstone so a delayed save cannot bring the old memory back from the crypt.
            var updated = DateTimeOffset.UtcNow;
            var forgotten = new RememberedEntry(id, Wire.Hash(Wire.Pack(new { id, updated, forgotten = true })), "", null, updated, true);
            SaveMemory(forgotten, "forgotten"); return forgotten;
        }
    }
    private void SaveMemory(RememberedEntry entry, string kind)
    {
        using var transaction = db.BeginTransaction();
        Exec("INSERT INTO memories VALUES($id,$body) ON CONFLICT(id) DO UPDATE SET body=$body", ("$id", entry.Id), ("$body", Wire.Pack(entry)));
        var change = new MemoryChange(Guid.NewGuid().ToString("N"), entry.Id, kind, entry.Version, entry.Updated);
        Exec("INSERT INTO memory_changes VALUES($id,$body)", ("$id", change.Id), ("$body", Wire.Pack(change)));
        testFault?.Invoke("before-memory-commit"); transaction.Commit();
    }
    public RememberedEntry Recall(MemorySelection selection)
    {
        lock (gate)
        {
            MemoryId(selection.Id); var entry = Memory(selection.Id);
            if (entry == null || entry.Forgotten || entry.Version != selection.Version || SourceStatus(entry) != "current")
                throw new InvalidOperationException("A selected memory or its source changed. Review the memory before starting a new task.");
            return entry;
        }
    }
    public void AssertMemoriesCurrent(Run run)
    {
        lock (gate)
        {
            foreach (var entry in run.PreparedContext?.Memories ?? []) Recall(new(entry.Id, entry.Version));
        }
    }
}
