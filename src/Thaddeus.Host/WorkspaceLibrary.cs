using System.Text.RegularExpressions;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record LibraryEntry(string Key, string? Folder, string[] Tags, string UpdatedBy, DateTimeOffset UpdatedAt);
public record LibraryPins(string PrincipalId, string[] Keys);
public record LibraryLedger(int Version, string[] Folders, LibraryEntry[] Entries, LibraryPins[] Pins);
/// <summary>What one reader sees: shared folders and entries, and only their own pins.</summary>
public record LibraryView(int Version, string[] Folders, LibraryEntry[] Entries, string[] Pins);
public record LibraryEntryChange(int ExpectedVersion, string? Folder, string[]? Tags);
public record LibraryFolderMove(string From, string? To);
public record LibraryFoldersChange(int ExpectedVersion, string[]? Folders, LibraryFolderMove[]? Moves);
public record LibraryPinsChange(string[]? Keys);

/// <summary>Organization metadata for the workspace Library: folders, tags and personal pins.
/// Items stay in their own stores; this ledger only records where each one is filed.</summary>
public sealed partial class WorkspaceLibrary(Store store)
{
    private const string Key = "workspace-library-v1";
    public const int MaxFolders = 400, MaxDepth = 5, MaxSegment = 60, MaxPath = 200, MaxTags = 12, MaxTag = 32, MaxPins = 24;
    private const string Conflict = "The library changed. Refresh and try again.";
    [GeneratedRegex("\\A(wiki|page|media|source|draft|deliverable|brief):[A-Za-z0-9_.:-]{1,100}\\z")] private static partial Regex ItemKey();
    private LibraryLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<LibraryLedger>(json) : new(0, [], [], []);
    private void Write(LibraryLedger value) => store.Setting(Key, Wire.Pack(value));

    private static LibraryView View(LibraryLedger ledger, string principalId) => new(ledger.Version, ledger.Folders, ledger.Entries,
        ledger.Pins.FirstOrDefault(pins => pins.PrincipalId == principalId)?.Keys ?? []);

    public LibraryView View(string principalId) { lock (store) return View(Read(), principalId); }

    public static string ValidKey(string? key) =>
        key != null && ItemKey().IsMatch(key) ? key : throw new ArgumentException("That library item is not recognized.");

