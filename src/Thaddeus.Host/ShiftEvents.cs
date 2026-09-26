using System.Collections.Concurrent;

namespace Thaddeus.Host;

public record ShiftEvent(int N, DateTimeOffset At, string Kind, string Text, string? Key = null);

/// <summary>What a shift is doing as it does it: each model step as it starts, each page it reads, each grade and fix, each save.
/// Kept in memory for the live view (the shift's records hold what's lasting), the last few hundred per shift.</summary>
public sealed class ShiftEvents
{
    const int Keep = 300;
    readonly ConcurrentDictionary<string, List<ShiftEvent>> feeds = new();

    public void Add(string shiftId, string kind, string text, string? key = null)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        if (text.Length == 0) return;
        if (text.Length > 280) text = text[..279].TrimEnd() + "…";
        var feed = feeds.GetOrAdd(shiftId, _ => []);
        lock (feed)
        {
            feed.Add(new ShiftEvent(feed.Count == 0 ? 1 : feed[^1].N + 1, DateTimeOffset.UtcNow, kind, text, key));
            if (feed.Count > Keep) feed.RemoveRange(0, feed.Count - Keep);
        }
    }

    public ShiftEvent[] After(string shiftId, int after)
    {
        if (!feeds.TryGetValue(shiftId, out var feed)) return [];
        lock (feed) return [.. feed.Where(item => item.N > after)];
    }
}

/// <summary>Notes a cycle keeps for its stage summary, each also told to the live view as it's written.</summary>
public sealed class NarratedNotes(ShiftEvents events, string shiftId, string kind) : List<string>
{
    public new void Add(string note) { base.Add(note); events.Add(shiftId, kind, note); }
}
