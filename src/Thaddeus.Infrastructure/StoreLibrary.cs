using System.Text.RegularExpressions;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

// The owner's intentions are not execution receipts. A busy butler does not finish your errands by decree.
public record TrackingPlan(string Section = "daily", string Cadence = "none", decimal? Current = null, decimal? Target = null,
    string? Unit = null, string? NextStep = null, DateOnly? NextCheckIn = null, DateOnly? LastCheckIn = null);
public record LibraryItem(string Id, string Kind, string Title, string Content, string Status, string? Url,
    DateOnly? Due, string Version, DateTimeOffset Created, DateTimeOffset Updated, TrackingPlan? Tracking = null, string? Category = null, string? Prompt = null);
public record LibraryEdit(string Kind, string Title, string Content, string Status, string? Url, DateOnly? Due, string Version,
    TrackingPlan? Tracking = null, string? Category = null, string? Prompt = null);
public record LibraryChange(string Id, string ItemId, string Kind, string Version, DateTimeOffset At);

public sealed partial class Store
{
    public LibraryItem[] Library() { lock (gate) return Query("SELECT body FROM library ORDER BY rowid DESC").Select(Wire.Unpack<LibraryItem>).ToArray(); }
    public LibraryChange[] LibraryChanges() { lock (gate) return Query("SELECT body FROM library_changes ORDER BY rowid").Select(Wire.Unpack<LibraryChange>).ToArray(); }
    public long LibraryCursor() { lock (gate) return long.Parse(Query("SELECT CAST(COALESCE(MAX(rowid),0) AS TEXT) FROM library_changes").Single()); }
    public LibraryItem EditLibrary(string id, LibraryEdit edit)
    {
        lock (gate)
        {
            if (!Regex.IsMatch(id, "\\A[a-f0-9]{32}\\z")) throw new ArgumentException("Invalid collection item ID.");
            if (edit.Kind is not ("todo" or "idea" or "feed") || edit.Status is not ("open" or "done" or "archived") ||
                (edit.Kind == "idea" && edit.Status == "done")) throw new ArgumentException("Choose a valid collection and item status.");
            if (string.IsNullOrWhiteSpace(edit.Title) || edit.Title.Length > 160 || edit.Content == null || edit.Content.Length > 12000)
                throw new ArgumentException("Use a title up to 160 characters and notes up to 12,000 characters.");
            if (edit.Due != null && edit.Kind != "todo") throw new ArgumentException("Only to-do items have due dates.");
            if (edit.Tracking is { } tracking && (edit.Kind != "todo" || tracking.Section is not ("tracked" or "daily" or "weekly" or "goals") ||
                tracking.Cadence is not ("none" or "daily" or "weekly") || tracking.Unit?.Length > 40 || tracking.NextStep?.Length > 1000 ||
                tracking.Current is < -1_000_000 or > 1_000_000 || tracking.Target is < -1_000_000 or > 1_000_000))
                throw new ArgumentException("Choose a valid task section, repeat interval, and bounded progress values.");
            if (edit.Category?.Length > 60 || edit.Prompt?.Length > 2000 || (edit.Kind != "idea" && (edit.Category != null || edit.Prompt != null)))
                throw new ArgumentException("Idea categories and prompts must fit within their limits.");
            var url = string.IsNullOrWhiteSpace(edit.Url) ? null : edit.Url.Trim();
            if (url != null && (url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                parsed.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(parsed.UserInfo)))
                throw new ArgumentException("Use an HTTP or HTTPS link without embedded credentials.");
            var current = Query("SELECT body FROM library WHERE id=$id", ("$id", id)).Select(Wire.Unpack<LibraryItem>).SingleOrDefault();
            if ((current?.Version ?? "absent") != edit.Version) throw new InvalidOperationException("This item changed in another window. Reload the saved item before editing it again.");
            if (current != null && current.Kind != edit.Kind) throw new ArgumentException("An item's collection cannot change.");
            if (current == null && Query("SELECT CAST(COUNT(*) AS TEXT) FROM library").Single() is var count && int.Parse(count) >= 5000)
                throw new InvalidOperationException("This host has reached 5,000 collection items. Export your data before clearing it.");
            var now = DateTimeOffset.UtcNow;
            var item = new LibraryItem(id, edit.Kind, edit.Title.Trim(), edit.Content, edit.Status, url, edit.Due,
                Guid.NewGuid().ToString("N"), current?.Created ?? now, now, edit.Tracking, edit.Category?.Trim(), edit.Prompt?.Trim());
            var kind = current == null ? "created" : current.Status == item.Status ? "edited" : item.Status;
            var change = new LibraryChange(Guid.NewGuid().ToString("N"), id, kind, item.Version, now);
            using var transaction = db.BeginTransaction();
            Exec("INSERT INTO library VALUES($id,$body) ON CONFLICT(id) DO UPDATE SET body=$body", ("$id", id), ("$body", Wire.Pack(item)));
            Exec("INSERT INTO library_changes(id,body) VALUES($id,$body)", ("$id", change.Id), ("$body", Wire.Pack(change)));
            testFault?.Invoke("before-library-commit");
            transaction.Commit();
            return item;
        }
    }
}