    /// <summary>Trims each segment; null for unfiled. Rejects empty segments, control characters and oversized paths.</summary>
    public static string? NormalizeFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.Length > MaxPath * 4) throw new ArgumentException($"A folder path can be up to {MaxPath} characters.");
        var trimmed = path.Trim();
        if (trimmed.StartsWith('/') || trimmed.EndsWith('/')) throw new ArgumentException("A folder path cannot start or end with a slash.");
        var segments = trimmed.Split('/').Select(segment => segment.Trim()).ToArray();
        if (segments.Length > MaxDepth) throw new ArgumentException($"Folders can be nested up to {MaxDepth} levels deep.");
        foreach (var segment in segments)
        {
            if (segment.Length is 0 or > MaxSegment) throw new ArgumentException($"Each folder name needs 1–{MaxSegment} characters.");
            if (segment.Any(char.IsControl)) throw new ArgumentException("Folder names cannot contain control characters.");
        }
        var normalized = string.Join('/', segments);
        return normalized.Length <= MaxPath ? normalized : throw new ArgumentException($"A folder path can be up to {MaxPath} characters.");
    }

    public static string[] NormalizeTags(string[]? tags)
    {
        var result = new List<string>();
        foreach (var raw in tags ?? [])
        {
            var tag = (raw ?? "").Trim().ToLowerInvariant();
            if (tag.Length is 0 or > MaxTag || !tag.All(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_'))
                throw new ArgumentException($"Tags need 1–{MaxTag} letters, numbers, spaces, dashes or underscores.");
            if (!result.Contains(tag, StringComparer.Ordinal)) result.Add(tag);
        }
        return result.Count <= MaxTags ? result.ToArray() : throw new ArgumentException($"An item can have up to {MaxTags} tags.");
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        for (var index = path.IndexOf('/'); index >= 0; index = path.IndexOf('/', index + 1)) yield return path[..index];
        yield return path;
    }

    private static bool Within(string path, string prefix) =>
        string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds a path and its ancestors, reusing an existing spelling for each level. Returns the stored spelling.</summary>
    private static string Include(List<string> folders, string path)
    {
        var canonical = "";
        foreach (var segment in path.Split('/'))
        {
            var candidate = canonical.Length == 0 ? segment : canonical + "/" + segment;
            var existing = folders.FirstOrDefault(folder => string.Equals(folder, candidate, StringComparison.OrdinalIgnoreCase));
            if (existing == null) folders.Add(candidate);
            canonical = existing ?? candidate;
        }
        return canonical;
    }

    private static string[] Sorted(List<string> folders) =>
        folders.Count <= MaxFolders
            ? folders.Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : throw new ArgumentException($"The library can hold up to {MaxFolders} folders.");

    private static LibraryEntry[] Ordered(IEnumerable<LibraryEntry> entries) =>
        entries.Where(entry => entry.Folder != null || entry.Tags.Length > 0).OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();

    public LibraryView SaveEntry(string key, LibraryEntryChange change, string author, string principalId)
    {
        ValidKey(key);
        var folder = NormalizeFolder(change.Folder);
        var tags = NormalizeTags(change.Tags);
        lock (store)
        {
            var ledger = Read();
            if (change.ExpectedVersion != ledger.Version) throw new InvalidOperationException(Conflict);
            var folders = ledger.Folders.ToList();
            if (folder != null) folder = Include(folders, folder);
            var entry = new LibraryEntry(key, folder, tags, author, DateTimeOffset.UtcNow);
            var next = ledger with { Version = ledger.Version + 1, Folders = Sorted(folders),
                Entries = Ordered(ledger.Entries.Where(item => item.Key != key).Append(entry)) };
            Write(next);
            return View(next, principalId);
        }
    }

    /// <summary>Replaces the folder list. Moves re-file entries and sub-folders from one prefix to another, or to unfiled;
    /// any entry left in a folder that no longer exists becomes unfiled.</summary>
    public LibraryView SaveFolders(LibraryFoldersChange change, string author, string principalId)
    {
        if (change.Folders == null) throw new ArgumentException("Send the complete folder list.");
        if (change.Folders.Length > MaxFolders) throw new ArgumentException($"The library can hold up to {MaxFolders} folders.");
        var moves = (change.Moves ?? []).Select(move => move == null ? throw new ArgumentException("Each move needs a folder to move from.")
            : (From: NormalizeFolder(move.From) ?? throw new ArgumentException("Each move needs a folder to move from."), To: NormalizeFolder(move.To))).ToArray();
        if (moves.Length > MaxFolders) throw new ArgumentException("Too many folder moves in one change.");
        string? Relocate(string? path)
        {
            if (path == null) return null;
            foreach (var (from, to) in moves)
            {
                if (path == null || !Within(path, from)) continue;
                path = to == null ? null : NormalizeFolder(to + path[from.Length..]);
            }
            return path;
        }
        var requested = change.Folders.Select(NormalizeFolder).ToArray();
        lock (store)
        {
            var ledger = Read();
            if (change.ExpectedVersion != ledger.Version) throw new InvalidOperationException(Conflict);
            var folders = new List<string>();
            foreach (var path in requested.Select(Relocate).OfType<string>()) Include(folders, path);
            // Sub-folders travel with a moved folder even when the client listed only its new top level.
            foreach (var path in ledger.Folders.Where(folder => moves.Any(move => Within(folder, move.From))).Select(Relocate).OfType<string>())
                Include(folders, path);
            var now = DateTimeOffset.UtcNow;
            var entries = ledger.Entries.Select(entry =>
            {
                var moved = Relocate(entry.Folder);
                var kept = moved == null ? null : folders.FirstOrDefault(folder => string.Equals(folder, moved, StringComparison.OrdinalIgnoreCase));
                return kept == entry.Folder ? entry : entry with { Folder = kept, UpdatedBy = author, UpdatedAt = now };
            });
            var next = ledger with { Version = ledger.Version + 1, Folders = Sorted(folders), Entries = Ordered(entries) };
            Write(next);
            return View(next, principalId);
        }
    }

    public LibraryView SavePins(string principalId, LibraryPinsChange change)
    {
        if (change.Keys == null) throw new ArgumentException("Send the complete list of pinned items.");
        var keys = change.Keys.Select(ValidKey).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length > MaxPins) throw new ArgumentException($"You can pin up to {MaxPins} items.");
        lock (store)
        {
            var ledger = Read();
            var others = ledger.Pins.Where(pins => pins.PrincipalId != principalId);
            var next = ledger with { Version = ledger.Version + 1,
                Pins = (keys.Length == 0 ? others : others.Append(new LibraryPins(principalId, keys))).ToArray() };
            Write(next);
            return View(next, principalId);
        }
    }
}
