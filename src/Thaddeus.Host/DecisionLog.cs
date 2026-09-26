using System.Globalization;
using System.Text;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public record DecisionEntry(DateTimeOffset At, string By, string What, string Decision, string Why, string? Key);
record DecisionLedger(DecisionEntry[] Entries, string? WikiId);

/// <summary>The owner's decisions in one place, newest first: drafts approved or rejected, page copy, experiments started, declined
/// or decided, each with the reason given. Kept as the Library document Company → Decision log, which the employee also reads.</summary>
public sealed class DecisionLog(Store store, CompanyWiki wiki, WorkspaceLibrary library, ILogger<DecisionLog> logger)
{
    private const string Key = "decision-log-v1";
    const string Author = "Decision log";

    DecisionLedger Read() => store.Setting(Key) is { } json ? Wire.Unpack<DecisionLedger>(json) : new([], null);
    public DecisionEntry[] Entries() { lock (store) return [.. Read().Entries.Reverse()]; }

    public void Record(string by, string what, string decision, string? why, string? key = null)
    {
        try
        {
            lock (store)
            {
                var ledger = Read();
                var entry = new DecisionEntry(DateTimeOffset.UtcNow, Clip(by, 80), Clip(what, 200), Clip(decision, 40), Clip(why ?? "", 600), key);
                var entries = ledger.Entries.TakeLast(499).Append(entry).ToArray();
                var existing = ledger.WikiId != null ? wiki.List().FirstOrDefault(page => page.Id == ledger.WikiId) : null;
                var page = wiki.Save(new WikiChange(Guid.NewGuid().ToString("N"), existing?.Id, existing?.Version ?? 0, "company", "company", "Decision log", Render(entries), "fact", "active"), Author);
                if (existing == null)
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        try { library.SaveEntry("wiki:" + page.Id, new LibraryEntryChange(library.View("").Version, "Company", ["decisions"]), Author, "employee"); break; }
                        catch (InvalidOperationException) when (attempt < 2) { }
                    }
                store.Setting(Key, Wire.Pack(new DecisionLedger(entries, page.Id)));
            }
        }
        // The decision itself already happened; a log that can't be written must never undo or block it.
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException) { logger.LogWarning("The decision log wasn't updated: {Error}", error.Message); }
    }

    static string Clip(string value, int limit) { var text = value.Replace('\n', ' ').Trim(); return text.Length > limit ? text[..limit] + "…" : text; }

    static string Render(DecisionEntry[] entries)
    {
        var text = new StringBuilder("# Decision log\n\n_Every decision the owner made on the employee's work, newest first, with the reason given. Written by the host as decisions happen; the employee reads it._\n");
        foreach (var month in entries.Reverse().GroupBy(entry => entry.At.ToLocalTime().ToString("MMMM yyyy", CultureInfo.InvariantCulture)))
        {
            text.Append($"\n## {month.Key}\n\n| When | What | Decision | Why | By |\n|---|---|---|---|---|\n");
            foreach (var entry in month)
                text.Append($"| {entry.At.ToLocalTime():MMM d, h:mm tt} | {Cell(entry.What)} | {Cell(entry.Decision)} | {(entry.Why.Length > 0 ? Cell(entry.Why) : "—")} | {Cell(entry.By)} |\n");
        }
        if (entries.Length == 0) text.Append("\n_No decisions yet._\n");
        return text.ToString();
    }

    static string Cell(string value) => value.Replace("|", "\\|");
}
